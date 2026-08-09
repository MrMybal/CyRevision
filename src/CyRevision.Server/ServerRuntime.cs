using System.Collections.Concurrent;
using CyRevision.Backup;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Git;
using CyRevision.Security;
using CyRevision.Sync;
using CyRevision.Vpn;

namespace CyRevision.Server;

public sealed class ServerRuntime : IAsyncDisposable
{
    private readonly ServerOptions _options;
    private readonly IProjectCatalog _catalog;
    private readonly IGitRepositoryService _git;
    private readonly ISyncthingProfileStore _syncProfiles;
    private readonly IGitPeerExchangeService _gitExchange;
    private readonly IVpnProfileStore _vpnProfiles;
    private readonly WireGuardKeyService _vpnKeys;
    private readonly ManagedWireGuardEngine _vpnEngine;
    private readonly ConcurrentDictionary<Guid, ManagedSyncthingEngine> _syncEngines = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectGates = new();

    public ServerRuntime(
        ServerOptions options,
        IProjectCatalog catalog,
        IGitRepositoryService git,
        ISyncthingProfileStore syncProfiles,
        IGitPeerExchangeService gitExchange,
        IVpnProfileStore vpnProfiles,
        WireGuardKeyService vpnKeys,
        ManagedWireGuardEngine vpnEngine)
    {
        _options = options;
        _catalog = catalog;
        _git = git;
        _syncProfiles = syncProfiles;
        _gitExchange = gitExchange;
        _vpnProfiles = vpnProfiles;
        _vpnKeys = vpnKeys;
        _vpnEngine = vpnEngine;
    }

    public async Task<ProjectDefinition> CreateProjectAsync(
        CreateServerProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ProjectPreset preset = ProjectPresets.All.FirstOrDefault(item => item.Kind == request.Preset)
                               ?? throw new ArgumentException("Unsupported project preset.", nameof(request));
        string path = string.IsNullOrWhiteSpace(request.ExistingPath)
            ? Path.Combine(_options.ProjectsDirectory, SanitizeDirectoryName(request.Name))
            : Path.GetFullPath(request.ExistingPath);
        if (!_options.IsAllowedProjectPath(path))
        {
            throw new UnauthorizedAccessException("The project path is outside the configured server roots.");
        }

        if ((await _catalog.GetAllAsync(cancellationToken)).Any(project =>
                string.Equals(Path.GetFullPath(project.RootPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("This directory is already registered as a CyRevision project.");
        }

        Directory.CreateDirectory(path);
        if (preset.Features.GitEnabled && !Directory.Exists(Path.Combine(path, ".git")))
        {
            await _git.InitializeAsync(path, cancellationToken);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProjectDefinition project = new(
            Guid.NewGuid(),
            request.Name.Trim(),
            path,
            preset.Features,
            preset.Retention,
            CreatedAt: now,
            LastOpenedAt: now,
            BackupStorePath: Path.Combine(_options.BackupDirectory, SanitizeDirectoryName(request.Name)));
        await _catalog.UpsertAsync(project, cancellationToken);
        return project;
    }

    public async Task RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await StopSyncAsync(projectId, cancellationToken);
        VpnProjectProfile? vpnProfile = await _vpnProfiles.GetAsync(projectId, cancellationToken);
        if (vpnProfile is not null &&
            (await _vpnEngine.GetStatusAsync(vpnProfile, cancellationToken)).State == VpnRuntimeState.Running)
        {
            throw new InvalidOperationException("Stop the CyRevision VPN tunnel before removing this project.");
        }
        await _catalog.RemoveAsync(projectId, cancellationToken);
    }

    public async Task<GitRepositoryStatus?> GetGitStatusAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        return project.Features.GitEnabled ? await _git.GetStatusAsync(project.RootPath, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<GitRevision>> GetGitHistoryAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        return project.Features.GitEnabled
            ? await _git.GetHistoryAsync(project.RootPath, cancellationToken: cancellationToken)
            : [];
    }

    public async Task<BackupSnapshot> CreateBackupAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        SemaphoreSlim gate = _projectGates.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            FileSystemBackupService service = CreateBackupService(project);
            return await service.CreateSnapshotAsync(project.Id, project.RootPath, project.Retention, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupSnapshot>> GetBackupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        return await CreateBackupService(project).GetSnapshotsAsync(project.Id, cancellationToken);
    }

    public async Task<SyncthingProfile> ConfigureSyncAsync(
        Guid projectId,
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        await StopSyncAsync(projectId, cancellationToken);
        return await _syncProfiles.CreateOrUpdateAsync(
            project.Id,
            executablePath,
            ResolveExchangeDirectory(project),
            cancellationToken);
    }

    public async Task<SyncEngineStatus> StartSyncAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        if (!project.Features.PeerSyncEnabled)
        {
            throw new InvalidOperationException("Synchronization is disabled for this project profile.");
        }

        SyncthingProfile profile = await _syncProfiles.GetAsync(projectId, cancellationToken)
                                    ?? throw new InvalidOperationException("Configure the Syncthing executable first.");
        ManagedSyncthingEngine engine = _syncEngines.GetOrAdd(projectId, _ => new ManagedSyncthingEngine(profile.ToIsolationOptions()));
        await engine.StartAsync(cancellationToken);
        await ConfigureFolderAsync(project, profile, [], cancellationToken);
        if (project.Features.GitEnabled)
        {
            await ExchangeGitAsync(projectId, cancellationToken);
        }
        return engine.Status;
    }

    public async Task<SyncEngineStatus> PauseSyncAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!_syncEngines.TryGetValue(projectId, out ManagedSyncthingEngine? engine))
        {
            return new SyncEngineStatus(SyncEngineState.Stopped, 0, 0);
        }

        await engine.PauseAsync(cancellationToken);
        return engine.Status;
    }

    public async Task<SyncEngineStatus> StopSyncAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!_syncEngines.TryRemove(projectId, out ManagedSyncthingEngine? engine))
        {
            return new SyncEngineStatus(SyncEngineState.Stopped, 0, 0);
        }

        await engine.DisposeAsync();
        return new SyncEngineStatus(SyncEngineState.Stopped, 0, 0, "Instance serveur CyRevision arrêtée");
    }

    public async Task<SyncEngineStatus> GetSyncStatusAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!_syncEngines.TryGetValue(projectId, out ManagedSyncthingEngine? engine))
        {
            return new SyncEngineStatus(SyncEngineState.Stopped, 0, 0);
        }

        return await engine.RefreshStatusAsync(cancellationToken);
    }

    public async Task<VpnProjectProfile> ConfigureVpnAsync(
        Guid projectId,
        ConfigureServerVpnRequest request,
        CancellationToken cancellationToken = default)
    {
        await GetProjectAsync(projectId, cancellationToken);
        WireGuardInstallation installation = _vpnKeys.DetectInstallation();
        if (!installation.CanGenerateKeys)
        {
            throw new FileNotFoundException("WireGuard wg is not installed on this server.");
        }

        string privateKeyPath = Path.Combine(_options.VpnDirectory, "keys", projectId.ToString("N"), "private.key");
        VpnProjectProfile profile = await _vpnProfiles.GetAsync(projectId, cancellationToken)
                                    ?? VpnProfileFactory.CreateDefault(projectId, privateKeyPath);
        profile = profile with
        {
            NetworkCidr = request.NetworkCidr ?? profile.NetworkCidr,
            LocalAddress = request.LocalAddress ?? profile.LocalAddress,
            ListenPort = request.ListenPort ?? profile.ListenPort,
            PublicEndpoint = string.IsNullOrWhiteSpace(request.PublicEndpoint) ? profile.PublicEndpoint : request.PublicEndpoint.Trim(),
            LocalCapabilities = request.Capabilities,
            WireGuardExecutablePath = installation.WireGuardExecutablePath ?? profile.WireGuardExecutablePath,
            WgExecutablePath = installation.WgExecutablePath ?? profile.WgExecutablePath,
            WgQuickExecutablePath = installation.WgQuickExecutablePath ?? profile.WgQuickExecutablePath,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (string.IsNullOrWhiteSpace(profile.PublicKey) || !File.Exists(profile.PrivateKeyPath))
        {
            (string publicKey, string keyPath) = await _vpnKeys.GenerateKeyPairAsync(
                profile.WgExecutablePath!, privateKeyPath, cancellationToken);
            profile = profile with { PublicKey = publicKey, PrivateKeyPath = keyPath };
        }

        await _vpnProfiles.SaveAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<VpnProjectProfile> GetVpnProfileAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await GetProjectAsync(projectId, cancellationToken);
        return await _vpnProfiles.GetAsync(projectId, cancellationToken)
               ?? throw new InvalidOperationException("WireGuard is not configured for this project.");
    }

    public async Task<VpnEngineStatus> GetVpnStatusAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await _vpnEngine.GetStatusAsync(await GetVpnProfileAsync(projectId, cancellationToken), cancellationToken);

    public async Task<VpnEngineStatus> StartVpnAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await _vpnEngine.StartAsync(await GetVpnProfileAsync(projectId, cancellationToken), cancellationToken);

    public async Task<VpnEngineStatus> StopVpnAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await _vpnEngine.StopAsync(await GetVpnProfileAsync(projectId, cancellationToken), cancellationToken);

    public async Task<SignedVpnInvitation> CreateVpnInvitationAsync(
        Guid projectId,
        VpnNodeCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        VpnProjectProfile profile = await GetVpnProfileAsync(projectId, cancellationToken);
        using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile, cancellationToken);
        return VpnPeerExchangeCodec.CreateInvitation(profile, identity, capabilities, TimeSpan.FromHours(24));
    }

    public async Task<string> JoinVpnInvitationAsync(
        Guid projectId,
        string invitationText,
        VpnNodeCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        VpnProjectProfile profile = await GetVpnProfileAsync(projectId, cancellationToken);
        SignedVpnInvitation invitation = VpnPeerExchangeCodec.ImportInvitation(invitationText);
        profile = VpnPeerExchangeCodec.ApplyInvitation(profile, invitation) with { LocalCapabilities = capabilities };
        using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile, cancellationToken);
        VpnJoinResponse response = VpnPeerExchangeCodec.CreateJoinResponse(invitation, profile, identity, capabilities);
        await _vpnProfiles.SaveAsync(profile, cancellationToken);
        return VpnPeerExchangeCodec.ExportJoinResponse(response);
    }

    public async Task<VpnPeerDefinition> AcceptVpnResponseAsync(
        Guid projectId,
        string responseText,
        CancellationToken cancellationToken = default)
    {
        VpnProjectProfile profile = await GetVpnProfileAsync(projectId, cancellationToken);
        using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile, cancellationToken);
        VpnPeerDefinition peer = VpnPeerExchangeCodec.ValidateJoinResponse(
            VpnPeerExchangeCodec.ImportJoinResponse(responseText), projectId, identity.Identity);
        if (profile.Peers.Any(item => item.PeerId == peer.PeerId || item.PublicKey == peer.PublicKey || item.TunnelAddress == peer.TunnelAddress))
        {
            throw new InvalidOperationException("The VPN peer, key or address is already registered.");
        }

        profile = profile with { Peers = [.. profile.Peers, peer], UpdatedAt = DateTimeOffset.UtcNow };
        await _vpnProfiles.SaveAsync(profile, cancellationToken);
        return peer;
    }

    public async Task RemoveVpnPeerAsync(Guid projectId, Guid peerId, CancellationToken cancellationToken = default)
    {
        VpnProjectProfile profile = await GetVpnProfileAsync(projectId, cancellationToken);
        if (!profile.Peers.Any(peer => peer.PeerId == peerId))
        {
            throw new KeyNotFoundException("VPN peer not found.");
        }

        await _vpnProfiles.SaveAsync(profile with
        {
            Peers = profile.Peers.Where(peer => peer.PeerId != peerId).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task<PeerInvitationPackage> CreatePeerInvitationAsync(
        Guid projectId,
        PeerRole role,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, SyncthingProfile _, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        using FileDeviceIdentityStore identity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        JsonPeerAdmissionService admission = CreateAdmissionService(project.Id, identity);
        return await admission.CreateInvitationAsync(project.Id, role, TimeSpan.FromHours(24), cancellationToken);
    }

    public async Task<string> PrepareJoinRequestAsync(
        Guid projectId,
        string invitationText,
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, SyncthingProfile _, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        PeerInvitationOffer offer = PeerExchangeCodec.ImportInvitation(invitationText);
        if (offer.Invitation.ProjectId != project.Id || string.IsNullOrWhiteSpace(verificationCode))
        {
            throw new InvalidOperationException("The invitation or out-of-band verification code is invalid.");
        }

        using FileDeviceIdentityStore identity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        string pendingPath = GetPendingInvitationPath(project.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(
            pendingPath,
            PeerExchangeCodec.ExportInvitation(new PeerInvitationPackage(
                offer.Invitation,
                offer.OneTimeToken,
                verificationCode.Trim(),
                offer.IssuerIdentity)),
            cancellationToken);
        return PeerExchangeCodec.ExportJoinRequest(new PeerJoinRequest(offer, identity.Identity, verificationCode.Trim()));
    }

    public async Task<string> ApproveJoinRequestAsync(
        Guid projectId,
        string joinRequestText,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, SyncthingProfile profile, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        PeerJoinRequest request = PeerExchangeCodec.ImportJoinRequest(joinRequestText);
        if (request.InvitationOffer.Invitation.ProjectId != project.Id)
        {
            throw new InvalidOperationException("The join request targets another project.");
        }

        using FileDeviceIdentityStore identity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        if (identity.Identity.DeviceId != request.InvitationOffer.IssuerIdentity.DeviceId)
        {
            throw new UnauthorizedAccessException("This server did not issue the invitation.");
        }

        JsonPeerAdmissionService admission = CreateAdmissionService(project.Id, identity);
        MembershipCertificate certificate = await admission.ApproveDeviceAsync(
            request.InvitationOffer.Invitation,
            request.InvitationOffer.OneTimeToken,
            request.Device,
            request.VerificationCode,
            cancellationToken);
        PeerMembershipGrant grant = new(
            request.InvitationOffer.Invitation.InvitationId,
            certificate,
            identity.Identity);
        string sharedGrantPath = Path.Combine(
            profile.ExchangeDirectory,
            "members",
            certificate.Device.DeviceId.ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(sharedGrantPath)!);
        await File.WriteAllTextAsync(sharedGrantPath, PeerExchangeCodec.ExportMembershipGrant(grant), cancellationToken);

        using SyncthingApiClient api = new(profile.ApiEndpoint, profile.ApiKey);
        await api.PutDeviceAsync(new SyncthingDeviceConfiguration(request.Device.SyncthingDeviceId, request.Device.DisplayName), cancellationToken);
        IReadOnlyList<MembershipCertificate> members = await admission.GetMembersAsync(project.Id, cancellationToken);
        await ConfigureFolderAsync(project, profile, members.Select(member => member.Device.SyncthingDeviceId).ToArray(), cancellationToken);
        return PeerExchangeCodec.ExportMembershipGrant(grant);
    }

    public async Task<MembershipCertificate> ImportMembershipGrantAsync(
        Guid projectId,
        string grantText,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, SyncthingProfile profile, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        string pendingPath = GetPendingInvitationPath(project.Id);
        if (!File.Exists(pendingPath))
        {
            throw new UnauthorizedAccessException("The original pending invitation is missing.");
        }

        PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(grantText);
        PeerInvitationOffer pending = PeerExchangeCodec.ImportInvitation(await File.ReadAllTextAsync(pendingPath, cancellationToken));
        bool expectedIssuer = pending.IssuerIdentity.DeviceId == grant.IssuerIdentity.DeviceId &&
                              string.Equals(pending.IssuerIdentity.SigningPublicKey, grant.IssuerIdentity.SigningPublicKey, StringComparison.Ordinal);
        if (grant.Certificate.ProjectId != project.Id ||
            grant.InvitationId != pending.Invitation.InvitationId ||
            !expectedIssuer ||
            !PeerExchangeCodec.VerifyGrant(grant))
        {
            throw new UnauthorizedAccessException("The membership grant is invalid.");
        }

        using FileDeviceIdentityStore localIdentity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        if (grant.Certificate.Device.DeviceId != localIdentity.Identity.DeviceId)
        {
            throw new UnauthorizedAccessException("The membership grant belongs to another device.");
        }

        string localGrantPath = GetLocalGrantPath(project.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(localGrantPath)!);
        await File.WriteAllTextAsync(localGrantPath, grantText, cancellationToken);
        File.Delete(pendingPath);
        using SyncthingApiClient api = new(profile.ApiEndpoint, profile.ApiKey);
        await api.PutDeviceAsync(new SyncthingDeviceConfiguration(
            grant.IssuerIdentity.SyncthingDeviceId,
            grant.IssuerIdentity.DisplayName), cancellationToken);
        await ConfigureFolderAsync(project, profile, [grant.IssuerIdentity.SyncthingDeviceId], cancellationToken);
        return grant.Certificate;
    }

    public async Task<IReadOnlyList<MembershipCertificate>> GetMembersAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, _, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        using FileDeviceIdentityStore identity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        return await CreateAdmissionService(project.Id, identity).GetMembersAsync(project.Id, cancellationToken);
    }

    public async Task RevokePeerAsync(
        Guid projectId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, SyncthingProfile profile, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        using FileDeviceIdentityStore identity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        JsonPeerAdmissionService admission = CreateAdmissionService(project.Id, identity);
        IReadOnlyList<MembershipCertificate> before = await admission.GetMembersAsync(project.Id, cancellationToken);
        MembershipCertificate member = before.FirstOrDefault(item => item.Device.DeviceId == deviceId)
                                       ?? throw new KeyNotFoundException("The peer is not an active member.");
        await admission.RevokeDeviceAsync(project.Id, deviceId, cancellationToken);
        File.Delete(Path.Combine(profile.ExchangeDirectory, "members", deviceId.ToString("N") + ".json"));
        using SyncthingApiClient api = new(profile.ApiEndpoint, profile.ApiKey);
        await api.DeleteDeviceAsync(member.Device.SyncthingDeviceId, cancellationToken);
        IReadOnlyList<MembershipCertificate> remaining = await admission.GetMembersAsync(project.Id, cancellationToken);
        await ConfigureFolderAsync(project, profile, remaining.Select(item => item.Device.SyncthingDeviceId).ToArray(), cancellationToken);
    }

    public async Task<GitPeerExchangeResult> ExchangeGitAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        (ProjectDefinition project, SyncthingProfile profile, ManagedSyncthingEngine engine) = await GetRunningSyncContextAsync(projectId, cancellationToken);
        if (!project.Features.GitEnabled)
        {
            throw new InvalidOperationException("Git is disabled for this project.");
        }

        using FileDeviceIdentityStore identity = await OpenLocalIdentityAsync(project, engine.DeviceId, cancellationToken);
        IReadOnlyCollection<DeviceIdentity> authorized = await GetAuthorizedDevicesAsync(project, profile, identity, cancellationToken);
        Guid? transaction = await _gitExchange.ExportAsync(
            project.Id,
            project.RootPath,
            profile.ExchangeDirectory,
            identity,
            cancellationToken);
        GitPeerExchangeResult imported = await _gitExchange.ImportAsync(
            project.Id,
            project.RootPath,
            profile.ExchangeDirectory,
            Path.Combine(_options.DataDirectory, "git-exchange-state", project.Id.ToString("N")),
            authorized,
            identity.Identity.DeviceId,
            cancellationToken);
        return imported with { ExportedTransactionId = transaction };
    }

    public async ValueTask DisposeAsync()
    {
        foreach ((_, ManagedSyncthingEngine engine) in _syncEngines)
        {
            await engine.DisposeAsync();
        }

        _syncEngines.Clear();
    }

    private async Task<ProjectDefinition> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await _catalog.FindByIdAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

    private async Task<(ProjectDefinition Project, SyncthingProfile Profile, ManagedSyncthingEngine Engine)> GetRunningSyncContextAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        ProjectDefinition project = await GetProjectAsync(projectId, cancellationToken);
        SyncthingProfile profile = await _syncProfiles.GetAsync(projectId, cancellationToken)
                                    ?? throw new InvalidOperationException("Syncthing is not configured for this project.");
        if (!_syncEngines.TryGetValue(projectId, out ManagedSyncthingEngine? engine) ||
            engine.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused))
        {
            throw new InvalidOperationException("The project Sync engine is not running.");
        }

        return (project, profile, engine);
    }

    private async Task ConfigureFolderAsync(
        ProjectDefinition project,
        SyncthingProfile profile,
        IReadOnlyCollection<string> deviceIds,
        CancellationToken cancellationToken)
    {
        string folderType = "sendreceive";
        string grantPath = GetLocalGrantPath(project.Id);
        if (File.Exists(grantPath))
        {
            PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(await File.ReadAllTextAsync(grantPath, cancellationToken));
            if (grant.Certificate.Role is PeerRole.ReadOnly or PeerRole.Backup or PeerRole.EncryptedArchive)
            {
                folderType = "receiveonly";
            }
        }

        using SyncthingApiClient api = new(profile.ApiEndpoint, profile.ApiKey);
        await api.PutFolderAsync(new SyncthingFolderConfiguration(
            profile.FolderId,
            project.Name,
            profile.ExchangeDirectory,
            deviceIds.Distinct(StringComparer.Ordinal).ToArray(),
            folderType,
            project.Features.BackupEnabled ? "simple" : string.Empty,
            project.Retention.MaxVersionsPerFile,
            project.Retention.MaximumAge is { } age ? Math.Max(1, (int)Math.Round(age.TotalDays)) : null), cancellationToken);
    }

    private async Task<IReadOnlyCollection<DeviceIdentity>> GetAuthorizedDevicesAsync(
        ProjectDefinition project,
        SyncthingProfile profile,
        IDeviceIdentityStore localIdentity,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, DeviceIdentity> devices = new()
        {
            [localIdentity.Identity.DeviceId] = localIdentity.Identity
        };
        JsonPeerAdmissionService admission = CreateAdmissionService(project.Id, localIdentity);
        foreach (MembershipCertificate member in (await admission.GetMembersAsync(project.Id, cancellationToken))
                     .Where(item => admission.VerifyCertificate(item) && CanWriteGit(item.Role)))
        {
            devices[member.Device.DeviceId] = member.Device;
        }

        string localGrantPath = GetLocalGrantPath(project.Id);
        if (File.Exists(localGrantPath))
        {
            PeerMembershipGrant localGrant = PeerExchangeCodec.ImportMembershipGrant(await File.ReadAllTextAsync(localGrantPath, cancellationToken));
            if (PeerExchangeCodec.VerifyGrant(localGrant))
            {
                devices[localGrant.IssuerIdentity.DeviceId] = localGrant.IssuerIdentity;
            }
        }

        string sharedMembersPath = Path.Combine(profile.ExchangeDirectory, "members");
        if (Directory.Exists(sharedMembersPath))
        {
            foreach (string path in Directory.EnumerateFiles(sharedMembersPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(await File.ReadAllTextAsync(path, cancellationToken));
                    bool knownIssuer = devices.TryGetValue(grant.IssuerIdentity.DeviceId, out DeviceIdentity? issuer) &&
                                       string.Equals(issuer.SigningPublicKey, grant.IssuerIdentity.SigningPublicKey, StringComparison.Ordinal);
                    if (grant.Certificate.ProjectId == project.Id &&
                        knownIssuer &&
                        CanWriteGit(grant.Certificate.Role) &&
                        PeerExchangeCodec.VerifyGrant(grant))
                    {
                        devices[grant.Certificate.Device.DeviceId] = grant.Certificate.Device;
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
                {
                    // A partially synchronized grant will be retried on the next exchange pass.
                }
            }
        }

        return devices.Values.ToArray();
    }

    private async Task<FileDeviceIdentityStore> OpenLocalIdentityAsync(
        ProjectDefinition project,
        string syncthingDeviceId,
        CancellationToken cancellationToken) =>
        await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(GetSecurityPath(project.Id), "local-device"),
            Environment.MachineName,
            syncthingDeviceId,
            cancellationToken: cancellationToken);

    private async Task<FileDeviceIdentityStore> OpenVpnIdentityAsync(
        VpnProjectProfile profile,
        CancellationToken cancellationToken) =>
        await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_options.VpnDirectory, "security", profile.ProjectId.ToString("N"), "local-device"),
            Environment.MachineName,
            "vpn:" + profile.PublicKey[..Math.Min(12, profile.PublicKey.Length)],
            cancellationToken: cancellationToken);

    private JsonPeerAdmissionService CreateAdmissionService(Guid projectId, IDeviceIdentityStore identity) =>
        new(Path.Combine(GetSecurityPath(projectId), "admission"), identity);

    private string GetSecurityPath(Guid projectId) =>
        Path.Combine(_options.DataDirectory, "security", "projects", projectId.ToString("N"));

    private string GetPendingInvitationPath(Guid projectId) =>
        Path.Combine(GetSecurityPath(projectId), "pending-invitation.json");

    private string GetLocalGrantPath(Guid projectId) =>
        Path.Combine(GetSecurityPath(projectId), "membership-grant.json");

    private static bool CanWriteGit(PeerRole role) =>
        role is PeerRole.Owner or PeerRole.Administrator or PeerRole.Contributor;

    private FileSystemBackupService CreateBackupService(ProjectDefinition project) =>
        new(new BackupStoreOptions(project.BackupStorePath ?? Path.Combine(_options.BackupDirectory, project.Id.ToString("N"))));

    private string ResolveExchangeDirectory(ProjectDefinition project) =>
        project.Features.GitEnabled
            ? Path.Combine(_options.DataDirectory, "git-exchange", project.Id.ToString("N"))
            : project.RootPath;

    private static string SanitizeDirectoryName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string value = new(name.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
    }
}
