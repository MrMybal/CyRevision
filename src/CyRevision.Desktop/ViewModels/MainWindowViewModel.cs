using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CyRevision.Backup;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Core.Updates;
using CyRevision.Code;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Plugins;
using CyRevision.Diff;
using CyRevision.Discord;
using CyRevision.Discord.Control;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;
using CyRevision.PullRequests;
using CyRevision.RemoteBuild;
using CyRevision.Security;
using CyRevision.Sync;
using CyRevision.Vpn;

namespace CyRevision.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IProjectCatalog _projectCatalog;
    private readonly IGitRepositoryService _gitService;
    private readonly ApplicationPaths _applicationPaths;
    private readonly ISyncthingProfileStore _syncthingProfileStore;
    private readonly IGitPeerExchangeService _gitPeerExchangeService;
    private readonly IAssetDiffService _assetDiffService;
    private readonly IVpnProfileStore _vpnProfileStore;
    private readonly WireGuardKeyService _wireGuardKeyService;
    private readonly WireGuardConfigService _wireGuardConfiguration;
    private readonly ManagedWireGuardEngine _wireGuardEngine;
    private readonly WireGuardRuntimeResolver _wireGuardRuntimeResolver;
    private readonly VpnNetworkSetupService _vpnNetworkSetupService;
    private readonly VpnSyncExchangeService _vpnSyncExchangeService;
    private readonly ISwarmProfileStore _swarmProfileStore;
    private readonly SwarmSetupService _swarmSetupService;
    private readonly IVpnFileExchangeProfileStore _vpnFileExchangeProfileStore;
    private readonly VpnFileExchangeService _vpnFileExchangeService;
    private readonly ILfsManagementProfileStore _lfsManagementProfileStore;
    private readonly LfsStorageManager _lfsStorageManager;
    private readonly JsonRemoteBuildConnectionStore _remoteBuildConnectionStore;
    private readonly RemoteBuildSnapshotBuilder _remoteBuildSnapshotBuilder;
    private readonly LocalizationService _localization;
    private readonly OfflineDocumentationService _documentationService;
    private readonly ApplicationUpdateService _updateService;
    private readonly JsonDiscordAgentStore _discordAgentStore;
    private readonly DiscordProjectAgent _discordAgent;
    private readonly DiscordControlConnectionStore _discordControlConnectionStore;
    private readonly CyRevisionPluginManager _pluginManager;
    private readonly CodeWorkspaceService _codeWorkspaceService;
    private readonly IPullRequestService _pullRequestService;
    private readonly string? _initialProjectPath;
    private ProjectItemViewModel? _selectedProject;
    private GitChangeViewModel? _selectedChange;
    private GitBranch? _selectedBranch;
    private BackupSnapshotViewModel? _selectedBackup;
    private bool _isBusy;
    private string _statusMessage = "Initialisation…";
    private string _toolStatus = "Git : vérification…";
    private string _currentBranch = "—";
    private string _repositoryPath = "Aucun projet ouvert";
    private string _changeSummary = "0 modification";
    private string _commitMessage = string.Empty;
    private string _diffText = "Sélectionnez un fichier pour afficher son diff.";
    private string _lfsPattern = "*.uasset";
    private string _remoteUrl = string.Empty;
    private string _backupStorePath = string.Empty;
    private string _coldArchivePath = string.Empty;
    private string _coldArchiveAfterDays = "180";
    private string _coldArchiveStatus = "Archive froide facultative — aucune suppression automatique.";
    private RetentionMode _selectedRetentionMode = RetentionMode.Timeline;
    private string _retentionVersions = "30";
    private string _retentionDays = "90";
    private string _retentionBudgetGb = string.Empty;
    private ProjectPreset? _selectedPreset;
    private SyncthingProfile? _currentSyncProfile;
    private ManagedSyncthingEngine? _syncEngine;
    private Guid? _syncEngineProjectId;
    private string _syncthingExecutablePath = string.Empty;
    private string _syncState = "Sync désactivé";
    private string _syncDetails = "Aucune instance CyRevision n'est lancée.";
    private string _peerExchangeText = string.Empty;
    private PeerRole _selectedPeerRole = PeerRole.Contributor;
    private string _peerVerificationCode = string.Empty;
    private string _assetBaselinePath = string.Empty;
    private string _assetCandidatePath = string.Empty;
    private string _assetDiffReport = "Choisissez deux fichiers, ou comparez une modification Git à HEAD.";
    private Bitmap? _assetDiffPreview;
    private PeerMemberViewModel? _selectedPeerMember;
    private string _reservationSummary = "Aucune réservation souple active.";
    private string _gitGraphSummary = "Analyse optionnelle non lancée.";
    private string _gitGraphCommitLimit = "250";
    private string _gitGraphFileLimit = "80";
    private bool _gitGraphIncludeAllBranches = true;
    private IReadOnlyList<GitGraphCommit> _gitGraphCommits = [];
    private IReadOnlyList<GitFileActivity> _gitFileActivities = [];
    private IReadOnlyList<GitFileRelation> _gitFileRelations = [];
    private IReadOnlyList<GitContributorActivity> _gitContributors = [];
    private IReadOnlyList<GitDailyActivity> _gitDailyActivity = [];
    private IReadOnlyList<GitFileActivity> _gitHotFiles = [];
    private string _gitInsightsSummary = "Analyse d'activité non lancée.";
    private string _projectMembersSummary = "No project selected.";
    private IReadOnlyList<GitFileActivity> _unrealDependencyFiles = [];
    private IReadOnlyList<GitFileRelation> _unrealDependencyRelations = [];
    private IReadOnlyList<UnrealAssetNode> _unrealAssets = [];
    private string _unrealDependencySummary = "Analyse Unreal hors moteur non lancée.";
    private IReadOnlyList<GitRevision> _allExplorerRevisions = [];
    private GitRevision? _selectedExplorerRevision;
    private GitRevision? _selectedComparisonRevision;
    private GitCommitFileChange? _selectedExplorerFile;
    private string _explorerSearch = string.Empty;
    private string _explorerSummary = "Sélectionnez une révision pour l'inspecter.";
    private string _explorerDiff = "Le diff de la révision apparaîtra ici.";
    private string? _comparisonFromHash;
    private string? _comparisonToHash;
    private GitRevision? _multiRestoreCommit;
    private string? _multiRestoreLoadedHash;
    private MultiRestoreFileViewModel? _selectedMultiRestoreFile;
    private GitMultiRestorePlan? _multiRestorePlan;
    private string _multiRestoreSummary = "Choose a commit to compose a safe multi-file restore.";
    private string _multiRestoreDiff = "Select a file to review the change before composing the restore.";
    private bool _multiRestoreOverwriteLocalChanges;
    private GitBranch? _branchCompareSource;
    private GitBranch? _branchCompareTarget;
    private CherryPickCommitViewModel? _selectedCherryPickCommit;
    private GitBranchComparison? _branchComparison;
    private GitCherryPickPlan? _cherryPickPlan;
    private GitCherryPickMode _selectedCherryPickMode = GitCherryPickMode.KeepCommits;
    private string _combinedCherryPickMessage = string.Empty;
    private string _branchComparisonSummary = "Choose a source and target branch to compare their commits.";
    private string _cherryPickPlanSummary = "Compare branches, then select the source-only commits to apply.";
    private string _cherryPickDiff = "Select a commit to inspect its patch.";
    private LfsTrackedFile? _selectedLfsFile;
    private LfsFileVersion? _selectedLfsVersion;
    private string _lfsTimelineSummary = "Sélectionnez un fichier LFS pour afficher ses versions.";
    private string _lfsLocksSummary = "Git LFS locks have not been loaded.";
    private Bitmap? _lfsPreview;
    private LfsHistoryTransferMode _selectedLfsHistoryMode = LfsHistoryTransferMode.OnDemand;
    private string _smartSyncRecentVersionCount = "3";
    private bool _smartSyncReplicateBackups;
    private string _smartSyncPlanSummary = "Plan calculé localement — aucun transfert lancé.";
    private string _peerLfsTransferSummary = "Aucun inventaire de pair vérifié pour le moment.";
    private LfsManagementProfile? _currentLfsManagementProfile;
    private LfsCleanupPlan? _lfsCleanupPlan;
    private string _lfsExternalStoragePath = string.Empty;
    private string _lfsManagementArchivePath = string.Empty;
    private string _lfsCleanupRemoteName = "origin";
    private string _lfsRequiredCopies = "1";
    private string _lfsCleanupGraceDays = "7";
    private string _lfsPeerProofMaximumAgeHours = "24";
    private bool _lfsVerifyRemote = true;
    private bool _lfsRemoveOriginalAfterRelocation;
    private string _lfsStorageSummary = "Analyze the repository before cleaning LFS storage.";
    private string _lfsRemoteVerification = "Remote verification has not run.";
    private VpnProjectProfile? _currentVpnProfile;
    private string _vpnState = "VPN non configuré";
    private string _vpnDetails = "WireGuard reste indépendant de Git et de Sync.";
    private string _wireGuardExecutablePath = string.Empty;
    private string _vpnNetworkCidr = string.Empty;
    private string _vpnLocalAddress = string.Empty;
    private string _vpnPublicEndpoint = string.Empty;
    private string _vpnListenPort = "51820";
    private string _vpnExchangeText = string.Empty;
    private string _vpnConfigurationPreview = "Configurez WireGuard pour afficher le tunnel CyRevision.";
    private VpnNodeCapabilities _selectedVpnCapability = VpnNodeCapabilities.GeneralAccess;
    private VpnNodeCapabilities _selectedVpnInvitationCapability = VpnNodeCapabilities.GeneralAccess;
    private VpnPeerViewModel? _selectedVpnPeer;
    private VpnSetupPlan? _currentVpnSetupPlan;
    private bool _vpnSetupAcceptIncoming;
    private bool _vpnSetupAllowSwarm;
    private bool _vpnSetupAllowControlApi;
    private bool _vpnSetupAllowFileExchange;
    private bool _vpnSetupAllowRemoteBuild;
    private bool _vpnCanApplyFirewall;
    private bool _vpnCanOpenRouter;
    private string _vpnSetupSummary = "Run the guided setup to inspect this computer.";
    private string _vpnSetupNetwork = "Local network not inspected.";
    private string _vpnFirewallStatus = "Firewall not inspected.";
    private string _vpnComputerGuide = string.Empty;
    private string _vpnRouterGuide = string.Empty;
    private string _vpnFirewallCommands = string.Empty;
    private string _vpnConnectivityStatus = "Tunnel connectivity not tested.";
    private VpnSyncMessageViewModel? _selectedVpnSyncMessage;
    private string _vpnSyncStatus = "Sync exchange not inspected.";
    private SwarmProjectProfile? _currentSwarmProfile;
    private SwarmNodeRole _selectedSwarmRole = SwarmNodeRole.Agent;
    private string _swarmCoordinatorAddress = string.Empty;
    private string _swarmCoordinatorAlias = string.Empty;
    private string _swarmAgentPath = string.Empty;
    private string _swarmCoordinatorPath = string.Empty;
    private string _swarmOptionsPath = string.Empty;
    private string _swarmAgentGroup = "Default";
    private string _swarmAllowedGroup = "DefaultDeployed";
    private string _swarmAllowedAgents = "*";
    private string _swarmCacheFolder = string.Empty;
    private string _swarmStatus = "Select a VPN-enabled project to configure Unreal Swarm.";
    private string _swarmDiagnostic = "No Swarm connection or configuration test has run.";
    private VpnFileExchangeCredentials? _currentVpnFileExchange;
    private VpnFileExchangeHost? _vpnFileExchangeHost;
    private string _vpnFileListenPort = VpnFileExchangeDefaults.Port.ToString();
    private string _vpnFileInboxPath = string.Empty;
    private string _vpnFileSharedFolderPath = string.Empty;
    private string _vpnFileAccessToken = string.Empty;
    private bool _vpnFileAllowReceive = true;
    private bool _vpnFileAllowBrowse = true;
    private bool _vpnFileAllowDownload = true;
    private bool _vpnFileStartAutomatically;
    private VpnPeerViewModel? _selectedVpnFilePeer;
    private VpnSharedFileViewModel? _selectedVpnSharedFile;
    private string _vpnFileStatus = "VPN file exchange is not configured.";
    private RemoteBuildCredentials? _currentRemoteBuildCredentials;
    private RemoteBuildJobStatus? _currentRemoteBuildJob;
    private CancellationTokenSource? _remoteBuildCancellation;
    private string _remoteBuildEndpoint = "http://127.0.0.1:47841";
    private string _remoteBuildAccessToken = string.Empty;
    private string _remoteBuildRecipeId = string.Empty;
    private RemoteBuildSourceMode _selectedRemoteBuildSourceMode = RemoteBuildSourceMode.ExistingWorkspace;
    private string _remoteBuildArtifactDestination = string.Empty;
    private string _remoteBuildMaximumUploadGb = "100";
    private bool _remoteBuildAllowPrivateHttp;
    private string _remoteBuildStatus = "Remote build agent is not configured.";
    private string _remoteBuildLog = "No remote build has run.";
    private LanguageOption? _selectedLanguage;
    private IReadOnlyList<DocumentationTopic> _allDocumentationTopics = [];
    private DocumentationTopic? _selectedDocumentationTopic;
    private string _documentationSearch = string.Empty;
    private ApplicationUpdateInfo? _availableUpdate;
    private string _latestApplicationVersion = "—";
    private string _updateStatus = "Recherche de mise à jour non lancée.";
    private string _updateReleaseNotes = string.Empty;
    private bool _hasUpdateAvailable;
    private bool _isCheckingForUpdates;
    private bool _isDownloadingUpdate;
    private double _updateProgress;
    private VpnBackendMode _selectedVpnBackendMode = VpnBackendMode.SystemInstallation;
    private string _vpnBackendDetails = "Use the WireGuard installation already present on this system.";
    private DiscordAgentProfile? _currentDiscordProfile;
    private string _discordWebhookUrl = string.Empty;
    private string _discordDisplayName = "CyRevision";
    private string _discordProjectLabel = string.Empty;
    private string _discordRepositoryWebUrl = string.Empty;
    private string _discordPollIntervalSeconds = "30";
    private bool _discordNotifyCommits = true;
    private bool _discordNotifyBranchChanges = true;
    private bool _discordStartAutomatically;
    private bool _discordWebhookConfigured;
    private bool _discordIsRunning;
    private string _discordAgentState = "Discord agent not configured";
    private string _discordAgentDetails = "Add a channel webhook to enable project notifications.";
    private string _discordLastActivity = "No check performed.";
    private DiscordControlConnection? _currentDiscordControlConnection;
    private DiscordAgentExecutionMode _selectedDiscordExecutionMode = DiscordAgentExecutionMode.Integrated;
    private string _discordAgentEndpoint = "http://127.0.0.1:47831";
    private string _discordAgentApiToken = string.Empty;
    private string _discordAgentRepositoryPath = string.Empty;
    private bool _discordAllowPrivateHttp;
    private bool _discordControlTokenConfigured;
    private string _discordConnectionSummary = "Integrated agent — runs with the desktop application.";
    private PluginItemViewModel? _selectedPlugin;
    private bool _isUnrealIntegrationEnabled;
    private string _unrealProjectPath = string.Empty;
    private string _unrealPluginSummary = "Enable the Unreal Engine Integration plugin to inspect and install CyRevisionUnreal.";
    private string _unrealBridgeSummary = "The optional Unreal bridge is disabled.";
    private UnrealProjectInspection? _unrealProjectInspection;
    private IUnrealIntegrationPlugin? _subscribedUnrealPlugin;
    private CancellationTokenSource? _codeSearchCancellation;
    private CancellationTokenSource? _aiAgentCancellation;
    private CodeTreeNode? _selectedCodeNode;
    private CodeSearchResult? _selectedCodeSearchResult;
    private CodeSymbol? _selectedCodeSymbol;
    private string _codeTreeFilter = string.Empty;
    private bool _codeIncludeHidden;
    private string _codeWorkspaceSummary = "Select a project to explore its code.";
    private string _codeSearchQuery = string.Empty;
    private string _codeFilePatterns = string.Empty;
    private bool _codeSearchMatchCase;
    private bool _codeSearchWholeWord;
    private bool _codeSearchRegex;
    private string _codeSearchSummary = "Ctrl+Shift+F searches the entire project.";
    private string _codePreviewText = string.Empty;
    private string _codePreviewSummary = "Select a file to preview it.";
    private string _codeSelectionSummary = "Select lines in the preview, then request their Git history.";
    private bool _isAiIntegrationEnabled;
    private AiProviderDescriptor? _selectedAiProvider;
    private string _aiModel = string.Empty;
    private string _aiEndpoint = string.Empty;
    private string _aiExecutablePath = "codex";
    private string _aiApiKey = string.Empty;
    private string _aiPrompt = string.Empty;
    private string _aiResponse = "Enable the optional AI Workspace plugin to connect Codex or an API provider.";
    private string _aiStatus = "AI integration disabled.";
    private bool _aiAllowModify;
    private bool _aiAllowNetwork;
    private bool _aiStageAfterRun;
    private bool _aiCommitAfterRun;
    private string _aiCommitMessage = "Apply AI-assisted changes";
    private AiMcpServerViewModel? _selectedAiMcpServer;
    private bool _aiMcpEnabled;
    private bool _aiMcpEmergencyBlocked;
    private bool _aiMcpBlockUnmanagedServers = true;
    private string _aiMcpStatus = "MCP is disabled for this project.";
    private PullRequestRepository? _pullRequestRepository;
    private PullRequestSummary? _selectedPullRequest;
    private PullRequestFile? _selectedPullRequestFile;
    private PullRequestDetails? _selectedPullRequestDetails;
    private PullRequestStateFilter _pullRequestStateFilter = PullRequestStateFilter.Open;
    private PullRequestMergeMethod _pullRequestMergeMethod = PullRequestMergeMethod.Squash;
    private PullRequestReviewAction _pullRequestReviewAction = PullRequestReviewAction.Comment;
    private string _pullRequestStatus = "Select a GitHub-backed project to manage pull requests.";
    private string _pullRequestApiBaseUrl = string.Empty;
    private string _pullRequestToken = string.Empty;
    private string _pullRequestTokenEnvironmentVariable = "GITHUB_TOKEN";
    private string _pullRequestPatch = "Select a changed file to inspect its patch.";
    private string _newPullRequestTitle = string.Empty;
    private string _newPullRequestBody = string.Empty;
    private string _newPullRequestHeadBranch = string.Empty;
    private string _newPullRequestBaseBranch = "main";
    private bool _newPullRequestIsDraft;
    private string _pullRequestComment = string.Empty;
    private string _pullRequestReviewBody = string.Empty;
    private int _pullRequestDetailsLoadVersion;

    public MainWindowViewModel(
        IProjectCatalog projectCatalog,
        IGitRepositoryService gitService,
        ApplicationPaths applicationPaths,
        ISyncthingProfileStore syncthingProfileStore,
        IGitPeerExchangeService gitPeerExchangeService,
        IAssetDiffService assetDiffService,
        IVpnProfileStore vpnProfileStore,
        WireGuardKeyService wireGuardKeyService,
        WireGuardConfigService wireGuardConfiguration,
        ManagedWireGuardEngine wireGuardEngine,
        WireGuardRuntimeResolver wireGuardRuntimeResolver,
        VpnNetworkSetupService vpnNetworkSetupService,
        VpnSyncExchangeService vpnSyncExchangeService,
        ISwarmProfileStore swarmProfileStore,
        SwarmSetupService swarmSetupService,
        IVpnFileExchangeProfileStore vpnFileExchangeProfileStore,
        VpnFileExchangeService vpnFileExchangeService,
        ILfsManagementProfileStore lfsManagementProfileStore,
        LfsStorageManager lfsStorageManager,
        JsonRemoteBuildConnectionStore remoteBuildConnectionStore,
        RemoteBuildSnapshotBuilder remoteBuildSnapshotBuilder,
        LocalizationService localization,
        OfflineDocumentationService documentationService,
        ApplicationUpdateService updateService,
        JsonDiscordAgentStore discordAgentStore,
        DiscordProjectAgent discordAgent,
        DiscordControlConnectionStore discordControlConnectionStore,
        CyRevisionPluginManager pluginManager,
        CodeWorkspaceService codeWorkspaceService,
        IPullRequestService pullRequestService,
        string? initialProjectPath = null)
    {
        _projectCatalog = projectCatalog;
        _gitService = gitService;
        _applicationPaths = applicationPaths;
        _syncthingProfileStore = syncthingProfileStore;
        _gitPeerExchangeService = gitPeerExchangeService;
        _assetDiffService = assetDiffService;
        _vpnProfileStore = vpnProfileStore;
        _wireGuardKeyService = wireGuardKeyService;
        _wireGuardConfiguration = wireGuardConfiguration;
        _wireGuardEngine = wireGuardEngine;
        _wireGuardRuntimeResolver = wireGuardRuntimeResolver;
        _vpnNetworkSetupService = vpnNetworkSetupService;
        _vpnSyncExchangeService = vpnSyncExchangeService;
        _swarmProfileStore = swarmProfileStore;
        _swarmSetupService = swarmSetupService;
        _vpnFileExchangeProfileStore = vpnFileExchangeProfileStore;
        _vpnFileExchangeService = vpnFileExchangeService;
        _lfsManagementProfileStore = lfsManagementProfileStore;
        _lfsStorageManager = lfsStorageManager;
        _remoteBuildConnectionStore = remoteBuildConnectionStore;
        _remoteBuildSnapshotBuilder = remoteBuildSnapshotBuilder;
        _localization = localization;
        _documentationService = documentationService;
        _updateService = updateService;
        _discordAgentStore = discordAgentStore;
        _discordAgent = discordAgent;
        _discordControlConnectionStore = discordControlConnectionStore;
        _pluginManager = pluginManager;
        _codeWorkspaceService = codeWorkspaceService;
        _pullRequestService = pullRequestService;
        _discordAgent.StatusChanged += OnDiscordAgentStatusChanged;
        _selectedLanguage = localization.Languages.FirstOrDefault(language =>
            string.Equals(language.Code, localization.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase));
        _initialProjectPath = initialProjectPath;
        ReloadDocumentation();
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<GitChangeViewModel> Changes { get; } = [];

    public ObservableCollection<GitRevision> History { get; } = [];

    public ObservableCollection<GitBranch> Branches { get; } = [];

    public ObservableCollection<LfsTrackedPattern> LfsPatterns { get; } = [];

    public ObservableCollection<LfsFileLock> LfsLocks { get; } = [];

    public ObservableCollection<LfsFileLock> MyLfsLocks { get; } = [];

    public ObservableCollection<GitCommitFileChange> ExplorerFiles { get; } = [];

    public ObservableCollection<GitFileRevision> ExplorerFileHistory { get; } = [];

    public ObservableCollection<MultiRestoreFileViewModel> MultiRestoreFiles { get; } = [];

    public ObservableCollection<GitMultiRestoreOperation> MultiRestoreOperations { get; } = [];

    public ObservableCollection<CherryPickCommitViewModel> BranchComparisonCommits { get; } = [];

    public ObservableCollection<GitBranch> BranchCompareSources { get; } = [];

    public ObservableCollection<GitBranch> BranchCompareTargets { get; } = [];

    public ObservableCollection<LfsTrackedFile> LfsFiles { get; } = [];

    public ObservableCollection<LfsFileVersion> LfsVersions { get; } = [];

    public ObservableCollection<LfsCleanupItem> LfsCleanupItems { get; } = [];

    public ObservableCollection<SmartSyncPlanItem> SmartSyncPlanItems { get; } = [];

    public ObservableCollection<BackupSnapshotViewModel> Backups { get; } = [];

    public ObservableCollection<PeerMemberViewModel> PeerMembers { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> SyncProjectMembers { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> GitProjectMembers { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> VpnProjectMembers { get; } = [];

    public ObservableCollection<AdvisoryReservationViewModel> AdvisoryReservations { get; } = [];

    public ObservableCollection<DocumentationTopic> DocumentationTopics { get; } = [];

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    public ObservableCollection<CodeTreeNode> CodeTree { get; } = [];

    public ObservableCollection<CodeSearchResult> CodeSearchResults { get; } = [];

    public ObservableCollection<CodeHistoryEntry> CodeHistory { get; } = [];

    public ObservableCollection<CodeSymbol> CodeSymbols { get; } = [];

    public ObservableCollection<AiProviderDescriptor> AiProviders { get; } = [];

    public ObservableCollection<AiMcpServerViewModel> AiMcpServers { get; } = [];

    public ObservableCollection<PullRequestSummary> PullRequests { get; } = [];

    public ObservableCollection<PullRequestFile> PullRequestFiles { get; } = [];

    public ObservableCollection<PullRequestReview> PullRequestReviews { get; } = [];

    public ObservableCollection<PullRequestComment> PullRequestComments { get; } = [];

    public IReadOnlyList<PullRequestStateFilter> PullRequestStateFilters { get; } = Enum.GetValues<PullRequestStateFilter>();

    public IReadOnlyList<PullRequestMergeMethod> PullRequestMergeMethods { get; } = Enum.GetValues<PullRequestMergeMethod>();

    public IReadOnlyList<PullRequestReviewAction> PullRequestReviewActions { get; } = Enum.GetValues<PullRequestReviewAction>();

    public IReadOnlyList<AiMcpTransport> AiMcpTransports { get; } = Enum.GetValues<AiMcpTransport>();

    public IReadOnlyList<AiMcpCapability> AiMcpCapabilities { get; } = Enum.GetValues<AiMcpCapability>();

    public IReadOnlyList<AiMcpApprovalMode> AiMcpApprovalModes { get; } = Enum.GetValues<AiMcpApprovalMode>();

    public IReadOnlyList<AiMcpHttpAuth> AiMcpHttpAuthModes { get; } = Enum.GetValues<AiMcpHttpAuth>();

    public IReadOnlyList<GitGraphCommit> GitGraphCommits
    {
        get => _gitGraphCommits;
        private set => SetProperty(ref _gitGraphCommits, value);
    }

    public IReadOnlyList<GitFileActivity> GitFileActivities
    {
        get => _gitFileActivities;
        private set => SetProperty(ref _gitFileActivities, value);
    }

    public IReadOnlyList<GitFileRelation> GitFileRelations
    {
        get => _gitFileRelations;
        private set => SetProperty(ref _gitFileRelations, value);
    }

    public IReadOnlyList<GitContributorActivity> GitContributors
    {
        get => _gitContributors;
        private set => SetProperty(ref _gitContributors, value);
    }

    public IReadOnlyList<GitDailyActivity> GitDailyActivity
    {
        get => _gitDailyActivity;
        private set => SetProperty(ref _gitDailyActivity, value);
    }

    public IReadOnlyList<GitFileActivity> GitHotFiles
    {
        get => _gitHotFiles;
        private set => SetProperty(ref _gitHotFiles, value);
    }

    public string GitInsightsSummary
    {
        get => _gitInsightsSummary;
        private set => SetProperty(ref _gitInsightsSummary, value);
    }

    public string ProjectMembersSummary
    {
        get => _projectMembersSummary;
        private set => SetProperty(ref _projectMembersSummary, value);
    }

    public IReadOnlyList<GitFileActivity> UnrealDependencyFiles
    {
        get => _unrealDependencyFiles;
        private set => SetProperty(ref _unrealDependencyFiles, value);
    }

    public IReadOnlyList<GitFileRelation> UnrealDependencyRelations
    {
        get => _unrealDependencyRelations;
        private set => SetProperty(ref _unrealDependencyRelations, value);
    }

    public IReadOnlyList<UnrealAssetNode> UnrealAssets
    {
        get => _unrealAssets;
        private set => SetProperty(ref _unrealAssets, value);
    }

    public string UnrealDependencySummary
    {
        get => _unrealDependencySummary;
        private set => SetProperty(ref _unrealDependencySummary, value);
    }

    public GitRevision? SelectedExplorerRevision
    {
        get => _selectedExplorerRevision;
        set
        {
            if (SetProperty(ref _selectedExplorerRevision, value) && value is not null)
            {
                if (SelectedComparisonRevision is null || SelectedComparisonRevision.Hash == value.Hash)
                {
                    SelectedComparisonRevision = History.FirstOrDefault(revision => revision.Hash != value.Hash);
                }

                _ = LoadExplorerRevisionAsync(value);
            }
        }
    }

    public GitRevision? SelectedComparisonRevision
    {
        get => _selectedComparisonRevision;
        set => SetProperty(ref _selectedComparisonRevision, value);
    }

    public GitCommitFileChange? SelectedExplorerFile
    {
        get => _selectedExplorerFile;
        set
        {
            if (SetProperty(ref _selectedExplorerFile, value))
            {
                _ = LoadExplorerFileAsync(value);
            }
        }
    }

    public string ExplorerSearch
    {
        get => _explorerSearch;
        set
        {
            if (SetProperty(ref _explorerSearch, value))
            {
                ApplyExplorerFilter();
            }
        }
    }

    public string ExplorerSummary
    {
        get => _explorerSummary;
        private set => SetProperty(ref _explorerSummary, value);
    }

    public string ExplorerDiff
    {
        get => _explorerDiff;
        private set => SetProperty(ref _explorerDiff, value);
    }

    public GitRevision? MultiRestoreCommit
    {
        get => _multiRestoreCommit;
        set
        {
            if (SetProperty(ref _multiRestoreCommit, value) &&
                value is not null &&
                !string.Equals(_multiRestoreLoadedHash, value.Hash, StringComparison.Ordinal))
            {
                _ = LoadMultiRestoreCommitAsync(value);
            }
        }
    }

    public MultiRestoreFileViewModel? SelectedMultiRestoreFile
    {
        get => _selectedMultiRestoreFile;
        set
        {
            if (SetProperty(ref _selectedMultiRestoreFile, value))
            {
                _ = LoadMultiRestoreDiffAsync(value);
            }
        }
    }

    public string MultiRestoreSummary
    {
        get => _multiRestoreSummary;
        private set => SetProperty(ref _multiRestoreSummary, value);
    }

    public string MultiRestoreDiff
    {
        get => _multiRestoreDiff;
        private set => SetProperty(ref _multiRestoreDiff, value);
    }

    public bool MultiRestoreOverwriteLocalChanges
    {
        get => _multiRestoreOverwriteLocalChanges;
        set
        {
            if (SetProperty(ref _multiRestoreOverwriteLocalChanges, value))
            {
                OnPropertyChanged(nameof(CanApplyMultiRestore));
            }
        }
    }

    public bool CanApplyMultiRestore =>
        _multiRestorePlan?.CanApply == true &&
        (!_multiRestorePlan.HasLocalChanges || MultiRestoreOverwriteLocalChanges);

    public GitBranch? BranchCompareSource
    {
        get => _branchCompareSource;
        set
        {
            if (SetProperty(ref _branchCompareSource, value))
            {
                InvalidateCherryPickPlan();
            }
        }
    }

    public GitBranch? BranchCompareTarget
    {
        get => _branchCompareTarget;
        set
        {
            if (SetProperty(ref _branchCompareTarget, value))
            {
                InvalidateCherryPickPlan();
            }
        }
    }

    public CherryPickCommitViewModel? SelectedCherryPickCommit
    {
        get => _selectedCherryPickCommit;
        set
        {
            if (SetProperty(ref _selectedCherryPickCommit, value))
            {
                _ = LoadCherryPickDiffAsync(value);
            }
        }
    }

    public GitCherryPickMode SelectedCherryPickMode
    {
        get => _selectedCherryPickMode;
        set
        {
            if (SetProperty(ref _selectedCherryPickMode, value))
            {
                InvalidateCherryPickPlan();
                OnPropertyChanged(nameof(CombinesCherryPickCommits));
            }
        }
    }

    public IReadOnlyList<GitCherryPickMode> CherryPickModes { get; } = Enum.GetValues<GitCherryPickMode>();

    public bool CombinesCherryPickCommits => SelectedCherryPickMode == GitCherryPickMode.CombineIntoOne;

    public string CombinedCherryPickMessage
    {
        get => _combinedCherryPickMessage;
        set
        {
            if (SetProperty(ref _combinedCherryPickMessage, value))
            {
                InvalidateCherryPickPlan();
            }
        }
    }

    public string BranchComparisonSummary
    {
        get => _branchComparisonSummary;
        private set => SetProperty(ref _branchComparisonSummary, value);
    }

    public string CherryPickPlanSummary
    {
        get => _cherryPickPlanSummary;
        private set => SetProperty(ref _cherryPickPlanSummary, value);
    }

    public string CherryPickDiff
    {
        get => _cherryPickDiff;
        private set => SetProperty(ref _cherryPickDiff, value);
    }

    public bool CanApplyCherryPick => _cherryPickPlan?.CanApply == true;

    public LfsTrackedFile? SelectedLfsFile
    {
        get => _selectedLfsFile;
        set
        {
            if (SetProperty(ref _selectedLfsFile, value))
            {
                _ = LoadLfsTimelineAsync(value);
            }
        }
    }

    public LfsFileVersion? SelectedLfsVersion
    {
        get => _selectedLfsVersion;
        set
        {
            if (SetProperty(ref _selectedLfsVersion, value))
            {
                LoadLfsPreview(value);
                OnPropertyChanged(nameof(CanRequestSelectedLfsVersion));
            }
        }
    }

    public bool CanRequestSelectedLfsVersion => SelectedLfsVersion?.CanRequestFromPeer == true;

    public string LfsTimelineSummary
    {
        get => _lfsTimelineSummary;
        private set => SetProperty(ref _lfsTimelineSummary, value);
    }

    public string LfsLocksSummary
    {
        get => _lfsLocksSummary;
        private set => SetProperty(ref _lfsLocksSummary, value);
    }

    public Bitmap? LfsPreview
    {
        get => _lfsPreview;
        private set
        {
            Bitmap? previous = _lfsPreview;
            if (SetProperty(ref _lfsPreview, value))
            {
                previous?.Dispose();
            }
        }
    }

    public LfsHistoryTransferMode SelectedLfsHistoryMode
    {
        get => _selectedLfsHistoryMode;
        set
        {
            if (SetProperty(ref _selectedLfsHistoryMode, value))
            {
                UpdateSmartSyncPlan();
            }
        }
    }

    public string SmartSyncRecentVersionCount
    {
        get => _smartSyncRecentVersionCount;
        set
        {
            if (SetProperty(ref _smartSyncRecentVersionCount, value))
            {
                UpdateSmartSyncPlan();
            }
        }
    }

    public bool SmartSyncReplicateBackups
    {
        get => _smartSyncReplicateBackups;
        set
        {
            if (SetProperty(ref _smartSyncReplicateBackups, value))
            {
                UpdateSmartSyncPlan();
            }
        }
    }

    public string SmartSyncPlanSummary
    {
        get => _smartSyncPlanSummary;
        private set => SetProperty(ref _smartSyncPlanSummary, value);
    }

    public string PeerLfsTransferSummary
    {
        get => _peerLfsTransferSummary;
        private set => SetProperty(ref _peerLfsTransferSummary, value);
    }

    public string LfsExternalStoragePath
    {
        get => _lfsExternalStoragePath;
        set => SetProperty(ref _lfsExternalStoragePath, value);
    }

    public string LfsManagementArchivePath
    {
        get => _lfsManagementArchivePath;
        set => SetProperty(ref _lfsManagementArchivePath, value);
    }

    public string LfsCleanupRemoteName
    {
        get => _lfsCleanupRemoteName;
        set => SetProperty(ref _lfsCleanupRemoteName, value);
    }

    public string LfsRequiredCopies
    {
        get => _lfsRequiredCopies;
        set => SetProperty(ref _lfsRequiredCopies, value);
    }

    public string LfsCleanupGraceDays
    {
        get => _lfsCleanupGraceDays;
        set => SetProperty(ref _lfsCleanupGraceDays, value);
    }

    public string LfsPeerProofMaximumAgeHours
    {
        get => _lfsPeerProofMaximumAgeHours;
        set => SetProperty(ref _lfsPeerProofMaximumAgeHours, value);
    }

    public bool LfsVerifyRemote
    {
        get => _lfsVerifyRemote;
        set => SetProperty(ref _lfsVerifyRemote, value);
    }

    public bool LfsRemoveOriginalAfterRelocation
    {
        get => _lfsRemoveOriginalAfterRelocation;
        set => SetProperty(ref _lfsRemoveOriginalAfterRelocation, value);
    }

    public string LfsStorageSummary
    {
        get => _lfsStorageSummary;
        private set => SetProperty(ref _lfsStorageSummary, value);
    }

    public string LfsRemoteVerification
    {
        get => _lfsRemoteVerification;
        private set => SetProperty(ref _lfsRemoteVerification, value);
    }

    public string RemoteBuildEndpoint
    {
        get => _remoteBuildEndpoint;
        set => SetProperty(ref _remoteBuildEndpoint, value);
    }

    public string RemoteBuildAccessToken
    {
        get => _remoteBuildAccessToken;
        set => SetProperty(ref _remoteBuildAccessToken, value);
    }

    public string RemoteBuildRecipeId
    {
        get => _remoteBuildRecipeId;
        set => SetProperty(ref _remoteBuildRecipeId, value);
    }

    public RemoteBuildSourceMode SelectedRemoteBuildSourceMode
    {
        get => _selectedRemoteBuildSourceMode;
        set => SetProperty(ref _selectedRemoteBuildSourceMode, value);
    }

    public string RemoteBuildArtifactDestination
    {
        get => _remoteBuildArtifactDestination;
        set => SetProperty(ref _remoteBuildArtifactDestination, value);
    }

    public string RemoteBuildMaximumUploadGb
    {
        get => _remoteBuildMaximumUploadGb;
        set => SetProperty(ref _remoteBuildMaximumUploadGb, value);
    }

    public bool RemoteBuildAllowPrivateHttp
    {
        get => _remoteBuildAllowPrivateHttp;
        set => SetProperty(ref _remoteBuildAllowPrivateHttp, value);
    }

    public string RemoteBuildStatus
    {
        get => _remoteBuildStatus;
        private set => SetProperty(ref _remoteBuildStatus, value);
    }

    public string RemoteBuildLog
    {
        get => _remoteBuildLog;
        private set => SetProperty(ref _remoteBuildLog, value);
    }

    public bool IsRemoteBuildRunning => _remoteBuildCancellation is not null;

    public ObservableCollection<VpnPeerViewModel> VpnPeers { get; } = [];

    public ObservableCollection<VpnSyncMessageViewModel> VpnSyncMessages { get; } = [];

    public ObservableCollection<VpnSharedFileViewModel> VpnSharedFiles { get; } = [];

    public IReadOnlyList<RetentionMode> RetentionModes { get; } = Enum.GetValues<RetentionMode>();

    public IReadOnlyList<ProjectPreset> Presets { get; } = ProjectPresets.All;

    public IReadOnlyList<PeerRole> PeerRoles { get; } = Enum.GetValues<PeerRole>();

    public IReadOnlyList<VpnNodeCapabilities> VpnCapabilities { get; } =
        Enum.GetValues<VpnNodeCapabilities>().Where(value => value != VpnNodeCapabilities.None).ToArray();

    public IReadOnlyList<SwarmNodeRole> SwarmRoles { get; } = Enum.GetValues<SwarmNodeRole>();

    public IReadOnlyList<LanguageOption> Languages => _localization.Languages;

    public IReadOnlyList<LfsHistoryTransferMode> LfsHistoryModes { get; } = Enum.GetValues<LfsHistoryTransferMode>();

    public IReadOnlyList<RemoteBuildSourceMode> RemoteBuildSourceModes { get; } = Enum.GetValues<RemoteBuildSourceMode>();

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && value is not null)
            {
                _localization.SetLanguage(value.Code);
                ReloadDocumentation();
                if (_currentVpnSetupPlan is not null)
                {
                    ApplyVpnSetupPlan(_currentVpnSetupPlan);
                }
            }
        }
    }

    public string DocumentationSearch
    {
        get => _documentationSearch;
        set
        {
            if (SetProperty(ref _documentationSearch, value))
            {
                FilterDocumentation();
            }
        }
    }

    public DocumentationTopic? SelectedDocumentationTopic
    {
        get => _selectedDocumentationTopic;
        set => SetProperty(ref _selectedDocumentationTopic, value);
    }

    public string CurrentApplicationVersion => _updateService.CurrentVersion.ToString();

    public string LatestApplicationVersion
    {
        get => _latestApplicationVersion;
        private set => SetProperty(ref _latestApplicationVersion, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public string UpdateReleaseNotes
    {
        get => _updateReleaseNotes;
        private set => SetProperty(ref _updateReleaseNotes, value);
    }

    public bool HasUpdateAvailable
    {
        get => _hasUpdateAvailable;
        private set
        {
            if (SetProperty(ref _hasUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(CanInstallUpdate));
            }
        }
    }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingForUpdates, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(CanInstallUpdate));
            }
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (SetProperty(ref _isDownloadingUpdate, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(CanInstallUpdate));
            }
        }
    }

    public double UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    public bool CanCheckForUpdates => !IsCheckingForUpdates && !IsDownloadingUpdate;

    public bool CanInstallUpdate =>
        HasUpdateAvailable && _availableUpdate?.Package is not null && !IsCheckingForUpdates && !IsDownloadingUpdate;

    public string? UpdateReleasePageUrl => _availableUpdate?.ReleasePage.AbsoluteUri;

    public ProjectItemViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedProject));
            if (value is not null && Directory.Exists(value.RootPath) &&
                Directory.EnumerateFiles(value.RootPath, "*.uproject", SearchOption.TopDirectoryOnly).Any())
            {
                UnrealProjectPath = value.RootPath;
                RefreshUnrealInspection();
            }
            _ = LoadSelectedProjectAsync();
        }
    }

    public GitChangeViewModel? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (SetProperty(ref _selectedChange, value))
            {
                _ = LoadSelectedDiffAsync();
            }
        }
    }

    public GitBranch? SelectedBranch
    {
        get => _selectedBranch;
        set => SetProperty(ref _selectedBranch, value);
    }

    public BackupSnapshotViewModel? SelectedBackup
    {
        get => _selectedBackup;
        set => SetProperty(ref _selectedBackup, value);
    }

    public bool HasSelectedProject => SelectedProject is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ToolStatus
    {
        get => _toolStatus;
        private set => SetProperty(ref _toolStatus, value);
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        private set => SetProperty(ref _currentBranch, value);
    }

    public string RepositoryPath
    {
        get => _repositoryPath;
        private set => SetProperty(ref _repositoryPath, value);
    }

    public string ChangeSummary
    {
        get => _changeSummary;
        private set => SetProperty(ref _changeSummary, value);
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set => SetProperty(ref _commitMessage, value);
    }

    public string DiffText
    {
        get => _diffText;
        private set => SetProperty(ref _diffText, value);
    }

    public string LfsPattern
    {
        get => _lfsPattern;
        set => SetProperty(ref _lfsPattern, value);
    }

    public string RemoteUrl
    {
        get => _remoteUrl;
        set => SetProperty(ref _remoteUrl, value);
    }

    public string BackupStorePath
    {
        get => _backupStorePath;
        private set => SetProperty(ref _backupStorePath, value);
    }

    public string ColdArchivePath
    {
        get => _coldArchivePath;
        private set => SetProperty(ref _coldArchivePath, value);
    }

    public string ColdArchiveAfterDays
    {
        get => _coldArchiveAfterDays;
        set => SetProperty(ref _coldArchiveAfterDays, value);
    }

    public string ColdArchiveStatus
    {
        get => _coldArchiveStatus;
        private set => SetProperty(ref _coldArchiveStatus, value);
    }

    public RetentionMode SelectedRetentionMode
    {
        get => _selectedRetentionMode;
        set => SetProperty(ref _selectedRetentionMode, value);
    }

    public string RetentionVersions
    {
        get => _retentionVersions;
        set => SetProperty(ref _retentionVersions, value);
    }

    public string RetentionDays
    {
        get => _retentionDays;
        set => SetProperty(ref _retentionDays, value);
    }

    public string RetentionBudgetGb
    {
        get => _retentionBudgetGb;
        set => SetProperty(ref _retentionBudgetGb, value);
    }

    public ProjectPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public string SyncthingExecutablePath
    {
        get => _syncthingExecutablePath;
        private set => SetProperty(ref _syncthingExecutablePath, value);
    }

    public string SyncState
    {
        get => _syncState;
        private set => SetProperty(ref _syncState, value);
    }

    public string SyncDetails
    {
        get => _syncDetails;
        private set => SetProperty(ref _syncDetails, value);
    }

    public string PeerExchangeText
    {
        get => _peerExchangeText;
        set => SetProperty(ref _peerExchangeText, value);
    }

    public PeerRole SelectedPeerRole
    {
        get => _selectedPeerRole;
        set => SetProperty(ref _selectedPeerRole, value);
    }

    public string PeerVerificationCode
    {
        get => _peerVerificationCode;
        set => SetProperty(ref _peerVerificationCode, value);
    }

    public string AssetBaselinePath
    {
        get => _assetBaselinePath;
        private set => SetProperty(ref _assetBaselinePath, value);
    }

    public string AssetCandidatePath
    {
        get => _assetCandidatePath;
        private set => SetProperty(ref _assetCandidatePath, value);
    }

    public string AssetDiffReport
    {
        get => _assetDiffReport;
        private set => SetProperty(ref _assetDiffReport, value);
    }

    public Bitmap? AssetDiffPreview
    {
        get => _assetDiffPreview;
        private set
        {
            Bitmap? previous = _assetDiffPreview;
            if (SetProperty(ref _assetDiffPreview, value))
            {
                previous?.Dispose();
            }
        }
    }

    public PeerMemberViewModel? SelectedPeerMember
    {
        get => _selectedPeerMember;
        set => SetProperty(ref _selectedPeerMember, value);
    }

    public string ReservationSummary
    {
        get => _reservationSummary;
        private set => SetProperty(ref _reservationSummary, value);
    }

    public string GitGraphSummary
    {
        get => _gitGraphSummary;
        private set => SetProperty(ref _gitGraphSummary, value);
    }

    public string GitGraphCommitLimit
    {
        get => _gitGraphCommitLimit;
        set => SetProperty(ref _gitGraphCommitLimit, value);
    }

    public string GitGraphFileLimit
    {
        get => _gitGraphFileLimit;
        set => SetProperty(ref _gitGraphFileLimit, value);
    }

    public bool GitGraphIncludeAllBranches
    {
        get => _gitGraphIncludeAllBranches;
        set => SetProperty(ref _gitGraphIncludeAllBranches, value);
    }

    public string VpnState
    {
        get => _vpnState;
        private set => SetProperty(ref _vpnState, value);
    }

    public IReadOnlyList<VpnBackendMode> VpnBackendModes { get; } = Enum.GetValues<VpnBackendMode>();

    public VpnBackendMode SelectedVpnBackendMode
    {
        get => _selectedVpnBackendMode;
        set
        {
            if (SetProperty(ref _selectedVpnBackendMode, value))
            {
                UpdateVpnBackendDetails();
            }
        }
    }

    public string VpnBackendDetails
    {
        get => _vpnBackendDetails;
        private set => SetProperty(ref _vpnBackendDetails, value);
    }

    public string VpnDetails
    {
        get => _vpnDetails;
        private set => SetProperty(ref _vpnDetails, value);
    }

    public string WireGuardExecutablePath
    {
        get => _wireGuardExecutablePath;
        private set => SetProperty(ref _wireGuardExecutablePath, value);
    }

    public string VpnNetworkCidr
    {
        get => _vpnNetworkCidr;
        set
        {
            if (SetProperty(ref _vpnNetworkCidr, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public string VpnLocalAddress
    {
        get => _vpnLocalAddress;
        set => SetProperty(ref _vpnLocalAddress, value);
    }

    public string VpnPublicEndpoint
    {
        get => _vpnPublicEndpoint;
        set => SetProperty(ref _vpnPublicEndpoint, value);
    }

    public string VpnListenPort
    {
        get => _vpnListenPort;
        set
        {
            if (SetProperty(ref _vpnListenPort, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public string VpnExchangeText
    {
        get => _vpnExchangeText;
        set => SetProperty(ref _vpnExchangeText, value);
    }

    public string VpnConfigurationPreview
    {
        get => _vpnConfigurationPreview;
        private set => SetProperty(ref _vpnConfigurationPreview, value);
    }

    public VpnNodeCapabilities SelectedVpnCapability
    {
        get => _selectedVpnCapability;
        set => SetProperty(ref _selectedVpnCapability, value);
    }

    public VpnNodeCapabilities SelectedVpnInvitationCapability
    {
        get => _selectedVpnInvitationCapability;
        set => SetProperty(ref _selectedVpnInvitationCapability, value);
    }

    public VpnPeerViewModel? SelectedVpnPeer
    {
        get => _selectedVpnPeer;
        set => SetProperty(ref _selectedVpnPeer, value);
    }

    public bool VpnSetupAcceptIncoming
    {
        get => _vpnSetupAcceptIncoming;
        set
        {
            if (SetProperty(ref _vpnSetupAcceptIncoming, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public bool VpnSetupAllowSwarm
    {
        get => _vpnSetupAllowSwarm;
        set
        {
            if (SetProperty(ref _vpnSetupAllowSwarm, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public bool VpnSetupAllowControlApi
    {
        get => _vpnSetupAllowControlApi;
        set
        {
            if (SetProperty(ref _vpnSetupAllowControlApi, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public bool VpnSetupAllowFileExchange
    {
        get => _vpnSetupAllowFileExchange;
        set
        {
            if (SetProperty(ref _vpnSetupAllowFileExchange, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public bool VpnSetupAllowRemoteBuild
    {
        get => _vpnSetupAllowRemoteBuild;
        set
        {
            if (SetProperty(ref _vpnSetupAllowRemoteBuild, value))
                InvalidateVpnSetupPlan();
        }
    }

    public bool VpnCanApplyFirewall
    {
        get => _vpnCanApplyFirewall;
        private set => SetProperty(ref _vpnCanApplyFirewall, value);
    }

    public bool VpnCanOpenRouter
    {
        get => _vpnCanOpenRouter;
        private set => SetProperty(ref _vpnCanOpenRouter, value);
    }

    public string VpnSetupSummary
    {
        get => _vpnSetupSummary;
        private set => SetProperty(ref _vpnSetupSummary, value);
    }

    public string VpnSetupNetwork
    {
        get => _vpnSetupNetwork;
        private set => SetProperty(ref _vpnSetupNetwork, value);
    }

    public string VpnFirewallStatus
    {
        get => _vpnFirewallStatus;
        private set => SetProperty(ref _vpnFirewallStatus, value);
    }

    public string VpnComputerGuide
    {
        get => _vpnComputerGuide;
        private set => SetProperty(ref _vpnComputerGuide, value);
    }

    public string VpnRouterGuide
    {
        get => _vpnRouterGuide;
        private set => SetProperty(ref _vpnRouterGuide, value);
    }

    public string VpnFirewallCommands
    {
        get => _vpnFirewallCommands;
        private set => SetProperty(ref _vpnFirewallCommands, value);
    }

    public string VpnConnectivityStatus
    {
        get => _vpnConnectivityStatus;
        private set => SetProperty(ref _vpnConnectivityStatus, value);
    }

    public VpnSyncMessageViewModel? SelectedVpnSyncMessage
    {
        get => _selectedVpnSyncMessage;
        set => SetProperty(ref _selectedVpnSyncMessage, value);
    }

    public string VpnSyncStatus
    {
        get => _vpnSyncStatus;
        private set => SetProperty(ref _vpnSyncStatus, value);
    }

    public SwarmNodeRole SelectedSwarmRole
    {
        get => _selectedSwarmRole;
        set
        {
            if (SetProperty(ref _selectedSwarmRole, value))
            {
                OnPropertyChanged(nameof(SwarmHostsCoordinator));
            }
        }
    }

    public bool SwarmHostsCoordinator => SelectedSwarmRole == SwarmNodeRole.CoordinatorAndAgent;

    public string SwarmCoordinatorAddress
    {
        get => _swarmCoordinatorAddress;
        set => SetProperty(ref _swarmCoordinatorAddress, value);
    }

    public string SwarmCoordinatorAlias
    {
        get => _swarmCoordinatorAlias;
        set => SetProperty(ref _swarmCoordinatorAlias, value);
    }

    public string SwarmAgentPath
    {
        get => _swarmAgentPath;
        set => SetProperty(ref _swarmAgentPath, value);
    }

    public string SwarmCoordinatorPath
    {
        get => _swarmCoordinatorPath;
        set => SetProperty(ref _swarmCoordinatorPath, value);
    }

    public string SwarmOptionsPath
    {
        get => _swarmOptionsPath;
        set => SetProperty(ref _swarmOptionsPath, value);
    }

    public string SwarmAgentGroup
    {
        get => _swarmAgentGroup;
        set => SetProperty(ref _swarmAgentGroup, value);
    }

    public string SwarmAllowedGroup
    {
        get => _swarmAllowedGroup;
        set => SetProperty(ref _swarmAllowedGroup, value);
    }

    public string SwarmAllowedAgents
    {
        get => _swarmAllowedAgents;
        set => SetProperty(ref _swarmAllowedAgents, value);
    }

    public string SwarmCacheFolder
    {
        get => _swarmCacheFolder;
        set => SetProperty(ref _swarmCacheFolder, value);
    }

    public string SwarmStatus
    {
        get => _swarmStatus;
        private set => SetProperty(ref _swarmStatus, value);
    }

    public string SwarmDiagnostic
    {
        get => _swarmDiagnostic;
        private set => SetProperty(ref _swarmDiagnostic, value);
    }

    public string VpnFileListenPort
    {
        get => _vpnFileListenPort;
        set
        {
            if (SetProperty(ref _vpnFileListenPort, value))
            {
                InvalidateVpnSetupPlan();
            }
        }
    }

    public string VpnFileInboxPath
    {
        get => _vpnFileInboxPath;
        set => SetProperty(ref _vpnFileInboxPath, value);
    }

    public string VpnFileSharedFolderPath
    {
        get => _vpnFileSharedFolderPath;
        set => SetProperty(ref _vpnFileSharedFolderPath, value);
    }

    public string VpnFileAccessToken
    {
        get => _vpnFileAccessToken;
        set => SetProperty(ref _vpnFileAccessToken, value);
    }

    public bool VpnFileAllowReceive
    {
        get => _vpnFileAllowReceive;
        set => SetProperty(ref _vpnFileAllowReceive, value);
    }

    public bool VpnFileAllowBrowse
    {
        get => _vpnFileAllowBrowse;
        set => SetProperty(ref _vpnFileAllowBrowse, value);
    }

    public bool VpnFileAllowDownload
    {
        get => _vpnFileAllowDownload;
        set => SetProperty(ref _vpnFileAllowDownload, value);
    }

    public bool VpnFileStartAutomatically
    {
        get => _vpnFileStartAutomatically;
        set => SetProperty(ref _vpnFileStartAutomatically, value);
    }

    public VpnPeerViewModel? SelectedVpnFilePeer
    {
        get => _selectedVpnFilePeer;
        set => SetProperty(ref _selectedVpnFilePeer, value);
    }

    public VpnSharedFileViewModel? SelectedVpnSharedFile
    {
        get => _selectedVpnSharedFile;
        set => SetProperty(ref _selectedVpnSharedFile, value);
    }

    public string VpnFileStatus
    {
        get => _vpnFileStatus;
        private set => SetProperty(ref _vpnFileStatus, value);
    }

    public bool VpnFileHostRunning => _vpnFileExchangeHost?.IsRunning == true;

    public string DiscordWebhookUrl
    {
        get => _discordWebhookUrl;
        set => SetProperty(ref _discordWebhookUrl, value);
    }

    public string DiscordDisplayName
    {
        get => _discordDisplayName;
        set => SetProperty(ref _discordDisplayName, value);
    }

    public string DiscordProjectLabel
    {
        get => _discordProjectLabel;
        set => SetProperty(ref _discordProjectLabel, value);
    }

    public string DiscordRepositoryWebUrl
    {
        get => _discordRepositoryWebUrl;
        set => SetProperty(ref _discordRepositoryWebUrl, value);
    }

    public string DiscordPollIntervalSeconds
    {
        get => _discordPollIntervalSeconds;
        set => SetProperty(ref _discordPollIntervalSeconds, value);
    }

    public bool DiscordNotifyCommits
    {
        get => _discordNotifyCommits;
        set => SetProperty(ref _discordNotifyCommits, value);
    }

    public bool DiscordNotifyBranchChanges
    {
        get => _discordNotifyBranchChanges;
        set => SetProperty(ref _discordNotifyBranchChanges, value);
    }

    public bool DiscordStartAutomatically
    {
        get => _discordStartAutomatically;
        set => SetProperty(ref _discordStartAutomatically, value);
    }

    public bool DiscordWebhookConfigured
    {
        get => _discordWebhookConfigured;
        private set
        {
            if (SetProperty(ref _discordWebhookConfigured, value))
            {
                OnPropertyChanged(nameof(DiscordWebhookHint));
            }
        }
    }

    public string DiscordWebhookHint => DiscordWebhookConfigured
        ? "Webhook configured — leave blank to keep it, or paste a new URL to replace it."
        : "Paste the incoming webhook URL created for the target Discord channel.";

    public bool DiscordIsRunning
    {
        get => _discordIsRunning;
        private set => SetProperty(ref _discordIsRunning, value);
    }

    public string DiscordAgentState
    {
        get => _discordAgentState;
        private set => SetProperty(ref _discordAgentState, value);
    }

    public string DiscordAgentDetails
    {
        get => _discordAgentDetails;
        private set => SetProperty(ref _discordAgentDetails, value);
    }

    public string DiscordLastActivity
    {
        get => _discordLastActivity;
        private set => SetProperty(ref _discordLastActivity, value);
    }

    public IReadOnlyList<DiscordAgentExecutionMode> DiscordExecutionModes { get; } =
        Enum.GetValues<DiscordAgentExecutionMode>();

    public DiscordAgentExecutionMode SelectedDiscordExecutionMode
    {
        get => _selectedDiscordExecutionMode;
        set
        {
            if (SetProperty(ref _selectedDiscordExecutionMode, value))
            {
                OnPropertyChanged(nameof(DiscordUsesAutonomousAgent));
                DiscordConnectionSummary = value == DiscordAgentExecutionMode.Integrated
                    ? "Integrated agent — runs only while the desktop application is open."
                    : "Autonomous agent — controlled over its authenticated network API.";
            }
        }
    }

    public bool DiscordUsesAutonomousAgent =>
        SelectedDiscordExecutionMode == DiscordAgentExecutionMode.Autonomous;

    public string DiscordAgentEndpoint
    {
        get => _discordAgentEndpoint;
        set => SetProperty(ref _discordAgentEndpoint, value);
    }

    public string DiscordAgentApiToken
    {
        get => _discordAgentApiToken;
        set => SetProperty(ref _discordAgentApiToken, value);
    }

    public string DiscordAgentRepositoryPath
    {
        get => _discordAgentRepositoryPath;
        set => SetProperty(ref _discordAgentRepositoryPath, value);
    }

    public bool DiscordAllowPrivateHttp
    {
        get => _discordAllowPrivateHttp;
        set => SetProperty(ref _discordAllowPrivateHttp, value);
    }

    public bool DiscordControlTokenConfigured
    {
        get => _discordControlTokenConfigured;
        private set
        {
            if (SetProperty(ref _discordControlTokenConfigured, value))
            {
                OnPropertyChanged(nameof(DiscordControlTokenHint));
            }
        }
    }

    public string DiscordControlTokenHint => DiscordControlTokenConfigured
        ? "Control token saved locally — leave blank to keep it."
        : "Paste the token printed by CyRevision.Discord.Agent.";

    public string DiscordConnectionSummary
    {
        get => _discordConnectionSummary;
        private set => SetProperty(ref _discordConnectionSummary, value);
    }

    public PluginItemViewModel? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            if (SetProperty(ref _selectedPlugin, value))
            {
                OnPropertyChanged(nameof(CanEnableSelectedPlugin));
                OnPropertyChanged(nameof(CanDisableSelectedPlugin));
            }
        }
    }

    public bool CanEnableSelectedPlugin => SelectedPlugin is { IsEnabled: false };

    public bool CanDisableSelectedPlugin => SelectedPlugin is { IsEnabled: true };

    public bool IsUnrealIntegrationEnabled
    {
        get => _isUnrealIntegrationEnabled;
        private set => SetProperty(ref _isUnrealIntegrationEnabled, value);
    }

    public string UnrealProjectPath
    {
        get => _unrealProjectPath;
        private set => SetProperty(ref _unrealProjectPath, value);
    }

    public string UnrealPluginSummary
    {
        get => _unrealPluginSummary;
        private set => SetProperty(ref _unrealPluginSummary, value);
    }

    public string UnrealBridgeSummary
    {
        get => _unrealBridgeSummary;
        private set => SetProperty(ref _unrealBridgeSummary, value);
    }

    public string UnrealEditorPluginVersion => _unrealProjectInspection?.BundledPluginVersion ?? "—";

    public string UnrealInstalledPluginVersion => _unrealProjectInspection?.InstalledPluginVersion ?? "Not installed";

    public bool CanInstallUnrealEditorPlugin =>
        IsUnrealIntegrationEnabled && _unrealProjectInspection?.IsValid == true;

    public CodeTreeNode? SelectedCodeNode
    {
        get => _selectedCodeNode;
        set
        {
            if (SetProperty(ref _selectedCodeNode, value))
            {
                _ = LoadCodeNodeAsync(value);
            }
        }
    }

    public CodeSearchResult? SelectedCodeSearchResult
    {
        get => _selectedCodeSearchResult;
        set
        {
            if (SetProperty(ref _selectedCodeSearchResult, value) && value is not null)
            {
                _ = OpenCodeSearchResultAsync(value);
            }
        }
    }

    public CodeSymbol? SelectedCodeSymbol
    {
        get => _selectedCodeSymbol;
        set => SetProperty(ref _selectedCodeSymbol, value);
    }

    public string CodeTreeFilter
    {
        get => _codeTreeFilter;
        set => SetProperty(ref _codeTreeFilter, value);
    }

    public bool CodeIncludeHidden
    {
        get => _codeIncludeHidden;
        set => SetProperty(ref _codeIncludeHidden, value);
    }

    public string CodeWorkspaceSummary
    {
        get => _codeWorkspaceSummary;
        private set => SetProperty(ref _codeWorkspaceSummary, value);
    }

    public string CodeSearchQuery
    {
        get => _codeSearchQuery;
        set => SetProperty(ref _codeSearchQuery, value);
    }

    public string CodeFilePatterns
    {
        get => _codeFilePatterns;
        set => SetProperty(ref _codeFilePatterns, value);
    }

    public bool CodeSearchMatchCase
    {
        get => _codeSearchMatchCase;
        set => SetProperty(ref _codeSearchMatchCase, value);
    }

    public bool CodeSearchWholeWord
    {
        get => _codeSearchWholeWord;
        set => SetProperty(ref _codeSearchWholeWord, value);
    }

    public bool CodeSearchRegex
    {
        get => _codeSearchRegex;
        set => SetProperty(ref _codeSearchRegex, value);
    }

    public string CodeSearchSummary
    {
        get => _codeSearchSummary;
        private set => SetProperty(ref _codeSearchSummary, value);
    }

    public string CodePreviewText
    {
        get => _codePreviewText;
        private set => SetProperty(ref _codePreviewText, value);
    }

    public string CodePreviewSummary
    {
        get => _codePreviewSummary;
        private set => SetProperty(ref _codePreviewSummary, value);
    }

    public string CodeSelectionSummary
    {
        get => _codeSelectionSummary;
        private set => SetProperty(ref _codeSelectionSummary, value);
    }

    public bool IsAiIntegrationEnabled
    {
        get => _isAiIntegrationEnabled;
        private set => SetProperty(ref _isAiIntegrationEnabled, value);
    }

    public AiProviderDescriptor? SelectedAiProvider
    {
        get => _selectedAiProvider;
        set
        {
            if (!SetProperty(ref _selectedAiProvider, value) || value is null) return;
            AiModel = value.DefaultModel;
            AiEndpoint = value.DefaultEndpoint;
            OnPropertyChanged(nameof(AiProviderDescription));
            OnPropertyChanged(nameof(AiProviderRequiresKey));
            OnPropertyChanged(nameof(AiProviderSupportsEdits));
        }
    }

    public string AiProviderDescription => SelectedAiProvider?.Description ?? "No provider selected.";

    public bool AiProviderRequiresKey => SelectedAiProvider?.RequiresApiKey == true;

    public bool AiProviderSupportsEdits => SelectedAiProvider?.SupportsWorkspaceEdits == true;

    public string AiModel
    {
        get => _aiModel;
        set => SetProperty(ref _aiModel, value);
    }

    public string AiEndpoint
    {
        get => _aiEndpoint;
        set => SetProperty(ref _aiEndpoint, value);
    }

    public string AiApiKey
    {
        get => _aiApiKey;
        set => SetProperty(ref _aiApiKey, value);
    }

    public string AiExecutablePath
    {
        get => _aiExecutablePath;
        set => SetProperty(ref _aiExecutablePath, value);
    }

    public string AiPrompt
    {
        get => _aiPrompt;
        set => SetProperty(ref _aiPrompt, value);
    }

    public string AiResponse
    {
        get => _aiResponse;
        private set => SetProperty(ref _aiResponse, value);
    }

    public string AiStatus
    {
        get => _aiStatus;
        private set => SetProperty(ref _aiStatus, value);
    }

    public bool AiAllowModify
    {
        get => _aiAllowModify;
        set
        {
            if (!SetProperty(ref _aiAllowModify, value)) return;
            if (!value)
            {
                AiStageAfterRun = false;
                AiCommitAfterRun = false;
            }
        }
    }

    public bool AiAllowNetwork
    {
        get => _aiAllowNetwork;
        set => SetProperty(ref _aiAllowNetwork, value);
    }

    public bool AiStageAfterRun
    {
        get => _aiStageAfterRun;
        set
        {
            if (value && !AiAllowModify) AiAllowModify = true;
            if (!SetProperty(ref _aiStageAfterRun, value)) return;
            if (!value) AiCommitAfterRun = false;
        }
    }

    public bool AiCommitAfterRun
    {
        get => _aiCommitAfterRun;
        set
        {
            if (value)
            {
                if (!AiAllowModify) AiAllowModify = true;
                if (!AiStageAfterRun) AiStageAfterRun = true;
            }
            SetProperty(ref _aiCommitAfterRun, value);
        }
    }

    public string AiCommitMessage
    {
        get => _aiCommitMessage;
        set => SetProperty(ref _aiCommitMessage, value);
    }

    public AiMcpServerViewModel? SelectedAiMcpServer
    {
        get => _selectedAiMcpServer;
        set
        {
            if (SetProperty(ref _selectedAiMcpServer, value))
            {
                OnPropertyChanged(nameof(HasSelectedAiMcpServer));
            }
        }
    }

    public bool HasSelectedAiMcpServer => SelectedAiMcpServer is not null;

    public bool AiMcpEnabled
    {
        get => _aiMcpEnabled;
        set
        {
            if (SetProperty(ref _aiMcpEnabled, value)) UpdateAiMcpStatus();
        }
    }

    public bool AiMcpEmergencyBlocked
    {
        get => _aiMcpEmergencyBlocked;
        private set
        {
            if (SetProperty(ref _aiMcpEmergencyBlocked, value))
            {
                OnPropertyChanged(nameof(AiMcpEmergencyState));
                UpdateAiMcpStatus();
            }
        }
    }

    public string AiMcpEmergencyState => AiMcpEmergencyBlocked ? "BLOCKED" : "READY";

    public bool AiMcpBlockUnmanagedServers
    {
        get => _aiMcpBlockUnmanagedServers;
        set
        {
            if (SetProperty(ref _aiMcpBlockUnmanagedServers, value)) UpdateAiMcpStatus();
        }
    }

    public string AiMcpStatus
    {
        get => _aiMcpStatus;
        private set => SetProperty(ref _aiMcpStatus, value);
    }

    public PullRequestSummary? SelectedPullRequest
    {
        get => _selectedPullRequest;
        set
        {
            if (SetProperty(ref _selectedPullRequest, value))
            {
                OnPropertyChanged(nameof(HasSelectedPullRequest));
                OnPropertyChanged(nameof(CanMergeSelectedPullRequest));
                _ = LoadSelectedPullRequestAsync(value);
            }
        }
    }

    public PullRequestFile? SelectedPullRequestFile
    {
        get => _selectedPullRequestFile;
        set
        {
            if (SetProperty(ref _selectedPullRequestFile, value))
            {
                PullRequestPatch = value is null
                    ? "Select a changed file to inspect its patch."
                    : string.IsNullOrWhiteSpace(value.Patch)
                        ? "GitHub did not provide a text patch for this file. It may be binary or too large."
                        : value.Patch;
            }
        }
    }

    public PullRequestStateFilter PullRequestStateFilter
    {
        get => _pullRequestStateFilter;
        set => SetProperty(ref _pullRequestStateFilter, value);
    }

    public PullRequestMergeMethod PullRequestMergeMethod
    {
        get => _pullRequestMergeMethod;
        set => SetProperty(ref _pullRequestMergeMethod, value);
    }

    public PullRequestReviewAction PullRequestReviewAction
    {
        get => _pullRequestReviewAction;
        set => SetProperty(ref _pullRequestReviewAction, value);
    }

    public string PullRequestStatus
    {
        get => _pullRequestStatus;
        private set => SetProperty(ref _pullRequestStatus, value);
    }

    public string PullRequestApiBaseUrl
    {
        get => _pullRequestApiBaseUrl;
        set => SetProperty(ref _pullRequestApiBaseUrl, value);
    }

    public string PullRequestToken
    {
        get => _pullRequestToken;
        set
        {
            if (SetProperty(ref _pullRequestToken, value))
                OnPropertyChanged(nameof(PullRequestAuthenticationState));
        }
    }

    public string PullRequestTokenEnvironmentVariable
    {
        get => _pullRequestTokenEnvironmentVariable;
        set
        {
            if (SetProperty(ref _pullRequestTokenEnvironmentVariable, value))
                OnPropertyChanged(nameof(PullRequestAuthenticationState));
        }
    }

    public string PullRequestAuthenticationState => !string.IsNullOrWhiteSpace(PullRequestToken)
        ? "Session token ready · never persisted"
        : !string.IsNullOrWhiteSpace(ResolvePullRequestToken())
            ? $"Using environment variable {PullRequestTokenEnvironmentVariable.Trim()}"
            : "Public read only · add a token for write operations";

    public string PullRequestRepositoryName => _pullRequestRepository?.FullName ?? "No supported remote detected";

    public bool HasSelectedPullRequest => SelectedPullRequest is not null;

    public bool CanMergeSelectedPullRequest =>
        SelectedPullRequest is { IsMerged: false } pull && pull.State.Equals("open", StringComparison.OrdinalIgnoreCase);

    public string PullRequestDetailsSummary => _selectedPullRequestDetails?.ChangeSummary ?? "Select a pull request.";

    public string PullRequestBody => string.IsNullOrWhiteSpace(_selectedPullRequestDetails?.Body)
        ? "No description provided."
        : _selectedPullRequestDetails.Body;

    public string PullRequestPatch
    {
        get => _pullRequestPatch;
        private set => SetProperty(ref _pullRequestPatch, value);
    }

    public string NewPullRequestTitle
    {
        get => _newPullRequestTitle;
        set => SetProperty(ref _newPullRequestTitle, value);
    }

    public string NewPullRequestBody
    {
        get => _newPullRequestBody;
        set => SetProperty(ref _newPullRequestBody, value);
    }

    public string NewPullRequestHeadBranch
    {
        get => _newPullRequestHeadBranch;
        set => SetProperty(ref _newPullRequestHeadBranch, value);
    }

    public string NewPullRequestBaseBranch
    {
        get => _newPullRequestBaseBranch;
        set => SetProperty(ref _newPullRequestBaseBranch, value);
    }

    public bool NewPullRequestIsDraft
    {
        get => _newPullRequestIsDraft;
        set => SetProperty(ref _newPullRequestIsDraft, value);
    }

    public string PullRequestComment
    {
        get => _pullRequestComment;
        set => SetProperty(ref _pullRequestComment, value);
    }

    public string PullRequestReviewBody
    {
        get => _pullRequestReviewBody;
        set => SetProperty(ref _pullRequestReviewBody, value);
    }

    public async Task InitializeAsync()
    {
        await _pluginManager.InitializeAsync();
        RefreshPluginCatalog();
        await RunOperationAsync("Chargement des projets…", async () =>
        {
            GitToolAvailability availability = await _gitService.GetToolAvailabilityAsync();
            ToolStatus = availability.GitAvailable
                ? $"{availability.GitVersion} · {(availability.LfsAvailable ? availability.LfsVersion : "Git LFS absent")}"
                : "Git est introuvable";

            IReadOnlyList<ProjectDefinition> definitions = await _projectCatalog.GetAllAsync();
            Projects.Clear();
            foreach (ProjectDefinition definition in definitions.OrderByDescending(project => project.LastOpenedAt))
            {
                if (Projects.Any(project => ProjectPathsEqual(project.RootPath, definition.RootPath)))
                {
                    continue;
                }
                Projects.Add(new ProjectItemViewModel(definition));
            }

            if (!string.IsNullOrWhiteSpace(_initialProjectPath) && Directory.Exists(_initialProjectPath))
            {
                string fullPath = Path.GetFullPath(_initialProjectPath);
                ProjectItemViewModel? known = Projects.FirstOrDefault(project =>
                    ProjectPathsEqual(project.RootPath, fullPath));
                if (known is null)
                {
                    if (Directory.Exists(Path.Combine(fullPath, ".git")))
                    {
                        GitRepositoryStatus repository = await _gitService.GetStatusAsync(fullPath);
                        ProjectDefinition definition = CreateGitProject(repository.RootPath);
                        await _projectCatalog.UpsertAsync(definition);
                        known = new ProjectItemViewModel(definition);
                        Projects.Insert(0, known);
                    }
                    else
                    {
                        ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == ProjectPresetKind.SyncOnly);
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        ProjectDefinition definition = new(
                            Guid.NewGuid(),
                            new DirectoryInfo(fullPath).Name,
                            fullPath,
                            preset.Features,
                            preset.Retention,
                            CreatedAt: now,
                            LastOpenedAt: now);
                        await _projectCatalog.UpsertAsync(definition);
                        known = new ProjectItemViewModel(definition);
                        Projects.Insert(0, known);
                    }
                }

                SelectedProject = known;
            }

            if (SelectedProject is null && Projects.Count > 0)
            {
                SelectedProject = Projects[0];
            }
        }, "Prêt");

        _ = CheckForUpdatesAsync();
    }

    public async Task RefreshCodeWorkspaceAsync()
    {
        if (SelectedProject is null)
        {
            ClearCodeWorkspace();
            return;
        }

        CodeWorkspaceSummary = "Indexing workspace…";
        try
        {
            CodeWorkspaceSnapshot snapshot = await _codeWorkspaceService.BuildTreeAsync(
                SelectedProject.RootPath,
                CodeTreeFilter,
                CodeIncludeHidden);
            ReplaceCollection(CodeTree, snapshot.Roots);
            CodeWorkspaceSummary = $"{snapshot.FileCount:N0} files · {snapshot.DirectoryCount:N0} folders · " +
                                   $"indexed in {snapshot.Elapsed.TotalMilliseconds:N0} ms" +
                                   (snapshot.WasTruncated ? " · safety limit reached" : string.Empty);
            SelectedCodeNode = FindFirstFile(CodeTree);
        }
        catch (Exception exception)
        {
            CodeWorkspaceSummary = exception.Message;
        }
    }

    public async Task SearchCodeAsync()
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(CodeSearchQuery))
        {
            CodeSearchSummary = "Enter a search term and select a project.";
            return;
        }

        _codeSearchCancellation?.Cancel();
        _codeSearchCancellation?.Dispose();
        _codeSearchCancellation = new CancellationTokenSource();
        CancellationToken token = _codeSearchCancellation.Token;
        CodeSearchSummary = "Searching…";
        try
        {
            CodeSearchReport report = await _codeWorkspaceService.SearchAsync(
                SelectedProject.RootPath,
                CodeSearchQuery,
                new CodeSearchOptions(
                    CodeSearchMatchCase,
                    CodeSearchWholeWord,
                    CodeSearchRegex,
                    CodeIncludeHidden,
                    CodeFilePatterns),
                token);
            ReplaceCollection(CodeSearchResults, report.Results);
            CodeSearchSummary = $"{report.Results.Count:N0} result(s) in {report.FilesScanned:N0} file(s) · " +
                                $"{report.Elapsed.TotalMilliseconds:N0} ms · " +
                                (report.UsedRipgrep ? "ripgrep engine" : "managed fallback") +
                                (report.WasTruncated ? " · result limit reached" : string.Empty);
            SelectedCodeSearchResult = CodeSearchResults.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            CodeSearchSummary = "Search cancelled.";
        }
        catch (Exception exception)
        {
            CodeSearchSummary = exception.Message;
        }
    }

    public void CancelCodeSearch() => _codeSearchCancellation?.Cancel();

    public async Task LoadCodeSelectionHistoryAsync(int selectionStart, int selectionEnd)
    {
        if (SelectedProject is null || SelectedCodeNode is not { IsDirectory: false } node)
        {
            CodeSelectionSummary = "Select a file and one or more lines first.";
            return;
        }

        CodeSelection selection = CodeWorkspaceService.SelectionFromOffsets(
            CodePreviewText,
            selectionStart,
            selectionEnd);
        CodeSelectionSummary = $"Following Git history for lines {selection.StartLine}–{selection.EndLine}…";
        try
        {
            IReadOnlyList<CodeHistoryEntry> history = await _codeWorkspaceService.GetHistoryAsync(
                SelectedProject.RootPath,
                node.RelativePath,
                selection);
            ReplaceCollection(CodeHistory, history);
            CodeSelectionSummary = history.Count == 0
                ? $"No Git history found for lines {selection.StartLine}–{selection.EndLine}."
                : $"{history.Count} revision(s) affected lines {selection.StartLine}–{selection.EndLine}.";
        }
        catch (Exception exception)
        {
            CodeSelectionSummary = exception.Message;
        }
    }

    public async Task ResolvePullRequestRepositoryAsync()
    {
        ClearPullRequestData(clearConnection: false);
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled)
        {
            PullRequestStatus = "Select a Git project first.";
            return;
        }

        string? remoteUrl = await _gitService.GetRemoteUrlAsync(SelectedProject.RootPath);
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            _pullRequestRepository = null;
            PullRequestStatus = "The origin remote is not configured.";
            OnPropertyChanged(nameof(PullRequestRepositoryName));
            return;
        }

        if (string.IsNullOrWhiteSpace(RemoteUrl)) RemoteUrl = remoteUrl;
        if (!_pullRequestService.TryResolveRepository(
                remoteUrl,
                string.IsNullOrWhiteSpace(PullRequestApiBaseUrl) ? null : PullRequestApiBaseUrl,
                out PullRequestRepository? repository) || repository is null)
        {
            _pullRequestRepository = null;
            PullRequestStatus = "This remote is not supported yet. GitLab and Forgejo will use provider adapters.";
            OnPropertyChanged(nameof(PullRequestRepositoryName));
            return;
        }

        _pullRequestRepository = repository;
        if (string.IsNullOrWhiteSpace(NewPullRequestHeadBranch)) NewPullRequestHeadBranch = CurrentBranch;
        if (string.IsNullOrWhiteSpace(NewPullRequestBaseBranch)) NewPullRequestBaseBranch = "main";
        PullRequestStatus = $"{repository.Provider} repository detected · click Refresh to load pull requests.";
        OnPropertyChanged(nameof(PullRequestRepositoryName));
        OnPropertyChanged(nameof(PullRequestAuthenticationState));
    }

    public async Task RefreshPullRequestsAsync()
    {
        if (_pullRequestRepository is null) await ResolvePullRequestRepositoryAsync();
        if (_pullRequestRepository is null) return;
        await RunOperationAsync("Loading pull requests…", async () =>
        {
            try
            {
                await RefreshPullRequestsCoreAsync();
            }
            catch (Exception exception)
            {
                PullRequestStatus = exception.Message;
                throw;
            }
        }, "Pull requests loaded");
    }

    public async Task CreatePullRequestAsync()
    {
        if (_pullRequestRepository is null) await ResolvePullRequestRepositoryAsync();
        if (_pullRequestRepository is null) return;
        if (!TryGetPullRequestWriteToken(out string token)) return;
        CreatePullRequestRequest request = new(
            NewPullRequestTitle,
            NewPullRequestBody,
            NewPullRequestHeadBranch,
            NewPullRequestBaseBranch,
            NewPullRequestIsDraft);
        await RunOperationAsync("Creating pull request…", async () =>
        {
            try
            {
                PullRequestSummary created = await _pullRequestService.CreateAsync(
                    _pullRequestRepository, request, token);
                NewPullRequestTitle = string.Empty;
                NewPullRequestBody = string.Empty;
                NewPullRequestIsDraft = false;
                await RefreshPullRequestsCoreAsync(created.Number);
                PullRequestStatus = $"Pull request #{created.Number} created.";
            }
            catch (Exception exception)
            {
                PullRequestStatus = exception.Message;
                throw;
            }
        }, "Pull request created");
    }

    public async Task AddPullRequestCommentAsync()
    {
        if (_pullRequestRepository is null || SelectedPullRequest is null) return;
        if (!TryGetPullRequestWriteToken(out string token)) return;
        string body = PullRequestComment;
        await RunOperationAsync("Publishing pull request comment…", async () =>
        {
            try
            {
                await _pullRequestService.AddCommentAsync(
                    _pullRequestRepository, SelectedPullRequest.Number, body, token);
                PullRequestComment = string.Empty;
                await LoadSelectedPullRequestAsync(SelectedPullRequest);
                PullRequestStatus = "Comment published.";
            }
            catch (Exception exception)
            {
                PullRequestStatus = exception.Message;
                throw;
            }
        }, "Comment published");
    }

    public async Task SubmitPullRequestReviewAsync()
    {
        if (_pullRequestRepository is null || SelectedPullRequest is null) return;
        if (!TryGetPullRequestWriteToken(out string token)) return;
        string body = PullRequestReviewBody;
        CyRevision.PullRequests.PullRequestReviewAction action = this.PullRequestReviewAction;
        await RunOperationAsync("Submitting pull request review…", async () =>
        {
            try
            {
                await _pullRequestService.SubmitReviewAsync(
                    _pullRequestRepository, SelectedPullRequest.Number, body, action, token);
                PullRequestReviewBody = string.Empty;
                await LoadSelectedPullRequestAsync(SelectedPullRequest);
                PullRequestStatus = $"{action} review submitted.";
            }
            catch (Exception exception)
            {
                PullRequestStatus = exception.Message;
                throw;
            }
        }, "Review submitted");
    }

    public async Task MergeSelectedPullRequestAsync()
    {
        if (_pullRequestRepository is null || SelectedPullRequest is null) return;
        if (!TryGetPullRequestWriteToken(out string token)) return;
        int number = SelectedPullRequest.Number;
        CyRevision.PullRequests.PullRequestMergeMethod method = this.PullRequestMergeMethod;
        await RunOperationAsync($"Merging pull request #{number}…", async () =>
        {
            try
            {
                MergePullRequestResult result = await _pullRequestService.MergeAsync(
                    _pullRequestRepository, number, method, token);
                await RefreshPullRequestsCoreAsync(number);
                PullRequestStatus = result.Message;
            }
            catch (Exception exception)
            {
                PullRequestStatus = exception.Message;
                throw;
            }
        }, $"Pull request #{number} merged");
    }

    public async Task ToggleSelectedPullRequestStateAsync()
    {
        if (_pullRequestRepository is null || SelectedPullRequest is null) return;
        if (!TryGetPullRequestWriteToken(out string token)) return;
        int number = SelectedPullRequest.Number;
        bool open = !SelectedPullRequest.State.Equals("open", StringComparison.OrdinalIgnoreCase);
        await RunOperationAsync(open ? "Reopening pull request…" : "Closing pull request…", async () =>
        {
            try
            {
                await _pullRequestService.SetStateAsync(_pullRequestRepository, number, open, token);
                await RefreshPullRequestsCoreAsync(number);
                PullRequestStatus = open ? "Pull request reopened." : "Pull request closed.";
            }
            catch (Exception exception)
            {
                PullRequestStatus = exception.Message;
                throw;
            }
        }, open ? "Pull request reopened" : "Pull request closed");
    }

    public async Task CheckoutSelectedPullRequestAsync()
    {
        if (SelectedProject is null || SelectedPullRequest is null) return;
        int number = SelectedPullRequest.Number;
        await RunOperationAsync($"Checking out pull request #{number}…", async () =>
        {
            GitRepositoryStatus status = await _gitService.GetStatusAsync(SelectedProject.RootPath);
            if (status.Changes.Count > 0)
                throw new InvalidOperationException("Commit, stash, or discard working-tree changes before checking out a pull request.");
            string remoteReference = $"refs/remotes/cyrevision/pull/{number}";
            await _gitService.FetchReferenceAsync(
                SelectedProject.RootPath,
                "origin",
                $"+pull/{number}/head:{remoteReference}");
            string localBranch = $"cyrevision/pr-{number}";
            IReadOnlyList<GitBranch> branches = await _gitService.GetBranchesAsync(SelectedProject.RootPath);
            if (branches.Any(branch => !branch.IsRemote && branch.Name.Equals(localBranch, StringComparison.Ordinal)))
            {
                await _gitService.CheckoutBranchAsync(SelectedProject.RootPath, localBranch);
                await _gitService.FastForwardAsync(SelectedProject.RootPath, remoteReference);
            }
            else
            {
                await _gitService.CreateBranchFromAsync(SelectedProject.RootPath, localBranch, remoteReference);
            }
            await RefreshCoreAsync();
            PullRequestStatus = $"Pull request #{number} checked out as {localBranch}.";
        }, $"Pull request #{number} checked out");
    }

    public void OpenSelectedPullRequestInBrowser()
    {
        if (SelectedPullRequest is null) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedPullRequest.WebUrl.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private async Task RefreshPullRequestsCoreAsync(int? selectNumber = null)
    {
        if (_pullRequestRepository is null) return;
        int? previousNumber = selectNumber ?? SelectedPullRequest?.Number;
        IReadOnlyList<PullRequestSummary> pulls = await _pullRequestService.ListAsync(
            _pullRequestRepository,
            PullRequestStateFilter,
            ResolvePullRequestToken());
        ReplaceCollection(PullRequests, pulls);
        PullRequestStatus = $"{pulls.Count} pull request(s) · {_pullRequestRepository.FullName} · {PullRequestStateFilter}";
        SelectedPullRequest = previousNumber is int number
            ? PullRequests.FirstOrDefault(pull => pull.Number == number) ?? PullRequests.FirstOrDefault()
            : PullRequests.FirstOrDefault();
    }

    private async Task LoadSelectedPullRequestAsync(PullRequestSummary? pullRequest)
    {
        int loadVersion = Interlocked.Increment(ref _pullRequestDetailsLoadVersion);
        if (_pullRequestRepository is null || pullRequest is null)
        {
            ClearPullRequestDetails();
            return;
        }

        PullRequestStatus = $"Loading pull request #{pullRequest.Number}…";
        try
        {
            PullRequestDetails details = await _pullRequestService.GetDetailsAsync(
                _pullRequestRepository,
                pullRequest.Number,
                ResolvePullRequestToken());
            if (loadVersion != _pullRequestDetailsLoadVersion || SelectedPullRequest?.Number != pullRequest.Number) return;
            _selectedPullRequestDetails = details;
            ReplaceCollection(PullRequestFiles, details.Files);
            ReplaceCollection(PullRequestReviews, details.Reviews);
            ReplaceCollection(PullRequestComments, details.Comments);
            SelectedPullRequestFile = PullRequestFiles.FirstOrDefault();
            OnPropertyChanged(nameof(PullRequestDetailsSummary));
            OnPropertyChanged(nameof(PullRequestBody));
            OnPropertyChanged(nameof(CanMergeSelectedPullRequest));
            PullRequestStatus = $"Pull request #{pullRequest.Number} loaded.";
        }
        catch (Exception exception)
        {
            if (loadVersion == _pullRequestDetailsLoadVersion) PullRequestStatus = exception.Message;
        }
    }

    private string? ResolvePullRequestToken()
    {
        if (!string.IsNullOrWhiteSpace(PullRequestToken)) return PullRequestToken.Trim();
        if (string.IsNullOrWhiteSpace(PullRequestTokenEnvironmentVariable)) return null;
        try
        {
            return Environment.GetEnvironmentVariable(PullRequestTokenEnvironmentVariable.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private bool TryGetPullRequestWriteToken(out string token)
    {
        token = ResolvePullRequestToken() ?? string.Empty;
        if (token.Length > 0) return true;

        PullRequestStatus = "A session token or token environment variable is required for this operation.";
        StatusMessage = PullRequestStatus;
        return false;
    }

    private void ClearPullRequestData(bool clearConnection = true)
    {
        Interlocked.Increment(ref _pullRequestDetailsLoadVersion);
        PullRequests.Clear();
        ClearPullRequestDetails();
        SelectedPullRequest = null;
        if (clearConnection)
        {
            _pullRequestRepository = null;
            PullRequestToken = string.Empty;
            PullRequestApiBaseUrl = string.Empty;
            PullRequestStatus = "Select a GitHub-backed project to manage pull requests.";
            OnPropertyChanged(nameof(PullRequestRepositoryName));
        }
    }

    private void ClearPullRequestDetails()
    {
        _selectedPullRequestDetails = null;
        PullRequestFiles.Clear();
        PullRequestReviews.Clear();
        PullRequestComments.Clear();
        SelectedPullRequestFile = null;
        OnPropertyChanged(nameof(PullRequestDetailsSummary));
        OnPropertyChanged(nameof(PullRequestBody));
    }

    public void AddAiMcpServer(AiMcpTransport transport)
    {
        AiMcpServerViewModel server = AiMcpServerViewModel.Create(transport, AiMcpServers.Count + 1);
        AiMcpServers.Add(server);
        SelectedAiMcpServer = server;
        UpdateAiMcpStatus();
    }

    public void RemoveSelectedAiMcpServer()
    {
        if (SelectedAiMcpServer is null) return;
        int index = AiMcpServers.IndexOf(SelectedAiMcpServer);
        AiMcpServers.Remove(SelectedAiMcpServer);
        SelectedAiMcpServer = AiMcpServers.Count == 0
            ? null
            : AiMcpServers[Math.Clamp(index, 0, AiMcpServers.Count - 1)];
        UpdateAiMcpStatus();
    }

    public async Task SaveAiMcpProfileAsync()
    {
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        if (plugin is null || SelectedProject is null)
        {
            AiMcpStatus = "Enable AI Workspace and select a project first.";
            return;
        }

        AiMcpProjectProfile profile;
        try
        {
            profile = CreateCurrentAiMcpProfile();
            EnsureUniqueMcpIds(profile.Servers);
        }
        catch (Exception exception)
        {
            AiMcpStatus = exception.Message;
            return;
        }

        await RunOperationAsync("Saving MCP policy…", async () =>
        {
            await plugin.SaveMcpProfileAsync(profile);
            UpdateAiMcpStatus("Policy saved locally outside the repository.");
        }, "MCP policy saved");
    }

    public async Task EmergencyBlockAiMcpAsync()
    {
        AiMcpEmergencyBlocked = true;
        _aiAgentCancellation?.Cancel();
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        if (plugin is not null && SelectedProject is not null)
        {
            await plugin.SaveMcpProfileAsync(CreateCurrentAiMcpProfile());
        }
        UpdateAiMcpStatus("Emergency block applied. Any running CyRevision AI task was cancelled.");
    }

    public async Task UnblockAiMcpAsync()
    {
        AiMcpEmergencyBlocked = false;
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        if (plugin is not null && SelectedProject is not null)
        {
            await plugin.SaveMcpProfileAsync(CreateCurrentAiMcpProfile());
        }
        UpdateAiMcpStatus("Emergency block removed. Individual server policies still apply.");
    }

    public async Task RunAiAgentAsync()
    {
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        if (plugin is null || SelectedAiProvider is null || SelectedProject is null)
        {
            AiStatus = "Enable AI Workspace, select a provider, and open a project.";
            return;
        }
        if (string.IsNullOrWhiteSpace(AiPrompt))
        {
            AiStatus = "Describe the task for the AI agent.";
            return;
        }

        AiWorkspacePermission permissions = AiWorkspacePermission.ReadRepository;
        if (AiAllowModify) permissions |= AiWorkspacePermission.ModifyFiles;
        if (AiAllowNetwork) permissions |= AiWorkspacePermission.NetworkAccess;
        if (AiStageAfterRun) permissions |= AiWorkspacePermission.StageChanges;
        if (AiCommitAfterRun) permissions |= AiWorkspacePermission.CreateCommit;
        AiMcpProjectProfile mcpProfile = CreateCurrentAiMcpProfile();
        AiAgentRequest request = new(
            SelectedProject.RootPath,
            AiPrompt,
            SelectedCodeNode is { IsDirectory: false }
                ? $"FILE: {SelectedCodeNode.RelativePath}{Environment.NewLine}{CodePreviewText[..Math.Min(CodePreviewText.Length, 100_000)]}"
                : string.Empty,
            SelectedAiProvider,
            AiModel,
            AiEndpoint,
            AiExecutablePath,
            string.IsNullOrWhiteSpace(AiApiKey) ? null : AiApiKey,
            permissions,
            mcpProfile);

        _aiAgentCancellation?.Cancel();
        _aiAgentCancellation?.Dispose();
        _aiAgentCancellation = new CancellationTokenSource();
        CancellationToken aiCancellationToken = _aiAgentCancellation.Token;
        await RunOperationAsync("Running AI agent…", async () =>
        {
            AiStatus = "Agent running inside the selected permission boundary…";
            AiAgentResult result = await plugin.RunAsync(request, aiCancellationToken);
            AiResponse = string.IsNullOrWhiteSpace(result.Response) ? result.Diagnostic : result.Response;
            AiStatus = result.Succeeded
                ? $"Completed in {result.Duration:g}."
                : $"Agent failed (exit {result.ExitCode}): {result.Diagnostic}";
            if (!result.Succeeded) throw new InvalidOperationException(AiStatus);
            if (SelectedProject.Definition.Features.GitEnabled && (AiStageAfterRun || AiCommitAfterRun))
            {
                GitRepositoryStatus status = await _gitService.GetStatusAsync(SelectedProject.RootPath);
                string[] paths = status.Changes.Select(change => change.Path).Distinct().ToArray();
                if (paths.Length > 0 && AiCommitAfterRun)
                {
                    if (string.IsNullOrWhiteSpace(AiCommitMessage))
                        throw new InvalidOperationException("A commit message is required for the Git broker.");
                    await _gitService.CreateRevisionAsync(SelectedProject.RootPath, AiCommitMessage, paths);
                    AiStatus += $" CyRevision created commit '{AiCommitMessage.Trim()}'.";
                }
                else if (paths.Length > 0 && AiStageAfterRun)
                {
                    await _gitService.StageAsync(SelectedProject.RootPath, paths);
                    AiStatus += $" CyRevision staged {paths.Length} changed file(s).";
                }
                await RefreshCoreAsync();
                await RefreshCodeWorkspaceAsync();
            }
            AiApiKey = string.Empty;
        }, "AI task completed");
    }

    private AiMcpProjectProfile CreateCurrentAiMcpProfile()
    {
        if (SelectedProject is null) return AiMcpProjectProfile.CreateDefault(Guid.Empty);
        return new AiMcpProjectProfile(
            SelectedProject.Id,
            AiMcpEnabled,
            AiMcpEmergencyBlocked,
            AiMcpBlockUnmanagedServers,
            AiMcpServers.Select(server => server.ToConfiguration()).ToArray(),
            DateTimeOffset.UtcNow);
    }

    private async Task LoadAiMcpProfileCoreAsync()
    {
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        if (plugin is null || SelectedProject is null)
        {
            ClearAiMcpProfile();
            return;
        }

        try
        {
            AiMcpProjectProfile profile = await plugin.GetMcpProfileAsync(SelectedProject.Id);
            AiMcpEnabled = profile.Enabled;
            AiMcpEmergencyBlocked = profile.EmergencyBlocked;
            AiMcpBlockUnmanagedServers = profile.BlockUnmanagedServers;
            ReplaceCollection(AiMcpServers, profile.Servers.Select(server => new AiMcpServerViewModel(server)));
            SelectedAiMcpServer = AiMcpServers.FirstOrDefault();
            UpdateAiMcpStatus("Loaded from local project settings.");
        }
        catch (Exception exception)
        {
            ClearAiMcpProfile();
            AiMcpStatus = exception.Message;
        }
    }

    private void ClearAiMcpProfile()
    {
        AiMcpServers.Clear();
        SelectedAiMcpServer = null;
        AiMcpEnabled = false;
        AiMcpEmergencyBlocked = false;
        AiMcpBlockUnmanagedServers = true;
        AiMcpStatus = "MCP is disabled for this project.";
    }

    private void UpdateAiMcpStatus(string? suffix = null)
    {
        int enabled = AiMcpServers.Count(server => server.Enabled);
        string state = AiMcpEmergencyBlocked
            ? "ALL MCP BLOCKED"
            : AiMcpEnabled
                ? $"MCP enabled · {enabled}/{AiMcpServers.Count} server(s) enabled"
                : "MCP disabled for this project";
        string isolation = AiMcpBlockUnmanagedServers
            ? " · unmanaged Codex MCP servers blocked"
            : " · unmanaged Codex MCP servers allowed";
        AiMcpStatus = state + isolation + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : " · " + suffix);
    }

    private static void EnsureUniqueMcpIds(IEnumerable<AiMcpServerConfiguration> servers)
    {
        string[] duplicateIds = servers.GroupBy(server => server.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException("MCP server IDs must be unique: " + string.Join(", ", duplicateIds));
        }
    }

    private async Task OpenCodeSearchResultAsync(CodeSearchResult result)
    {
        CodeTreeNode? node = FindNode(CodeTree, result.RelativePath);
        if (node is not null)
        {
            SelectedCodeNode = node;
        }
        else if (SelectedProject is not null)
        {
            await LoadCodeNodeAsync(new CodeTreeNode(
                Path.GetFileName(result.RelativePath),
                result.RelativePath,
                result.FullPath,
                false,
                size: new FileInfo(result.FullPath).Length));
        }
        CodePreviewSummary += $" · match at line {result.LineNumber}, column {result.ColumnNumber}";
    }

    private async Task LoadCodeNodeAsync(CodeTreeNode? node)
    {
        CodeHistory.Clear();
        CodeSymbols.Clear();
        CodePreviewText = string.Empty;
        if (SelectedProject is null || node is null)
        {
            CodePreviewSummary = "Select a file to preview it.";
            return;
        }

        try
        {
            if (node.IsDirectory)
            {
                CodePreviewSummary = $"Folder · {node.RelativePath} · {node.Children.Count} visible item(s)";
                if (SelectedProject.Definition.Features.GitEnabled)
                {
                    ReplaceCollection(CodeHistory, await _codeWorkspaceService.GetHistoryAsync(
                        SelectedProject.RootPath, node.RelativePath));
                }
                return;
            }
            CodeFilePreview preview = await _codeWorkspaceService.ReadPreviewAsync(
                SelectedProject.RootPath, node.RelativePath);
            CodePreviewText = preview.IsBinary
                ? "Binary preview is not available. Use Asset diff for supported visual formats."
                : preview.Text;
            CodePreviewSummary = $"{preview.RelativePath} · {preview.Summary}";
            ReplaceCollection(CodeSymbols, preview.Symbols);
            if (SelectedProject.Definition.Features.GitEnabled)
            {
                ReplaceCollection(CodeHistory, await _codeWorkspaceService.GetHistoryAsync(
                    SelectedProject.RootPath, node.RelativePath));
            }
            CodeSelectionSummary = "Select lines in the preview, then request their Git history.";
        }
        catch (Exception exception)
        {
            CodePreviewSummary = exception.Message;
        }
    }

    private static CodeTreeNode? FindFirstFile(IEnumerable<CodeTreeNode> nodes)
    {
        foreach (CodeTreeNode node in nodes)
        {
            if (!node.IsDirectory) return node;
            CodeTreeNode? child = FindFirstFile(node.Children);
            if (child is not null) return child;
        }
        return null;
    }

    private static CodeTreeNode? FindNode(IEnumerable<CodeTreeNode> nodes, string relativePath)
    {
        foreach (CodeTreeNode node in nodes)
        {
            if (string.Equals(node.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)) return node;
            CodeTreeNode? child = FindNode(node.Children, relativePath);
            if (child is not null) return child;
        }
        return null;
    }

    public void SetUnrealProjectPath(string path)
    {
        UnrealProjectPath = Path.GetFullPath(path);
        RefreshUnrealInspection();
    }

    public async Task EnableSelectedPluginAsync()
    {
        if (SelectedPlugin is null)
        {
            return;
        }

        string pluginId = SelectedPlugin.Id;
        await RunOperationAsync("Enabling plugin…", async () =>
        {
            await _pluginManager.EnableAsync(pluginId);
            RefreshPluginCatalog(pluginId);
        }, "Plugin enabled");
    }

    public async Task DisableSelectedPluginAsync()
    {
        if (SelectedPlugin is null)
        {
            return;
        }

        string pluginId = SelectedPlugin.Id;
        await RunOperationAsync("Disabling plugin…", async () =>
        {
            DetachUnrealPluginEvents();
            await _pluginManager.DisableAsync(pluginId);
            RefreshPluginCatalog(pluginId);
        }, "Plugin disabled");
    }

    public async Task InstallUnrealEditorPluginAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || string.IsNullOrWhiteSpace(UnrealProjectPath))
        {
            StatusMessage = "Enable the Unreal Engine Integration plugin and select an Unreal project.";
            return;
        }

        await RunOperationAsync("Installing CyRevisionUnreal…", async () =>
        {
            string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CyRevision.Desktop");
            UnrealPluginInstallationResult result = await plugin.InstallOrUpdateEditorPluginAsync(
                UnrealProjectPath,
                executable);
            UnrealPluginSummary = result.Message + (result.BackupDirectory is null
                ? string.Empty
                : $" Backup: {result.BackupDirectory}");
            RefreshUnrealInspection();
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }
        }, "CyRevisionUnreal installed and the private loopback connection configured");
    }

    public async Task ConfigureUnrealBridgeAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || string.IsNullOrWhiteSpace(UnrealProjectPath))
        {
            StatusMessage = "Enable the Unreal Engine Integration plugin and select an Unreal project.";
            return;
        }

        await RunOperationAsync("Configuring the Unreal bridge…", async () =>
        {
            string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CyRevision.Desktop");
            UnrealBridgeStatus bridge = await plugin.ConfigureProjectConnectionAsync(UnrealProjectPath, executable);
            UnrealBridgeSummary = FormatBridgeStatus(bridge);
        }, "Unreal project authorized on the private loopback bridge");
    }

    public async Task CheckForUpdatesAsync()
    {
        if (!CanCheckForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = "Recherche de la dernière release publiée…";
        try
        {
            ApplicationUpdateInfo update = await _updateService.CheckAsync();
            _availableUpdate = update;
            LatestApplicationVersion = update.LatestVersion.ToString();
            HasUpdateAvailable = update.IsUpdateAvailable;
            UpdateReleaseNotes = update.ReleaseNotes.Trim();
            OnPropertyChanged(nameof(UpdateReleasePageUrl));
            OnPropertyChanged(nameof(CanInstallUpdate));

            if (!update.IsUpdateAvailable)
            {
                UpdateStatus = $"CyRevision {CurrentApplicationVersion} est à jour.";
            }
            else if (update.Package is null)
            {
                UpdateStatus = $"Version {LatestApplicationVersion} disponible, mais aucun installateur compatible n'a été publié.";
            }
            else
            {
                UpdateStatus = $"Version {LatestApplicationVersion} disponible · {update.Package.Name}";
            }
        }
        catch (Exception exception)
        {
            _availableUpdate = null;
            HasUpdateAvailable = false;
            UpdateStatus = $"Vérification impossible : {exception.Message}";
            OnPropertyChanged(nameof(UpdateReleasePageUrl));
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    public async Task<string?> DownloadAvailableUpdateAsync()
    {
        if (!CanInstallUpdate || _availableUpdate is null)
        {
            return null;
        }

        IsDownloadingUpdate = true;
        UpdateProgress = 0;
        try
        {
            Progress<double> progress = new(value =>
            {
                UpdateProgress = Math.Round(value * 100, 1);
                UpdateStatus = $"Téléchargement et vérification… {value:P0}";
            });
            string path = await _updateService.DownloadAsync(
                _availableUpdate,
                Path.Combine(_applicationPaths.CacheDirectory, "updates"),
                progress);
            UpdateProgress = 100;
            UpdateStatus = "Mise à jour téléchargée et vérifiée par SHA-256. L'installateur va s'ouvrir.";
            return path;
        }
        catch (Exception exception)
        {
            UpdateStatus = $"Téléchargement impossible : {exception.Message}";
            return null;
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    public async Task AddExistingRepositoryAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        await RunOperationAsync("Ouverture du dépôt…", async () =>
        {
            GitRepositoryStatus status = await _gitService.GetStatusAsync(fullPath);
            ProjectDefinition definition = CreateGitProject(status.RootPath);
            await SaveAndSelectProjectAsync(definition);
        }, "Dépôt ajouté à CyRevision");
    }

    public async Task CreateGitRepositoryAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        await RunOperationAsync("Création du dépôt Git…", async () =>
        {
            await _gitService.InitializeAsync(fullPath);
            ProjectDefinition definition = CreateGitProject(fullPath);
            await SaveAndSelectProjectAsync(definition);
        }, "Dépôt Git créé avec Git LFS local");
    }

    public async Task AddFolderProjectAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        await RunOperationAsync("Ajout du dossier…", async () =>
        {
            ProjectItemViewModel? existing = Projects.FirstOrDefault(project =>
                ProjectPathsEqual(project.RootPath, fullPath));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == ProjectPresetKind.SyncOnly);
            ProjectDefinition definition = new(
                existing?.Id ?? Guid.NewGuid(),
                new DirectoryInfo(fullPath).Name,
                fullPath,
                preset.Features,
                preset.Retention,
                CreatedAt: existing?.Definition.CreatedAt ?? now,
                LastOpenedAt: now);
            await SaveAndSelectProjectAsync(definition);
        }, "Dossier ajouté en mode Sync uniquement (moteur arrêté)");
    }

    public async Task RemoveSelectedProjectAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (_currentVpnProfile?.ProjectId == SelectedProject.Id &&
            (await _wireGuardEngine.GetStatusAsync(_currentVpnProfile)).State == VpnRuntimeState.Running)
        {
            StatusMessage = "Arrêtez le tunnel VPN CyRevision avant de retirer ce projet du catalogue";
            return;
        }

        Guid projectId = SelectedProject.Id;
        await RunOperationAsync("Retrait du catalogue…", async () =>
        {
            await _projectCatalog.RemoveAsync(projectId);
            ProjectItemViewModel? item = Projects.FirstOrDefault(project => project.Id == projectId);
            if (item is not null)
            {
                Projects.Remove(item);
            }

            SelectedProject = Projects.FirstOrDefault();
            if (SelectedProject is null)
            {
                ClearRepositoryView();
            }
        }, "Projet retiré du catalogue — aucun fichier supprimé");
    }

    public Task RefreshAsync() => RunOperationAsync("Actualisation…", async () =>
    {
        await RefreshCoreAsync();
        await LoadAdvisoryReservationsCoreAsync();
    }, "Dépôt et réservations actualisés");

    public Task RefreshAdvisoryReservationsAsync() => RunOperationAsync(
        "Actualisation des réservations souples…",
        LoadAdvisoryReservationsCoreAsync,
        "Réservations souples actualisées");

    public async Task AnalyzeGitGraphsAsync()
    {
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled)
        {
            StatusMessage = "Sélectionnez un projet utilisant Git pour construire les graphes";
            return;
        }

        if (!int.TryParse(GitGraphCommitLimit, out int commitLimit) || commitLimit is < 10 or > 1000)
        {
            StatusMessage = "Le nombre de commits doit être compris entre 10 et 1000";
            return;
        }

        if (!int.TryParse(GitGraphFileLimit, out int fileLimit) || fileLimit is < 10 or > 150)
        {
            StatusMessage = "Le nombre de fichiers doit être compris entre 10 et 150";
            return;
        }

        await RunOperationAsync("Construction des graphes Git…", async () =>
        {
            Task<IReadOnlyList<GitGraphCommit>> commitsTask = _gitService.GetCommitGraphAsync(
                SelectedProject.RootPath,
                commitLimit,
                GitGraphIncludeAllBranches);
            Task<GitFileActivityGraph> filesTask = _gitService.GetFileActivityGraphAsync(
                SelectedProject.RootPath,
                commitLimit,
                fileLimit,
                GitGraphIncludeAllBranches);
            Task<GitRepositoryInsights> insightsTask = _gitService.GetRepositoryInsightsAsync(
                SelectedProject.RootPath,
                Math.Min(1000, Math.Max(commitLimit, 500)),
                GitGraphIncludeAllBranches);
            Task<UnrealDependencyGraph>? unrealTask = IsUnrealIntegrationEnabled
                ? _assetDiffService.ScanUnrealDependenciesAsync(
                    SelectedProject.RootPath,
                    Math.Min(1000, Math.Max(fileLimit * 5, 100)))
                : null;
            List<Task> analysisTasks = [commitsTask, filesTask, insightsTask];
            if (unrealTask is not null)
            {
                analysisTasks.Add(unrealTask);
            }
            await Task.WhenAll(analysisTasks);
            IReadOnlyList<GitGraphCommit> commits = await commitsTask;
            GitFileActivityGraph files = await filesTask;
            GitRepositoryInsights insights = await insightsTask;
            GitGraphCommits = commits.ToArray();
            GitFileActivities = files.Files.ToArray();
            GitFileRelations = files.Relations.ToArray();
            GitContributors = insights.Contributors.ToArray();
            GitDailyActivity = insights.DailyActivity.ToArray();
            GitHotFiles = insights.HotFiles.ToArray();
            GitInsightsSummary = $"{insights.CommitCount} commits · {insights.ContributorCount} contributeur(s) · " +
                                 $"{insights.FileCount} fichier(s) · +{insights.AddedLines} / -{insights.DeletedLines} · " +
                                 $"{insights.BinaryChanges} changement(s) binaire(s)";
            if (unrealTask is not null)
            {
                UnrealDependencyGraph unreal = await unrealTask;
                UnrealAssets = unreal.Assets.ToArray();
                UnrealDependencyFiles = unreal.Assets.Select(asset => new GitFileActivity(
                    asset.Path,
                    GitFileKind.UnrealAsset,
                    Math.Max(1, asset.DependencyCount + asset.ReferencedByCount),
                    0,
                    0,
                    1,
                    DateTimeOffset.MinValue)).ToArray();
                UnrealDependencyRelations = unreal.Dependencies.Select(dependency => new GitFileRelation(
                    dependency.SourcePath,
                    dependency.TargetPath,
                    1)).ToArray();
                UnrealDependencySummary = $"{unreal.InspectedAssetCount} / {unreal.TotalAssetCount} asset(s) inspecté(s) · " +
                                          $"{unreal.Dependencies.Count} dépendance(s) résolue(s) · " +
                                          $"{unreal.UnresolvedReferenceCount} référence(s) externe(s) ou non résolue(s)";
            }
            else
            {
                UnrealDependencyFiles = [];
                UnrealDependencyRelations = [];
                UnrealAssets = [];
                UnrealDependencySummary = "Enable the Unreal Engine Integration plugin to analyze Unreal dependencies.";
            }
            int mergeCount = commits.Count(commit => commit.IsMerge);
            int binaryFiles = files.Files.Count(file => file.BinaryChangeCount > 0);
            GitGraphSummary = $"{commits.Count} commits · {mergeCount} merges · " +
                              $"{files.TotalFileCount} fichiers analysés · {binaryFiles} binaires · " +
                              $"{files.Relations.Count} relations analysées";
        }, "Graphes Git construits — analyse en lecture seule");
    }

    public Task SelectExplorerCommitAsync(string commitHash)
    {
        if (string.IsNullOrWhiteSpace(commitHash))
        {
            return Task.CompletedTask;
        }

        GitRevision? revision = _allExplorerRevisions.FirstOrDefault(item => item.Hash == commitHash);
        if (revision is not null)
        {
            SelectedExplorerRevision = revision;
            return Task.CompletedTask;
        }

        return SelectExplorerCommitCoreAsync(commitHash);
    }

    public async Task CompareExplorerCommitsAsync()
    {
        if (SelectedProject is null || SelectedExplorerRevision is null || SelectedComparisonRevision is null)
        {
            StatusMessage = "Sélectionnez deux révisions à comparer";
            return;
        }

        GitRevision target = SelectedExplorerRevision;
        GitRevision baseline = SelectedComparisonRevision;
        await RunOperationAsync("Comparaison des révisions…", async () =>
        {
            GitCommitComparison comparison = await _gitService.CompareCommitsAsync(
                SelectedProject.RootPath,
                baseline.Hash,
                target.Hash);
            _comparisonFromHash = baseline.Hash;
            _comparisonToHash = target.Hash;
            ReplaceCollection(ExplorerFiles, comparison.Files);
            ExplorerFileHistory.Clear();
            ExplorerSummary = $"{baseline.ShortHash} ↔ {target.ShortHash} · {comparison.Files.Count} fichier(s) · " +
                              $"+{comparison.AddedLines} / -{comparison.DeletedLines} · {comparison.BinaryFileCount} binaire(s)";
            ExplorerDiff = await _gitService.GetComparisonDiffAsync(
                SelectedProject.RootPath,
                baseline.Hash,
                target.Hash);
            SelectedExplorerFile = ExplorerFiles.FirstOrDefault();
        }, "Comparaison prête — aucune donnée modifiée");
    }

    public async Task LoadMultiRestoreCommitAsync(GitRevision? revision = null)
    {
        if (SelectedProject is null)
        {
            return;
        }

        GitRevision? selected = revision ?? MultiRestoreCommit ?? SelectedExplorerRevision ?? History.FirstOrDefault();
        if (selected is null)
        {
            StatusMessage = "No commit is available for multi restore.";
            return;
        }

        await RunOperationAsync("Loading the multi restore composer…", async () =>
        {
            GitCommitDetails details = await _gitService.GetCommitDetailsAsync(SelectedProject.RootPath, selected.Hash);
            foreach (MultiRestoreFileViewModel previous in MultiRestoreFiles)
            {
                previous.CompositionChanged -= OnMultiRestoreCompositionChanged;
            }

            MultiRestoreFiles.Clear();
            foreach (GitCommitFileChange change in details.Files)
            {
                MultiRestoreFileViewModel item = new(change);
                item.CompositionChanged += OnMultiRestoreCompositionChanged;
                MultiRestoreFiles.Add(item);
            }

            _multiRestoreLoadedHash = details.Revision.Hash;
            MultiRestoreCommit = details.Revision;
            SelectedMultiRestoreFile = MultiRestoreFiles.FirstOrDefault();
            InvalidateMultiRestorePlan();
            MultiRestoreSummary = $"{details.Revision.ShortHash} · {details.Revision.Subject} · " +
                                  $"{details.Files.Count} changed file(s). Select only the versions to compose.";
        }, "Multi restore composer ready — no file changed");
    }

    public void SelectAllMultiRestoreFiles(bool selected)
    {
        foreach (MultiRestoreFileViewModel item in MultiRestoreFiles)
        {
            item.IsSelected = selected;
        }
        InvalidateMultiRestorePlan();
    }

    public async Task PreviewMultiRestoreAsync()
    {
        if (SelectedProject is null || MultiRestoreCommit is null)
        {
            return;
        }

        GitMultiRestoreSelection[] selections = MultiRestoreFiles
            .Where(item => item.IsSelected)
            .Select(item => new GitMultiRestoreSelection(item.Path, item.RestorePoint))
            .ToArray();
        await RunOperationAsync("Building the multi restore safety preview…", async () =>
        {
            GitMultiRestorePlan plan = await _gitService.CreateMultiRestorePlanAsync(
                SelectedProject.RootPath,
                MultiRestoreCommit.Hash,
                selections);
            _multiRestorePlan = plan;
            ReplaceCollection(MultiRestoreOperations, plan.Operations);
            MultiRestoreSummary = $"{plan.Operations.Count} operation(s) · " +
                                  $"{plan.Operations.Count(item => item.Kind == GitMultiRestoreOperationKind.Restore)} restore · " +
                                  $"{plan.Operations.Count(item => item.Kind == GitMultiRestoreOperationKind.Delete)} delete" +
                                  (plan.HasLocalChanges ? " · local changes detected" : "") +
                                  (plan.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, plan.Warnings) : string.Empty);
            OnPropertyChanged(nameof(CanApplyMultiRestore));
        }, "Safety preview ready — no file changed");
    }

    public async Task ApplyMultiRestoreAsync()
    {
        if (SelectedProject is null || _multiRestorePlan is null || !CanApplyMultiRestore)
        {
            return;
        }

        GitMultiRestorePlan plan = _multiRestorePlan;
        await RunOperationAsync("Applying the composed file versions…", async () =>
        {
            GitMultiRestoreResult result = await _gitService.ApplyMultiRestorePlanAsync(
                SelectedProject.RootPath,
                plan,
                MultiRestoreOverwriteLocalChanges);
            _multiRestorePlan = null;
            MultiRestoreOperations.Clear();
            OnPropertyChanged(nameof(CanApplyMultiRestore));
            MultiRestoreSummary = $"Applied {result.ChangedPaths.Count} path(s). Safety backup: {result.BackupDirectory}";
            await RefreshCoreAsync();
        }, "Multi restore applied to the working tree — review the Changes tab before committing");
    }

    public async Task CompareBranchesForCompositionAsync()
    {
        if (SelectedProject is null || BranchCompareSource is null || BranchCompareTarget is null)
        {
            StatusMessage = "Choose both a source and a target branch.";
            return;
        }
        if (BranchCompareSource.Name.Equals(BranchCompareTarget.Name, StringComparison.Ordinal))
        {
            StatusMessage = "Source and target must be different branches.";
            return;
        }

        await RunOperationAsync("Comparing branch histories and patch equivalence…", async () =>
        {
            GitBranchComparison comparison = await _gitService.CompareBranchesAsync(
                SelectedProject.RootPath,
                BranchCompareSource.Name,
                BranchCompareTarget.Name);
            _branchComparison = comparison;
            foreach (CherryPickCommitViewModel previous in BranchComparisonCommits)
            {
                previous.CompositionChanged -= OnCherryPickCompositionChanged;
            }

            BranchComparisonCommits.Clear();
            foreach (GitBranchComparisonCommit commit in comparison.Commits.OrderBy(item => item.Revision.AuthoredAt))
            {
                CherryPickCommitViewModel item = new(commit);
                item.CompositionChanged += OnCherryPickCompositionChanged;
                BranchComparisonCommits.Add(item);
            }

            SelectedCherryPickCommit = BranchComparisonCommits.FirstOrDefault(item => item.CanCherryPick)
                                       ?? BranchComparisonCommits.FirstOrDefault();
            BranchComparisonSummary = $"{comparison.SourceBranch} → {comparison.TargetBranch} · " +
                                      $"{comparison.SourceOnlyCount} source-only · {comparison.TargetOnlyCount} target-only · " +
                                      $"{comparison.EquivalentCount} patch-equivalent";
            InvalidateCherryPickPlan();
        }, "Branch comparison ready — read-only analysis");
    }

    public void SelectAllSourceOnlyCommits(bool selected)
    {
        foreach (CherryPickCommitViewModel item in BranchComparisonCommits.Where(item => item.CanCherryPick))
        {
            item.IsSelected = selected;
        }
        InvalidateCherryPickPlan();
    }

    public void MoveSelectedCherryPickCommit(int offset)
    {
        if (SelectedCherryPickCommit is null || !SelectedCherryPickCommit.CanCherryPick || offset == 0)
        {
            return;
        }

        int currentIndex = BranchComparisonCommits.IndexOf(SelectedCherryPickCommit);
        int targetIndex = currentIndex + Math.Sign(offset);
        if (targetIndex < 0 || targetIndex >= BranchComparisonCommits.Count ||
            !BranchComparisonCommits[targetIndex].CanCherryPick)
        {
            return;
        }

        BranchComparisonCommits.Move(currentIndex, targetIndex);
        InvalidateCherryPickPlan();
    }

    public async Task PreviewCherryPickAsync()
    {
        if (SelectedProject is null || BranchCompareSource is null || BranchCompareTarget is null)
        {
            return;
        }

        string[] commits = BranchComparisonCommits
            .Where(item => item.CanCherryPick && item.IsSelected)
            .Select(item => item.Hash)
            .ToArray();
        await RunOperationAsync("Building the cherry-pick safety preview…", async () =>
        {
            GitCherryPickPlan plan = await _gitService.CreateCherryPickPlanAsync(
                SelectedProject.RootPath,
                BranchCompareSource.Name,
                BranchCompareTarget.Name,
                commits,
                SelectedCherryPickMode,
                CombinedCherryPickMessage);
            _cherryPickPlan = plan;
            CherryPickPlanSummary = $"{plan.OrderedCommits.Count} commit(s) → {plan.TargetBranch} · " +
                                    (plan.UsesTemporaryWorktree ? "isolated temporary worktree" : "active clean worktree") +
                                    (plan.Mode == GitCherryPickMode.CombineIntoOne ? " · combine into one commit" : " · keep original commits") +
                                    (plan.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, plan.Warnings) : string.Empty);
            OnPropertyChanged(nameof(CanApplyCherryPick));
        }, "Cherry-pick preview ready — nothing applied or pushed");
    }

    public async Task ApplyCherryPickAsync()
    {
        if (SelectedProject is null || _cherryPickPlan is null || !CanApplyCherryPick)
        {
            return;
        }

        GitCherryPickPlan plan = _cherryPickPlan;
        await RunOperationAsync("Applying the cherry-pick composition…", async () =>
        {
            GitCherryPickResult result = await _gitService.ApplyCherryPickPlanAsync(SelectedProject.RootPath, plan);
            _cherryPickPlan = null;
            OnPropertyChanged(nameof(CanApplyCherryPick));
            CherryPickPlanSummary = $"Applied {result.AppliedCommitCount} commit(s) to {result.TargetBranch}. " +
                                    "The branch was updated locally; nothing was pushed.";
            await RefreshCoreAsync();
        }, "Cherry-pick composition completed locally — push remains manual");
    }

    private async Task LoadMultiRestoreDiffAsync(MultiRestoreFileViewModel? item)
    {
        if (SelectedProject is null || MultiRestoreCommit is null || item is null)
        {
            MultiRestoreDiff = "Select a file to inspect its commit patch.";
            return;
        }

        try
        {
            MultiRestoreDiff = await _gitService.GetCommitDiffAsync(
                SelectedProject.RootPath,
                MultiRestoreCommit.Hash,
                item.Path);
            if (string.IsNullOrWhiteSpace(MultiRestoreDiff))
            {
                MultiRestoreDiff = item.IsLfsObject
                    ? "Git LFS object — the safety preview verifies that the selected object exists locally."
                    : "No textual patch is available for this file.";
            }
        }
        catch (Exception exception)
        {
            MultiRestoreDiff = exception.Message;
        }
    }

    private async Task LoadCherryPickDiffAsync(CherryPickCommitViewModel? item)
    {
        if (SelectedProject is null || item is null)
        {
            CherryPickDiff = "Select a commit to inspect its patch.";
            return;
        }

        try
        {
            CherryPickDiff = await _gitService.GetCommitDiffAsync(SelectedProject.RootPath, item.Hash);
        }
        catch (Exception exception)
        {
            CherryPickDiff = exception.Message;
        }
    }

    private void OnMultiRestoreCompositionChanged(object? sender, EventArgs e) => InvalidateMultiRestorePlan();

    private void InvalidateMultiRestorePlan()
    {
        _multiRestorePlan = null;
        MultiRestoreOperations.Clear();
        OnPropertyChanged(nameof(CanApplyMultiRestore));
    }

    private void OnCherryPickCompositionChanged(object? sender, EventArgs e) => InvalidateCherryPickPlan();

    private void InvalidateCherryPickPlan()
    {
        _cherryPickPlan = null;
        OnPropertyChanged(nameof(CanApplyCherryPick));
    }

    public async Task ExportSelectedExplorerFileAsync(string destinationPath)
    {
        if (SelectedProject is null || SelectedExplorerRevision is null || SelectedExplorerFile is null)
        {
            return;
        }

        await RunOperationAsync("Export de la version…", async () =>
        {
            if (SelectedExplorerFile.LfsPointer is not null)
            {
                IReadOnlyList<LfsFileVersion> versions = await _gitService.GetLfsFileVersionsAsync(
                    SelectedProject.RootPath,
                    SelectedExplorerFile.Path);
                LfsFileVersion? version = versions.FirstOrDefault(item =>
                    item.Pointer.OidSha256 == SelectedExplorerFile.LfsPointer.OidSha256);
                if (version is null)
                {
                    throw new InvalidOperationException("Cette version LFS n'est pas disponible dans la chronologie locale.");
                }

                await _gitService.ExportLfsFileVersionAsync(SelectedProject.RootPath, version, destinationPath);
            }
            else
            {
                await _gitService.ExportFileFromRevisionAsync(
                    SelectedProject.RootPath,
                    SelectedExplorerFile.Path,
                    SelectedExplorerRevision.Hash,
                    destinationPath);
            }
        }, "Version exportée sans modifier le projet");
    }

    public async Task ExportSelectedLfsVersionAsync(string destinationPath)
    {
        if (SelectedProject is null || SelectedLfsVersion is null)
        {
            return;
        }

        await RunOperationAsync("Export de l'objet LFS…", () => _gitService.ExportLfsFileVersionAsync(
            SelectedProject.RootPath,
            SelectedLfsVersion,
            destinationPath), "Version LFS exportée");
    }

    public async Task RestoreSelectedLfsVersionAsync()
    {
        if (SelectedProject is null || SelectedLfsVersion is null)
        {
            return;
        }

        LfsFileVersion version = SelectedLfsVersion;
        await RunOperationAsync("Restauration de la version LFS…", async () =>
        {
            await _gitService.RestoreLfsFileVersionAsync(SelectedProject.RootPath, version);
            await RefreshCoreAsync();
            SelectedLfsFile = LfsFiles.FirstOrDefault(file => file.Path == version.Path);
        }, "Ancienne version restaurée dans le dossier de travail — non indexée");
    }

    public async Task RequestSelectedLfsVersionFromPeerAsync()
    {
        if (SelectedLfsVersion is null || !SelectedLfsVersion.CanRequestFromPeer ||
            !TryGetRunningSyncContext(out ProjectDefinition? project, out SyncthingProfile? profile, out ManagedSyncthingEngine? engine))
        {
            return;
        }

        LfsFileVersion version = SelectedLfsVersion;
        await RunOperationAsync("Création de la demande LFS signée…", async () =>
        {
            using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(project!, engine!.DeviceId);
            Guid requestId = await _gitPeerExchangeService.RequestLfsObjectAsync(
                project!.Id,
                profile!.ExchangeDirectory,
                identity,
                version.Pointer.OidSha256,
                $"Time Machine: {version.Path} @ {version.Revision.ShortHash}");
            PeerLfsTransferSummary = $"Demande {requestId.ToString("N")[..8]} mise en file · " +
                                     "le pair publiera l'objet lors de son prochain échange";
        }, "Demande LFS signée prête à être synchronisée");
    }

    public async Task RemoveExpiredAdvisoryReservationsAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync("Nettoyage des réservations expirées…", async () =>
        {
            int removed = await GetAdvisoryReservationStore(SelectedProject.Definition).RemoveExpiredAsync();
            await LoadAdvisoryReservationsCoreAsync();
            ReservationSummary = removed == 0
                ? "Aucune réservation expirée à nettoyer."
                : $"{removed} réservation(s) expirée(s) supprimée(s).";
        }, "Nettoyage terminé");
    }

    public async Task StageAllAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        string[] paths = Changes.Where(change => !change.Change.IsStaged).Select(change => change.Path).Distinct().ToArray();
        if (paths.Length == 0)
        {
            StatusMessage = "Aucune modification à indexer";
            return;
        }

        await RunOperationAsync("Indexation…", async () =>
        {
            await _gitService.StageAsync(SelectedProject.RootPath, paths);
            await RefreshCoreAsync();
        }, $"{paths.Length} fichier(s) indexé(s)");
    }

    public async Task UnstageAllAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        string[] paths = Changes.Where(change => change.Change.IsStaged).Select(change => change.Path).Distinct().ToArray();
        if (paths.Length == 0)
        {
            StatusMessage = "Aucune modification dans l’index";
            return;
        }

        await RunOperationAsync("Retrait de l’index…", async () =>
        {
            await _gitService.UnstageAsync(SelectedProject.RootPath, paths);
            await RefreshCoreAsync();
        }, $"{paths.Length} fichier(s) retiré(s) de l’index");
    }

    public async Task CommitAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            StatusMessage = "Un message de révision est requis";
            return;
        }

        string[] paths = Changes.Select(change => change.Path).Distinct().ToArray();
        if (paths.Length == 0)
        {
            StatusMessage = "Aucune modification à enregistrer";
            return;
        }

        string message = CommitMessage;
        await RunOperationAsync("Création de la révision…", async () =>
        {
            await _gitService.CreateRevisionAsync(SelectedProject.RootPath, message, paths);
            CommitMessage = string.Empty;
            if (_syncEngine is not null && SelectedProject.Definition.Features.PeerSyncEnabled)
            {
                await ExchangeGitCoreAsync();
            }
            await RefreshCoreAsync();
        }, "Révision créée");
    }

    public async Task CreateBranchAsync(string branchName)
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        await RunOperationAsync("Création de la branche…", async () =>
        {
            await _gitService.CreateBranchAsync(SelectedProject.RootPath, branchName.Trim());
            await RefreshCoreAsync();
        }, $"Branche {branchName.Trim()} créée");
    }

    public async Task CheckoutSelectedBranchAsync()
    {
        if (SelectedProject is null || SelectedBranch is null || SelectedBranch.IsCurrent)
        {
            return;
        }

        string branchName = SelectedBranch.Name;
        await RunOperationAsync("Changement de branche…", async () =>
        {
            if (SelectedBranch.IsRemote)
            {
                string localBranchName = "peer-" + SelectedBranch.ShortCommitHash;
                if (Branches.Any(branch => !branch.IsRemote && string.Equals(branch.Name, localBranchName, StringComparison.Ordinal)))
                {
                    await _gitService.CheckoutBranchAsync(SelectedProject.RootPath, localBranchName);
                }
                else
                {
                    await _gitService.CreateBranchFromAsync(SelectedProject.RootPath, localBranchName, branchName);
                }
            }
            else
            {
                await _gitService.CheckoutBranchAsync(SelectedProject.RootPath, branchName);
            }
            await RefreshCoreAsync();
        }, $"Branche {branchName} active");
    }

    public async Task MergeSelectedBranchAsync()
    {
        if (SelectedProject is null || SelectedBranch is null || SelectedBranch.IsCurrent)
        {
            return;
        }

        string branchName = SelectedBranch.Name;
        await RunOperationAsync("Intégration de la branche…", async () =>
        {
            await _gitService.MergeBranchAsync(SelectedProject.RootPath, branchName);
            await RefreshCoreAsync();
        }, $"Branche {branchName} intégrée");
    }

    public Task FetchAsync() => RunGitNetworkOperationAsync("Récupération des références…", _gitService.FetchAsync, "Fetch terminé");

    public Task PullAsync() => RunGitNetworkOperationAsync("Récupération des changements…", _gitService.PullAsync, "Pull terminé");

    public Task PushAsync() => RunGitNetworkOperationAsync("Publication des changements…", _gitService.PushAsync, "Push terminé");

    public async Task SaveRemoteAsync()
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(RemoteUrl))
        {
            StatusMessage = "Une URL de remote est requise";
            return;
        }

        await RunOperationAsync("Configuration du remote…", async () =>
        {
            await _gitService.AddOrUpdateRemoteAsync(SelectedProject.RootPath, "origin", RemoteUrl.Trim());
            await RefreshCoreAsync();
        }, "Remote origin configuré");
    }

    public async Task TrackLfsPatternAsync()
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(LfsPattern))
        {
            return;
        }

        string pattern = LfsPattern.Trim();
        await RunOperationAsync("Configuration Git LFS…", async () =>
        {
            await _gitService.TrackLfsPatternAsync(SelectedProject.RootPath, pattern);
            await RefreshCoreAsync();
        }, $"{pattern} est maintenant suivi par Git LFS");
    }

    public Task RefreshLfsLocksAsync() => RunOperationAsync(
        "Refreshing Git LFS locksâ€¦",
        LoadLfsLocksCoreAsync,
        "Git LFS locks refreshed");

    public async Task UnlockLfsLockAsync(LfsFileLock fileLock, bool force)
    {
        ArgumentNullException.ThrowIfNull(fileLock);
        if (SelectedProject is null)
        {
            return;
        }
        if (!fileLock.IsOurs && !force)
        {
            StatusMessage = "A lock owned by another user requires an explicit force unlock.";
            return;
        }

        await RunOperationAsync(
            force ? $"Force-unlocking {fileLock.Path}â€¦" : $"Unlocking {fileLock.Path}â€¦",
            async () =>
            {
                await _gitService.UnlockLfsFileAsync(SelectedProject.RootPath, fileLock.Id, force);
                await LoadLfsLocksCoreAsync();
            },
            $"Git LFS lock removed: {fileLock.Path}");
    }

    public async Task UnlockAllLfsLocksAsync(bool forceEveryLock)
    {
        if (SelectedProject is null)
        {
            return;
        }

        LfsFileLock[] targets = (forceEveryLock ? LfsLocks : MyLfsLocks).ToArray();
        if (targets.Length == 0)
        {
            StatusMessage = forceEveryLock ? "No Git LFS lock to remove." : "You do not own any Git LFS lock.";
            return;
        }

        await RunOperationAsync(
            forceEveryLock ? "Force-unlocking every Git LFS fileâ€¦" : "Unlocking all of your Git LFS filesâ€¦",
            async () =>
            {
                List<string> failures = [];
                int removed = 0;
                foreach (LfsFileLock item in targets)
                {
                    try
                    {
                        await _gitService.UnlockLfsFileAsync(
                            SelectedProject.RootPath,
                            item.Id,
                            force: forceEveryLock);
                        removed++;
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{item.Path}: {exception.Message}");
                    }
                }

                await LoadLfsLocksCoreAsync();
                if (failures.Count > 0)
                {
                    throw new GitOperationException(
                        $"Removed {removed}/{targets.Length} lock(s). " + string.Join(" | ", failures.Take(3)));
                }
            },
            forceEveryLock ? "Every visible Git LFS lock was removed" : "All of your Git LFS locks were removed");
    }

    public void SetLfsExternalStoragePath(string path) => LfsExternalStoragePath = Path.GetFullPath(path);

    public void SetLfsManagementArchivePath(string path) => LfsManagementArchivePath = Path.GetFullPath(path);

    public async Task SaveLfsManagementAsync()
    {
        if (SelectedProject is null)
            return;
        await RunOperationAsync("Saving safe LFS storage policy...", async () =>
        {
            LfsManagementProfile profile = BuildLfsManagementProfile();
            await _lfsManagementProfileStore.SaveAsync(profile);
            _currentLfsManagementProfile = profile;
        }, "Safe LFS storage policy saved");
    }

    public async Task AnalyzeLfsStorageAsync()
    {
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled)
            return;
        await RunOperationAsync("Analyzing local LFS objects and retention evidence...", async () =>
        {
            LfsManagementProfile profile = BuildLfsManagementProfile();
            await _lfsManagementProfileStore.SaveAsync(profile);
            _currentLfsManagementProfile = profile;
            PeerLfsAvailabilityCache peers = await _gitPeerExchangeService.GetCachedLfsAvailabilityAsync(
                GetGitExchangeStatePath(SelectedProject.Id), SelectedProject.Id);
            _lfsCleanupPlan = await _lfsStorageManager.AnalyzeAsync(
                SelectedProject.RootPath, profile, peers);
            ReplaceCollection(LfsCleanupItems, _lfsCleanupPlan.Objects);
            LfsStorageSummary = $"{_lfsCleanupPlan.Objects.Count} local object(s) · " +
                                $"{_lfsCleanupPlan.ReclaimableCount} safely reclaimable · " +
                                $"{FormatByteSize(_lfsCleanupPlan.ReclaimableBytes)} · storage {_lfsCleanupPlan.StoragePath}";
            LfsRemoteVerification = _lfsCleanupPlan.RemoteVerificationOutput;
        }, "LFS safety analysis complete");
    }

    public async Task ArchiveLfsCandidatesAsync()
    {
        if (_lfsCleanupPlan is null || string.IsNullOrWhiteSpace(LfsManagementArchivePath))
        {
            LfsStorageSummary = "Analyze first and choose a verified LFS archive directory.";
            return;
        }
        await RunOperationAsync("Creating verified LFS archive copies...", async () =>
        {
            LfsArchiveResult result = await _lfsStorageManager.ArchiveUnreferencedAsync(
                _lfsCleanupPlan, LfsManagementArchivePath);
            LfsStorageSummary = $"Archived {result.ArchivedObjects} object(s), {FormatByteSize(result.ArchivedBytes)}. Analyze again before cleanup.";
            _lfsCleanupPlan = null;
            LfsCleanupItems.Clear();
        }, "Verified LFS archive updated");
    }

    public async Task ExecuteLfsCleanupAsync()
    {
        if (_lfsCleanupPlan is null || _lfsCleanupPlan.ReclaimableCount == 0)
        {
            LfsStorageSummary = "No verified LFS objects are currently eligible for cleanup.";
            return;
        }
        await RunOperationAsync("Deleting only LFS objects with verified retention evidence...", async () =>
        {
            LfsCleanupResult result = await _lfsStorageManager.ExecuteAsync(_lfsCleanupPlan);
            LfsStorageSummary = $"Reclaimed {FormatByteSize(result.ReclaimedBytes)} from {result.DeletedObjects} object(s); " +
                                $"{result.SkippedObjects} re-protected · audit {result.AuditPath}";
            _lfsCleanupPlan = null;
            LfsCleanupItems.Clear();
            await RefreshCoreAsync();
        }, "Safe LFS cleanup complete");
    }

    public async Task RelocateLfsStorageAsync()
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(LfsExternalStoragePath))
        {
            LfsStorageSummary = "Choose an external LFS storage directory.";
            return;
        }
        await RunOperationAsync("Copying and verifying LFS storage before activation...", async () =>
        {
            LfsRelocationResult result = await _lfsStorageManager.RelocateAsync(
                SelectedProject.RootPath,
                LfsExternalStoragePath,
                LfsRemoveOriginalAfterRelocation);
            LfsStorageSummary = $"Active LFS storage: {result.ActiveStoragePath} · {result.CopiedObjects} verified object(s) · " +
                                (result.OriginalObjectsRemoved ? "old object cache removed" : "old cache retained");
            await RefreshCoreAsync();
        }, "External LFS storage activated");
    }

    public void SetRemoteBuildArtifactDestination(string path) =>
        RemoteBuildArtifactDestination = Path.GetFullPath(path);

    public async Task SaveRemoteBuildAsync()
    {
        if (SelectedProject is null)
            return;
        await RunOperationAsync("Saving remote build connection...", async () =>
        {
            RemoteBuildCredentials credentials = BuildRemoteBuildCredentials();
            await _remoteBuildConnectionStore.SaveAsync(credentials);
            _currentRemoteBuildCredentials = credentials;
        }, "Remote build connection saved");
    }

    public async Task TestRemoteBuildAsync()
    {
        if (SelectedProject is null)
            return;
        await RunOperationAsync("Testing remote build agent...", async () =>
        {
            RemoteBuildCredentials credentials = BuildRemoteBuildCredentials();
            using RemoteBuildClient client = new(credentials);
            RemoteBuildAgentStatus health = await client.GetHealthAsync();
            IReadOnlyList<RemoteBuildProjectDescriptor> projects = await client.GetProjectsAsync();
            RemoteBuildProjectDescriptor project = projects.FirstOrDefault(item => item.ProjectId == SelectedProject.Id)
                                                   ?? throw new InvalidOperationException(
                                                       "Agent is reachable, but this project ID is not allowlisted in agent.json.");
            string recipes = string.Join(", ", project.Recipes.Select(recipe => recipe.Id));
            RemoteBuildStatus = $"{health.Service} {health.Version} · {health.RunningJobs} running · recipes: {recipes}";
        }, "Remote build agent connection verified");
    }

    public async Task StartRemoteBuildAsync()
    {
        if (SelectedProject is null || _remoteBuildCancellation is not null)
            return;
        Guid projectId = SelectedProject.Id;
        string projectName = SelectedProject.Name;
        string projectRoot = SelectedProject.RootPath;
        RemoteBuildCredentials credentials;
        try
        {
            credentials = BuildRemoteBuildCredentials();
            await _remoteBuildConnectionStore.SaveAsync(credentials);
        }
        catch (Exception exception)
        {
            RemoteBuildStatus = exception.Message;
            return;
        }

        _currentRemoteBuildCredentials = credentials;
        _remoteBuildCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsRemoteBuildRunning));
        string? snapshotPath = null;
        try
        {
            RemoteBuildStatus = "Preparing remote build request...";
            string revision = _allExplorerRevisions.FirstOrDefault()?.Hash ?? string.Empty;
            if (credentials.Profile.SourceMode == RemoteBuildSourceMode.UploadedSnapshot)
            {
                string snapshots = Path.Combine(_applicationPaths.CacheDirectory, "remote-build", "snapshots");
                Directory.CreateDirectory(snapshots);
                snapshotPath = Path.Combine(snapshots, Guid.NewGuid().ToString("N") + ".zip");
                RemoteBuildSnapshotResult snapshot = await _remoteBuildSnapshotBuilder.CreateAsync(
                    projectRoot,
                    snapshotPath,
                    credentials.Profile.MaximumUploadBytes,
                    _remoteBuildCancellation.Token);
                revision = snapshot.Revision;
                RemoteBuildStatus = $"Uploading {snapshot.FileCount} file(s), {FormatByteSize(snapshot.SourceBytes)}...";
            }

            using RemoteBuildClient client = new(credentials);
            _currentRemoteBuildJob = await client.StartAsync(
                credentials.Profile, snapshotPath, revision, _remoteBuildCancellation.Token);
            while (_currentRemoteBuildJob.State is RemoteBuildJobState.Queued or RemoteBuildJobState.Preparing or
                   RemoteBuildJobState.Running or RemoteBuildJobState.Packaging)
            {
                RemoteBuildStatus = $"{_currentRemoteBuildJob.State}: {_currentRemoteBuildJob.Message}";
                RemoteBuildLog = _currentRemoteBuildJob.LogTail;
                await Task.Delay(TimeSpan.FromSeconds(2), _remoteBuildCancellation.Token);
                _currentRemoteBuildJob = await client.GetJobAsync(
                    projectId, _currentRemoteBuildJob.JobId, _remoteBuildCancellation.Token);
            }

            RemoteBuildLog = _currentRemoteBuildJob.LogTail;
            RemoteBuildStatus = $"{_currentRemoteBuildJob.State}: {_currentRemoteBuildJob.Message}";
            if (_currentRemoteBuildJob.State == RemoteBuildJobState.Succeeded)
            {
                string name = $"{SanitizeFileName(projectName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{_currentRemoteBuildJob.JobId.ToString("N")[..8]}.zip";
                string destination = Path.Combine(credentials.Profile.ArtifactDestination, name);
                string downloaded = await client.DownloadArtifactsAsync(
                    projectId, _currentRemoteBuildJob.JobId, destination, _remoteBuildCancellation.Token);
                RemoteBuildStatus = $"Remote build succeeded · artifacts: {downloaded}";
            }
        }
        catch (OperationCanceledException)
        {
            RemoteBuildStatus = "Remote build monitoring cancelled.";
        }
        catch (Exception exception)
        {
            RemoteBuildStatus = exception.Message;
        }
        finally
        {
            if (snapshotPath is not null)
                File.Delete(snapshotPath);
            _remoteBuildCancellation.Dispose();
            _remoteBuildCancellation = null;
            OnPropertyChanged(nameof(IsRemoteBuildRunning));
        }
    }

    public async Task CancelRemoteBuildAsync()
    {
        if (SelectedProject is not null && _currentRemoteBuildCredentials is not null && _currentRemoteBuildJob is not null)
        {
            try
            {
                using RemoteBuildClient client = new(_currentRemoteBuildCredentials);
                await client.CancelAsync(SelectedProject.Id, _currentRemoteBuildJob.JobId);
            }
            catch (Exception exception)
            {
                RemoteBuildStatus = exception.Message;
            }
        }
        _remoteBuildCancellation?.Cancel();
    }

    public async Task SetBackupStoreAsync(string path)
    {
        if (SelectedProject is null)
        {
            return;
        }

        BackupStorePath = Path.GetFullPath(path);
        await SaveBackupSettingsAsync();
    }

    public void SetColdArchivePath(string path)
    {
        ColdArchivePath = Path.GetFullPath(path);
        ColdArchiveStatus = "Emplacement choisi — enregistrez la stratégie avant l'archivage.";
    }

    public async Task SaveBackupSettingsAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync("Enregistrement de la stratégie de sauvegarde…", async () =>
        {
            RetentionPolicy retention = BuildRetentionPolicy();
            string storePath = ResolveBackupStorePath(SelectedProject.Definition);
            ProjectFeatures features = SelectedProject.Definition.Features with { BackupEnabled = true };
            ProjectDefinition updated = SelectedProject.Definition with
            {
                Features = features,
                Retention = retention,
                BackupStorePath = storePath,
                ColdArchivePath = string.IsNullOrWhiteSpace(ColdArchivePath) ? null : Path.GetFullPath(ColdArchivePath),
                ColdArchiveAfterDays = string.IsNullOrWhiteSpace(ColdArchivePath)
                    ? null
                    : int.TryParse(ColdArchiveAfterDays, out int archiveDays) && archiveDays > 0
                        ? archiveDays
                        : 180
            };
            updated.Validate();
            await _projectCatalog.UpsertAsync(updated);
            SelectedProject.Update(updated);
            BackupStorePath = storePath;
            await GetBackupService(updated).ApplyRetentionAsync(updated.Id, retention);
            await LoadBackupsCoreAsync();
        }, "Stratégie de sauvegarde enregistrée");
    }

    public async Task ArchiveOldBackupsAsync()
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(ColdArchivePath))
        {
            StatusMessage = "Choisissez et enregistrez un emplacement d'archive froide";
            return;
        }

        int days = int.TryParse(ColdArchiveAfterDays, out int parsedDays) && parsedDays > 0 ? parsedDays : 180;
        await RunOperationAsync("Archivage des anciens snapshots…", async () =>
        {
            ColdArchiveResult result = await new FileSystemColdArchiveService().ArchiveEligibleAsync(
                SelectedProject.Id,
                ResolveBackupStorePath(SelectedProject.Definition),
                new ColdArchivePolicy(Path.GetFullPath(ColdArchivePath), TimeSpan.FromDays(days)));
            ColdArchiveStatus = result.EligibleSnapshots == 0
                ? "Aucun snapshot ancien n'est éligible. Les cinq plus récents restent dans le stockage actif."
                : $"{result.ArchivedSnapshots} snapshot(s) copié(s) · {result.ExistingSnapshots} déjà présent(s) · " +
                  $"{result.CopiedObjects} objet(s) ajouté(s) à l'archive";
        }, "Archivage froid terminé — le stockage actif est conservé");
    }

    public async Task CreateBackupAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync("Création du snapshot…", async () =>
        {
            ProjectDefinition definition = await EnsureBackupConfiguredAsync();
            await GetBackupService(definition).CreateSnapshotAsync(
                definition.Id,
                definition.RootPath,
                definition.Retention);
            await LoadBackupsCoreAsync();
        }, "Snapshot créé et dédupliqué");
    }

    public async Task RestoreSelectedBackupAsync(string destinationDirectory)
    {
        if (SelectedProject is null || SelectedBackup is null)
        {
            StatusMessage = "Sélectionnez un snapshot à restaurer";
            return;
        }

        Guid snapshotId = SelectedBackup.SnapshotId;
        await RunOperationAsync("Restauration du snapshot…", async () =>
        {
            await GetBackupService(SelectedProject.Definition).RestoreSnapshotAsync(
                snapshotId,
                destinationDirectory,
                overwrite: false);
        }, $"Snapshot restauré dans {destinationDirectory}");
    }

    public void SetAssetBaseline(string path) => AssetBaselinePath = Path.GetFullPath(path);

    public void SetAssetCandidate(string path) => AssetCandidatePath = Path.GetFullPath(path);

    public async Task CompareAssetsAsync()
    {
        if (string.IsNullOrWhiteSpace(AssetBaselinePath) || string.IsNullOrWhiteSpace(AssetCandidatePath))
        {
            StatusMessage = "Sélectionnez les deux fichiers à comparer";
            return;
        }

        await RunOperationAsync("Analyse des assets hors moteur…", async () =>
        {
            AssetDiffResult result = await _assetDiffService.CompareAsync(
                AssetBaselinePath,
                AssetCandidatePath,
                GetDiffArtifactDirectory());
            ApplyAssetDiffResult(result);
        }, "Comparaison terminée");
    }

    public async Task CompareSelectedChangeToHeadAsync()
    {
        if (SelectedProject is null || SelectedChange is null || !SelectedProject.Definition.Features.GitEnabled)
        {
            StatusMessage = "Sélectionnez une modification Git à comparer";
            return;
        }

        await RunOperationAsync("Extraction de la version HEAD…", async () =>
        {
            string artifactDirectory = GetDiffArtifactDirectory();
            Directory.CreateDirectory(artifactDirectory);
            string extension = Path.GetExtension(SelectedChange.Path);
            string baselinePath = Path.Combine(artifactDirectory, $"head-{Guid.NewGuid():N}{extension}");
            await _gitService.ExportFileFromRevisionAsync(
                SelectedProject.RootPath,
                SelectedChange.Path,
                "HEAD",
                baselinePath);
            string candidatePath = Path.Combine(SelectedProject.RootPath, SelectedChange.Path.Replace('/', Path.DirectorySeparatorChar));
            AssetBaselinePath = baselinePath;
            AssetCandidatePath = candidatePath;
            AssetDiffResult result = await _assetDiffService.CompareAsync(
                baselinePath,
                candidatePath,
                artifactDirectory);
            ApplyAssetDiffResult(result);
        }, "Comparaison avec HEAD terminée");
    }

    public async Task ApplySelectedPresetAsync()
    {
        if (SelectedProject is null || SelectedPreset is null)
        {
            return;
        }

        ProjectPreset preset = SelectedPreset;
        await RunOperationAsync("Application du mode projet…", async () =>
        {
            if (preset.Features.GitEnabled && !Directory.Exists(Path.Combine(SelectedProject.RootPath, ".git")))
            {
                await _gitService.InitializeAsync(SelectedProject.RootPath);
            }

            ProjectDefinition updated = SelectedProject.Definition with
            {
                Features = preset.Features,
                Retention = preset.Retention
            };
            await _projectCatalog.UpsertAsync(updated);
            SelectedProject.Update(updated);
            LoadBackupSettings(updated);
            if (!preset.Features.PeerSyncEnabled)
            {
                await StopSyncCoreAsync();
            }

            await RefreshCoreAsync();
            await RefreshCodeWorkspaceAsync();
            await LoadAiMcpProfileCoreAsync();
            await LoadBackupsCoreAsync();
            await LoadSyncProfileCoreAsync();
            await LoadVpnProfileCoreAsync();
            await LoadAdvisoryReservationsCoreAsync();
        }, $"Mode « {preset.Name} » appliqué");
    }

    public async Task SetSyncthingExecutableAsync(string executablePath)
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync("Configuration de Syncthing…", async () =>
        {
            _currentSyncProfile = await _syncthingProfileStore.CreateOrUpdateAsync(
                SelectedProject.Id,
                executablePath,
                ResolveSyncExchangeDirectory(SelectedProject.Definition));
            SyncthingExecutablePath = _currentSyncProfile.ExecutablePath;
            SyncState = "Sync prêt";
            SyncDetails = $"API locale isolée : {_currentSyncProfile.ApiEndpoint}";
        }, "Exécutable Syncthing configuré pour ce projet");
    }

    public async Task StartSyncAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (!SelectedProject.Definition.Features.PeerSyncEnabled)
        {
            StatusMessage = "Choisissez un mode contenant Sync avant de démarrer le moteur";
            return;
        }

        if (_currentSyncProfile is null)
        {
            StatusMessage = "Sélectionnez d'abord l'exécutable Syncthing dédié";
            return;
        }

        await RunOperationAsync("Démarrage du moteur Sync isolé…", async () =>
        {
            string desiredExchangePath = ResolveSyncExchangeDirectory(SelectedProject.Definition);
            if (!string.Equals(_currentSyncProfile.ExchangeDirectory, desiredExchangePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentSyncProfile = await _syncthingProfileStore.CreateOrUpdateAsync(
                    SelectedProject.Id,
                    _currentSyncProfile.ExecutablePath,
                    desiredExchangePath);
            }

            if (_syncEngineProjectId != SelectedProject.Id)
            {
                await StopSyncCoreAsync();
            }

            _syncEngine ??= new ManagedSyncthingEngine(_currentSyncProfile.ToIsolationOptions());
            _syncEngineProjectId = SelectedProject.Id;
            await _syncEngine.StartAsync();
            await ConfigureCurrentSyncFolderAsync();
            await ExchangeGitCoreAsync();
            await LoadPeerMembersCoreAsync();
            UpdateSyncStatus(_syncEngine.Status);
        }, "Synchronisation CyRevision active");
    }

    public async Task PauseSyncAsync()
    {
        if (_syncEngine is null)
        {
            return;
        }

        await RunOperationAsync("Mise en pause de Sync…", async () =>
        {
            await _syncEngine.PauseAsync();
            UpdateSyncStatus(_syncEngine.Status);
        }, "Synchronisation en pause");
    }

    public async Task ResumeSyncAsync()
    {
        if (_syncEngine is null)
        {
            return;
        }

        await RunOperationAsync("Reprise de Sync…", async () =>
        {
            await _syncEngine.ResumeAsync();
            UpdateSyncStatus(_syncEngine.Status);
        }, "Synchronisation reprise");
    }

    public async Task StopSyncAsync()
    {
        await RunOperationAsync("Arrêt de l'instance Sync CyRevision…", StopSyncCoreAsync, "Instance Sync CyRevision arrêtée");
    }

    public async Task ExchangeGitAsync()
    {
        await RunOperationAsync("Échange des transactions Git signées…", async () =>
        {
            await ExchangeGitCoreAsync();
            await RefreshCoreAsync();
        }, "Transactions Git publiées et branches des pairs actualisées");
    }

    public async Task CreatePeerInvitationAsync()
    {
        if (!TryGetRunningSyncContext(out ProjectDefinition? project, out SyncthingProfile? profile, out ManagedSyncthingEngine? engine))
        {
            return;
        }

        await RunOperationAsync("Création de l'invitation sécurisée…", async () =>
        {
            using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(project!, engine!.DeviceId);
            JsonPeerAdmissionService admission = CreateAdmissionService(project!.Id, identity);
            PeerInvitationPackage package = await admission.CreateInvitationAsync(
                project.Id,
                SelectedPeerRole,
                TimeSpan.FromHours(24));
            PeerExchangeText = PeerExchangeCodec.ExportInvitation(package);
            PeerVerificationCode = package.VerificationCode;
            SyncDetails = $"Invitation {SelectedPeerRole} valable 24 h. Transmettez le code par un autre canal.";
        }, "Invitation prête à être transmise par un canal sûr");
    }

    public async Task PreparePeerJoinRequestAsync()
    {
        if (!TryGetRunningSyncContext(out ProjectDefinition? project, out _, out ManagedSyncthingEngine? engine))
        {
            return;
        }

        await RunOperationAsync("Préparation de la demande d'adhésion…", async () =>
        {
            PeerInvitationOffer offer = PeerExchangeCodec.ImportInvitation(PeerExchangeText);
            if (offer.Invitation.ProjectId != project!.Id)
            {
                throw new InvalidOperationException("Cette invitation concerne un autre projet.");
            }

            if (string.IsNullOrWhiteSpace(PeerVerificationCode))
            {
                throw new InvalidOperationException("Le code de vérification reçu par le second canal est requis.");
            }

            using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(project, engine!.DeviceId);
            string pendingPath = Path.Combine(GetProjectSecurityPath(project.Id), "pending-invitation.json");
            Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
            await File.WriteAllTextAsync(pendingPath, PeerExchangeCodec.ExportInvitation(new PeerInvitationPackage(
                offer.Invitation,
                offer.OneTimeToken,
                PeerVerificationCode.Trim(),
                offer.IssuerIdentity)));
            PeerExchangeText = PeerExchangeCodec.ExportJoinRequest(new PeerJoinRequest(
                offer,
                identity.Identity,
                PeerVerificationCode.Trim()));
            PeerVerificationCode = string.Empty;
        }, "Demande d'adhésion prête à renvoyer au propriétaire");
    }

    public async Task ApprovePeerJoinRequestAsync()
    {
        if (!TryGetRunningSyncContext(out ProjectDefinition? project, out SyncthingProfile? profile, out _))
        {
            return;
        }

        await RunOperationAsync("Vérification et approbation du pair…", async () =>
        {
            PeerJoinRequest request = PeerExchangeCodec.ImportJoinRequest(PeerExchangeText);
            if (request.InvitationOffer.Invitation.ProjectId != project!.Id)
            {
                throw new InvalidOperationException("Cette demande concerne un autre projet.");
            }

            using FileDeviceIdentityStore ownerIdentity = await OpenLocalDeviceIdentityAsync(project, request.InvitationOffer.IssuerIdentity.SyncthingDeviceId);
            if (ownerIdentity.Identity.DeviceId != request.InvitationOffer.IssuerIdentity.DeviceId)
            {
                throw new UnauthorizedAccessException("Cette machine n'est pas l'émetteur de l'invitation.");
            }

            JsonPeerAdmissionService admission = CreateAdmissionService(project.Id, ownerIdentity);
            MembershipCertificate certificate = await admission.ApproveDeviceAsync(
                request.InvitationOffer.Invitation,
                request.InvitationOffer.OneTimeToken,
                request.Device,
                request.VerificationCode);
            using SyncthingApiClient api = new(profile!.ApiEndpoint, profile.ApiKey);
            await api.PutDeviceAsync(new SyncthingDeviceConfiguration(
                request.Device.SyncthingDeviceId,
                request.Device.DisplayName));
            await ConfigureCurrentSyncFolderAsync();
            PeerMembershipGrant grant = new(
                request.InvitationOffer.Invitation.InvitationId,
                certificate,
                ownerIdentity.Identity);
            string sharedGrantPath = Path.Combine(
                profile.ExchangeDirectory,
                "members",
                certificate.Device.DeviceId.ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(sharedGrantPath)!);
            await File.WriteAllTextAsync(sharedGrantPath, PeerExchangeCodec.ExportMembershipGrant(grant));
            PeerExchangeText = PeerExchangeCodec.ExportMembershipGrant(grant);
            await LoadPeerMembersCoreAsync();
        }, "Pair approuvé, signé et ajouté à Syncthing");
    }

    public async Task ImportMembershipGrantAsync()
    {
        if (!TryGetRunningSyncContext(out ProjectDefinition? project, out SyncthingProfile? profile, out ManagedSyncthingEngine? engine))
        {
            return;
        }

        await RunOperationAsync("Vérification du certificat d'adhésion…", async () =>
        {
            PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(PeerExchangeText);
            string pendingPath = Path.Combine(GetProjectSecurityPath(project!.Id), "pending-invitation.json");
            if (!File.Exists(pendingPath))
            {
                throw new UnauthorizedAccessException("L'invitation d'origine n'est pas disponible sur cet appareil.");
            }

            PeerInvitationOffer pendingOffer = PeerExchangeCodec.ImportInvitation(await File.ReadAllTextAsync(pendingPath));
            bool expectedIssuer = pendingOffer.IssuerIdentity.DeviceId == grant.IssuerIdentity.DeviceId &&
                                  string.Equals(
                                      pendingOffer.IssuerIdentity.SigningPublicKey,
                                      grant.IssuerIdentity.SigningPublicKey,
                                      StringComparison.Ordinal);
            if (grant.Certificate.ProjectId != project.Id ||
                grant.InvitationId != pendingOffer.Invitation.InvitationId ||
                !expectedIssuer ||
                !PeerExchangeCodec.VerifyGrant(grant))
            {
                throw new UnauthorizedAccessException("Le certificat d'adhésion est invalide.");
            }

            using FileDeviceIdentityStore localIdentity = await OpenLocalDeviceIdentityAsync(project, engine!.DeviceId);
            if (grant.Certificate.Device.DeviceId != localIdentity.Identity.DeviceId)
            {
                throw new UnauthorizedAccessException("Le certificat a été émis pour un autre appareil.");
            }

            string grantPath = Path.Combine(GetProjectSecurityPath(project.Id), "membership-grant.json");
            Directory.CreateDirectory(Path.GetDirectoryName(grantPath)!);
            await File.WriteAllTextAsync(grantPath, PeerExchangeCodec.ExportMembershipGrant(grant));
            using SyncthingApiClient api = new(profile!.ApiEndpoint, profile.ApiKey);
            await api.PutDeviceAsync(new SyncthingDeviceConfiguration(
                grant.IssuerIdentity.SyncthingDeviceId,
                grant.IssuerIdentity.DisplayName));
            await api.PutFolderAsync(CreateFolderConfiguration(profile, [grant.IssuerIdentity.SyncthingDeviceId]));
            File.Delete(pendingPath);
        }, "Certificat valide et propriétaire ajouté à Syncthing");
    }

    public async Task RevokeSelectedPeerAsync()
    {
        if (SelectedPeerMember is null ||
            !TryGetRunningSyncContext(out ProjectDefinition? project, out SyncthingProfile? profile, out ManagedSyncthingEngine? engine))
        {
            StatusMessage = "Sélectionnez un pair actif à révoquer";
            return;
        }

        PeerMemberViewModel selected = SelectedPeerMember;
        await RunOperationAsync("Révocation du pair…", async () =>
        {
            using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(project!, engine!.DeviceId);
            JsonPeerAdmissionService admission = CreateAdmissionService(project!.Id, identity);
            await admission.RevokeDeviceAsync(project.Id, selected.DeviceId);
            File.Delete(Path.Combine(profile!.ExchangeDirectory, "members", selected.DeviceId.ToString("N") + ".json"));
            using SyncthingApiClient api = new(profile.ApiEndpoint, profile.ApiKey);
            await api.DeleteDeviceAsync(selected.Certificate.Device.SyncthingDeviceId);
            await ConfigureCurrentSyncFolderAsync();
            await LoadPeerMembersCoreAsync();
        }, $"Pair {selected.DisplayName} révoqué");
    }

    public async Task ConfigureVpnAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync("Configuration de WireGuard…", async () =>
        {
            if (_currentVpnProfile is not null &&
                _currentVpnProfile.BackendMode != SelectedVpnBackendMode &&
                (await _wireGuardEngine.GetStatusAsync(_currentVpnProfile)).State == VpnRuntimeState.Running)
            {
                throw new InvalidOperationException(
                    "Stop the active CyRevision tunnel before changing the WireGuard backend.");
            }

            WireGuardInstallation installation = _wireGuardRuntimeResolver.Detect(SelectedVpnBackendMode);
            bool integratedComplete = installation.BackendMode != VpnBackendMode.IntegratedRuntime ||
                                      OperatingSystem.IsWindows() ||
                                      !string.IsNullOrWhiteSpace(installation.UserspaceExecutablePath);
            if (!installation.CanGenerateKeys || !installation.CanManageTunnel || !integratedComplete)
            {
                throw new FileNotFoundException(SelectedVpnBackendMode == VpnBackendMode.IntegratedRuntime
                    ? $"The integrated WireGuard runtime is incomplete. Install its platform package in '{installation.RuntimeDirectory}'."
                    : "WireGuard is unavailable. Install the official application and run detection again.");
            }

            string keyPath = Path.Combine(
                _applicationPaths.VpnDirectory,
                "keys",
                SelectedProject.Id.ToString("N"),
                "private.key");
            VpnProjectProfile profile = _currentVpnProfile ?? VpnProfileFactory.CreateDefault(SelectedProject.Id, keyPath);
            profile = profile with
            {
                WireGuardExecutablePath = installation.WireGuardExecutablePath ?? profile.WireGuardExecutablePath,
                WgExecutablePath = installation.WgExecutablePath ?? profile.WgExecutablePath,
                WgQuickExecutablePath = installation.WgQuickExecutablePath ?? profile.WgQuickExecutablePath,
                BackendMode = installation.BackendMode,
                UserspaceExecutablePath = installation.UserspaceExecutablePath,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            if (string.IsNullOrWhiteSpace(profile.PublicKey) || !File.Exists(profile.PrivateKeyPath))
            {
                (string publicKey, string privatePath) = await _wireGuardKeyService.GenerateKeyPairAsync(
                    profile.WgExecutablePath!, keyPath);
                profile = profile with { PublicKey = publicKey, PrivateKeyPath = privatePath };
            }

            await _vpnProfileStore.SaveAsync(profile);
            _currentVpnProfile = profile;
            ApplyVpnProfile(profile);
            await RefreshVpnStatusCoreAsync();
            await LoadSwarmAndFileExchangeCoreAsync(profile);
        }, "WireGuard est configuré pour ce projet");
    }

    public async Task SaveVpnSettingsAsync()
    {
        await RunOperationAsync("Enregistrement du profil VPN…", async () =>
        {
            await SaveVpnFormCoreAsync();
            await RefreshVpnStatusCoreAsync();
        }, "Profil VPN enregistré");
    }

    public async Task StartVpnAsync()
    {
        await RunOperationAsync("Activation du tunnel VPN CyRevision…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            VpnEngineStatus status = await _wireGuardEngine.StartAsync(profile);
            ApplyVpnStatus(status);
        }, "Tunnel VPN CyRevision actif");
    }

    public async Task StopVpnAsync()
    {
        if (_currentVpnProfile is null)
        {
            return;
        }

        await RunOperationAsync("Arrêt du tunnel VPN CyRevision…", async () =>
        {
            VpnEngineStatus status = await _wireGuardEngine.StopAsync(_currentVpnProfile);
            ApplyVpnStatus(status);
        }, "Tunnel VPN CyRevision arrêté");
    }

    public async Task RefreshVpnAsync() => await RunOperationAsync(
        "Actualisation du VPN…",
        RefreshVpnStatusCoreAsync,
        "État VPN actualisé");

    public async Task InspectVpnSetupAsync()
    {
        await RunOperationAsync("Inspecting network and firewall…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            _currentVpnSetupPlan = await _vpnNetworkSetupService.InspectAsync(
                profile,
                CreateVpnSetupOptions());
            ApplyVpnSetupPlan(_currentVpnSetupPlan);
        }, "VPN setup diagnosis completed");
    }

    public async Task ApplyVpnFirewallAsync()
    {
        await RunOperationAsync("Applying the generated firewall rules…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            VpnSetupOptions options = CreateVpnSetupOptions();
            _currentVpnSetupPlan = await _vpnNetworkSetupService.InspectAsync(profile, options);
            if (!_currentVpnSetupPlan.CanApplyAutomatically)
            {
                throw new InvalidOperationException(
                    "Automatic firewall configuration is unavailable. Follow the generated computer steps.");
            }

            await _vpnNetworkSetupService.ApplyFirewallAsync(_currentVpnSetupPlan);
            _currentVpnSetupPlan = await _vpnNetworkSetupService.InspectAsync(profile, options);
            ApplyVpnSetupPlan(_currentVpnSetupPlan);
        }, "CyRevision firewall rules applied");
    }

    public async Task TestVpnConnectivityAsync()
    {
        await RunOperationAsync("Testing WireGuard handshakes…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            VpnConnectivityReport report = await _vpnNetworkSetupService.TestConnectivityAsync(profile);
            string peers = report.Peers.Count == 0
                ? string.Empty
                : Environment.NewLine + string.Join(
                    Environment.NewLine,
                    report.Peers.Select(peer => peer.RecentHandshake
                        ? $"✓ {peer.DisplayName} · {peer.TunnelAddress} · {peer.LastHandshakeAt?.ToLocalTime():g}"
                        : $"○ {peer.DisplayName} · {peer.TunnelAddress} · no recent handshake"));
            VpnConnectivityStatus = report.Summary + peers;
        }, "VPN connectivity test completed");
    }

    public async Task RemoveVpnFirewallAsync()
    {
        await RunOperationAsync("Removing only the CyRevision firewall rules…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            VpnSetupOptions options = CreateVpnSetupOptions();
            _currentVpnSetupPlan = await _vpnNetworkSetupService.InspectAsync(profile, options);
            if (!_currentVpnSetupPlan.CanApplyAutomatically)
            {
                throw new InvalidOperationException(
                    "Automatic firewall configuration is unavailable. Follow the generated removal commands.");
            }

            await _vpnNetworkSetupService.RemoveFirewallAsync(_currentVpnSetupPlan);
            _currentVpnSetupPlan = await _vpnNetworkSetupService.InspectAsync(profile, options);
            ApplyVpnSetupPlan(_currentVpnSetupPlan);
        }, "CyRevision firewall rules removed");
    }

    public async Task OpenVpnRouterAsync()
    {
        if (_currentVpnSetupPlan?.RouterAdminUri is null)
        {
            await InspectVpnSetupAsync();
        }

        if (_currentVpnSetupPlan?.RouterAdminUri is not { } routerUri)
        {
            StatusMessage = "No private router gateway was detected.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = routerUri.AbsoluteUri,
                UseShellExecute = true
            });
            StatusMessage = "Router administration page opened in the default browser";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task PublishVpnExchangeViaSyncAsync()
    {
        await RunOperationAsync("Publishing the signed VPN message through Sync…", async () =>
        {
            SyncthingProfile profile = RequireVpnSyncProfile();
            VpnSyncMessage message = await _vpnSyncExchangeService.PublishAsync(
                profile.ExchangeDirectory,
                VpnExchangeText);
            await RefreshVpnSyncMessagesCoreAsync();
            SelectedVpnSyncMessage = VpnSyncMessages.FirstOrDefault(item =>
                item.Message.Envelope.MessageId == message.Envelope.MessageId);
            VpnSyncStatus = _syncEngine?.Status.State == SyncEngineState.Running
                ? "Signed public VPN message published; Sync is transferring it to authorized peers."
                : "Signed public VPN message queued locally; start Sync to transfer it.";
        }, "Signed VPN message published through Sync");
    }

    public async Task RefreshVpnSyncMessagesAsync() => await RunOperationAsync(
        "Reading signed VPN messages from Sync…",
        RefreshVpnSyncMessagesCoreAsync,
        "VPN Sync messages refreshed");

    public Task LoadSelectedVpnSyncMessageAsync()
    {
        if (SelectedVpnSyncMessage is null)
        {
            return Task.CompletedTask;
        }

        VpnExchangeText = _vpnSyncExchangeService.LoadPayload(SelectedVpnSyncMessage.Message);
        VpnSyncStatus = "Signed message loaded. Review it, then join or accept it explicitly.";
        StatusMessage = "VPN Sync message loaded for explicit review";
        return Task.CompletedTask;
    }

    public async Task CreateVpnInvitationAsync()
    {
        await RunOperationAsync("Création de l'invitation VPN signée…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile);
            SignedVpnInvitation invitation = VpnPeerExchangeCodec.CreateInvitation(
                profile,
                identity,
                SelectedVpnInvitationCapability,
                TimeSpan.FromHours(24));
            VpnExchangeText = VpnPeerExchangeCodec.ExportInvitation(invitation);
            VpnDetails = $"Invitation {SelectedVpnInvitationCapability} valable 24 h, sans accès Git ni Sync implicite.";
        }, "Invitation VPN prête à transmettre");
    }

    public async Task JoinVpnInvitationAsync()
    {
        await RunOperationAsync("Préparation du pair VPN…", async () =>
        {
            if (_currentVpnProfile is null)
            {
                throw new InvalidOperationException("Configurez d'abord WireGuard sur cet appareil.");
            }

            SignedVpnInvitation invitation = VpnPeerExchangeCodec.ImportInvitation(VpnExchangeText);
            VpnProjectProfile profile = VpnPeerExchangeCodec.ApplyInvitation(_currentVpnProfile, invitation) with
            {
                LocalCapabilities = SelectedVpnInvitationCapability
            };
            using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile);
            VpnJoinResponse response = VpnPeerExchangeCodec.CreateJoinResponse(
                invitation,
                profile,
                identity,
                SelectedVpnInvitationCapability);
            await _vpnProfileStore.SaveAsync(profile);
            _currentVpnProfile = profile;
            ApplyVpnProfile(profile);
            VpnExchangeText = VpnPeerExchangeCodec.ExportJoinResponse(response);
        }, "Réponse VPN signée prête à renvoyer au propriétaire");
    }

    public async Task AcceptVpnResponseAsync()
    {
        await RunOperationAsync("Validation de la réponse VPN…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile);
            VpnJoinResponse response = VpnPeerExchangeCodec.ImportJoinResponse(VpnExchangeText);
            VpnPeerDefinition peer = VpnPeerExchangeCodec.ValidateJoinResponse(
                response,
                profile.ProjectId,
                identity.Identity);
            if (profile.Peers.Any(item => item.PeerId == peer.PeerId ||
                                          item.PublicKey == peer.PublicKey ||
                                          item.TunnelAddress == peer.TunnelAddress))
            {
                throw new InvalidOperationException("Ce pair, cette clé ou cette adresse VPN est déjà enregistré.");
            }

            profile = profile with { Peers = [.. profile.Peers, peer], UpdatedAt = DateTimeOffset.UtcNow };
            await _vpnProfileStore.SaveAsync(profile);
            _currentVpnProfile = profile;
            ApplyVpnProfile(profile);
        }, "Pair VPN autorisé");
    }

    public async Task RemoveSelectedVpnPeerAsync()
    {
        if (_currentVpnProfile is null || SelectedVpnPeer is null)
        {
            return;
        }

        VpnPeerViewModel selected = SelectedVpnPeer;
        await RunOperationAsync("Retrait du pair VPN…", async () =>
        {
            _currentVpnProfile = _currentVpnProfile with
            {
                Peers = _currentVpnProfile.Peers.Where(peer => peer.PeerId != selected.PeerId).ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _vpnProfileStore.SaveAsync(_currentVpnProfile);
            ApplyVpnProfile(_currentVpnProfile);
        }, $"Pair VPN {selected.DisplayName} retiré");
    }

    public void SetSwarmAgentPath(string path) => SwarmAgentPath = path;

    public void SetSwarmCoordinatorPath(string path) => SwarmCoordinatorPath = path;

    public void SetSwarmOptionsPath(string path) => SwarmOptionsPath = path;

    public void SetSwarmCacheFolder(string path) => SwarmCacheFolder = path;

    public async Task SaveSwarmAsync() => await RunOperationAsync(
        "Saving the Unreal Swarm VPN sessionâ€¦",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            SwarmStatus = $"{profile.Role} · coordinator {profile.CoordinatorAddress} · TCP 8008/8009 over VPN";
        },
        "Unreal Swarm VPN session saved");

    public async Task DiagnoseSwarmAsync() => await RunOperationAsync(
        "Testing Swarm, VPN, DNS, firewall and coordinator portsâ€¦",
        async () =>
        {
            VpnProjectProfile vpn = await SaveVpnFormCoreAsync();
            SwarmProjectProfile swarm = await SaveSwarmFormCoreAsync();
            SwarmDiagnosticReport report = await _swarmSetupService.DiagnoseAsync(swarm, vpn);
            SwarmDiagnostic = report.Summary + Environment.NewLine + Environment.NewLine + string.Join(
                Environment.NewLine + Environment.NewLine,
                report.Checks.Select(check =>
                {
                    string icon = check.State switch
                    {
                        SwarmCheckState.Passed => "PASS",
                        SwarmCheckState.Warning => "WARN",
                        SwarmCheckState.Failed => "FAIL",
                        _ => "SKIP"
                    };
                    string remediation = string.IsNullOrWhiteSpace(check.Remediation)
                        ? string.Empty
                        : Environment.NewLine + "Fix: " + check.Remediation;
                    return $"[{icon}] {check.Name} · {check.Detail}{remediation}";
                }));
            SwarmStatus = report.Ready
                ? "Swarm over VPN is ready."
                : "Swarm setup needs attention; follow the exact fixes in the diagnostic report.";
        },
        "Swarm VPN diagnostic completed");

    public async Task ApplySwarmOptionsAsync() => await RunOperationAsync(
        "Applying Swarm Agent settings with backupâ€¦",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            SwarmOptionsUpdateResult result = await _swarmSetupService.UpdateAgentOptionsAsync(profile);
            SwarmStatus = $"Updated {string.Join(", ", result.UpdatedFields)}. Backup: {result.BackupPath}";
        },
        "Swarm Agent configuration applied");

    public async Task ApplySwarmDnsAsync() => await RunOperationAsync(
        "Applying the project-owned local Swarm DNS aliasâ€¦",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            await _swarmSetupService.ApplyLocalDnsAliasAsync(profile);
            SwarmStatus = $"Local alias {profile.CoordinatorAlias} -> {profile.CoordinatorAddress} applied and DNS cache flushed.";
        },
        "Local Swarm DNS alias applied");

    public async Task RemoveSwarmDnsAsync() => await RunOperationAsync(
        "Removing only the CyRevision Swarm DNS blockâ€¦",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            await _swarmSetupService.RemoveLocalDnsAliasAsync(profile);
            SwarmStatus = $"Local alias block for {profile.CoordinatorAlias} removed.";
        },
        "Local Swarm DNS alias removed");

    public async Task LaunchSwarmAgentAsync() => await RunOperationAsync(
        "Launching Swarm Agentâ€¦",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            _swarmSetupService.LaunchAgent(profile);
            await Task.CompletedTask;
        },
        "Swarm Agent launched");

    public async Task LaunchSwarmCoordinatorAsync() => await RunOperationAsync(
        "Launching Swarm Coordinatorâ€¦",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            _swarmSetupService.LaunchCoordinator(profile);
            await Task.CompletedTask;
        },
        "Swarm Coordinator launched");

    public void SetVpnFileInboxPath(string path) => VpnFileInboxPath = path;

    public void SetVpnFileSharedFolderPath(string path) => VpnFileSharedFolderPath = path;

    public async Task SaveVpnFileExchangeAsync() => await RunOperationAsync(
        "Saving secure VPN file exchangeâ€¦",
        async () =>
        {
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            VpnFileStatus = $"Saved · binds only to {credentials.Profile.ListenAddress}:{credentials.Profile.Port} · project token required";
        },
        "Secure VPN file exchange saved");

    public async Task StartVpnFileExchangeAsync() => await RunOperationAsync(
        "Starting the VPN-only file endpointâ€¦",
        async () =>
        {
            VpnProjectProfile vpn = await SaveVpnFormCoreAsync();
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            if (_vpnFileExchangeHost is not null)
            {
                await _vpnFileExchangeHost.DisposeAsync();
            }
            _vpnFileExchangeHost = _vpnFileExchangeService.CreateHost(credentials, vpn);
            _vpnFileExchangeHost.Start();
            VpnFileStatus = $"Listening on {_vpnFileExchangeHost.Endpoint} only · WireGuard encrypted · token authenticated";
            OnPropertyChanged(nameof(VpnFileHostRunning));
        },
        "VPN file endpoint started");

    public async Task StopVpnFileExchangeAsync() => await RunOperationAsync(
        "Stopping the VPN file endpointâ€¦",
        async () =>
        {
            if (_vpnFileExchangeHost is not null)
            {
                await _vpnFileExchangeHost.DisposeAsync();
                _vpnFileExchangeHost = null;
            }
            VpnFileStatus = "VPN file endpoint stopped. Shared and received files remain on disk.";
            OnPropertyChanged(nameof(VpnFileHostRunning));
        },
        "VPN file endpoint stopped");

    public async Task TestVpnFilePeerAsync() => await RunOperationAsync(
        "Testing the selected VPN file peerâ€¦",
        async () =>
        {
            VpnPeerViewModel peer = SelectedVpnFilePeer
                                    ?? throw new InvalidOperationException("Select a VPN peer first.");
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            VpnFileStatus = await _vpnFileExchangeService.TestAsync(
                peer.TunnelAddress, credentials.Profile.Port, credentials.AccessToken);
        },
        "VPN file peer authenticated");

    public async Task RefreshVpnSharedFilesAsync() => await RunOperationAsync(
        "Reading the selected peer shared folderâ€¦",
        async () =>
        {
            VpnPeerViewModel peer = SelectedVpnFilePeer
                                    ?? throw new InvalidOperationException("Select a VPN peer first.");
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            IReadOnlyList<VpnSharedFile> files = await _vpnFileExchangeService.ListAsync(
                peer.TunnelAddress, credentials.Profile.Port, credentials.AccessToken);
            ReplaceCollection(VpnSharedFiles, files.Select(file => new VpnSharedFileViewModel(file)));
            SelectedVpnSharedFile = VpnSharedFiles.FirstOrDefault();
            VpnFileStatus = $"{files.Count} shared file(s) exposed by {peer.DisplayName}.";
        },
        "Remote shared folder refreshed");

    public async Task SendVpnFileAsync(string path) => await RunOperationAsync(
        "Sending and verifying the file through WireGuardâ€¦",
        async () =>
        {
            VpnPeerViewModel peer = SelectedVpnFilePeer
                                    ?? throw new InvalidOperationException("Select a VPN peer first.");
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            VpnFileTransferResult result = await _vpnFileExchangeService.SendFileAsync(
                peer.TunnelAddress, credentials.Profile.Port, credentials.AccessToken, path);
            VpnFileStatus = $"Sent {result.Name} · {result.Size:N0} bytes · SHA-256 {result.Sha256[..12]}â€¦";
        },
        "VPN file sent and verified");

    public async Task DownloadVpnSharedFileAsync(string destinationPath) => await RunOperationAsync(
        "Downloading and verifying the shared file through WireGuardâ€¦",
        async () =>
        {
            VpnPeerViewModel peer = SelectedVpnFilePeer
                                    ?? throw new InvalidOperationException("Select a VPN peer first.");
            VpnSharedFileViewModel remote = SelectedVpnSharedFile
                                            ?? throw new InvalidOperationException("Select a shared file first.");
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            VpnFileTransferResult result = await _vpnFileExchangeService.DownloadFileAsync(
                peer.TunnelAddress,
                credentials.Profile.Port,
                credentials.AccessToken,
                remote.RelativePath,
                destinationPath);
            VpnFileStatus = $"Received {result.Name} · SHA-256 {result.Sha256[..12]}â€¦ · {result.DestinationPath}";
        },
        "Shared file downloaded and verified");

    public async Task RotateVpnFileTokenAsync() => await RunOperationAsync(
        "Rotating the project file-exchange tokenâ€¦",
        async () =>
        {
            if (SelectedProject is null)
            {
                throw new InvalidOperationException("Select a project first.");
            }
            if (_vpnFileExchangeHost is not null)
            {
                await _vpnFileExchangeHost.DisposeAsync();
                _vpnFileExchangeHost = null;
            }
            _currentVpnFileExchange = await _vpnFileExchangeProfileStore.RotateTokenAsync(SelectedProject.Id);
            ApplyVpnFileExchangeProfile(_currentVpnFileExchange);
            VpnFileStatus = "Token rotated and endpoint stopped. Copy the new token to authorized peers, then restart the endpoint.";
            OnPropertyChanged(nameof(VpnFileHostRunning));
        },
        "VPN file token rotated");

    public async Task ConnectDiscordAgentAsync()
    {
        if (SelectedProject is null || !DiscordUsesAutonomousAgent)
        {
            return;
        }

        await RunOperationAsync("Connecting to the autonomous Discord agent…", async () =>
        {
            DiscordControlConnection connection = BuildDiscordControlConnection();
            using DiscordAgentControlClient client = CreateDiscordControlClient(connection);
            DiscordAgentHostStatus host = await client.GetHostStatusAsync();
            await _discordControlConnectionStore.SaveAsync(connection);
            _currentDiscordControlConnection = connection;
            DiscordControlTokenConfigured = true;
            DiscordAgentApiToken = string.Empty;
            DiscordAgentPublicStatus? projectStatus = await client.GetProjectStatusAsync(SelectedProject.Id);
            if (projectStatus is not null)
            {
                ApplyAutonomousDiscordStatus(projectStatus);
            }
            else
            {
                DiscordIsRunning = false;
                DiscordWebhookConfigured = false;
                DiscordAgentState = "Autonomous agent connected — project not configured";
                DiscordAgentDetails = "Save this project configuration to register it on the autonomous agent.";
            }

            DiscordConnectionSummary = $"Connected to {host.Service} {host.Version} · " +
                                       $"{host.RunningProjects}/{host.ConfiguredProjects} project agent(s) running";
        }, "Autonomous Discord agent connected");
    }

    public async Task LaunchLocalDiscordAgentAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        SelectedDiscordExecutionMode = DiscordAgentExecutionMode.Autonomous;
        await RunOperationAsync("Launching the local autonomous Discord agent…", async () =>
        {
            string executableName = OperatingSystem.IsWindows()
                ? "CyRevision.Discord.Agent.exe"
                : "CyRevision.Discord.Agent";
            string executablePath = Path.Combine(AppContext.BaseDirectory, "Agent", executableName);
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "The autonomous agent is not present beside this CyRevision build.",
                    executablePath);
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process? process = Process.Start(startInfo);
            process?.Dispose();

            DiscordAgentEndpoint = "http://127.0.0.1:47831";
            DiscordAllowPrivateHttp = false;
            DiscordAgentRepositoryPath = SelectedProject.RootPath;
            string tokenPath = Path.Combine(
                _applicationPaths.DiscordDirectory,
                "agent-host",
                "control-token.txt");
            for (int attempt = 0; attempt < 50 && !File.Exists(tokenPath); attempt++)
            {
                await Task.Delay(200);
            }

            if (!File.Exists(tokenPath))
            {
                throw new TimeoutException(
                    "The local autonomous agent did not create its control token within ten seconds.");
            }

            DiscordAgentApiToken = (await File.ReadAllTextAsync(tokenPath)).Trim();
            DiscordControlConnection connection = BuildDiscordControlConnection();
            using DiscordAgentControlClient client = CreateDiscordControlClient(connection);
            DiscordAgentHostStatus? host = null;
            Exception? lastConnectionError = null;
            for (int attempt = 0; attempt < 50 && host is null; attempt++)
            {
                try
                {
                    host = await client.GetHostStatusAsync();
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastConnectionError = exception;
                    await Task.Delay(200);
                }
            }

            if (host is null)
            {
                throw new TimeoutException(
                    "The local autonomous agent did not open its control API within ten seconds.",
                    lastConnectionError);
            }

            await _discordControlConnectionStore.SaveAsync(connection);
            _currentDiscordControlConnection = connection;
            DiscordControlTokenConfigured = true;
            DiscordAgentApiToken = string.Empty;
            DiscordConnectionSummary = $"Local sidecar connected · {host.RunningProjects}/{host.ConfiguredProjects} running";
        }, "Local autonomous Discord agent launched");
    }

    public async Task SaveDiscordAgentAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (DiscordUsesAutonomousAgent)
        {
            await SaveAutonomousDiscordAgentAsync(startAfterSave: false);
            return;
        }

        await RunOperationAsync("Saving the Discord agent…", async () =>
        {
            DiscordAgentProfile profile = BuildDiscordProfile();
            await SaveIntegratedDiscordPreferenceAsync();
            await _discordAgentStore.SaveProfileAsync(profile);
            _currentDiscordProfile = profile;
            DiscordWebhookConfigured = true;
            DiscordWebhookUrl = string.Empty;
            if (_discordAgent.IsRunning)
            {
                await _discordAgent.StartAsync(profile, SelectedProject.RootPath, SelectedProject.Name);
            }

            DiscordAgentState = _discordAgent.IsRunning
                ? "Discord agent watching"
                : "Discord agent ready — stopped";
            DiscordAgentDetails = "Settings are stored locally for this project.";
        }, "Discord agent settings saved");
    }

    public async Task StartDiscordAgentAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (DiscordUsesAutonomousAgent)
        {
            await SaveAutonomousDiscordAgentAsync(startAfterSave: true);
            return;
        }

        if (!SelectedProject.Definition.Features.GitEnabled)
        {
            StatusMessage = "The Discord commit agent requires a Git-enabled project.";
            return;
        }

        await RunOperationAsync("Starting the Discord agent…", async () =>
        {
            DiscordAgentProfile profile = BuildDiscordProfile();
            await SaveIntegratedDiscordPreferenceAsync();
            await _discordAgentStore.SaveProfileAsync(profile);
            _currentDiscordProfile = profile;
            DiscordWebhookConfigured = true;
            DiscordWebhookUrl = string.Empty;
            await _discordAgent.StartAsync(profile, SelectedProject.RootPath, SelectedProject.Name);
        }, "Discord agent started");
    }

    public async Task StopDiscordAgentAsync()
    {
        if (DiscordUsesAutonomousAgent)
        {
            await RunAutonomousCommandAsync(
                (client, projectId) => client.StopAsync(projectId),
                "Stopping the autonomous Discord agent…",
                "Autonomous Discord agent stopped");
            return;
        }

        await RunOperationAsync(
            "Stopping the Discord agent…",
            () => _discordAgent.StopAsync(),
            "Discord agent stopped");
    }

    public async Task CheckDiscordAgentNowAsync()
    {
        if (DiscordUsesAutonomousAgent)
        {
            await RunAutonomousCommandAsync(
                (client, projectId) => client.PollNowAsync(projectId),
                "Checking the autonomous Discord agent…",
                "Autonomous Discord check completed");
            return;
        }

        if (!_discordAgent.IsRunning)
        {
            StatusMessage = "Start the Discord agent before checking for project updates.";
            return;
        }

        await RunOperationAsync(
            "Checking Git for Discord updates…",
            () => _discordAgent.PollNowAsync(),
            "Discord check completed");
    }

    public async Task TestDiscordWebhookAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (DiscordUsesAutonomousAgent)
        {
            await SaveAutonomousDiscordAgentAsync(startAfterSave: false, sendTestAfterSave: true);
            return;
        }

        await RunOperationAsync("Sending a Discord test message…", async () =>
        {
            DiscordAgentProfile profile = BuildDiscordProfile();
            await _discordAgent.SendTestAsync(profile, SelectedProject.Name);
        }, "Discord test message sent");
    }

    public async Task RemoveDiscordWebhookAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (DiscordUsesAutonomousAgent)
        {
            Guid remoteProjectId = SelectedProject.Id;
            await RunOperationAsync("Removing the autonomous Discord configuration…", async () =>
            {
                using DiscordAgentControlClient client = CreateDiscordControlClient();
                await client.RemoveAsync(remoteProjectId);
                await _discordControlConnectionStore.RemoveAsync(remoteProjectId);
                _currentDiscordControlConnection = null;
                ResetDiscordView();
            }, "Autonomous Discord configuration removed");
            return;
        }

        Guid projectId = SelectedProject.Id;
        await RunOperationAsync("Removing the Discord webhook…", async () =>
        {
            await _discordAgent.StopAsync();
            await _discordAgentStore.RemoveProjectAsync(projectId);
            ResetDiscordView();
        }, "Discord webhook removed from local settings");
    }

    public Task RefreshProjectMembersAsync() =>
        RunOperationAsync(
            "Refreshing project membersâ€¦",
            () => RefreshProjectMembersCoreAsync(testConnections: true),
            "Project member overview refreshed");

    private async Task RefreshProjectMembersCoreAsync(bool testConnections)
    {
        SyncProjectMembers.Clear();
        GitProjectMembers.Clear();
        VpnProjectMembers.Clear();
        if (SelectedProject is null)
        {
            ProjectMembersSummary = "No project selected.";
            return;
        }

        await LoadPeerMembersCoreAsync();
        Dictionary<string, SyncthingPeerConnectionStatus> syncConnections = new(StringComparer.OrdinalIgnoreCase);
        if (_currentSyncProfile is not null &&
            _syncEngine?.Status.State is SyncEngineState.Running or SyncEngineState.Paused)
        {
            try
            {
                using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
                foreach (SyncthingPeerConnectionStatus connection in await api.GetPeerConnectionsAsync())
                {
                    syncConnections[connection.DeviceId] = connection;
                }
            }
            catch
            {
                // Authorized identities remain visible when the local Sync API is unavailable.
            }
        }

        foreach (PeerMemberViewModel member in PeerMembers)
        {
            string deviceId = member.Certificate.Device.SyncthingDeviceId;
            syncConnections.TryGetValue(deviceId, out SyncthingPeerConnectionStatus? connection);
            bool online = connection?.Connected == true;
            SyncProjectMembers.Add(new ProjectParticipantViewModel(
                member.DisplayName,
                member.DeviceIdShort,
                member.Role,
                online ? "Connected" : _syncEngine is null ? "Sync stopped" : "Offline",
                connection?.LastSeenAt?.ToLocalTime().ToString("g") ?? member.Certificate.IssuedAt.ToLocalTime().ToString("g"),
                connection?.Address ?? "Authorized project device",
                online ? "#78D7B7" : "#A9ABB2",
                online));
        }

        foreach (IGrouping<string, GitRevision> contributor in History
                     .GroupBy(revision => string.IsNullOrWhiteSpace(revision.AuthorEmail)
                         ? revision.AuthorName
                         : revision.AuthorEmail,
                         StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Count()))
        {
            GitRevision latest = contributor.OrderByDescending(revision => revision.AuthoredAt).First();
            GitProjectMembers.Add(new ProjectParticipantViewModel(
                latest.AuthorName,
                string.IsNullOrWhiteSpace(latest.AuthorEmail) ? "No email" : latest.AuthorEmail,
                "Contributor",
                $"{contributor.Count()} commit(s)",
                latest.AuthoredAt.ToLocalTime().ToString("g"),
                latest.Subject,
                "#61AFEF",
                false));
        }

        IReadOnlyDictionary<Guid, VpnPeerConnectivity> vpnConnectivity = new Dictionary<Guid, VpnPeerConnectivity>();
        if (testConnections && _currentVpnProfile is not null)
        {
            try
            {
                VpnEngineStatus status = await _wireGuardEngine.GetStatusAsync(_currentVpnProfile);
                if (status.State == VpnRuntimeState.Running)
                {
                    VpnConnectivityReport report = await _vpnNetworkSetupService.TestConnectivityAsync(_currentVpnProfile);
                    vpnConnectivity = report.Peers.ToDictionary(peer => peer.PeerId);
                }
            }
            catch
            {
                // Configured peers remain visible even if WireGuard cannot be queried.
            }
        }

        foreach (VpnPeerViewModel peer in VpnPeers)
        {
            vpnConnectivity.TryGetValue(peer.PeerId, out VpnPeerConnectivity? connectivity);
            bool online = connectivity?.RecentHandshake == true;
            VpnProjectMembers.Add(new ProjectParticipantViewModel(
                peer.DisplayName,
                peer.TunnelAddress,
                peer.Capabilities,
                !peer.Peer.Enabled ? "Disabled" : online ? "Connected" : "Configured",
                connectivity?.LastHandshakeAt?.ToLocalTime().ToString("g") ?? "â€”",
                peer.Endpoint,
                online ? "#78D7B7" : peer.Peer.Enabled ? "#E5C07B" : "#7E8189",
                online));
        }

        ProjectMembersSummary =
            $"Sync {SyncProjectMembers.Count(member => member.IsOnline)}/{SyncProjectMembers.Count} online Â· " +
            $"Git {GitProjectMembers.Count} contributor(s) Â· " +
            $"VPN {VpnProjectMembers.Count(member => member.IsOnline)}/{VpnProjectMembers.Count} connected";
    }

    private async Task LoadSelectedProjectAsync()
    {
        await _discordAgent.StopAsync();
        ClearPullRequestData();
        if (SelectedProject is null)
        {
            await StopSyncCoreAsync();
            ResetDiscordView();
            ClearRepositoryView();
            return;
        }

        if (_syncEngineProjectId is not null && _syncEngineProjectId != SelectedProject.Id)
        {
            await StopSyncCoreAsync();
        }

        ProjectDefinition updated = SelectedProject.Definition with { LastOpenedAt = DateTimeOffset.UtcNow };
        SelectedProject.Update(updated);
        LoadBackupSettings(updated);
        SelectedPreset = ProjectPresets.All.FirstOrDefault(preset =>
            preset.Features == updated.Features && preset.Retention.Mode == updated.Retention.Mode);
        ClearGitGraphView();
        try
        {
            await _projectCatalog.UpsertAsync(updated);
            StatusMessage = "Chargement du dépôt…";
            await RefreshCoreAsync();
            await LoadLfsManagementCoreAsync();
            await LoadLfsLocksCoreAsync();
            await LoadRemoteBuildCoreAsync();
            await LoadBackupsCoreAsync();
            await LoadSyncProfileCoreAsync();
            await LoadVpnProfileCoreAsync();
            await LoadDiscordProfileCoreAsync();
            await LoadAdvisoryReservationsCoreAsync();
            await RefreshCodeWorkspaceAsync();
            await LoadAiMcpProfileCoreAsync();
            await ResolvePullRequestRepositoryAsync();
            await RefreshProjectMembersCoreAsync(testConnections: false);
            StatusMessage = "Dépôt chargé";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task LoadSelectedDiffAsync()
    {
        if (SelectedProject is null || SelectedChange is null)
        {
            DiffText = "Sélectionnez un fichier pour afficher son diff.";
            return;
        }

        try
        {
            DiffText = await _gitService.GetDiffAsync(
                SelectedProject.RootPath,
                SelectedChange.Path,
                SelectedChange.Change.IsStaged);
            if (string.IsNullOrWhiteSpace(DiffText))
            {
                DiffText = SelectedChange.Change.IsLfsObject
                    ? "Fichier LFS binaire — le diff visuel sera proposé par CyRevision Diff."
                    : "Aucun diff textuel disponible pour ce fichier.";
            }
        }
        catch (Exception exception)
        {
            DiffText = exception.Message;
        }
    }

    private async Task SelectExplorerCommitCoreAsync(string commitHash)
    {
        if (SelectedProject is null)
        {
            return;
        }

        try
        {
            GitCommitDetails details = await _gitService.GetCommitDetailsAsync(SelectedProject.RootPath, commitHash);
            _allExplorerRevisions = _allExplorerRevisions.Append(details.Revision).ToArray();
            SelectedExplorerRevision = details.Revision;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task LoadExplorerRevisionAsync(GitRevision revision)
    {
        if (SelectedProject is null)
        {
            return;
        }

        _comparisonFromHash = null;
        _comparisonToHash = null;
        try
        {
            GitCommitDetails details = await _gitService.GetCommitDetailsAsync(
                SelectedProject.RootPath,
                revision.Hash);
            if (SelectedExplorerRevision?.Hash != revision.Hash)
            {
                return;
            }

            ReplaceCollection(ExplorerFiles, details.Files);
            ExplorerSummary = $"{details.Revision.ShortHash} · {details.Revision.Subject} · " +
                              $"{details.Files.Count} fichier(s) · +{details.AddedLines} / -{details.DeletedLines} · " +
                              $"{details.BinaryFileCount} binaire(s)";
            ExplorerDiff = await _gitService.GetCommitDiffAsync(SelectedProject.RootPath, revision.Hash);
            SelectedExplorerFile = ExplorerFiles.FirstOrDefault();
        }
        catch (Exception exception)
        {
            ExplorerSummary = exception.Message;
            ExplorerFiles.Clear();
            ExplorerFileHistory.Clear();
            MultiRestoreFiles.Clear();
            MultiRestoreOperations.Clear();
            BranchComparisonCommits.Clear();
            BranchCompareSources.Clear();
            BranchCompareTargets.Clear();
            ExplorerDiff = exception.Message;
        }
    }

    private async Task LoadExplorerFileAsync(GitCommitFileChange? file)
    {
        if (SelectedProject is null || SelectedExplorerRevision is null || file is null)
        {
            ExplorerFileHistory.Clear();
            return;
        }

        string revisionHash = SelectedExplorerRevision.Hash;
        try
        {
            Task<IReadOnlyList<GitFileRevision>> historyTask = _gitService.GetFileHistoryAsync(
                SelectedProject.RootPath,
                file.Path,
                100);
            Task<string> diffTask = _comparisonFromHash is not null && _comparisonToHash is not null
                ? _gitService.GetComparisonDiffAsync(
                    SelectedProject.RootPath,
                    _comparisonFromHash,
                    _comparisonToHash,
                    file.Path)
                : _gitService.GetCommitDiffAsync(SelectedProject.RootPath, revisionHash, file.Path);
            await Task.WhenAll(historyTask, diffTask);
            if (SelectedExplorerFile?.Path != file.Path)
            {
                return;
            }

            ReplaceCollection(ExplorerFileHistory, await historyTask);
            string diff = await diffTask;
            ExplorerDiff = string.IsNullOrWhiteSpace(diff)
                ? file.IsLfsObject
                    ? "Objet Git LFS : utilisez la Time Machine pour prévisualiser ou exporter cette version."
                    : "Aucun diff textuel disponible pour ce fichier."
                : diff;
        }
        catch (Exception exception)
        {
            ExplorerDiff = exception.Message;
        }
    }

    private void ApplyExplorerFilter()
    {
        string query = ExplorerSearch.Trim();
        IEnumerable<GitRevision> filtered = _allExplorerRevisions;
        if (query.Length > 0)
        {
            filtered = filtered.Where(revision =>
                revision.Hash.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                revision.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                revision.AuthorName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                revision.AuthorEmail.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceCollection(History, filtered);
    }

    private void UpdateSmartSyncPlan()
    {
        if (SelectedProject is null)
        {
            SmartSyncPlanItems.Clear();
            SmartSyncPlanSummary = "Aucun projet sélectionné.";
            return;
        }

        int recentCount = int.TryParse(SmartSyncRecentVersionCount, out int parsedRecent)
            ? Math.Clamp(parsedRecent, 1, 100)
            : 3;
        SmartSyncInventory inventory = new(
            _allExplorerRevisions.Count,
            Changes.Count,
            LfsFiles.Count(file => file.IsAvailableLocally),
            LfsFiles.Where(file => file.IsAvailableLocally).Sum(file => file.Pointer.Size),
            LfsFiles.Count(file => !file.IsAvailableLocally),
            LfsVersions.Skip(1).Count(version => version.IsAvailableLocally),
            LfsVersions.Skip(1).Where(version => version.IsAvailableLocally).Sum(version => version.Pointer.Size),
            Backups.Count);
        SmartSyncPlan plan = new SmartSyncPlanner().Build(
            SelectedProject.Definition.Features,
            inventory,
            new SmartSyncPolicy(SelectedLfsHistoryMode, recentCount, SmartSyncReplicateBackups));
        ReplaceCollection(SmartSyncPlanItems, plan.Items);
        SmartSyncPlanSummary = $"{plan.ImmediateItemCount} élément(s) prioritaire(s) · " +
                               $"{plan.DeferredItemCount} différé(s) / à la demande · aucun transfert automatique";
    }

    private async Task LoadLfsTimelineAsync(LfsTrackedFile? file)
    {
        LfsPreview = null;
        LfsVersions.Clear();
        SelectedLfsVersion = null;
        if (SelectedProject is null || file is null)
        {
            LfsTimelineSummary = "Sélectionnez un fichier LFS pour afficher ses versions.";
            return;
        }

        LfsTimelineSummary = "Lecture de l'historique LFS…";
        try
        {
            IReadOnlyList<LfsFileVersion> versions = await _gitService.GetLfsFileVersionsAsync(
                SelectedProject.RootPath,
                file.Path,
                200);
            PeerLfsAvailabilityCache peerAvailability = await _gitPeerExchangeService.GetCachedLfsAvailabilityAsync(
                GetGitExchangeStatePath(SelectedProject.Id),
                SelectedProject.Id);
            if (SelectedLfsFile?.Path != file.Path)
            {
                return;
            }

            Dictionary<string, PeerLfsObjectAvailability> peerObjects = peerAvailability.Objects.ToDictionary(
                item => item.OidSha256,
                StringComparer.OrdinalIgnoreCase);
            LfsFileVersion[] enrichedVersions = versions.Select(version =>
            {
                List<LfsObjectLocation> locations = [];
                if (peerObjects.TryGetValue(version.Pointer.OidSha256, out PeerLfsObjectAvailability? available))
                {
                    locations.AddRange(available.Peers.Select(peer => new LfsObjectLocation(
                        LfsStorageKind.Peer,
                        peer.DisplayName,
                        peer.LastSeenAt,
                        peer.PublishedToExchange)));
                }

                LfsObjectLocation? archive = GetColdArchiveLocation(SelectedProject.Definition, version.Pointer.OidSha256);
                if (archive is not null)
                {
                    locations.Add(archive);
                }

                return version with { KnownLocations = locations };
            }).ToArray();
            ReplaceCollection(LfsVersions, enrichedVersions);
            int localCount = versions.Count(version => version.IsAvailableLocally);
            int peerCount = enrichedVersions.Count(version => version.HasPeerCopy);
            int archiveCount = enrichedVersions.Count(version => version.HasArchiveCopy);
            int missingCount = enrichedVersions.Count(version => !version.IsAvailableLocally && !version.HasPeerCopy && !version.HasArchiveCopy);
            LfsTimelineSummary = $"{versions.Count} version(s) unique(s) · {localCount} locale(s) · " +
                                 $"{peerCount} chez les pairs · {archiveCount} archivée(s) · {missingCount} inconnue(s)";
            PeerLfsTransferSummary = peerAvailability.GeneratedAt == DateTimeOffset.MinValue
                ? "Aucun inventaire de pair vérifié pour le moment."
                : $"Inventaire vérifié : {peerAvailability.Objects.Count} objet(s) chez les pairs · " +
                  $"actualisé {peerAvailability.GeneratedAt.LocalDateTime:g}";
            SelectedLfsVersion = LfsVersions.FirstOrDefault();
            UpdateSmartSyncPlan();
        }
        catch (Exception exception)
        {
            LfsTimelineSummary = exception.Message;
        }
    }

    private void LoadLfsPreview(LfsFileVersion? version)
    {
        LfsPreview = null;
        if (version is null)
        {
            return;
        }

        string shortOid = version.Pointer.OidSha256[..Math.Min(12, version.Pointer.OidSha256.Length)];
        LfsTimelineSummary = $"{version.Revision.ShortHash} · {version.Revision.Subject} · " +
                             $"{version.SizeText} · {version.Availability} · {shortOid}…";
        if (!version.IsAvailableLocally || string.IsNullOrWhiteSpace(version.LocalObjectPath))
        {
            return;
        }

        string extension = Path.GetExtension(version.Path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp"))
        {
            return;
        }

        try
        {
            LfsPreview = new Bitmap(version.LocalObjectPath);
        }
        catch
        {
            LfsPreview = null;
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (!SelectedProject.Definition.Features.GitEnabled)
        {
            RepositoryPath = SelectedProject.RootPath;
            CurrentBranch = "Mode sans Git";
            ChangeSummary = "Synchronisation / sauvegarde de fichiers";
            Changes.Clear();
            History.Clear();
            Branches.Clear();
            LfsPatterns.Clear();
            LfsLocks.Clear();
            MyLfsLocks.Clear();
            LfsLocksSummary = "Git is disabled for this project.";
            ExplorerFiles.Clear();
            ExplorerFileHistory.Clear();
            LfsFiles.Clear();
            LfsVersions.Clear();
            _allExplorerRevisions = [];
            SelectedExplorerRevision = null;
            SelectedLfsFile = null;
            SelectedBranch = null;
            SelectedChange = null;
            DiffText = "Git est désactivé pour ce projet.";
            return;
        }

        string rootPath = SelectedProject.RootPath;
        Task<GitRepositoryStatus> statusTask = _gitService.GetStatusAsync(rootPath);
        Task<IReadOnlyList<GitRevision>> historyTask = _gitService.GetHistoryAsync(rootPath);
        Task<IReadOnlyList<GitBranch>> branchesTask = _gitService.GetBranchesAsync(rootPath);
        Task<IReadOnlyList<LfsTrackedPattern>> lfsTask = _gitService.GetLfsPatternsAsync(rootPath);
        Task<IReadOnlyList<LfsTrackedFile>> lfsFilesTask = _gitService.GetLfsTrackedFilesAsync(rootPath);
        await Task.WhenAll(statusTask, historyTask, branchesTask, lfsTask, lfsFilesTask);

        GitRepositoryStatus status = await statusTask;
        CurrentBranch = status.IsDetachedHead ? $"HEAD {status.CurrentBranch}" : status.CurrentBranch;
        RepositoryPath = status.RootPath;
        ChangeSummary = $"{status.Changes.Count} modification(s) · ↑{status.AheadBy} ↓{status.BehindBy}";

        ReplaceCollection(Changes, status.Changes.Select(change => new GitChangeViewModel(change)));
        _allExplorerRevisions = (await historyTask).ToArray();
        ApplyExplorerFilter();
        ReplaceCollection(Branches, await branchesTask);
        RefreshCompositionBranches();
        ReplaceCollection(LfsPatterns, await lfsTask);
        ReplaceCollection(LfsFiles, await lfsFilesTask);
        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
        SelectedChange = Changes.FirstOrDefault();
        GitRevision? firstRevision = History.FirstOrDefault();
        if (firstRevision is not null && SelectedExplorerRevision?.Hash == firstRevision.Hash)
        {
            _ = LoadExplorerRevisionAsync(firstRevision);
        }
        else
        {
            SelectedExplorerRevision = firstRevision;
        }

        LfsTrackedFile? selectedLfsFile = LfsFiles.FirstOrDefault(file => file.Path == SelectedLfsFile?.Path)
                                              ?? LfsFiles.FirstOrDefault();
        if (selectedLfsFile is not null && SelectedLfsFile?.Path == selectedLfsFile.Path)
        {
            _ = LoadLfsTimelineAsync(selectedLfsFile);
        }
        else
        {
            SelectedLfsFile = selectedLfsFile;
        }
        UpdateSmartSyncPlan();
    }

    private async Task LoadLfsLocksCoreAsync()
    {
        LfsLocks.Clear();
        MyLfsLocks.Clear();
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled)
        {
            LfsLocksSummary = "Git is disabled for this project.";
            return;
        }

        try
        {
            IReadOnlyList<LfsFileLock> locks = await _gitService.GetLfsLocksAsync(SelectedProject.RootPath);
            ReplaceCollection(LfsLocks, locks);
            ReplaceCollection(MyLfsLocks, locks.Where(item => item.IsOurs));
            int others = locks.Count - MyLfsLocks.Count;
            bool cached = locks.Any(item => item.IsCached);
            LfsLocksSummary = $"{locks.Count} project lock(s) Â· {MyLfsLocks.Count} mine Â· {others} other user(s)" +
                              (cached ? " Â· cached/offline data" : " Â· verified with the LFS server");
        }
        catch (Exception exception)
        {
            LfsLocksSummary = "Unable to load Git LFS locks: " + exception.Message;
        }
    }

    private async Task LoadLfsManagementCoreAsync()
    {
        LfsCleanupItems.Clear();
        _lfsCleanupPlan = null;
        if (SelectedProject is null)
        {
            _currentLfsManagementProfile = null;
            return;
        }

        LfsManagementProfile profile = await _lfsManagementProfileStore.GetAsync(SelectedProject.Id)
                                       ?? LfsManagementProfile.CreateDefault(SelectedProject.Id) with
                                       {
                                           ArchivePath = Path.Combine(
                                               _applicationPaths.DataDirectory,
                                               "lfs-archives",
                                               SelectedProject.Id.ToString("N"))
                                       };
        _currentLfsManagementProfile = profile;
        LfsExternalStoragePath = profile.ExternalStoragePath;
        LfsManagementArchivePath = profile.ArchivePath;
        LfsCleanupRemoteName = profile.RemoteName;
        LfsRequiredCopies = profile.RequiredVerifiedCopies.ToString();
        LfsCleanupGraceDays = profile.GracePeriodDays.ToString();
        LfsPeerProofMaximumAgeHours = profile.PeerProofMaximumAgeHours.ToString();
        LfsVerifyRemote = profile.VerifyRemote;
        LfsStorageSummary = "Analyze the repository to classify protected, verified, and blocked LFS objects.";
        LfsRemoteVerification = "Remote verification has not run.";
    }

    private async Task LoadRemoteBuildCoreAsync()
    {
        _remoteBuildCancellation?.Cancel();
        if (SelectedProject is null)
        {
            _currentRemoteBuildCredentials = null;
            return;
        }

        string defaultDestination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "CyRevision Builds",
            SanitizeFileName(SelectedProject.Name));
        RemoteBuildCredentials credentials = await _remoteBuildConnectionStore.GetAsync(SelectedProject.Id)
                                             ?? new RemoteBuildCredentials(
                                                 RemoteBuildConnectionProfile.CreateDefault(
                                                     SelectedProject.Id, defaultDestination),
                                                 string.Empty);
        _currentRemoteBuildCredentials = credentials;
        RemoteBuildEndpoint = credentials.Profile.Endpoint;
        RemoteBuildAccessToken = credentials.AccessToken;
        RemoteBuildRecipeId = credentials.Profile.RecipeId;
        SelectedRemoteBuildSourceMode = credentials.Profile.SourceMode;
        RemoteBuildArtifactDestination = credentials.Profile.ArtifactDestination;
        RemoteBuildMaximumUploadGb = Math.Max(1, credentials.Profile.MaximumUploadBytes / (1024L * 1024 * 1024)).ToString();
        RemoteBuildAllowPrivateHttp = credentials.Profile.AllowPrivateHttp;
        RemoteBuildStatus = string.IsNullOrWhiteSpace(credentials.Profile.RecipeId)
            ? "Configure an allowlisted recipe ID from the remote agent."
            : "Remote build connection loaded; test it before starting a job.";
    }

    private async Task LoadSyncProfileCoreAsync()
    {
        if (SelectedProject is null)
        {
            _currentSyncProfile = null;
            SyncthingExecutablePath = string.Empty;
            SyncState = "Sync désactivé";
            SyncDetails = "Aucun projet sélectionné.";
            return;
        }

        _currentSyncProfile = await _syncthingProfileStore.GetAsync(SelectedProject.Id);
        SyncthingExecutablePath = _currentSyncProfile?.ExecutablePath ?? string.Empty;
        if (!SelectedProject.Definition.Features.PeerSyncEnabled)
        {
            SyncState = "Sync désactivé";
            SyncDetails = "Le moteur ne sera pas lancé dans le mode actuel.";
        }
        else if (_currentSyncProfile is null)
        {
            SyncState = "Sync à configurer";
            SyncDetails = "Choisissez l'exécutable Syncthing. Aucun processus existant ne sera réutilisé.";
        }
        else
        {
            SyncState = "Sync prêt — arrêté";
            SyncDetails = $"Profil isolé sur {_currentSyncProfile.ApiEndpoint}.";
        }
    }

    private async Task LoadVpnProfileCoreAsync()
    {
        if (_vpnFileExchangeHost is not null)
        {
            await _vpnFileExchangeHost.DisposeAsync();
            _vpnFileExchangeHost = null;
            OnPropertyChanged(nameof(VpnFileHostRunning));
        }
        if (SelectedProject is null)
        {
            _currentVpnProfile = null;
            ClearVpnView();
            return;
        }

        _currentVpnProfile = await _vpnProfileStore.GetAsync(SelectedProject.Id);
        if (_currentVpnProfile is null)
        {
            ClearVpnView();
            SelectedVpnBackendMode = VpnBackendMode.SystemInstallation;
            WireGuardInstallation detected = _wireGuardRuntimeResolver.Detect(SelectedVpnBackendMode);
            WireGuardExecutablePath = detected.WireGuardExecutablePath ?? detected.WgExecutablePath ?? string.Empty;
            VpnState = detected.CanGenerateKeys ? "WireGuard détecté — à configurer" : "WireGuard non détecté";
            VpnDetails = "Le VPN peut être utilisé seul, sans lancer Git ni Syncthing.";
            await RefreshVpnSyncMessagesCoreAsync();
            return;
        }

        VpnSetupAcceptIncoming = !string.IsNullOrWhiteSpace(_currentVpnProfile.PublicEndpoint);
        VpnSetupAllowSwarm = _currentVpnProfile.LocalCapabilities.HasFlag(VpnNodeCapabilities.SwarmAgent) ||
                             _currentVpnProfile.LocalCapabilities.HasFlag(VpnNodeCapabilities.SwarmCoordinator);
        VpnSetupAllowControlApi = false;
        ApplyVpnProfile(_currentVpnProfile);
        await RefreshVpnStatusCoreAsync();
        await RefreshVpnSyncMessagesCoreAsync();
        await LoadSwarmAndFileExchangeCoreAsync(_currentVpnProfile);
    }

    private async Task LoadDiscordProfileCoreAsync()
    {
        if (SelectedProject is null)
        {
            ResetDiscordView();
            return;
        }

        _currentDiscordControlConnection = await _discordControlConnectionStore.GetAsync(SelectedProject.Id);
        if (_currentDiscordControlConnection is null)
        {
            SelectedDiscordExecutionMode = DiscordAgentExecutionMode.Integrated;
            DiscordAgentEndpoint = "http://127.0.0.1:47831";
            DiscordAgentRepositoryPath = SelectedProject.RootPath;
            DiscordAllowPrivateHttp = false;
            DiscordControlTokenConfigured = false;
        }
        else
        {
            SelectedDiscordExecutionMode = _currentDiscordControlConnection.Mode;
            DiscordAgentEndpoint = _currentDiscordControlConnection.Endpoint;
            DiscordAgentRepositoryPath = _currentDiscordControlConnection.AgentRepositoryPath ?? SelectedProject.RootPath;
            DiscordAllowPrivateHttp = _currentDiscordControlConnection.AllowPrivateHttp;
            DiscordControlTokenConfigured = !string.IsNullOrWhiteSpace(_currentDiscordControlConnection.ApiToken);
        }

        DiscordAgentApiToken = string.Empty;
        _currentDiscordProfile = await _discordAgentStore.GetProfileAsync(SelectedProject.Id);
        DiscordWebhookUrl = string.Empty;
        if (_currentDiscordProfile is null)
        {
            DiscordDisplayName = "CyRevision";
            DiscordProjectLabel = SelectedProject.Name;
            DiscordRepositoryWebUrl = string.Empty;
            DiscordPollIntervalSeconds = "30";
            DiscordNotifyCommits = true;
            DiscordNotifyBranchChanges = true;
            DiscordStartAutomatically = false;
            DiscordWebhookConfigured = false;
            DiscordAgentState = "Discord agent not configured";
            DiscordAgentDetails = "Create an incoming webhook for the target channel, then paste its URL here.";
            DiscordLastActivity = "No check performed.";
            DiscordIsRunning = false;
        }
        else
        {
            DiscordDisplayName = _currentDiscordProfile.DisplayName;
            DiscordProjectLabel = _currentDiscordProfile.ProjectLabel ?? SelectedProject.Name;
            DiscordRepositoryWebUrl = _currentDiscordProfile.RepositoryWebUrl ?? string.Empty;
            DiscordPollIntervalSeconds = _currentDiscordProfile.PollIntervalSeconds.ToString();
            DiscordNotifyCommits = _currentDiscordProfile.NotifyCommits;
            DiscordNotifyBranchChanges = _currentDiscordProfile.NotifyBranchChanges;
            DiscordStartAutomatically = _currentDiscordProfile.StartAutomatically;
            DiscordWebhookConfigured = DiscordWebhookAddress.TryCreate(_currentDiscordProfile.WebhookUrl, out _);
            DiscordAgentState = "Discord agent ready — stopped";
            DiscordAgentDetails = "The webhook is stored in the local user configuration, outside the repository.";

            DiscordAgentCheckpoint? checkpoint = await _discordAgentStore.GetCheckpointAsync(SelectedProject.Id);
            DiscordLastActivity = checkpoint?.LastCheckedAt is { } checkedAt
                ? $"Last check: {checkedAt.ToLocalTime():g} · branch {checkpoint.LastBranch ?? "—"}"
                : "No check performed.";
        }

        if (DiscordUsesAutonomousAgent)
        {
            DiscordConnectionSummary = "Autonomous agent configured — checking the control API…";
            if (_currentDiscordControlConnection is null || !DiscordControlTokenConfigured)
            {
                DiscordAgentState = "Autonomous agent connection required";
                DiscordAgentDetails = "Enter the API endpoint and control token, then connect.";
                return;
            }

            try
            {
                using DiscordAgentControlClient client = CreateDiscordControlClient(_currentDiscordControlConnection);
                DiscordAgentHostStatus host = await client.GetHostStatusAsync();
                DiscordAgentPublicStatus? status = await client.GetProjectStatusAsync(SelectedProject.Id);
                DiscordConnectionSummary = $"Connected to {host.Service} {host.Version} · " +
                                           $"{host.RunningProjects}/{host.ConfiguredProjects} project agent(s) running";
                if (status is not null)
                {
                    ApplyAutonomousDiscordStatus(status);
                }
                else
                {
                    DiscordWebhookConfigured = false;
                    DiscordIsRunning = false;
                    DiscordAgentState = "Autonomous agent connected — project not configured";
                    DiscordAgentDetails = "Save this project configuration to register it on the autonomous agent.";
                }
            }
            catch (Exception exception)
            {
                DiscordAgentState = "Autonomous agent unavailable";
                DiscordAgentDetails = exception.Message;
                DiscordConnectionSummary = "The desktop application could not reach the autonomous agent.";
            }

            return;
        }

        DiscordConnectionSummary = "Integrated agent — runs only while the desktop application is open.";
        if (_currentDiscordProfile?.StartAutomatically == true &&
            SelectedProject.Definition.Features.GitEnabled &&
            DiscordWebhookConfigured)
        {
            try
            {
                await _discordAgent.StartAsync(
                    _currentDiscordProfile,
                    SelectedProject.RootPath,
                    SelectedProject.Name);
            }
            catch (Exception exception)
            {
                DiscordAgentState = "Discord agent error";
                DiscordAgentDetails = exception.Message;
            }
        }
    }

    private async Task SaveAutonomousDiscordAgentAsync(
        bool startAfterSave,
        bool sendTestAfterSave = false)
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync("Configuring the autonomous Discord agent…", async () =>
        {
            if (_discordAgent.IsRunning)
            {
                await _discordAgent.StopAsync();
            }

            DiscordControlConnection connection = BuildDiscordControlConnection();
            using DiscordAgentControlClient client = CreateDiscordControlClient(connection);
            _ = await client.GetHostStatusAsync();
            DiscordAgentConfigurationRequest configuration = BuildAutonomousDiscordConfiguration();
            await client.ConfigureAsync(configuration);
            if (startAfterSave)
            {
                await client.StartAsync(SelectedProject.Id);
            }

            if (sendTestAfterSave)
            {
                await client.SendTestAsync(SelectedProject.Id);
            }

            await _discordControlConnectionStore.SaveAsync(connection);
            _currentDiscordControlConnection = connection;
            DiscordControlTokenConfigured = true;
            DiscordAgentApiToken = string.Empty;
            DiscordWebhookUrl = string.Empty;
            DiscordAgentPublicStatus? status = await client.GetProjectStatusAsync(SelectedProject.Id);
            if (status is not null)
            {
                ApplyAutonomousDiscordStatus(status);
            }
        }, sendTestAfterSave
            ? "Autonomous Discord test message sent"
            : startAfterSave
                ? "Autonomous Discord agent started"
                : "Autonomous Discord configuration saved");
    }

    private async Task RunAutonomousCommandAsync(
        Func<DiscordAgentControlClient, Guid, Task<DiscordAgentCommandResult>> command,
        string progressMessage,
        string successMessage)
    {
        if (SelectedProject is null)
        {
            return;
        }

        Guid projectId = SelectedProject.Id;
        await RunOperationAsync(progressMessage, async () =>
        {
            using DiscordAgentControlClient client = CreateDiscordControlClient();
            await command(client, projectId);
            DiscordAgentPublicStatus? status = await client.GetProjectStatusAsync(projectId);
            if (status is not null)
            {
                ApplyAutonomousDiscordStatus(status);
            }
        }, successMessage);
    }

    private DiscordControlConnection BuildDiscordControlConnection()
    {
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("Select a project before connecting an autonomous Discord agent.");
        }

        string token = DiscordAgentApiToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _currentDiscordControlConnection?.ApiToken ?? string.Empty;
        }

        string repositoryPath = string.IsNullOrWhiteSpace(DiscordAgentRepositoryPath)
            ? SelectedProject.RootPath
            : DiscordAgentRepositoryPath.Trim();
        DiscordControlConnection connection = new(
            SelectedProject.Id,
            DiscordAgentExecutionMode.Autonomous,
            DiscordAgentEndpoint.Trim(),
            token,
            DiscordAllowPrivateHttp,
            repositoryPath);
        _ = CyRevision.Discord.Control.DiscordAgentEndpoint.Create(
            connection.Endpoint,
            connection.AllowPrivateHttp);
        if (connection.ApiToken.Length < 32)
        {
            throw new InvalidDataException("Enter the autonomous agent control token.");
        }

        return connection;
    }

    private async Task SaveIntegratedDiscordPreferenceAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (_currentDiscordControlConnection?.Mode == DiscordAgentExecutionMode.Autonomous &&
            _currentDiscordControlConnection.ApiToken.Length >= 32)
        {
            using DiscordAgentControlClient remote = CreateDiscordControlClient(_currentDiscordControlConnection);
            await remote.StopAsync(SelectedProject.Id);
        }

        DiscordControlConnection connection = new(
            SelectedProject.Id,
            DiscordAgentExecutionMode.Integrated,
            _currentDiscordControlConnection?.Endpoint ?? "http://127.0.0.1:47831",
            _currentDiscordControlConnection?.ApiToken ?? string.Empty,
            _currentDiscordControlConnection?.AllowPrivateHttp ?? false,
            _currentDiscordControlConnection?.AgentRepositoryPath ?? SelectedProject.RootPath);
        await _discordControlConnectionStore.SaveAsync(connection);
        _currentDiscordControlConnection = connection;
    }

    private DiscordAgentConfigurationRequest BuildAutonomousDiscordConfiguration()
    {
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("Select a project before configuring the autonomous Discord agent.");
        }

        if (!int.TryParse(DiscordPollIntervalSeconds, out int pollIntervalSeconds))
        {
            throw new InvalidDataException("The polling interval must be a number of seconds.");
        }

        return new DiscordAgentConfigurationRequest(
            SelectedProject.Id,
            SelectedProject.Name,
            string.IsNullOrWhiteSpace(DiscordAgentRepositoryPath)
                ? SelectedProject.RootPath
                : DiscordAgentRepositoryPath.Trim(),
            string.IsNullOrWhiteSpace(DiscordWebhookUrl) ? null : DiscordWebhookUrl.Trim(),
            DiscordDisplayName.Trim(),
            string.IsNullOrWhiteSpace(DiscordProjectLabel) ? SelectedProject.Name : DiscordProjectLabel.Trim(),
            string.IsNullOrWhiteSpace(DiscordRepositoryWebUrl) ? null : DiscordRepositoryWebUrl.Trim(),
            pollIntervalSeconds,
            DiscordNotifyCommits,
            DiscordNotifyBranchChanges,
            DiscordStartAutomatically);
    }

    private DiscordAgentControlClient CreateDiscordControlClient() =>
        CreateDiscordControlClient(BuildDiscordControlConnection());

    private static DiscordAgentControlClient CreateDiscordControlClient(DiscordControlConnection connection) =>
        new(connection.Endpoint, connection.ApiToken, connection.AllowPrivateHttp);

    private void ApplyAutonomousDiscordStatus(DiscordAgentPublicStatus status)
    {
        DiscordWebhookConfigured = status.WebhookConfigured;
        DiscordIsRunning = status.IsRunning;
        DiscordDisplayName = status.DisplayName;
        DiscordProjectLabel = status.ProjectLabel ?? status.ProjectName;
        DiscordRepositoryWebUrl = status.RepositoryWebUrl ?? string.Empty;
        DiscordPollIntervalSeconds = status.PollIntervalSeconds.ToString();
        DiscordNotifyCommits = status.NotifyCommits;
        DiscordNotifyBranchChanges = status.NotifyBranchChanges;
        DiscordStartAutomatically = status.StartAutomatically;
        DiscordAgentRepositoryPath = status.RepositoryPath;
        DiscordAgentState = status.State switch
        {
            DiscordAgentRuntimeState.Starting => "Autonomous Discord agent starting",
            DiscordAgentRuntimeState.Watching => "Autonomous Discord agent watching",
            DiscordAgentRuntimeState.Sending => "Autonomous Discord agent sending",
            DiscordAgentRuntimeState.Error => "Autonomous Discord agent error",
            _ => "Autonomous Discord agent ready — stopped"
        };
        DiscordAgentDetails = status.Details;
        DiscordLastActivity = status.LastCheckedAt is { } checkedAt
            ? $"Last check: {checkedAt.ToLocalTime():g} · branch {status.Branch ?? "—"}"
            : "No check performed.";
    }

    private DiscordAgentProfile BuildDiscordProfile()
    {
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("Select a project before configuring the Discord agent.");
        }

        string webhookUrl = DiscordWebhookUrl.Trim();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            webhookUrl = _currentDiscordProfile?.WebhookUrl ?? string.Empty;
        }

        if (!int.TryParse(DiscordPollIntervalSeconds, out int pollIntervalSeconds))
        {
            throw new InvalidDataException("The polling interval must be a number of seconds.");
        }

        DiscordAgentProfile profile = new(
            SelectedProject.Id,
            webhookUrl,
            DiscordDisplayName.Trim(),
            string.IsNullOrWhiteSpace(DiscordProjectLabel) ? SelectedProject.Name : DiscordProjectLabel.Trim(),
            string.IsNullOrWhiteSpace(DiscordRepositoryWebUrl) ? null : DiscordRepositoryWebUrl.Trim(),
            pollIntervalSeconds,
            DiscordNotifyCommits,
            DiscordNotifyBranchChanges,
            DiscordStartAutomatically);
        profile.Validate();
        return profile;
    }

    private void ResetDiscordView()
    {
        _currentDiscordProfile = null;
        _currentDiscordControlConnection = null;
        SelectedDiscordExecutionMode = DiscordAgentExecutionMode.Integrated;
        DiscordAgentEndpoint = "http://127.0.0.1:47831";
        DiscordAgentApiToken = string.Empty;
        DiscordAgentRepositoryPath = string.Empty;
        DiscordAllowPrivateHttp = false;
        DiscordControlTokenConfigured = false;
        DiscordConnectionSummary = "Integrated agent — runs only while the desktop application is open.";
        DiscordWebhookUrl = string.Empty;
        DiscordDisplayName = "CyRevision";
        DiscordProjectLabel = string.Empty;
        DiscordRepositoryWebUrl = string.Empty;
        DiscordPollIntervalSeconds = "30";
        DiscordNotifyCommits = true;
        DiscordNotifyBranchChanges = true;
        DiscordStartAutomatically = false;
        DiscordWebhookConfigured = false;
        DiscordIsRunning = false;
        DiscordAgentState = "Discord agent not configured";
        DiscordAgentDetails = "Select a project and add a channel webhook.";
        DiscordLastActivity = "No check performed.";
    }

    private void OnDiscordAgentStatusChanged(object? sender, DiscordAgentStatus status)
    {
        void ApplyStatus()
        {
            DiscordIsRunning = status.State is not DiscordAgentRuntimeState.Stopped;
            DiscordAgentState = status.State switch
            {
                DiscordAgentRuntimeState.Starting => "Discord agent starting",
                DiscordAgentRuntimeState.Watching => "Discord agent watching",
                DiscordAgentRuntimeState.Sending => "Discord agent sending",
                DiscordAgentRuntimeState.Error => "Discord agent error — retry scheduled",
                _ => "Discord agent ready — stopped"
            };
            DiscordAgentDetails = status.Details;
            if (status.LastCheckedAt is { } checkedAt)
            {
                DiscordLastActivity = $"Last check: {checkedAt.ToLocalTime():g} · branch {status.Branch ?? "—"}";
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyStatus();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyStatus);
        }
    }

    private async Task LoadSwarmAndFileExchangeCoreAsync(VpnProjectProfile vpnProfile)
    {
        _currentSwarmProfile = await _swarmProfileStore.GetAsync(vpnProfile.ProjectId)
                               ?? SwarmProfileFactory.CreateDefault(vpnProfile);
        ApplySwarmProfile(_currentSwarmProfile);

        _currentVpnFileExchange = await _vpnFileExchangeProfileStore.GetOrCreateAsync(
            vpnProfile,
            Path.Combine(_applicationPaths.VpnDirectory, "transfers"));
        ApplyVpnFileExchangeProfile(_currentVpnFileExchange);
        VpnSetupAllowFileExchange = _currentVpnFileExchange.Profile.AllowReceive ||
                                    _currentVpnFileExchange.Profile.AllowBrowse ||
                                    _currentVpnFileExchange.Profile.AllowDownload;
        if (_currentVpnFileExchange.Profile.StartAutomatically)
        {
            try
            {
                _vpnFileExchangeHost = _vpnFileExchangeService.CreateHost(_currentVpnFileExchange, vpnProfile);
                _vpnFileExchangeHost.Start();
                VpnFileStatus = $"Listening on {_vpnFileExchangeHost.Endpoint} only · automatic project endpoint";
                OnPropertyChanged(nameof(VpnFileHostRunning));
            }
            catch (Exception exception) when (exception is SocketException or InvalidOperationException)
            {
                VpnFileStatus = "Automatic endpoint could not start: " + exception.Message;
            }
        }
    }

    private async Task<SwarmProjectProfile> SaveSwarmFormCoreAsync()
    {
        if (SelectedProject is null || _currentVpnProfile is null)
        {
            throw new InvalidOperationException("Configure the project VPN before configuring Unreal Swarm.");
        }

        string coordinatorAddress = SelectedSwarmRole == SwarmNodeRole.CoordinatorAndAgent
            ? _currentVpnProfile.LocalAddress
            : SwarmCoordinatorAddress.Trim();
        SwarmProjectProfile profile = new(
            SelectedProject.Id,
            SelectedSwarmRole,
            coordinatorAddress,
            SwarmCoordinatorAlias.Trim(),
            SwarmAgentPath.Trim(),
            SwarmCoordinatorPath.Trim(),
            SwarmOptionsPath.Trim(),
            SwarmAgentGroup.Trim(),
            SwarmAllowedGroup.Trim(),
            SwarmAllowedAgents.Trim(),
            SwarmCacheFolder.Trim(),
            DateTimeOffset.UtcNow);
        SwarmSetupService.ValidateProfile(profile);
        (uint network, int prefix) = VpnProfileValidator.ParseCidr(_currentVpnProfile.NetworkCidr);
        uint mask = uint.MaxValue << (32 - prefix);
        uint coordinator = VpnProfileValidator.ToUInt32(System.Net.IPAddress.Parse(profile.CoordinatorAddress));
        if ((coordinator & mask) != network)
        {
            throw new InvalidDataException("The Swarm Coordinator address must be inside the project VPN subnet.");
        }

        VpnNodeCapabilities capabilities = _currentVpnProfile.LocalCapabilities &
                                           ~(VpnNodeCapabilities.SwarmAgent | VpnNodeCapabilities.SwarmCoordinator);
        capabilities |= VpnNodeCapabilities.SwarmAgent;
        if (profile.Role == SwarmNodeRole.CoordinatorAndAgent)
        {
            capabilities |= VpnNodeCapabilities.SwarmCoordinator;
        }
        _currentVpnProfile = _currentVpnProfile with
        {
            LocalCapabilities = capabilities,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _vpnProfileStore.SaveAsync(_currentVpnProfile);
        await _swarmProfileStore.SaveAsync(profile);
        _currentSwarmProfile = profile;
        ApplySwarmProfile(profile);
        ApplyVpnProfile(_currentVpnProfile);
        VpnSetupAllowSwarm = true;
        return profile;
    }

    private async Task<VpnFileExchangeCredentials> SaveVpnFileExchangeFormCoreAsync()
    {
        if (SelectedProject is null || _currentVpnProfile is null)
        {
            throw new InvalidOperationException("Configure the project VPN before configuring file exchange.");
        }
        if (!int.TryParse(VpnFileListenPort, out int port))
        {
            throw new InvalidDataException("The VPN file-exchange port is invalid.");
        }
        VpnFileExchangeProfile profile = new(
            SelectedProject.Id,
            _currentVpnProfile.LocalAddress,
            port,
            VpnFileInboxPath.Trim(),
            VpnFileSharedFolderPath.Trim(),
            VpnFileAllowReceive,
            VpnFileAllowBrowse,
            VpnFileAllowDownload,
            VpnFileExchangeDefaults.MaxFileBytes,
            VpnFileStartAutomatically,
            DateTimeOffset.UtcNow);
        VpnFileExchangeCredentials credentials = new(profile, VpnFileAccessToken.Trim());
        VpnFileExchangeService.ValidateProfile(profile, credentials.AccessToken);
        await _vpnFileExchangeProfileStore.SaveAsync(credentials);
        _currentVpnFileExchange = credentials;
        ApplyVpnFileExchangeProfile(credentials);
        VpnSetupAllowFileExchange = profile.AllowReceive || profile.AllowBrowse || profile.AllowDownload;
        return credentials;
    }

    private void ApplySwarmProfile(SwarmProjectProfile profile)
    {
        SelectedSwarmRole = profile.Role;
        SwarmCoordinatorAddress = profile.CoordinatorAddress;
        SwarmCoordinatorAlias = profile.CoordinatorAlias;
        SwarmAgentPath = profile.SwarmAgentPath;
        SwarmCoordinatorPath = profile.SwarmCoordinatorPath;
        SwarmOptionsPath = profile.OptionsPath;
        SwarmAgentGroup = profile.AgentGroupName;
        SwarmAllowedGroup = profile.AllowedRemoteAgentGroup;
        SwarmAllowedAgents = profile.AllowedRemoteAgentNames;
        SwarmCacheFolder = profile.CacheFolder;
        SwarmStatus = $"{profile.Role} · coordinator {profile.CoordinatorAddress} · not tested";
    }

    private void ApplyVpnFileExchangeProfile(VpnFileExchangeCredentials credentials)
    {
        VpnFileListenPort = credentials.Profile.Port.ToString();
        VpnFileInboxPath = credentials.Profile.InboxPath;
        VpnFileSharedFolderPath = credentials.Profile.SharedFolderPath;
        VpnFileAccessToken = credentials.AccessToken;
        VpnFileAllowReceive = credentials.Profile.AllowReceive;
        VpnFileAllowBrowse = credentials.Profile.AllowBrowse;
        VpnFileAllowDownload = credentials.Profile.AllowDownload;
        VpnFileStartAutomatically = credentials.Profile.StartAutomatically;
        VpnFileStatus = $"Stopped · ready on {credentials.Profile.ListenAddress}:{credentials.Profile.Port} · project token stored locally";
    }

    private async Task<VpnProjectProfile> SaveVpnFormCoreAsync()
    {
        if (SelectedProject is null || _currentVpnProfile is null)
        {
            throw new InvalidOperationException("Configurez d'abord WireGuard pour ce projet.");
        }

        if (!int.TryParse(VpnListenPort, out int listenPort))
        {
            throw new InvalidDataException("Le port WireGuard n'est pas un nombre valide.");
        }

        VpnProjectProfile profile = _currentVpnProfile with
        {
            NetworkCidr = VpnNetworkCidr.Trim(),
            LocalAddress = VpnLocalAddress.Trim(),
            ListenPort = listenPort,
            PublicEndpoint = string.IsNullOrWhiteSpace(VpnPublicEndpoint) ? null : VpnPublicEndpoint.Trim(),
            LocalCapabilities = SelectedVpnCapability,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        VpnProfileValidator.Validate(profile);
        await _vpnProfileStore.SaveAsync(profile);
        _currentVpnProfile = profile;
        ApplyVpnProfile(profile);
        VpnConfigurationPreview = await _wireGuardConfiguration.RenderAsync(profile);
        return profile;
    }

    private async Task RefreshVpnStatusCoreAsync()
    {
        if (_currentVpnProfile is null)
        {
            return;
        }

        VpnConfigurationPreview = await _wireGuardConfiguration.RenderAsync(_currentVpnProfile);
        ApplyVpnStatus(await _wireGuardEngine.GetStatusAsync(_currentVpnProfile));
    }

    private void ApplyVpnProfile(VpnProjectProfile profile)
    {
        SelectedVpnBackendMode = profile.BackendMode;
        WireGuardExecutablePath = profile.WireGuardExecutablePath ?? profile.WgExecutablePath ?? string.Empty;
        VpnNetworkCidr = profile.NetworkCidr;
        VpnLocalAddress = profile.LocalAddress;
        VpnPublicEndpoint = profile.PublicEndpoint ?? string.Empty;
        VpnListenPort = profile.ListenPort.ToString();
        SelectedVpnCapability = profile.LocalCapabilities;
        ReplaceCollection(VpnPeers, profile.Peers.Select(peer => new VpnPeerViewModel(peer)));
        SelectedVpnPeer = VpnPeers.FirstOrDefault();
        SelectedVpnFilePeer = VpnPeers.FirstOrDefault(peer =>
                                  peer.Peer.Capabilities.HasFlag(VpnNodeCapabilities.ServiceHost))
                              ?? VpnPeers.FirstOrDefault();
    }

    private void ApplyVpnStatus(VpnEngineStatus status)
    {
        VpnState = status.State switch
        {
            VpnRuntimeState.Running => "VPN actif",
            VpnRuntimeState.Stopped => "VPN prêt — arrêté",
            VpnRuntimeState.Collision => "Conflit d'interface — aucune action",
            VpnRuntimeState.Unavailable => "WireGuard indisponible",
            VpnRuntimeState.Faulted => "Erreur VPN",
            _ => "VPN non configuré"
        };
        string swarm = _currentVpnProfile?.LocalCapabilities.HasFlag(VpnNodeCapabilities.SwarmCoordinator) == true
            ? " Coordinateur Swarm sur cette adresse VPN, ports 8008/8009."
            : _currentVpnProfile?.Peers.FirstOrDefault(peer => peer.Capabilities.HasFlag(VpnNodeCapabilities.SwarmCoordinator)) is { } coordinator
                ? $" Coordinateur Swarm : {coordinator.TunnelAddress}, ports 8008/8009."
                : string.Empty;
        VpnDetails = status.Message + swarm;
    }

    private async Task<FileDeviceIdentityStore> OpenVpnIdentityAsync(VpnProjectProfile profile) =>
        await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_applicationPaths.VpnDirectory, "security", profile.ProjectId.ToString("N"), "local-device"),
            Environment.MachineName,
            "vpn:" + profile.PublicKey[..Math.Min(12, profile.PublicKey.Length)]);

    private void ClearVpnView()
    {
        SelectedVpnBackendMode = VpnBackendMode.SystemInstallation;
        VpnState = "VPN non configuré";
        VpnDetails = "WireGuard reste indépendant de Git et de Sync.";
        WireGuardExecutablePath = string.Empty;
        VpnNetworkCidr = string.Empty;
        VpnLocalAddress = string.Empty;
        VpnPublicEndpoint = string.Empty;
        VpnListenPort = "51820";
        VpnExchangeText = string.Empty;
        VpnConfigurationPreview = "Configurez WireGuard pour afficher le tunnel CyRevision.";
        VpnPeers.Clear();
        SelectedVpnPeer = null;
        _currentVpnSetupPlan = null;
        VpnSetupAcceptIncoming = false;
        VpnSetupAllowSwarm = false;
        VpnSetupAllowControlApi = false;
        VpnSetupAllowFileExchange = false;
        VpnSetupAllowRemoteBuild = false;
        VpnCanApplyFirewall = false;
        VpnCanOpenRouter = false;
        VpnSetupSummary = "Run the guided setup to inspect this computer.";
        VpnSetupNetwork = "Local network not inspected.";
        VpnFirewallStatus = "Firewall not inspected.";
        VpnComputerGuide = string.Empty;
        VpnRouterGuide = string.Empty;
        VpnFirewallCommands = string.Empty;
        VpnConnectivityStatus = "Tunnel connectivity not tested.";
        VpnSyncMessages.Clear();
        SelectedVpnSyncMessage = null;
        VpnSyncStatus = "Sync exchange not inspected.";
        _currentSwarmProfile = null;
        SelectedSwarmRole = SwarmNodeRole.Agent;
        SwarmCoordinatorAddress = string.Empty;
        SwarmCoordinatorAlias = string.Empty;
        SwarmAgentPath = string.Empty;
        SwarmCoordinatorPath = string.Empty;
        SwarmOptionsPath = string.Empty;
        SwarmAgentGroup = "Default";
        SwarmAllowedGroup = "DefaultDeployed";
        SwarmAllowedAgents = "*";
        SwarmCacheFolder = string.Empty;
        SwarmStatus = "Configure the project VPN before creating a Swarm session.";
        SwarmDiagnostic = "No Swarm connection or configuration test has run.";
        _currentVpnFileExchange = null;
        VpnFileListenPort = VpnFileExchangeDefaults.Port.ToString();
        VpnFileInboxPath = string.Empty;
        VpnFileSharedFolderPath = string.Empty;
        VpnFileAccessToken = string.Empty;
        VpnFileAllowReceive = true;
        VpnFileAllowBrowse = true;
        VpnFileAllowDownload = true;
        VpnFileStartAutomatically = false;
        SelectedVpnFilePeer = null;
        VpnSharedFiles.Clear();
        SelectedVpnSharedFile = null;
        VpnFileStatus = "VPN file exchange is not configured.";
    }

    private VpnSetupOptions CreateVpnSetupOptions()
    {
        VpnSetupFeatures features = VpnSetupFeatures.None;
        if (VpnSetupAcceptIncoming)
        {
            features |= VpnSetupFeatures.AcceptIncomingTunnel;
        }

        if (VpnSetupAllowSwarm)
        {
            features |= VpnSetupFeatures.UnrealSwarm;
        }

        if (VpnSetupAllowControlApi)
        {
            features |= VpnSetupFeatures.CyRevisionControlApi;
        }

        if (VpnSetupAllowFileExchange)
        {
            features |= VpnSetupFeatures.SecureFileExchange;
        }

        if (VpnSetupAllowRemoteBuild)
        {
            features |= VpnSetupFeatures.RemoteBuildAgent;
        }

        int filePort = int.TryParse(VpnFileListenPort, out int parsed)
            ? parsed
            : VpnFileExchangeDefaults.Port;
        int buildPort = Uri.TryCreate(RemoteBuildEndpoint, UriKind.Absolute, out Uri? buildEndpoint) && buildEndpoint.Port > 0
            ? buildEndpoint.Port
            : VpnNetworkSetupService.RemoteBuildPort;
        return new VpnSetupOptions(features)
        {
            FileExchangePort = filePort,
            RemoteBuildPort = buildPort
        };
    }

    private void InvalidateVpnSetupPlan()
    {
        _currentVpnSetupPlan = null;
        VpnCanApplyFirewall = false;
        VpnCanOpenRouter = false;
        VpnSetupSummary = "Options changed — run the diagnosis again before applying anything.";
    }

    private void ApplyVpnSetupPlan(VpnSetupPlan plan)
    {
        string local = plan.Network.LocalIpv4Address ?? "not detected";
        string gateway = plan.Network.DefaultGateway ?? "not detected";
        VpnSetupNetwork = $"{plan.Platform} · {plan.Network.InterfaceName ?? "no active interface"} · " +
                          $"LAN {local} · gateway {gateway}";
        VpnFirewallStatus = plan.Rules.Count == 0
            ? "Client-only mode — no inbound firewall rule required."
            : plan.RulesAlreadyApplied switch
            {
                true => $"{plan.FirewallTool} · all generated rules are present",
                false => $"{plan.FirewallTool} · generated rules are not applied yet",
                null => $"{plan.FirewallTool} · verify the generated rules after applying them"
            };
        VpnCanApplyFirewall = plan.CanApplyAutomatically && plan.RemoveCommands.Count > 0;
        VpnCanOpenRouter = plan.RouterAdminUri is not null && plan.RequiresRouterPortForward;
        VpnComputerGuide = FormatNumberedSteps(plan.ComputerSteps.Select(_localization.Translate));
        VpnRouterGuide = FormatNumberedSteps(plan.RouterSteps.Concat(plan.Warnings).Select(_localization.Translate));
        VpnFirewallCommands = plan.ApplyCommands.Count == 0
            ? "No inbound rule will be created. Apply removes any previous CyRevision rules for this project."
            : string.Join(Environment.NewLine, plan.ApplyCommands.Select(command => command.Preview));
        VpnSetupSummary = plan.Options.AcceptIncomingTunnel
            ? "Host mode: configure this computer, then create one UDP forward on the router."
            : "Client-only mode: no router change is required; import a signed invitation to continue.";
    }

    private SyncthingProfile RequireVpnSyncProfile() =>
        _currentSyncProfile
        ?? throw new InvalidOperationException(
            "Configure the isolated CyRevision Sync profile before exchanging VPN messages through Sync.");

    private async Task RefreshVpnSyncMessagesCoreAsync()
    {
        if (_currentSyncProfile is null)
        {
            VpnSyncMessages.Clear();
            SelectedVpnSyncMessage = null;
            VpnSyncStatus = "Configure Sync to exchange signed VPN invitations automatically.";
            return;
        }

        IReadOnlyList<VpnSyncMessage> messages = await _vpnSyncExchangeService.ListAsync(
            _currentSyncProfile.ExchangeDirectory);
        Guid? selectedId = SelectedVpnSyncMessage?.Message.Envelope.MessageId;
        ReplaceCollection(VpnSyncMessages, messages.Select(message => new VpnSyncMessageViewModel(message)));
        SelectedVpnSyncMessage = VpnSyncMessages.FirstOrDefault(item =>
                                     item.Message.Envelope.MessageId == selectedId)
                                 ?? VpnSyncMessages.FirstOrDefault();
        VpnSyncStatus = messages.Count == 0
            ? "No signed VPN message is currently available in the Sync exchange."
            : $"{messages.Count} signed VPN message(s) available. Loading one never applies it automatically.";
    }

    private static string FormatNumberedSteps(IEnumerable<string> steps) => string.Join(
        Environment.NewLine,
        steps.Select((step, index) => $"{index + 1}. {step}"));

    private void UpdateVpnBackendDetails()
    {
        WireGuardInstallation installation = _wireGuardRuntimeResolver.Detect(SelectedVpnBackendMode);
        if (SelectedVpnBackendMode == VpnBackendMode.IntegratedRuntime)
        {
            bool ready = installation.CanGenerateKeys && installation.CanManageTunnel &&
                         (OperatingSystem.IsWindows() || !string.IsNullOrWhiteSpace(installation.UserspaceExecutablePath));
            VpnBackendDetails = ready
                ? $"Integrated runtime ready in {installation.RuntimeDirectory}. {installation.ValidationMessage}"
                : $"Integrated runtime package required in {installation.RuntimeDirectory}. {installation.ValidationMessage}";
            return;
        }

        VpnBackendDetails = installation.CanGenerateKeys && installation.CanManageTunnel
            ? "System WireGuard installation detected."
            : "System WireGuard installation not detected.";
    }

    private async Task ConfigureCurrentSyncFolderAsync()
    {
        if (SelectedProject is null || _currentSyncProfile is null || _syncEngine is null)
        {
            return;
        }

        HashSet<string> deviceIds = new(StringComparer.Ordinal);
        string securityPath = GetProjectSecurityPath(SelectedProject.Id);
        string grantPath = Path.Combine(securityPath, "membership-grant.json");
        if (File.Exists(grantPath))
        {
            PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(await File.ReadAllTextAsync(grantPath));
            if (PeerExchangeCodec.VerifyGrant(grant))
            {
                deviceIds.Add(grant.IssuerIdentity.SyncthingDeviceId);
            }
        }

        using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(
            SelectedProject.Definition,
            _syncEngine.DeviceId);
        JsonPeerAdmissionService admission = CreateAdmissionService(SelectedProject.Id, identity);
        IReadOnlyList<MembershipCertificate> members = await admission.GetMembersAsync(SelectedProject.Id);
        foreach (MembershipCertificate member in members.Where(admission.VerifyCertificate))
        {
            deviceIds.Add(member.Device.SyncthingDeviceId);
        }

        using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
        await api.PutFolderAsync(CreateFolderConfiguration(_currentSyncProfile, deviceIds));
    }

    private async Task LoadPeerMembersCoreAsync()
    {
        if (SelectedProject is null)
        {
            PeerMembers.Clear();
            SelectedPeerMember = null;
            return;
        }

        string identityDirectory = Path.Combine(GetProjectSecurityPath(SelectedProject.Id), "local-device");
        if (!File.Exists(Path.Combine(identityDirectory, "device-identity.json")) ||
            !File.Exists(Path.Combine(identityDirectory, "device-signing-key.pk8")))
        {
            PeerMembers.Clear();
            SelectedPeerMember = null;
            return;
        }

        using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(
            SelectedProject.Definition,
            _syncEngine?.DeviceId ?? "offline");
        JsonPeerAdmissionService admission = CreateAdmissionService(SelectedProject.Id, identity);
        IReadOnlyList<MembershipCertificate> members = await admission.GetMembersAsync(SelectedProject.Id);
        ReplaceCollection(
            PeerMembers,
            members.Where(admission.VerifyCertificate)
                .OrderBy(member => member.Device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(member => new PeerMemberViewModel(member)));
        SelectedPeerMember = PeerMembers.FirstOrDefault();
    }

    private async Task ExchangeGitCoreAsync()
    {
        if (SelectedProject is null ||
            !SelectedProject.Definition.Features.GitEnabled ||
            _currentSyncProfile is null ||
            _syncEngine is null ||
            _syncEngine.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused))
        {
            return;
        }

        using FileDeviceIdentityStore localIdentity = await OpenLocalDeviceIdentityAsync(
            SelectedProject.Definition,
            _syncEngine.DeviceId);
        IReadOnlyCollection<DeviceIdentity> authorizedDevices = await GetAuthorizedExchangeDevicesAsync(
            SelectedProject.Definition,
            _currentSyncProfile,
            localIdentity);
        GitPeerExchangeOptions options = BuildGitPeerExchangeOptions();
        GitPeerExportResult exported = await _gitPeerExchangeService.ExportDetailedAsync(
            SelectedProject.Id,
            SelectedProject.RootPath,
            _currentSyncProfile.ExchangeDirectory,
            localIdentity,
            authorizedDevices,
            options);
        GitPeerExchangeResult imported = await _gitPeerExchangeService.ImportDetailedAsync(
            SelectedProject.Id,
            SelectedProject.RootPath,
            _currentSyncProfile.ExchangeDirectory,
            GetGitExchangeStatePath(SelectedProject.Id),
            authorizedDevices,
            localIdentity.Identity.DeviceId,
            options);
        SyncDetails = $"Transaction {(exported.TransactionId is null ? "non créée" : exported.TransactionId.Value.ToString("N")[..8])} · " +
                      $"{imported.ImportedTransactions} transaction(s) reçue(s) · " +
                      $"{imported.ImportedLfsObjects} objet(s) LFS importé(s)";
        PeerLfsTransferSummary = $"{exported.PublishedLfsObjects} publié(s) · " +
                                 $"{imported.ImportedLfsObjects} importé(s) · " +
                                 $"{exported.ResumedLfsObjects + imported.ResumedLfsObjects} repris · " +
                                 $"{exported.DeferredLfsObjects + imported.DeferredLfsObjects} différé(s) · " +
                                 $"{imported.AvailablePeerLfsObjects} disponible(s) chez les pairs";
    }

    private GitPeerExchangeOptions BuildGitPeerExchangeOptions()
    {
        int recentCount = int.TryParse(SmartSyncRecentVersionCount, out int parsed)
            ? Math.Clamp(parsed, 1, 100)
            : 3;
        PeerLfsTransferMode mode = SelectedLfsHistoryMode switch
        {
            LfsHistoryTransferMode.Everything => PeerLfsTransferMode.AllAvailable,
            LfsHistoryTransferMode.RecentVersions => PeerLfsTransferMode.CurrentAndRecent,
            _ => PeerLfsTransferMode.CurrentRevisionOnly
        };
        return new GitPeerExchangeOptions(mode, recentCount, 10L * 1024 * 1024 * 1024);
    }

    private async Task<IReadOnlyCollection<DeviceIdentity>> GetAuthorizedExchangeDevicesAsync(
        ProjectDefinition project,
        SyncthingProfile profile,
        IDeviceIdentityStore localIdentity)
    {
        Dictionary<Guid, DeviceIdentity> devices = new()
        {
            [localIdentity.Identity.DeviceId] = localIdentity.Identity
        };
        JsonPeerAdmissionService admission = CreateAdmissionService(project.Id, localIdentity);
        foreach (MembershipCertificate member in (await admission.GetMembersAsync(project.Id))
                     .Where(member => admission.VerifyCertificate(member) && CanWriteGit(member.Role)))
        {
            devices[member.Device.DeviceId] = member.Device;
        }

        string localGrantPath = Path.Combine(GetProjectSecurityPath(project.Id), "membership-grant.json");
        if (File.Exists(localGrantPath))
        {
            PeerMembershipGrant localGrant = PeerExchangeCodec.ImportMembershipGrant(await File.ReadAllTextAsync(localGrantPath));
            if (PeerExchangeCodec.VerifyGrant(localGrant))
            {
                devices[localGrant.IssuerIdentity.DeviceId] = localGrant.IssuerIdentity;
            }
        }

        string sharedMembersPath = Path.Combine(profile.ExchangeDirectory, "members");
        if (Directory.Exists(sharedMembersPath))
        {
            foreach (string grantPath in Directory.EnumerateFiles(sharedMembersPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(await File.ReadAllTextAsync(grantPath));
                    bool correctProject = grant.Certificate.ProjectId == project.Id;
                    bool knownIssuer = devices.TryGetValue(grant.IssuerIdentity.DeviceId, out DeviceIdentity? issuer) &&
                                       string.Equals(issuer.SigningPublicKey, grant.IssuerIdentity.SigningPublicKey, StringComparison.Ordinal);
                    if (correctProject && knownIssuer && CanWriteGit(grant.Certificate.Role) && PeerExchangeCodec.VerifyGrant(grant))
                    {
                        devices[grant.Certificate.Device.DeviceId] = grant.Certificate.Device;
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
                {
                    // Incomplete files are ignored until Syncthing finishes transferring them.
                }
            }
        }

        return devices.Values.ToArray();
    }

    private static bool CanWriteGit(PeerRole role) =>
        role is PeerRole.Owner or PeerRole.Administrator or PeerRole.Contributor;

    private SyncthingFolderConfiguration CreateFolderConfiguration(
        SyncthingProfile profile,
        IReadOnlyCollection<string> deviceIds)
    {
        ProjectDefinition definition = SelectedProject?.Definition
            ?? throw new InvalidOperationException("Aucun projet sélectionné.");
        string versioningType = definition.Features.BackupEnabled ? "simple" : string.Empty;
        int? keepVersions = definition.Features.BackupEnabled
            ? definition.Retention.MaxVersionsPerFile ?? 30
            : null;
        int? cleanoutDays = definition.Features.BackupEnabled && definition.Retention.MaximumAge is { } maximumAge
            ? Math.Max(1, (int)Math.Round(maximumAge.TotalDays))
            : null;
        string folderType = "sendreceive";
        string grantPath = Path.Combine(GetProjectSecurityPath(definition.Id), "membership-grant.json");
        if (File.Exists(grantPath))
        {
            PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(File.ReadAllText(grantPath));
            if (grant.Certificate.Role is PeerRole.ReadOnly or PeerRole.Backup or PeerRole.EncryptedArchive)
            {
                folderType = "receiveonly";
            }
        }

        return new SyncthingFolderConfiguration(
            profile.FolderId,
            definition.Name,
            profile.ExchangeDirectory,
            deviceIds.ToArray(),
            folderType,
            versioningType,
            keepVersions,
            cleanoutDays);
    }

    private async Task StopSyncCoreAsync()
    {
        ManagedSyncthingEngine? engine = _syncEngine;
        _syncEngine = null;
        _syncEngineProjectId = null;
        if (engine is not null)
        {
            await engine.DisposeAsync();
        }

        SyncState = SelectedProject?.Definition.Features.PeerSyncEnabled == true
            ? "Sync prêt — arrêté"
            : "Sync désactivé";
        SyncDetails = "Seule l'instance possédée par CyRevision a été arrêtée.";
        PeerMembers.Clear();
        SelectedPeerMember = null;
    }

    private bool TryGetRunningSyncContext(
        out ProjectDefinition? project,
        out SyncthingProfile? profile,
        out ManagedSyncthingEngine? engine)
    {
        project = SelectedProject?.Definition;
        profile = _currentSyncProfile;
        engine = _syncEngine;
        if (project is null || profile is null || engine is null ||
            engine.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused))
        {
            StatusMessage = "Démarrez d'abord l'instance Sync CyRevision de ce projet";
            return false;
        }

        return true;
    }

    private async Task<FileDeviceIdentityStore> OpenLocalDeviceIdentityAsync(
        ProjectDefinition project,
        string syncthingDeviceId) =>
        await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(GetProjectSecurityPath(project.Id), "local-device"),
            Environment.MachineName,
            syncthingDeviceId);

    private JsonPeerAdmissionService CreateAdmissionService(Guid projectId, IDeviceIdentityStore identity) =>
        new(Path.Combine(GetProjectSecurityPath(projectId), "admission"), identity);

    private string GetProjectSecurityPath(Guid projectId) =>
        Path.Combine(_applicationPaths.DataDirectory, "security", "projects", projectId.ToString("N"));

    private string ResolveSyncExchangeDirectory(ProjectDefinition definition) =>
        definition.Features.GitEnabled
            ? Path.Combine(_applicationPaths.DataDirectory, "git-exchange", definition.Id.ToString("N"))
            : definition.RootPath;

    private string GetGitExchangeStatePath(Guid projectId) =>
        Path.Combine(_applicationPaths.DataDirectory, "git-exchange-state", projectId.ToString("N"));

    private static LfsObjectLocation? GetColdArchiveLocation(ProjectDefinition project, string oidSha256)
    {
        if (string.IsNullOrWhiteSpace(project.ColdArchivePath) || oidSha256.Length < 2)
        {
            return null;
        }

        string archiveRoot = Path.GetFullPath(project.ColdArchivePath);
        string objectPath = Path.Combine(archiveRoot, "objects", oidSha256[..2], oidSha256.Substring(2, 2), oidSha256);
        if (!File.Exists(objectPath))
        {
            objectPath = Path.Combine(archiveRoot, "objects", oidSha256[..2], oidSha256);
        }
        if (!File.Exists(objectPath))
        {
            return null;
        }

        string displayName = new DirectoryInfo(archiveRoot).Name;
        return new LfsObjectLocation(
            LfsStorageKind.Archive,
            string.IsNullOrWhiteSpace(displayName) ? archiveRoot : displayName,
            new DateTimeOffset(File.GetLastWriteTimeUtc(objectPath), TimeSpan.Zero),
            true);
    }

    private string ResolveAdvisoryPresenceDirectory(ProjectDefinition definition) =>
        definition.Features.GitEnabled
            ? Path.Combine(ResolveSyncExchangeDirectory(definition), "presence")
            : Path.Combine(definition.RootPath, ".cyrevision", "presence");

    private void UpdateSyncStatus(SyncEngineStatus status)
    {
        SyncState = status.State switch
        {
            SyncEngineState.Running => "Sync actif",
            SyncEngineState.Paused => "Sync en pause",
            SyncEngineState.Faulted => "Erreur Sync",
            SyncEngineState.Starting => "Démarrage Sync",
            SyncEngineState.Disabled => "Sync désactivé",
            _ => "Sync arrêté"
        };
        SyncDetails = $"{status.ConnectedPeers} pair(s) connecté(s) · {FormatByteSize(status.PendingBytes)} en attente" +
                      (string.IsNullOrWhiteSpace(status.Message) ? string.Empty : $" · {status.Message}");
    }

    private LfsManagementProfile BuildLfsManagementProfile()
    {
        if (SelectedProject is null)
            throw new InvalidOperationException("Select a project first.");
        int copies = int.TryParse(LfsRequiredCopies, out int parsedCopies) ? parsedCopies : 1;
        int grace = int.TryParse(LfsCleanupGraceDays, out int parsedGrace) ? parsedGrace : 7;
        int peerAge = int.TryParse(LfsPeerProofMaximumAgeHours, out int parsedPeerAge) ? parsedPeerAge : 24;
        LfsManagementProfile profile = new(
            SelectedProject.Id,
            string.IsNullOrWhiteSpace(LfsExternalStoragePath) ? string.Empty : Path.GetFullPath(LfsExternalStoragePath),
            string.IsNullOrWhiteSpace(LfsManagementArchivePath) ? string.Empty : Path.GetFullPath(LfsManagementArchivePath),
            string.IsNullOrWhiteSpace(LfsCleanupRemoteName) ? "origin" : LfsCleanupRemoteName.Trim(),
            copies,
            grace,
            peerAge,
            LfsVerifyRemote);
        profile.Validate();
        return profile;
    }

    private RemoteBuildCredentials BuildRemoteBuildCredentials()
    {
        if (SelectedProject is null)
            throw new InvalidOperationException("Select a project first.");
        if (!long.TryParse(RemoteBuildMaximumUploadGb, out long maximumGb) || maximumGb is < 1 or > 2048)
            throw new InvalidOperationException("Remote build upload limit must be between 1 and 2048 GB.");
        if (string.IsNullOrWhiteSpace(RemoteBuildAccessToken))
            throw new InvalidOperationException("Paste the build agent token through a trusted channel.");
        RemoteBuildConnectionProfile profile = new(
            SelectedProject.Id,
            RemoteBuildEndpoint.Trim(),
            RemoteBuildRecipeId.Trim(),
            SelectedRemoteBuildSourceMode,
            Path.GetFullPath(RemoteBuildArtifactDestination),
            checked(maximumGb * 1024L * 1024 * 1024),
            RemoteBuildAllowPrivateHttp);
        _ = CyRevision.RemoteBuild.RemoteBuildEndpoint.Create(profile.Endpoint, profile.AllowPrivateHttp);
        return new RemoteBuildCredentials(profile, RemoteBuildAccessToken.Trim());
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "project" : result.Trim();
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private string GetDiffArtifactDirectory() =>
        Path.Combine(
            _applicationPaths.CacheDirectory,
            "diffs",
            SelectedProject?.Id.ToString("N") ?? "external");

    private void ApplyAssetDiffResult(AssetDiffResult result)
    {
        List<string> report = [
            result.Summary,
            string.Empty,
            .. result.Metrics.Select(metric => $"{metric.Key} : {metric.Value}")
        ];
        if (result.Details.Count > 0)
        {
            report.Add(string.Empty);
            report.AddRange(result.Details.Take(200));
        }

        AssetDiffReport = string.Join(Environment.NewLine, report);
        AssetDiffPreview = result.PreviewImagePath is not null && File.Exists(result.PreviewImagePath)
            ? new Bitmap(result.PreviewImagePath)
            : null;
    }

    private async Task LoadBackupsCoreAsync()
    {
        if (SelectedProject is null ||
            (!SelectedProject.Definition.Features.BackupEnabled &&
             string.IsNullOrWhiteSpace(SelectedProject.Definition.BackupStorePath)))
        {
            Backups.Clear();
            SelectedBackup = null;
            return;
        }

        IReadOnlyList<BackupSnapshot> snapshots = await GetBackupService(SelectedProject.Definition)
            .GetSnapshotsAsync(SelectedProject.Id);
        ReplaceCollection(Backups, snapshots.Select(snapshot => new BackupSnapshotViewModel(snapshot)));
        SelectedBackup = Backups.FirstOrDefault();
        UpdateSmartSyncPlan();
    }

    private async Task LoadAdvisoryReservationsCoreAsync()
    {
        if (SelectedProject is null)
        {
            AdvisoryReservations.Clear();
            ReservationSummary = "Aucun projet sélectionné.";
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<AdvisoryReservation> reservations = await GetAdvisoryReservationStore(
                SelectedProject.Definition)
            .GetAllAsync(includeExpired: true);
        ReplaceCollection(
            AdvisoryReservations,
            reservations.Select(reservation => new AdvisoryReservationViewModel(reservation, now)));
        int activeCount = AdvisoryReservations.Count(reservation => !reservation.IsExpired);
        int expiredCount = AdvisoryReservations.Count - activeCount;
        ReservationSummary = activeCount == 0
            ? expiredCount == 0
                ? "Aucune réservation souple : tous les assets restent libres."
                : $"Aucune réservation active · {expiredCount} expirée(s) à nettoyer."
            : $"{activeCount} asset(s) signalé(s) en cours · {expiredCount} expirée(s) · aucun verrou bloquant.";
    }

    private IAdvisoryReservationStore GetAdvisoryReservationStore(ProjectDefinition definition) =>
        new JsonAdvisoryReservationStore(definition.Id, ResolveAdvisoryPresenceDirectory(definition));

    private async Task<ProjectDefinition> EnsureBackupConfiguredAsync()
    {
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("Aucun projet sélectionné.");
        }

        ProjectDefinition definition = SelectedProject.Definition;
        if (definition.Features.BackupEnabled && !string.IsNullOrWhiteSpace(definition.BackupStorePath))
        {
            return definition;
        }

        RetentionPolicy retention = BuildRetentionPolicy();
        definition = definition with
        {
            Features = definition.Features with { BackupEnabled = true },
            Retention = retention,
            BackupStorePath = ResolveBackupStorePath(definition)
        };
        await _projectCatalog.UpsertAsync(definition);
        SelectedProject.Update(definition);
        BackupStorePath = definition.BackupStorePath!;
        return definition;
    }

    private IBackupService GetBackupService(ProjectDefinition definition) =>
        new FileSystemBackupService(new BackupStoreOptions(ResolveBackupStorePath(definition)));

    private string ResolveBackupStorePath(ProjectDefinition definition) =>
        string.IsNullOrWhiteSpace(BackupStorePath)
            ? definition.BackupStorePath ?? Path.Combine(_applicationPaths.BackupDirectory, definition.Id.ToString("N"))
            : BackupStorePath;

    private RetentionPolicy BuildRetentionPolicy()
    {
        int? versions = int.TryParse(RetentionVersions, out int parsedVersions) && parsedVersions > 0
            ? parsedVersions
            : null;
        TimeSpan? maximumAge = int.TryParse(RetentionDays, out int parsedDays) && parsedDays > 0
            ? TimeSpan.FromDays(parsedDays)
            : null;
        long? budget = double.TryParse(RetentionBudgetGb, out double parsedBudget) && parsedBudget > 0
            ? checked((long)(parsedBudget * 1024 * 1024 * 1024))
            : null;

        return SelectedRetentionMode switch
        {
            RetentionMode.CurrentStateOnly => RetentionPolicy.CurrentStateOnly with { StorageBudgetBytes = budget },
            RetentionMode.Permanent => RetentionPolicy.KeepForever with { StorageBudgetBytes = budget },
            RetentionMode.LimitedVersions => new RetentionPolicy(SelectedRetentionMode, versions ?? 30, maximumAge, budget),
            _ => new RetentionPolicy(SelectedRetentionMode, versions, maximumAge, budget)
        };
    }

    private void LoadBackupSettings(ProjectDefinition definition)
    {
        BackupStorePath = definition.BackupStorePath ?? string.Empty;
        ColdArchivePath = definition.ColdArchivePath ?? string.Empty;
        ColdArchiveAfterDays = definition.ColdArchiveAfterDays?.ToString() ?? "180";
        ColdArchiveStatus = string.IsNullOrWhiteSpace(definition.ColdArchivePath)
            ? "Archive froide facultative — aucune suppression automatique."
            : "Archive froide configurée — les snapshots actifs ne sont jamais supprimés par l'archivage.";
        SelectedRetentionMode = definition.Retention.Mode;
        RetentionVersions = definition.Retention.MaxVersionsPerFile?.ToString() ?? "30";
        RetentionDays = definition.Retention.MaximumAge is { } age
            ? Math.Max(1, (int)Math.Round(age.TotalDays)).ToString()
            : "90";
        RetentionBudgetGb = definition.Retention.StorageBudgetBytes is { } budget
            ? (budget / 1024d / 1024d / 1024d).ToString("0.##")
            : string.Empty;
    }

    private async Task SaveAndSelectProjectAsync(ProjectDefinition definition)
    {
        await _projectCatalog.UpsertAsync(definition);
        ProjectItemViewModel? existing = Projects.FirstOrDefault(project => project.Id == definition.Id) ??
                                         Projects.FirstOrDefault(project =>
                                             ProjectPathsEqual(project.RootPath, definition.RootPath));
        if (existing is null)
        {
            existing = new ProjectItemViewModel(definition);
            Projects.Insert(0, existing);
        }
        else
        {
            existing.Update(definition);
        }

        SelectedProject = existing;
    }

    private ProjectDefinition CreateGitProject(string rootPath)
    {
        ProjectItemViewModel? existing = Projects.FirstOrDefault(project =>
            ProjectPathsEqual(project.RootPath, rootPath));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ProjectDefinition(
            existing?.Id ?? Guid.NewGuid(),
            new DirectoryInfo(rootPath).Name,
            rootPath,
            new ProjectFeatures(true, true, false, false, false),
            RetentionPolicy.CurrentStateOnly,
            CreatedAt: existing?.Definition.CreatedAt ?? now,
            LastOpenedAt: now);
    }

    private async Task RunGitNetworkOperationAsync(
        string progressMessage,
        Func<string, CancellationToken, Task> operation,
        string successMessage)
    {
        if (SelectedProject is null)
        {
            return;
        }

        await RunOperationAsync(progressMessage, async () =>
        {
            await operation(SelectedProject.RootPath, CancellationToken.None);
            await RefreshCoreAsync();
        }, successMessage);
    }

    private void ReloadDocumentation()
    {
        string? selectedId = SelectedDocumentationTopic?.Id;
        _allDocumentationTopics = _documentationService.Load(_localization.CurrentLanguageCode);
        FilterDocumentation(selectedId);
    }

    private void FilterDocumentation(string? preferredTopicId = null)
    {
        string search = DocumentationSearch.Trim();
        IEnumerable<DocumentationTopic> filtered = _allDocumentationTopics;
        if (search.Length > 0)
        {
            filtered = filtered.Where(topic =>
                ContainsSearch(topic.Title, search) ||
                ContainsSearch(topic.Category, search) ||
                ContainsSearch(topic.Summary, search) ||
                ContainsSearch(topic.Body, search) ||
                topic.Keywords?.Any(keyword => ContainsSearch(keyword, search)) == true);
        }

        DocumentationTopic[] topics = filtered.ToArray();
        ReplaceCollection(DocumentationTopics, topics);
        SelectedDocumentationTopic = topics.FirstOrDefault(topic =>
                                         string.Equals(topic.Id, preferredTopicId, StringComparison.OrdinalIgnoreCase))
                                     ?? topics.FirstOrDefault();
    }

    private static bool ContainsSearch(string? value, string search) =>
        value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true;

    private static bool ProjectPathsEqual(string left, string right)
    {
        string normalizedLeft = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedRight = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(
            normalizedLeft,
            normalizedRight,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private void RefreshCompositionBranches()
    {
        string? previousSource = BranchCompareSource?.Name;
        string? previousTarget = BranchCompareTarget?.Name;
        ReplaceCollection(BranchCompareSources, Branches);
        ReplaceCollection(BranchCompareTargets, Branches.Where(branch => !branch.IsRemote));

        BranchCompareSource = BranchCompareSources.FirstOrDefault(branch => branch.Name == previousSource)
                              ?? BranchCompareSources.FirstOrDefault(branch => branch.IsCurrent)
                              ?? BranchCompareSources.FirstOrDefault();
        BranchCompareTarget = BranchCompareTargets.FirstOrDefault(branch => branch.Name == previousTarget)
                              ?? BranchCompareTargets.FirstOrDefault(branch => !branch.IsCurrent)
                              ?? BranchCompareTargets.FirstOrDefault();
    }

    private async Task RunOperationAsync(string progressMessage, Func<Task> operation, string successMessage)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = progressMessage;
        try
        {
            await operation();
            StatusMessage = successMessage;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearRepositoryView()
    {
        RepositoryPath = "Aucun projet ouvert";
        CurrentBranch = "—";
        ChangeSummary = "0 modification";
        Changes.Clear();
        History.Clear();
        Branches.Clear();
        LfsPatterns.Clear();
        ExplorerFiles.Clear();
        ExplorerFileHistory.Clear();
        foreach (MultiRestoreFileViewModel item in MultiRestoreFiles)
        {
            item.CompositionChanged -= OnMultiRestoreCompositionChanged;
        }
        foreach (CherryPickCommitViewModel item in BranchComparisonCommits)
        {
            item.CompositionChanged -= OnCherryPickCompositionChanged;
        }
        MultiRestoreFiles.Clear();
        MultiRestoreOperations.Clear();
        BranchComparisonCommits.Clear();
        BranchCompareSources.Clear();
        BranchCompareTargets.Clear();
        MultiRestoreCommit = null;
        _multiRestoreLoadedHash = null;
        SelectedMultiRestoreFile = null;
        SelectedCherryPickCommit = null;
        BranchCompareSource = null;
        BranchCompareTarget = null;
        _multiRestorePlan = null;
        _branchComparison = null;
        _cherryPickPlan = null;
        MultiRestoreSummary = "Choose a commit to compose a safe multi-file restore.";
        MultiRestoreDiff = "Select a file to review the change before composing the restore.";
        BranchComparisonSummary = "Choose a source and target branch to compare their commits.";
        CherryPickPlanSummary = "Compare branches, then select the source-only commits to apply.";
        CherryPickDiff = "Select a commit to inspect its patch.";
        OnPropertyChanged(nameof(CanApplyMultiRestore));
        OnPropertyChanged(nameof(CanApplyCherryPick));
        LfsFiles.Clear();
        LfsVersions.Clear();
        LfsLocks.Clear();
        MyLfsLocks.Clear();
        LfsLocksSummary = "Git LFS locks have not been loaded.";
        SmartSyncPlanItems.Clear();
        SmartSyncPlanSummary = "Aucun projet sélectionné.";
        _allExplorerRevisions = [];
        SelectedExplorerRevision = null;
        SelectedComparisonRevision = null;
        SelectedExplorerFile = null;
        SelectedLfsFile = null;
        SelectedLfsVersion = null;
        ExplorerSummary = "Sélectionnez une révision pour l'inspecter.";
        ExplorerDiff = "Le diff de la révision apparaîtra ici.";
        LfsTimelineSummary = "Sélectionnez un fichier LFS pour afficher ses versions.";
        LfsPreview = null;
        Backups.Clear();
        SelectedBackup = null;
        BackupStorePath = string.Empty;
        ColdArchivePath = string.Empty;
        ColdArchiveAfterDays = "180";
        ColdArchiveStatus = "Archive froide facultative — aucune suppression automatique.";
        SelectedPreset = null;
        _currentSyncProfile = null;
        SyncthingExecutablePath = string.Empty;
        SyncState = "Sync désactivé";
        SyncDetails = "Aucun projet sélectionné.";
        PeerMembers.Clear();
        SelectedPeerMember = null;
        AdvisoryReservations.Clear();
        ReservationSummary = "Aucun projet sélectionné.";
        _currentVpnProfile = null;
        ClearVpnView();
        SyncProjectMembers.Clear();
        GitProjectMembers.Clear();
        VpnProjectMembers.Clear();
        ProjectMembersSummary = "No project selected.";
        DiffText = "Sélectionnez un projet Git.";
        ClearGitGraphView();
        ClearCodeWorkspace();
        ClearAiMcpProfile();
        ClearPullRequestData();
    }

    private void ClearCodeWorkspace()
    {
        CodeTree.Clear();
        CodeSearchResults.Clear();
        CodeHistory.Clear();
        CodeSymbols.Clear();
        SelectedCodeNode = null;
        SelectedCodeSearchResult = null;
        SelectedCodeSymbol = null;
        CodePreviewText = string.Empty;
        CodeWorkspaceSummary = "Select a project to explore its code.";
        CodeSearchSummary = "Ctrl+Shift+F searches the entire project.";
        CodePreviewSummary = "Select a file to preview it.";
        CodeSelectionSummary = "Select lines in the preview, then request their Git history.";
    }

    private void ClearGitGraphView()
    {
        GitGraphCommits = [];
        GitFileActivities = [];
        GitFileRelations = [];
        GitContributors = [];
        GitDailyActivity = [];
        GitHotFiles = [];
        GitInsightsSummary = "Analyse d'activité non lancée.";
        UnrealDependencyFiles = [];
        UnrealDependencyRelations = [];
        UnrealAssets = [];
        UnrealDependencySummary = "Analyse Unreal hors moteur non lancée.";
        GitGraphSummary = "Analyse optionnelle non lancée.";
    }

    private void RefreshPluginCatalog(string? selectedPluginId = null)
    {
        string? selection = selectedPluginId ?? SelectedPlugin?.Id;
        ReplaceCollection(
            Plugins,
            _pluginManager.Entries.Select(entry => new PluginItemViewModel(entry)));
        SelectedPlugin = Plugins.FirstOrDefault(plugin =>
                             string.Equals(plugin.Id, selection, StringComparison.OrdinalIgnoreCase))
                         ?? Plugins.FirstOrDefault();

        IUnrealIntegrationPlugin? unreal = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        IsUnrealIntegrationEnabled = unreal is not null;
        if (unreal is null)
        {
            DetachUnrealPluginEvents();
            _unrealProjectInspection = null;
            UnrealPluginSummary = "Enable the Unreal Engine Integration plugin to inspect and install CyRevisionUnreal.";
            UnrealBridgeSummary = "The optional Unreal bridge is disabled.";
        }
        else
        {
            AttachUnrealPluginEvents(unreal);
            UnrealBridgeSummary = FormatBridgeStatus(unreal.BridgeStatus);
            RefreshUnrealInspection();
        }

        OnPropertyChanged(nameof(UnrealEditorPluginVersion));
        OnPropertyChanged(nameof(UnrealInstalledPluginVersion));
        OnPropertyChanged(nameof(CanInstallUnrealEditorPlugin));

        IAiIntegrationPlugin? ai = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        IsAiIntegrationEnabled = ai is not null;
        ReplaceCollection(AiProviders, ai?.Providers ?? []);
        SelectedAiProvider = AiProviders.FirstOrDefault(provider => provider.Id == SelectedAiProvider?.Id)
                             ?? AiProviders.FirstOrDefault();
        if (ai is null)
        {
            AiStatus = "AI integration disabled.";
            AiResponse = "Enable the optional AI Workspace plugin from the Plugins tab.";
            ClearAiMcpProfile();
        }
        else
        {
            AiStatus = "AI Workspace ready. Read access only by default.";
            AiResponse = "Choose a provider, review permissions, and describe a task.";
            _ = LoadAiMcpProfileCoreAsync();
        }
    }

    private void RefreshUnrealInspection()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || string.IsNullOrWhiteSpace(UnrealProjectPath))
        {
            _unrealProjectInspection = null;
            OnPropertyChanged(nameof(UnrealEditorPluginVersion));
            OnPropertyChanged(nameof(UnrealInstalledPluginVersion));
            OnPropertyChanged(nameof(CanInstallUnrealEditorPlugin));
            return;
        }

        _unrealProjectInspection = plugin.InspectProject(UnrealProjectPath);
        UnrealPluginSummary = _unrealProjectInspection.Summary;
        OnPropertyChanged(nameof(UnrealEditorPluginVersion));
        OnPropertyChanged(nameof(UnrealInstalledPluginVersion));
        OnPropertyChanged(nameof(CanInstallUnrealEditorPlugin));
    }

    private void AttachUnrealPluginEvents(IUnrealIntegrationPlugin plugin)
    {
        if (ReferenceEquals(_subscribedUnrealPlugin, plugin))
        {
            return;
        }

        DetachUnrealPluginEvents();
        _subscribedUnrealPlugin = plugin;
        _subscribedUnrealPlugin.ProjectChanged += OnUnrealProjectChanged;
    }

    private void DetachUnrealPluginEvents()
    {
        if (_subscribedUnrealPlugin is not null)
        {
            _subscribedUnrealPlugin.ProjectChanged -= OnUnrealProjectChanged;
            _subscribedUnrealPlugin = null;
        }
    }

    private void OnUnrealProjectChanged(object? sender, UnrealProjectChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = $"Unreal Editor reported '{eventArgs.Action}' for {eventArgs.ProjectRoot}";
            if (SelectedProject is not null && ProjectPathsEqual(SelectedProject.RootPath, eventArgs.ProjectRoot))
            {
                _ = RefreshAsync();
            }
        });
    }

    private static string FormatBridgeStatus(UnrealBridgeStatus status) =>
        $"{(status.IsRunning ? "Connected" : "Stopped")} · {status.Endpoint} · " +
        $"{status.AuthorizedProjectCount} authorized project(s) · {status.Detail}";

    private static void ReplaceCollection<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        destination.Clear();
        foreach (T item in source)
        {
            destination.Add(item);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _remoteBuildCancellation?.Cancel();
        _codeSearchCancellation?.Cancel();
        _codeSearchCancellation?.Dispose();
        _aiAgentCancellation?.Cancel();
        _aiAgentCancellation?.Dispose();
        await StopSyncCoreAsync();
        if (_vpnFileExchangeHost is not null)
        {
            await _vpnFileExchangeHost.DisposeAsync();
            _vpnFileExchangeHost = null;
        }
        DetachUnrealPluginEvents();
        await _pluginManager.DisposeAsync();
        _discordAgent.StatusChanged -= OnDiscordAgentStatusChanged;
        await _discordAgent.DisposeAsync();
        await _pullRequestService.DisposeAsync();
        AssetDiffPreview = null;
        LfsPreview = null;
        _updateService.Dispose();
    }
}
