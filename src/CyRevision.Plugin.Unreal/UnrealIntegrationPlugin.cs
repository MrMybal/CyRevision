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
        "0.1.0",
        "Optional Unreal project integration and Editor plugin installer.",
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
        string? payload = ResolveBundledPluginDirectory(context);
        if (payload is not null)
        {
            _installer = new UnrealProjectPluginInstaller(payload);
        }

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

    public async ValueTask DisposeAsync()
    {
        if (_bridge is not null)
        {
            _bridge.ProjectChanged -= OnProjectChanged;
            await _bridge.DisposeAsync();
        }
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
        return new FilePresentationResult(
            ProviderId,
            FilePresentationKind.Metadata,
            equivalent ? "Unreal packages appear equivalent" : "Unreal package metadata changed",
            text);
    }

    private void OnProjectChanged(object? sender, UnrealProjectChangedEventArgs eventArgs) =>
        ProjectChanged?.Invoke(this, eventArgs);

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
