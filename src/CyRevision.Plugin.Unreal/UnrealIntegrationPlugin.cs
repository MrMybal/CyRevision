using System.Security.Cryptography;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

public sealed class UnrealIntegrationPlugin : IUnrealIntegrationPlugin, IFilePresentationProvider
{
    private const int BridgePort = 47832;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _sourceOverride;
    private Dictionary<string, UnrealConnectionRegistration> _connections = new(StringComparer.OrdinalIgnoreCase);
    private CyRevisionPluginContext? _context;
    private UnrealProjectPluginInstaller? _installer;
    private UnrealBuildService? _buildService;
    private UnrealAssetInspectionService? _assetInspectionService;
    private UnrealBridgeServer? _bridge;

    public UnrealIntegrationPlugin()
    {
    }

    public UnrealIntegrationPlugin(string sourceOverride)
    {
        _sourceOverride = sourceOverride;
    }

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.unreal",
        "Unreal Engine Integration",
        "0.4.0",
        "Optional Unreal project integration, headless asset previews, Editor plugin installer and multi-version build lab.",
        "Game engines");

    public string ProviderId => Descriptor.Id + ".files";

    public int Priority => 100;

    public event EventHandler<UnrealProjectChangedEventArgs>? ProjectChanged;

    public UnrealBridgeStatus BridgeStatus { get; private set; } = new(
        false,
        $"http://127.0.0.1:{BridgePort}/cyrevision/v1/",
        0,
        "Bridge is stopped.");

    public async Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _buildService = new UnrealBuildService(context.ConfigurationDirectory, context.DataDirectory);
        string? payload = ResolveBundledPluginDirectory(context);
        if (payload is not null)
        {
            _installer = new UnrealProjectPluginInstaller(payload);
        }
        _assetInspectionService = new UnrealAssetInspectionService(
            context.ConfigurationDirectory,
            _buildService,
            InspectProject);

        await LoadConnectionsAsync(cancellationToken);
        _bridge = new UnrealBridgeServer(BridgePort, context.ApplicationVersion, () => _connections.Values.ToArray());
        _bridge.ProjectChanged += OnProjectChanged;
        string? error = _bridge.Start();
        BridgeStatus = new UnrealBridgeStatus(
            error is null,
            _bridge.Endpoint,
            _connections.Count,
            error is null ? "Loopback bridge is listening." : $"Bridge could not start: {error}");
    }

    public UnrealProjectInspection InspectProject(string path) => _installer?.Inspect(path) ?? new UnrealProjectInspection(
        false,
        path,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        UnrealProjectKind.Unknown,
        UnrealPluginInstallMode.Unavailable,
        false,
        "Compatibility cannot be evaluated because the Unreal payload is missing.",
        UnrealProjectPluginInstaller.SupportedEngineVersions,
        UnrealProjectPluginInstaller.CurrentPlatform,
        [],
        false,
        null,
        null,
        false,
        "The installed CyRevision package does not contain the CyRevisionUnreal payload.");

    public async Task<UnrealPluginInstallationResult> InstallOrUpdateEditorPluginAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default)
    {
        if (_installer is null)
        {
            return new UnrealPluginInstallationResult(
                false, projectPath, string.Empty, null, string.Empty,
                "The installed CyRevision package does not contain the CyRevisionUnreal payload.");
        }

        UnrealPluginInstallationResult result = await Task.Run(
            () => _installer.InstallOrUpdate(projectPath, cancellationToken),
            cancellationToken);
        if (result.Succeeded)
        {
            await ConfigureProjectConnectionAsync(result.ProjectRoot, cyRevisionExecutablePath, cancellationToken);
        }
        return result;
    }

    public async Task<UnrealBridgeStatus> ConfigureProjectConnectionAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default)
    {
        UnrealProjectInspection inspection = InspectProject(projectPath);
        if (!inspection.IsValid)
        {
            throw new InvalidOperationException(inspection.Summary);
        }

        string root = NormalizePath(inspection.ProjectRoot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            UnrealConnectionRegistration registration = _connections.TryGetValue(root, out UnrealConnectionRegistration? current)
                ? current with { ExecutablePath = cyRevisionExecutablePath }
                : new UnrealConnectionRegistration(root, CreateToken(), cyRevisionExecutablePath);
            _connections[root] = registration;
            await SaveConnectionsCoreAsync(cancellationToken);
            UnrealProjectPluginInstaller.WriteBridgeSettings(
                root,
                BridgeStatus.Endpoint,
                registration.Token,
                registration.ExecutablePath);
        }
        finally
        {
            _gate.Release();
        }

        BridgeStatus = BridgeStatus with
        {
            AuthorizedProjectCount = _connections.Count,
            Detail = BridgeStatus.IsRunning ? "Bridge is ready for Unreal Editor." : BridgeStatus.Detail
        };
        return BridgeStatus;
    }

    public Task<UnrealBuildDiscovery> DiscoverBuildEnvironmentAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        GetBuildService().DiscoverAsync(projectPath, cancellationToken);

    public Task<UnrealBuildDiscovery> RefreshBuildEnvironmentAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        GetBuildService().DiscoverAsync(projectPath, cancellationToken, forceRefresh: true);

    public Task<UnrealAssetInspectionOptions> LoadAssetInspectionOptionsAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        GetAssetInspectionService().LoadOptionsAsync(projectPath, cancellationToken);

    public Task SaveAssetInspectionOptionsAsync(
        string projectPath,
        UnrealAssetInspectionOptions options,
        CancellationToken cancellationToken = default) =>
        GetAssetInspectionService().SaveOptionsAsync(projectPath, options, cancellationToken);

    public Task<UnrealAssetInspectionCacheStatus> GetAssetInspectionCacheStatusAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        GetAssetInspectionService().GetCacheStatusAsync(projectPath, cancellationToken);

    public Task<UnrealAssetInspectionCacheStatus> ClearAssetInspectionCacheAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        GetAssetInspectionService().ClearCacheAsync(projectPath, cancellationToken);

    public Task<UnrealBuildProfile?> LoadBuildProfileAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetBuildService().LoadProfileAsync(projectId, cancellationToken);

    public Task SaveBuildProfileAsync(
        UnrealBuildProfile profile,
        CancellationToken cancellationToken = default) =>
        GetBuildService().SaveProfileAsync(profile, cancellationToken);

    public Task<IReadOnlyList<UnrealBuildProfile>> LoadBuildPresetsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetBuildService().LoadPresetsAsync(projectId, cancellationToken);

    public Task SaveBuildPresetAsync(
        UnrealBuildProfile profile,
        CancellationToken cancellationToken = default) =>
        GetBuildService().SavePresetAsync(profile, cancellationToken);

    public Task DeleteBuildPresetAsync(
        Guid projectId,
        string presetName,
        CancellationToken cancellationToken = default) =>
        GetBuildService().DeletePresetAsync(projectId, presetName, cancellationToken);

    public Task<UnrealBuildResult> RunBuildAsync(
        UnrealBuildRequest request,
        IProgress<UnrealBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        GetBuildService().RunAsync(request, progress, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_bridge is not null)
        {
            _bridge.ProjectChanged -= OnProjectChanged;
            await _bridge.DisposeAsync();
        }
        _assetInspectionService?.Dispose();
        _gate.Dispose();
        BridgeStatus = BridgeStatus with { IsRunning = false, Detail = "Bridge is stopped." };
    }

    public bool CanPreview(FilePreviewRequest request) => IsUnrealPackage(request.FilePath);

    public bool CanCompare(FileDiffRequest request) =>
        IsUnrealPackage(request.CandidatePath) &&
        string.Equals(
            Path.GetExtension(request.BaselinePath),
            Path.GetExtension(request.CandidatePath),
            StringComparison.OrdinalIgnoreCase);

    public async Task<FilePresentationResult?> CreatePreviewAsync(
        FilePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanPreview(request)) return null;
        if (_assetInspectionService is not null)
        {
            try
            {
                FilePresentationResult? advanced = await _assetInspectionService
                    .TryCreatePreviewAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (advanced is not null) return advanced;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A headless Unreal timeout should not prevent the lightweight offline preview.
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // Keep the provider useful when the engine is missing or an asset cannot be loaded.
            }
        }
        UnrealPackageFingerprint fingerprint = await InspectPackageAsync(request.FilePath, cancellationToken);
        string text = string.Join(Environment.NewLine,
            "Unreal package preview (offline)",
            $"File: {request.RelativePath}",
            $"Kind: {fingerprint.Kind}",
            $"Size: {fingerprint.Size:N0} bytes",
            $"Header signature: {fingerprint.HeaderSignature}",
            $"Detected names: {string.Join(", ", fingerprint.Names.Take(40))}",
            string.Empty,
            "Open the project in Unreal Editor for a rendered asset preview.");
        return new FilePresentationResult(
            ProviderId,
            FilePresentationKind.Metadata,
            $"Unreal plugin · {fingerprint.Kind} · {fingerprint.Size:N0} bytes",
            text,
            Metadata: new Dictionary<string, string>
            {
                ["Kind"] = fingerprint.Kind,
                ["Size"] = fingerprint.Size.ToString(),
                ["Header"] = fingerprint.HeaderSignature
            });
    }

    public async Task<FilePresentationResult?> CreateDiffAsync(
        FileDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanCompare(request)) return null;
        if (_assetInspectionService is not null)
        {
            try
            {
                FilePresentationResult? semantic = await _assetInspectionService
                    .TryCreateDiffAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (semantic is not null) return semantic;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Fall back to the fast offline package fingerprint when headless inspection times out.
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
            {
                // A malformed or unsupported package must not break the universal diff surface.
            }
        }
        Task<UnrealPackageFingerprint> baselineTask = InspectPackageAsync(request.BaselinePath, cancellationToken);
        Task<UnrealPackageFingerprint> candidateTask = InspectPackageAsync(request.CandidatePath, cancellationToken);
        await Task.WhenAll(baselineTask, candidateTask);
        UnrealPackageFingerprint baseline = baselineTask.Result;
        UnrealPackageFingerprint candidate = candidateTask.Result;
        string[] addedNames = candidate.Names.Except(baseline.Names, StringComparer.Ordinal).Take(100).ToArray();
        string[] removedNames = baseline.Names.Except(candidate.Names, StringComparer.Ordinal).Take(100).ToArray();
        bool equivalent = baseline.Size == candidate.Size &&
                          baseline.HeaderSignature == candidate.HeaderSignature &&
                          addedNames.Length == 0 && removedNames.Length == 0;
        string text = string.Join(Environment.NewLine,
            "Unreal package diff (offline)",
            $"File: {request.RelativePath}",
            $"Size: {baseline.Size:N0} → {candidate.Size:N0} bytes ({candidate.Size - baseline.Size:+#;-#;0})",
            $"Header: {baseline.HeaderSignature} → {candidate.HeaderSignature}",
            $"Detected names added: {(addedNames.Length == 0 ? "none" : string.Join(", ", addedNames))}",
            $"Detected names removed: {(removedNames.Length == 0 ? "none" : string.Join(", ", removedNames))}",
            string.Empty,
            "This comparison is provided by the enabled Unreal plugin without opening the engine.");

        FilePresentationResult? renderedCandidate = null;
        if (_assetInspectionService is not null)
        {
            try
            {
                FileInfo candidateInfo = new(request.CandidatePath);
                renderedCandidate = await _assetInspectionService.TryCreatePreviewAsync(
                        new FilePreviewRequest(
                            request.ProjectRoot,
                            request.RelativePath,
                            request.CandidatePath,
                            candidateInfo.Exists ? candidateInfo.Length : 0),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) when (exception is IOException or InvalidOperationException) { }
        }

        if (renderedCandidate is { Kind: FilePresentationKind.Image, ImagePath: not null })
        {
            return new FilePresentationResult(
                ProviderId,
                FilePresentationKind.Image,
                equivalent ? "Unreal packages appear equivalent · rendered candidate" : "Unreal package changed · rendered candidate",
                text + Environment.NewLine + Environment.NewLine + renderedCandidate.TextContent,
                renderedCandidate.ImagePath,
                renderedCandidate.Metadata);
        }
        return new FilePresentationResult(
            ProviderId,
            FilePresentationKind.Metadata,
            equivalent ? "Unreal packages appear equivalent" : "Unreal package metadata changed",
            text);
    }

    private void OnProjectChanged(object? sender, UnrealProjectChangedEventArgs eventArgs) =>
        ProjectChanged?.Invoke(this, eventArgs);

    private UnrealBuildService GetBuildService() =>
        _buildService ?? throw new InvalidOperationException("Unreal integration plugin is not initialized.");

    private UnrealAssetInspectionService GetAssetInspectionService() =>
        _assetInspectionService ?? throw new InvalidOperationException("Unreal asset inspection service is not initialized.");

    private async Task LoadConnectionsAsync(CancellationToken cancellationToken)
    {
        string path = GetConnectionsPath();
        if (!File.Exists(path))
        {
            return;
        }

        await using FileStream stream = File.OpenRead(path);
        UnrealConnectionRegistration[] registrations =
            await JsonSerializer.DeserializeAsync<UnrealConnectionRegistration[]>(stream, JsonOptions, cancellationToken) ?? [];
        _connections = registrations.ToDictionary(
            registration => NormalizePath(registration.ProjectRoot),
            registration => registration,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveConnectionsCoreAsync(CancellationToken cancellationToken)
    {
        string path = GetConnectionsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, _connections.Values.ToArray(), JsonOptions, cancellationToken);
        }
        File.Move(temporary, path, true);
    }

    private string GetConnectionsPath() => Path.Combine(
        _context?.ConfigurationDirectory ?? throw new InvalidOperationException("Plugin is not initialized."),
        "plugins",
        "unreal",
        "connections.json");

    private string? ResolveBundledPluginDirectory(CyRevisionPluginContext context)
    {
        string[] candidates =
        [
            _sourceOverride ?? string.Empty,
            Path.Combine(context.ApplicationDirectory, "PluginPayloads", "Unreal", "CyRevisionUnreal"),
            Path.GetFullPath(Path.Combine(context.PackageDirectory, "..", "..", "PluginPayloads", "Unreal", "CyRevisionUnreal")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "plugins", "CyRevisionUnreal"))
        ];
        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, "CyRevisionUnreal.uplugin")));
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static bool IsUnrealPackage(string path) =>
        Path.GetExtension(path).Equals(".uasset", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".umap", StringComparison.OrdinalIgnoreCase);

    private static async Task<UnrealPackageFingerprint> InspectPackageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        if (!info.Exists) throw new FileNotFoundException("Unreal package not found.", path);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int length = (int)Math.Min(stream.Length, 2 * 1024 * 1024);
        byte[] bytes = new byte[length];
        int read = await stream.ReadAsync(bytes.AsMemory(0, length), cancellationToken);
        string header = Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, read)));
        HashSet<string> names = ExtractReadableNames(bytes.AsSpan(0, read));
        string kind = Path.GetExtension(path).Equals(".umap", StringComparison.OrdinalIgnoreCase)
            ? "Unreal map"
            : "Unreal asset";
        return new UnrealPackageFingerprint(kind, info.Length, header, names);
    }

    private static HashSet<string> ExtractReadableNames(ReadOnlySpan<byte> bytes)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        List<char> current = [];
        foreach (byte value in bytes)
        {
            if (value is >= 32 and <= 126)
            {
                current.Add((char)value);
                continue;
            }
            if (current.Count is >= 4 and <= 120)
            {
                string candidate = new(current.ToArray());
                if (candidate.Any(char.IsLetter) && candidate.All(character => char.IsLetterOrDigit(character) || "_./:- ".Contains(character)))
                    names.Add(candidate);
            }
            current.Clear();
            if (names.Count >= 500) break;
        }
        if (current.Count is >= 4 and <= 120)
        {
            string candidate = new(current.ToArray());
            if (candidate.Any(char.IsLetter) &&
                candidate.All(character => char.IsLetterOrDigit(character) || "_./:- ".Contains(character)))
                names.Add(candidate);
        }
        return names;
    }

    private sealed record UnrealPackageFingerprint(
        string Kind,
        long Size,
        string HeaderSignature,
        IReadOnlySet<string> Names);
}
