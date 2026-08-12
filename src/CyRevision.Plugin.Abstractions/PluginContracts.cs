namespace CyRevision.Plugin.Abstractions;

public sealed record CyRevisionPluginDescriptor(
    string Id,
    string Name,
    string Version,
    string Description,
    string Category);

public sealed record CyRevisionPluginContext(
    string ApplicationDirectory,
    string PackageDirectory,
    string ConfigurationDirectory,
    string DataDirectory,
    string ApplicationVersion);

public interface ICyRevisionPlugin : IAsyncDisposable
{
    CyRevisionPluginDescriptor Descriptor { get; }

    Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default);
}

public sealed record UnrealProjectInspection(
    bool IsValid,
    string ProjectRoot,
    string ProjectFile,
    string ProjectName,
    bool IsEditorPluginInstalled,
    string? InstalledPluginVersion,
    string? BundledPluginVersion,
    bool UpdateAvailable,
    string Summary);

public sealed record UnrealPluginInstallationResult(
    bool Succeeded,
    string ProjectRoot,
    string DestinationDirectory,
    string? BackupDirectory,
    string InstalledVersion,
    string Message);

public sealed record UnrealBridgeStatus(
    bool IsRunning,
    string Endpoint,
    int AuthorizedProjectCount,
    string Detail);

public sealed class UnrealProjectChangedEventArgs(string projectRoot, string action) : EventArgs
{
    public string ProjectRoot { get; } = projectRoot;

    public string Action { get; } = action;
}

public interface IUnrealIntegrationPlugin : ICyRevisionPlugin
{
    event EventHandler<UnrealProjectChangedEventArgs>? ProjectChanged;

    UnrealBridgeStatus BridgeStatus { get; }

    UnrealProjectInspection InspectProject(string path);

    Task<UnrealPluginInstallationResult> InstallOrUpdateEditorPluginAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default);

    Task<UnrealBridgeStatus> ConfigureProjectConnectionAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default);
}

[Flags]
public enum AiWorkspacePermission
{
    None = 0,
    ReadRepository = 1,
    ModifyFiles = 2,
    StageChanges = 4,
    CreateCommit = 8,
    NetworkAccess = 16
}

public enum AiProviderKind
{
    CodexCli,
    OpenAiApi,
    OpenAiCompatibleApi,
    CodexLocalProvider
}

public sealed record AiProviderDescriptor(
    string Id,
    string Name,
    AiProviderKind Kind,
    string DefaultModel,
    string DefaultEndpoint,
    string LocalProvider,
    bool SupportsWorkspaceEdits,
    bool RequiresApiKey,
    string Description);

public sealed record AiAgentRequest(
    string RepositoryPath,
    string Prompt,
    string Context,
    AiProviderDescriptor Provider,
    string Model,
    string Endpoint,
    string ExecutablePath,
    string? ApiKey,
    AiWorkspacePermission Permissions,
    AiMcpProjectProfile? McpProfile = null);

public sealed record AiAgentResult(
    bool Succeeded,
    string Response,
    string Diagnostic,
    int ExitCode,
    TimeSpan Duration);

public interface IAiIntegrationPlugin : ICyRevisionPlugin
{
    IReadOnlyList<AiProviderDescriptor> Providers { get; }

    Task<AiAgentResult> RunAsync(
        AiAgentRequest request,
        CancellationToken cancellationToken = default);

    Task<AiMcpProjectProfile> GetMcpProfileAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task SaveMcpProfileAsync(
        AiMcpProjectProfile profile,
        CancellationToken cancellationToken = default);
}

public enum AiMcpTransport
{
    Stdio,
    StreamableHttp
}

public enum AiMcpApprovalMode
{
    Auto,
    Prompt,
    Writes,
    Approve
}

public enum AiMcpCapability
{
    ReadOnly,
    ReadWrite
}

public enum AiMcpHttpAuth
{
    None,
    OAuth,
    ChatGpt
}

public sealed record AiMcpServerConfiguration(
    string Id,
    string Name,
    AiMcpTransport Transport,
    bool Enabled,
    bool Required,
    AiMcpCapability Capability,
    bool RequiresNetwork,
    string Command,
    string Arguments,
    string WorkingDirectory,
    string EnvironmentVariables,
    string ForwardEnvironmentVariables,
    string Url,
    string BearerTokenEnvironmentVariable,
    string HttpHeaders,
    string EnvironmentHttpHeaders,
    AiMcpHttpAuth HttpAuth,
    string OAuthScopes,
    string OAuthResource,
    string EnabledTools,
    string DisabledTools,
    string ToolApprovalOverrides,
    AiMcpApprovalMode ApprovalMode,
    int StartupTimeoutSeconds,
    int ToolTimeoutSeconds);

public sealed record AiMcpProjectProfile(
    Guid ProjectId,
    bool Enabled,
    bool EmergencyBlocked,
    bool BlockUnmanagedServers,
    IReadOnlyList<AiMcpServerConfiguration> Servers,
    DateTimeOffset UpdatedAt)
{
    public static AiMcpProjectProfile CreateDefault(Guid projectId) =>
        new(projectId, false, false, true, [], DateTimeOffset.UtcNow);
}
