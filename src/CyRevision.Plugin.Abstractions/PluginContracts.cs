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

/// <summary>
/// UI-neutral feature switches requested by a plugin-owned project mode. The host maps
/// these values to its current project configuration instead of making plugins reference
/// CyRevision.Core or Avalonia.
/// </summary>
public sealed record PluginProjectModeFeatures(
    bool GitEnabled,
    bool LfsEnabled,
    bool PeerSyncEnabled,
    bool BackupEnabled,
    bool StandardGitRemoteEnabled);

public enum PluginProjectModeRetentionKind
{
    CurrentStateOnly,
    Timeline,
    Permanent
}

public sealed record PluginProjectModeRetention(
    PluginProjectModeRetentionKind Kind,
    int? MaxVersionsPerFile = null,
    int? MaximumAgeDays = null,
    long? StorageBudgetBytes = null);

/// <summary>
/// Describes an operating mode contributed by an optional plugin. WorkspaceTabIds refer
/// to host surfaces registered by that plugin package (for example LoreWorkspaceTab).
/// The mode identifier only needs to be unique inside its provider plugin.
/// </summary>
public sealed record PluginProjectModeDescriptor(
    string Id,
    string Name,
    string Description,
    PluginProjectModeFeatures Features,
    PluginProjectModeRetention Retention,
    IReadOnlyList<string> WorkspaceTabIds,
    string CategoryLabel);

public sealed record PluginProjectModeContext(
    Guid ProjectId,
    string ProjectName,
    string ProjectRoot,
    IReadOnlyCollection<string> EnabledPluginIds);

public sealed record PluginProjectModeAvailability(
    bool IsAvailable,
    string Summary);

/// <summary>
/// Optional plugin capability used to add complete project operating modes. Modes are
/// project-scoped: the host only exposes them while their provider plugin is enabled for
/// the selected project, and the provider remains responsible for compatibility checks.
/// </summary>
public interface IProjectModeProvider : ICyRevisionPlugin
{
    IReadOnlyList<PluginProjectModeDescriptor> ProjectModes { get; }

    PluginProjectModeAvailability EvaluateProjectMode(
        string modeId,
        PluginProjectModeContext context);
}

public enum FilePresentationKind
{
    Text,
    Image,
    Metadata
}

public sealed record FilePreviewRequest(
    string ProjectRoot,
    string RelativePath,
    string FilePath,
    long FileSize);

public sealed record FileDiffRequest(
    string ProjectRoot,
    string RelativePath,
    string BaselinePath,
    string CandidatePath);

public sealed record FilePresentationResult(
    string ProviderId,
    FilePresentationKind Kind,
    string Summary,
    string TextContent = "",
    string? ImagePath = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Optional capability implemented by a CyRevision plugin. The desktop asks these providers
/// everywhere a file preview or diff is displayed, so support is not tied to a single tab.
/// Results stay UI-neutral: plugins never need to reference Avalonia.
/// </summary>
public interface IFilePresentationProvider
{
    string ProviderId { get; }

    int Priority => 0;

    bool CanPreview(FilePreviewRequest request);

    bool CanCompare(FileDiffRequest request);

    Task<FilePresentationResult?> CreatePreviewAsync(
        FilePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<FilePresentationResult?> CreateDiffAsync(
        FileDiffRequest request,
        CancellationToken cancellationToken = default);
}

public enum UnrealProjectKind
{
    Unknown,
    BlueprintOnly,
    Cpp
}

public enum UnrealPluginInstallMode
{
    Unavailable,
    Source,
    Precompiled
}

public static class UnrealPluginCompatibility
{
    public static IReadOnlyList<string> SupportedEngineVersions { get; } =
    [
        "4.27", "5.0", "5.1", "5.2", "5.3", "5.4", "5.5", "5.6", "5.7", "5.8"
    ];
}

public sealed record UnrealProjectInspection(
    bool IsValid,
    string ProjectRoot,
    string ProjectFile,
    string ProjectName,
    string EngineAssociation,
    string? EngineVersion,
    UnrealProjectKind ProjectKind,
    UnrealPluginInstallMode InstallMode,
    bool IsCompatible,
    string CompatibilityStatus,
    IReadOnlyList<string> SupportedEngineVersions,
    string PrecompiledPlatform,
    IReadOnlyList<string> AvailablePrecompiledVersions,
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

/// <summary>
/// Per-project settings for the optional Unreal asset inspector. The feature is deliberately
/// disabled by default because starting an Unreal commandlet is more expensive than the
/// lightweight package fingerprint used by the regular preview provider.
/// </summary>
public sealed record UnrealAssetInspectionOptions(
    bool Enabled,
    int PreviewResolution,
    long CacheBudgetBytes,
    bool RenderMeshThumbnails,
    int CommandTimeoutSeconds)
{
    public static UnrealAssetInspectionOptions Default { get; } = new(
        false,
        512,
        2L * 1024 * 1024 * 1024,
        true,
        180);
}

public sealed record UnrealAssetInspectionCacheStatus(
    string CacheDirectory,
    long SizeBytes,
    int EntryCount,
    DateTimeOffset? LastUpdated,
    string Summary);

public enum UnrealBuildTargetKind
{
    Project,
    Plugin
}

public enum UnrealBuildPlatform
{
    Win64,
    Linux,
    Android
}

public enum UnrealBuildConfiguration
{
    DebugGame,
    Development,
    Shipping
}

public sealed record UnrealBuildTargetDescriptor(
    string Id,
    string DisplayName,
    UnrealBuildTargetKind Kind,
    string TargetName,
    string SourcePath,
    string TargetType);

public sealed record UnrealEngineInstallation(
    string Version,
    string RootPath,
    string BuildScriptPath,
    string RunUatPath,
    bool IsInstalledBuild,
    string RecommendedLinuxToolchain,
    string RecommendedClangVersion,
    string? DetectedLinuxToolchainPath,
    string? DetectedClangVersion,
    bool LinuxToolchainReady,
    string? DetectedAndroidSdkPath,
    bool AndroidToolchainReady,
    string ToolchainSummary);

public sealed record UnrealBuildDiscovery(
    string ProjectFile,
    IReadOnlyList<UnrealEngineInstallation> Engines,
    IReadOnlyList<UnrealBuildTargetDescriptor> Targets,
    string Summary,
    bool IsCached = false,
    DateTimeOffset? CapturedAt = null);

public sealed record UnrealBuildProfile(
    Guid ProjectId,
    string EngineRoot,
    string TargetId,
    UnrealBuildPlatform Platform,
    UnrealBuildConfiguration Configuration,
    string LinuxToolchainPath,
    string AndroidSdkPath,
    string AndroidNdkPath,
    string JavaHomePath,
    string OutputDirectory,
    bool CookAndPackage,
    bool AutoConfigureToolchains,
    int TimeoutMinutes,
    DateTimeOffset UpdatedAt,
    string PresetName = "Default",
    int MaximumParallelBuilds = 1);

public sealed record UnrealBuildRequest(
    Guid ProjectId,
    string ProjectFile,
    UnrealEngineInstallation Engine,
    UnrealBuildTargetDescriptor Target,
    UnrealBuildPlatform Platform,
    UnrealBuildConfiguration Configuration,
    string LinuxToolchainPath,
    string AndroidSdkPath,
    string AndroidNdkPath,
    string JavaHomePath,
    string OutputDirectory,
    bool CookAndPackage,
    bool AutoConfigureToolchains,
    int TimeoutMinutes);

public sealed record UnrealBuildProgress(
    DateTimeOffset Timestamp,
    string Stream,
    string Text);

public enum UnrealBuildDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record UnrealBuildDiagnostic(
    UnrealBuildDiagnosticSeverity Severity,
    string Code,
    string Message,
    string File,
    int? Line,
    string RawLine);

public sealed record UnrealBuildResult(
    bool Succeeded,
    int ExitCode,
    string EngineVersion,
    string TargetName,
    UnrealBuildPlatform Platform,
    TimeSpan Duration,
    string LogPath,
    string OutputPath,
    string Summary,
    IReadOnlyList<UnrealBuildDiagnostic>? Diagnostics = null,
    int WarningCount = 0,
    int ErrorCount = 0);

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

    Task<UnrealAssetInspectionOptions> LoadAssetInspectionOptionsAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task SaveAssetInspectionOptionsAsync(
        string projectPath,
        UnrealAssetInspectionOptions options,
        CancellationToken cancellationToken = default);

    Task<UnrealAssetInspectionCacheStatus> GetAssetInspectionCacheStatusAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<UnrealAssetInspectionCacheStatus> ClearAssetInspectionCacheAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<UnrealBuildDiscovery> DiscoverBuildEnvironmentAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<UnrealBuildDiscovery> RefreshBuildEnvironmentAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<UnrealBuildProfile?> LoadBuildProfileAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task SaveBuildProfileAsync(
        UnrealBuildProfile profile,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnrealBuildProfile>> LoadBuildPresetsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task SaveBuildPresetAsync(
        UnrealBuildProfile profile,
        CancellationToken cancellationToken = default);

    Task DeleteBuildPresetAsync(
        Guid projectId,
        string presetName,
        CancellationToken cancellationToken = default);

    Task<UnrealBuildResult> RunBuildAsync(
        UnrealBuildRequest request,
        IProgress<UnrealBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public enum GameEngineKind
{
    Unity,
    Godot
}

public sealed record GameEngineProjectInspection(
    GameEngineKind Engine,
    bool IsValid,
    string ProjectRoot,
    string ProjectName,
    string EngineVersion,
    bool IsCompatible,
    string CompatibilityStatus,
    IReadOnlyList<string> SupportedVersions,
    bool IsEditorPluginInstalled,
    string? InstalledPluginVersion,
    string? BundledPluginVersion,
    bool UpdateAvailable,
    string Summary);

public sealed record GameEnginePluginInstallationResult(
    bool Succeeded,
    GameEngineKind Engine,
    string ProjectRoot,
    string DestinationDirectory,
    string? BackupDirectory,
    string InstalledVersion,
    string Message);

public sealed record GameEngineBridgeStatus(
    GameEngineKind Engine,
    bool IsRunning,
    string Endpoint,
    int AuthorizedProjectCount,
    string Detail);

public sealed class GameEngineProjectChangedEventArgs(
    GameEngineKind engine,
    string projectRoot,
    string action) : EventArgs
{
    public GameEngineKind Engine { get; } = engine;

    public string ProjectRoot { get; } = projectRoot;

    public string Action { get; } = action;
}

/// <summary>
/// Optional integration implemented by editor companions such as Unity and Godot.
/// The first contract deliberately covers project detection, installation and a private
/// loopback bridge only; file preview providers remain independent capabilities.
/// </summary>
public interface IGameEngineIntegrationPlugin : ICyRevisionPlugin
{
    event EventHandler<GameEngineProjectChangedEventArgs>? ProjectChanged;

    GameEngineKind Engine { get; }

    GameEngineBridgeStatus BridgeStatus { get; }

    GameEngineProjectInspection InspectProject(string path);

    Task<GameEnginePluginInstallationResult> InstallOrUpdateEditorPluginAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default);

    Task<GameEngineBridgeStatus> ConfigureProjectConnectionAsync(
        string projectPath,
        string cyRevisionExecutablePath,
        CancellationToken cancellationToken = default);
}

public sealed record LoreCliDetection(
    bool IsAvailable,
    string ExecutablePath,
    string Version,
    string Summary);

public sealed record LoreProjectInspection(
    bool IsProject,
    string ProjectRoot,
    string ConfigurationFile,
    string ServerUrl,
    string RepositoryName,
    string CurrentBranch,
    bool UnrealProjectDetected,
    bool UnrealCompanionInstalled,
    string? UnrealCompanionVersion,
    string Summary);

public sealed record LoreCommandResult(
    bool Succeeded,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string Summary);

public sealed record LoreUnrealCompanionInstallationResult(
    bool Succeeded,
    string ProjectRoot,
    string DestinationDirectory,
    string? BackupDirectory,
    string InstalledVersion,
    string Message);

/// <summary>
/// Optional project-management integration for Epic Games Lore. Lore remains an
/// external server/CLI product; CyRevision never embeds credentials or silently
/// scans/mutates a Lore workspace.
/// </summary>
public interface ILoreIntegrationPlugin : ICyRevisionPlugin
{
    LoreCliDetection DetectCli(string? configuredPath = null);

    Task SaveCliPathAsync(
        string executablePath,
        CancellationToken cancellationToken = default);

    LoreProjectInspection InspectProject(string path);

    Task<LoreCommandResult> ReadStatusAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<LoreCommandResult> ScanStatusAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<LoreCommandResult> ListBranchesAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    Task<LoreCommandResult> RunProjectCommandAsync(
        string projectPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<LoreUnrealCompanionInstallationResult> InstallOrUpdateUnrealCompanionAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
}

public sealed record PerforceProjectSettings(
    Guid ProjectId,
    string ProjectRoot,
    string ExecutablePath,
    string Server,
    string User,
    string Workspace,
    bool WriteOperationsEnabled);

public sealed record PerforceCliDetection(
    bool IsAvailable,
    string ExecutablePath,
    string Version,
    string Summary);

public sealed record PerforceConnectionStatus(
    bool CliAvailable,
    bool ServerReachable,
    bool Authenticated,
    bool WorkspaceValid,
    string ServerAddress,
    string UserName,
    string WorkspaceName,
    string WorkspaceRoot,
    string ServerVersion,
    string Summary);

public sealed record PerforceOpenedFile(
    string DepotPath,
    string LocalPath,
    string Action,
    string Change,
    string FileType,
    string User,
    string Workspace,
    bool IsLockedByOther);

public sealed record PerforceChangelist(
    int Number,
    string Status,
    string Description,
    string User,
    string Workspace,
    DateTimeOffset? UpdatedAt);

public sealed record PerforceFileRevision(
    int Revision,
    int Changelist,
    string Action,
    string FileType,
    string User,
    string Workspace,
    DateTimeOffset? SubmittedAt,
    string Description);

public sealed record PerforceCommandResult(
    bool Succeeded,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string Summary);

/// <summary>
/// Optional Helix Core integration. Authentication remains owned by the official P4 CLI
/// ticket store; CyRevision persists connection coordinates, never passwords or tickets.
/// Mutating methods must reject calls until WriteOperationsEnabled is set for the project.
/// </summary>
public interface IPerforceIntegrationPlugin : ICyRevisionPlugin
{
    PerforceCliDetection DetectCli(string? configuredPath = null);

    Task<PerforceProjectSettings?> LoadSettingsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default);

    Task<PerforceConnectionStatus> InspectConnectionAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PerforceOpenedFile>> GetOpenedFilesAsync(
        PerforceProjectSettings settings,
        bool includeOtherWorkspaces,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PerforceChangelist>> GetChangelistsAsync(
        PerforceProjectSettings settings,
        string status,
        int maximumCount = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PerforceFileRevision>> GetFileHistoryAsync(
        PerforceProjectSettings settings,
        string projectRelativePath,
        int maximumCount = 100,
        CancellationToken cancellationToken = default);

    Task<PerforceCommandResult> PreviewReconcileAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default);

    Task<PerforceCommandResult> ReconcileAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default);

    Task<PerforceCommandResult> OpenForEditAsync(
        PerforceProjectSettings settings,
        IReadOnlyList<string> projectRelativePaths,
        int? changelist = null,
        CancellationToken cancellationToken = default);

    Task<PerforceCommandResult> RevertAsync(
        PerforceProjectSettings settings,
        IReadOnlyList<string> projectRelativePaths,
        bool unchangedOnly,
        CancellationToken cancellationToken = default);

    Task<PerforceCommandResult> SubmitAsync(
        PerforceProjectSettings settings,
        int? changelist,
        string description,
        CancellationToken cancellationToken = default);

    Task<PerforceCommandResult> SyncAsync(
        PerforceProjectSettings settings,
        bool previewOnly,
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

public sealed record AiCodexDetectionResult(
    bool IsInstalled,
    bool IsRunning,
    string ExecutablePath,
    string Version,
    string Status);

public sealed record AiChatConnectRequest(
    string ProjectName,
    string RepositoryPath,
    string ExecutablePath,
    string Model,
    AiWorkspacePermission Permissions,
    string ThreadId = "",
    string WorkingDirectory = "",
    string PrePrompt = "");

public sealed record AiChatConnectionResult(
    bool Connected,
    string ThreadId,
    string Status);

public sealed record AiChatProgress(string Kind, string Text);

public sealed record AiChatTurnResult(
    bool Succeeded,
    string Response,
    string Diagnostic,
    string TurnId,
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

    bool IsCodexChatConnected => false;

    Task<AiCodexDetectionResult> DetectCodexAsync(
        string executablePath = "codex",
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiCodexDetectionResult(
            false,
            false,
            string.Empty,
            string.Empty,
            "This AI plugin does not provide a Codex App Server connection."));

    Task<AiChatConnectionResult> ConnectCodexChatAsync(
        AiChatConnectRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiChatConnectionResult(
            false,
            string.Empty,
            "This AI plugin does not provide a Codex App Server connection."));

    Task<AiChatTurnResult> SendCodexChatAsync(
        string message,
        IProgress<AiChatProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiChatTurnResult(
            false,
            string.Empty,
            "This AI plugin does not provide a Codex App Server connection.",
            string.Empty,
            TimeSpan.Zero));

    Task DisconnectCodexChatAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
