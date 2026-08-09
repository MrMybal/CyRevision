using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Security;

public sealed class JsonPeerAdmissionService : IPeerAdmissionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _storePath;
    private readonly IDeviceIdentityStore _administratorIdentity;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPeerAdmissionService(string storePath, IDeviceIdentityStore administratorIdentity)
    {
        _storePath = Path.GetFullPath(storePath);
        _administratorIdentity = administratorIdentity;
    }

    public async Task<PeerInvitationPackage> CreateInvitationAsync(
        Guid projectId,
        PeerRole role,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "An invitation must live between a moment and seven days.");
        }

        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Base64UrlEncode(tokenBytes);
        string tokenHash = HashToken(token);
        string verificationCode = CreateVerificationCode(tokenBytes);
        PeerInvitation invitation = new(
            Guid.NewGuid(),
            projectId,
            role,
            DateTimeOffset.UtcNow + lifetime,
            tokenHash,
            _administratorIdentity.Identity.UserId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(InvitationsPath);
            await WriteJsonAtomicallyAsync(GetInvitationPath(invitation.InvitationId), invitation, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return new PeerInvitationPackage(invitation, token, verificationCode, _administratorIdentity.Identity);
    }

    public async Task<MembershipCertificate> ApproveDeviceAsync(
        PeerInvitation invitation,
        string oneTimeToken,
        DeviceIdentity device,
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string invitationPath = GetInvitationPath(invitation.InvitationId);
            PeerInvitation stored = await ReadJsonAsync<PeerInvitation>(invitationPath, cancellationToken)
                                    ?? throw new InvalidOperationException("The invitation is unknown or already used.");
            if (stored != invitation || stored.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("The invitation is invalid or expired.");
            }

            byte[] tokenBytes;
            try
            {
                tokenBytes = Base64UrlDecode(oneTimeToken);
            }
            catch (FormatException)
            {
                throw new UnauthorizedAccessException("The invitation token is invalid.");
            }

            if (!FixedTimeEquals(stored.OneTimeTokenHash, HashToken(oneTimeToken)) ||
                !FixedTimeEquals(CreateVerificationCode(tokenBytes), verificationCode.Trim()))
            {
                throw new UnauthorizedAccessException("The invitation token or verification code is invalid.");
            }

            List<MembershipCertificate> members = await ReadMembersAsync(stored.ProjectId, cancellationToken);
            if (members.Any(member => member.Device.DeviceId == device.DeviceId ||
                                      string.Equals(member.Device.SyncthingDeviceId, device.SyncthingDeviceId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("This device is already a member of the project.");
            }

            long nextEpoch = members.Count == 0 ? 1 : members.Max(member => member.MembershipEpoch) + 1;
            DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
            byte[] payload = BuildCertificatePayload(stored.ProjectId, device, stored.Role, nextEpoch, issuedAt);
            MembershipCertificate certificate = new(
                stored.ProjectId,
                device,
                stored.Role,
                nextEpoch,
                issuedAt,
                _administratorIdentity.Sign(payload));
            members.Add(certificate);
            await WriteJsonAtomicallyAsync(GetMembersPath(stored.ProjectId), members, cancellationToken);
            File.Delete(invitationPath);
            return certificate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevokeDeviceAsync(Guid projectId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<MembershipCertificate> members = await ReadMembersAsync(projectId, cancellationToken);
            members.RemoveAll(member => member.Device.DeviceId == deviceId);
            await WriteJsonAtomicallyAsync(GetMembersPath(projectId), members, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MembershipCertificate>> GetMembersAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadMembersAsync(projectId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool VerifyCertificate(MembershipCertificate certificate)
    {
        byte[] payload = BuildCertificatePayload(
            certificate.ProjectId,
            certificate.Device,
            certificate.Role,
            certificate.MembershipEpoch,
            certificate.IssuedAt);
        return _administratorIdentity.Verify(payload, certificate.AdministratorSignature);
    }

    private string InvitationsPath => Path.Combine(_storePath, "invitations");

    private string MembersPath => Path.Combine(_storePath, "members");

    private string GetInvitationPath(Guid invitationId) => Path.Combine(InvitationsPath, invitationId.ToString("N") + ".json");

    private string GetMembersPath(Guid projectId) => Path.Combine(MembersPath, projectId.ToString("N") + ".json");

    private async Task<List<MembershipCertificate>> ReadMembersAsync(Guid projectId, CancellationToken cancellationToken) =>
        await ReadJsonAsync<List<MembershipCertificate>>(GetMembersPath(projectId), cancellationToken) ?? [];

    private static byte[] BuildCertificatePayload(
        Guid projectId,
        DeviceIdentity device,
        PeerRole role,
        long epoch,
        DateTimeOffset issuedAt) =>
        Encoding.UTF8.GetBytes(string.Join('\n',
            projectId.ToString("N"),
            device.UserId.ToString("N"),
            device.DeviceId.ToString("N"),
            device.DisplayName,
            device.SyncthingDeviceId,
            device.SigningPublicKey,
            role.ToString(),
            epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            issuedAt.ToUniversalTime().ToString("O")));

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string CreateVerificationCode(byte[] token)
    {
        byte[] hash = SHA256.HashData(token);
        uint value = BitConverter.ToUInt32(hash, 0) % 1_000_000;
        return value.ToString("D6");
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
