using System.Security.Cryptography;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

public sealed class UnrealIntegrationPlugin : IUnrealIntegrationPlugin
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
}
