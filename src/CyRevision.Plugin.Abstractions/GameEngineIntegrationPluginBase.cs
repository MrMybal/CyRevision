using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Plugin.Abstractions;

public abstract class GameEngineIntegrationPluginBase : IGameEngineIntegrationPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, GameEngineBridgeRegistration> _connections = new(StringComparer.OrdinalIgnoreCase);
    private CyRevisionPluginContext? _context;
    private LocalGameEngineBridgeServer? _bridge;
    private string? _payloadDirectory;

    public abstract CyRevisionPluginDescriptor Descriptor { get; }

    public abstract GameEngineKind Engine { get; }

    protected abstract int BridgePort { get; }

    protected abstract string ConnectionsDirectoryName { get; }

    public event EventHandler<GameEngineProjectChangedEventArgs>? ProjectChanged;

    public GameEngineBridgeStatus BridgeStatus { get; private set; } = new(
        GameEngineKind.Unity,
        false,
        string.Empty,
        0,
        "Bridge is stopped.");

    public async Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _payloadDirectory = ResolvePayloadDirectory(context);
        await LoadConnectionsAsync(cancellationToken);
        _bridge = new LocalGameEngineBridgeServer(BridgePort, context.ApplicationVersion, Engine, () => _connections.Values.ToArray());
        _bridge.ProjectChanged += OnProjectChanged;
        string? error = _bridge.Start();
        BridgeStatus = new GameEngineBridgeStatus(
            Engine,
            error is null,
            _bridge.Endpoint,
            _connections.Count,
            error is null ? $"Loopback bridge is ready for {Engine}." : $"Bridge could not start: {error}");
    }

    public GameEngineProjectInspection InspectProject(string path) => InspectProjectCore(path, _payloadDirectory);

    public async Task<GameEnginePluginInstallationResult> InstallOrUpdateEditorPluginAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default)
    {
        GameEnginePluginInstallationResult result = await Task.Run(
            () => InstallEditorPlugin(projectPath, _payloadDirectory, cancellationToken),
            cancellationToken);
        if (result.Succeeded)
            await ConfigureProjectConnectionAsync(result.ProjectRoot, cyRevisionExecutablePath, cancellationToken);
        return result;
    }

    public async Task<GameEngineBridgeStatus> ConfigureProjectConnectionAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default)
    {
        GameEngineProjectInspection inspection = InspectProject(projectPath);
        if (!inspection.IsValid) throw new InvalidOperationException(inspection.Summary);
        string root = NormalizePath(inspection.ProjectRoot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            GameEngineBridgeRegistration registration = _connections.TryGetValue(root, out GameEngineBridgeRegistration? current)
                ? current with { ExecutablePath = cyRevisionExecutablePath }
                : new GameEngineBridgeRegistration(root, CreateToken(), cyRevisionExecutablePath);
            _connections[root] = registration;
            await SaveConnectionsCoreAsync(cancellationToken);
            WriteBridgeSettings(root, BridgeStatus.Endpoint, registration.Token, registration.ExecutablePath);
        }
        finally
        {
            _gate.Release();
        }
        BridgeStatus = BridgeStatus with
        {
            AuthorizedProjectCount = _connections.Count,
            Detail = BridgeStatus.IsRunning ? $"Bridge is ready for {Engine}." : BridgeStatus.Detail
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

    protected abstract string? ResolvePayloadDirectory(CyRevisionPluginContext context);

    protected abstract GameEngineProjectInspection InspectProjectCore(string path, string? payloadDirectory);

    protected abstract GameEnginePluginInstallationResult InstallEditorPlugin(
        string projectPath,
        string? payloadDirectory,
        CancellationToken cancellationToken);

    protected abstract void WriteBridgeSettings(string projectRoot, string endpoint, string token, string executablePath);

    protected static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private void OnProjectChanged(object? sender, GameEngineProjectChangedEventArgs eventArgs) =>
        ProjectChanged?.Invoke(this, eventArgs);

    private async Task LoadConnectionsAsync(CancellationToken cancellationToken)
    {
        string path = GetConnectionsPath();
        if (!File.Exists(path)) return;
        await using FileStream stream = File.OpenRead(path);
        GameEngineBridgeRegistration[] registrations =
            await JsonSerializer.DeserializeAsync<GameEngineBridgeRegistration[]>(stream, JsonOptions, cancellationToken) ?? [];
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
            await JsonSerializer.SerializeAsync(stream, _connections.Values.ToArray(), JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
    }

    private string GetConnectionsPath() => Path.Combine(
        _context?.ConfigurationDirectory ?? throw new InvalidOperationException("Plugin is not initialized."),
        "plugins",
        ConnectionsDirectoryName,
        "connections.json");

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
