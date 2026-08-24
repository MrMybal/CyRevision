using System.Collections.ObjectModel;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CyRevision.Backup;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Core.Updates;
using CyRevision.Code;
using CyRevision.Desktop.Diagnostics;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Plugins;
using CyRevision.Desktop.Workspace;
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

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly FieldInfo[] ProjectSessionFields = typeof(MainWindowViewModel)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(ShouldCacheProjectField)
        .ToArray();
    private static readonly PropertyInfo[] ProjectSessionCollections = typeof(MainWindowViewModel)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(ShouldCacheProjectCollection)
        .ToArray();
    private static readonly IReadOnlyDictionary<string, FieldInfo> ProjectSessionFieldsByName =
        ProjectSessionFields.ToDictionary(field => field.Name, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, PropertyInfo> ProjectSessionCollectionsByName =
        ProjectSessionCollections.ToDictionary(property => property.Name, StringComparer.Ordinal);

    private readonly IProjectCatalog _projectCatalog;
    private readonly IGitRepositoryService _gitService;
    private readonly ApplicationPaths _applicationPaths;
    private readonly ISyncthingProfileStore _syncthingProfileStore;
    private readonly SyncthingRuntimeResolver _syncthingRuntimeResolver;
    private readonly SyncthingIgnoreFileService _syncthingIgnoreFileService;
    private readonly JsonLineSyncHistoryStore _syncHistoryStore;
    private readonly SyncConflictService _syncConflictService;
    private readonly SyncCommitService _syncCommitService = new();
    private readonly IGitPeerExchangeService _gitPeerExchangeService;
    private readonly IAssetDiffService _assetDiffService;
    private readonly FilePresentationService _filePresentationService;
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
    private readonly ITeamChatProfileStore _teamChatProfileStore;
    private readonly TeamChatService _teamChatService;
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
    private readonly ICiWorkflowService _ciWorkflowService;
    private readonly LocalChangePreferencesStore _localChangePreferencesStore;
    private readonly ApplicationLogService _applicationLogService;
    private readonly RepositoryConsoleService _repositoryConsoleService;
    private readonly ProjectGitCacheStore _projectGitCacheStore = new();
    private readonly GitIgnoreService _gitIgnoreService = new();
    private readonly ProjectLicenseService _projectLicenseService = new();
    private readonly GitAnnotationStore _gitAnnotationStore;
    private readonly Dictionary<string, string> _gitIgnoreDrafts = new(StringComparer.Ordinal);
    private readonly Stopwatch _applicationUptime = Stopwatch.StartNew();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly string? _initialProjectPath;
    private readonly HashSet<string> _localOnlyChangePaths = new(StringComparer.OrdinalIgnoreCase);
    private string[] _legacyProjectPluginIds = [];
    private ProjectItemViewModel? _selectedProject;
    private GitChangeViewModel? _selectedChange;
    private int _selectedDiffLoadVersion;
    private GitChangeTreeNode? _selectedChangeTreeNode;
    private GitBranch? _selectedBranch;
    private IReadOnlyList<GitBranch> _selectedBranches = [];
    private GitRevision? _selectedBranchRevision;
    private GitHistoricalWorktree? _selectedHistoricalWorktree;
    private bool _createHistoricalBranchInWorktree = true;
    private string _historicalWorktreeStatus = "No CyRevision historical worktree loaded.";
    private string _changeSearch = string.Empty;
    private string _changeSort = "Name";
    private string _branchSearch = string.Empty;
    private string _branchSort = "Name";
    private string _selectedBranchSummary = "Select a branch to inspect its commits without switching.";
    private GitBranchDetails? _selectedBranchDetails;
    private int _selectedBranchHistoryLoadVersion;
    private CancellationTokenSource? _branchHistoryCancellation;
    private bool _isChangesDiffPreviewEnabled = true;
    private BackupSnapshotViewModel? _selectedBackup;
    private bool _isBusy;
    private string _statusMessage = "Initialisation…";
    private bool _isActivityCenterExpanded;
    private bool _isActivityCenterDismissed;
    private string _toolStatus = "Git : vérification…";
    private string _currentBranch = "—";
    private string _repositoryPath = "Aucun projet ouvert";
    private string _changeSummary = "0 modification";
    private string _changeSummaryColor = "#A9ABB2";
    private string _currentProjectName = "No project";
    private int _projectLicenseLoadVersion;
    private ProjectLicenseSnapshot? _projectLicenseSnapshot;
    private ProjectLicenseTemplate? _selectedProjectLicenseTemplate;
    private string _projectLicenseDraft = string.Empty;
    private string _projectLicenseFileName = "LICENSE";
    private string _projectLicenseHolder = Environment.UserName;
    private string _projectLicenseYear = DateTimeOffset.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private string _projectLicenseStatus = "Select a project to inspect its license.";
    private string _projectLicenseDetectedId = "None";
    private bool _isProjectLicenseLoading;
    private bool _isProjectLoading;
    private double _projectLoadProgress;
    private string _projectLoadStage = "No project selected";
    private bool _includeRemoteHistory;
    private bool _isLfsInventoryLoaded;
    private string _changePreparationSummary = "No files selected for commit.";
    private int _includedChangeCount;
    private int _keptChangeCount;
    private int _foreignLockedIncludedCount;
    private bool _suspendChangePreparationSummary;
    private bool _changePreparationSummaryPending;
    private string _commitMessage = string.Empty;
    private string _diffText = "Sélectionnez un fichier pour afficher son diff.";
    private Bitmap? _diffPreviewImage;
    private string _diffPresentationSummary = string.Empty;
    private string _lfsPattern = "*.uasset";
    private string _remoteUrl = string.Empty;
    private string _backupStorePath = string.Empty;
    private string _coldArchivePath = string.Empty;
    private string _coldArchiveAfterDays = "180";
    private string _coldArchiveStatus = "Archive froide facultative — aucune suppression automatique.";
    private BackupArchiveProfile _selectedBackupArchiveProfile = BackupArchiveProfile.BuiltIn[0];
    private bool _removeArchivedHotCopies;
    private GitArchiveProfile _selectedGitArchiveProfile = GitArchiveProfile.BuiltIn[0];
    private GitArchiveCandidate? _selectedGitArchiveCandidate;
    private GitArchivedBranch? _selectedArchivedGitBranch;
    private string _gitArchiveStatus = "Analyze stale local branches before archiving. No branch is removed by default.";
    private bool _removeGitBranchAfterArchive;
    private RetentionMode _selectedRetentionMode = RetentionMode.Timeline;
    private string _retentionVersions = "30";
    private string _retentionDays = "90";
    private string _retentionBudgetGb = string.Empty;
    private ProjectPreset? _selectedPreset;
    private SyncthingProfile? _currentSyncProfile;
    private ManagedSyncthingEngine? _syncEngine;
    private Guid? _syncEngineProjectId;
    private string _syncthingExecutablePath = string.Empty;
    private string _syncthingRuntimeSummary = "No Syncthing runtime detected.";
    private SyncthingFolderMode _selectedSyncthingFolderMode = SyncthingFolderMode.SendReceive;
    private string _syncthingRescanInterval = "60";
    private bool _syncthingFileWatcherEnabled = true;
    private string _syncthingFolderSummary = "Start Syncthing to inspect folder differences.";
    private string _syncthingIgnoreRules = string.Empty;
    private string _syncthingIgnoreStatus = "No .stignore file loaded.";
    private string _syncSourceFolderPath = string.Empty;
    private string _syncVersionStorePath = string.Empty;
    private string _syncCompressedBackupPath = string.Empty;
    private bool _syncCompressedBackupEnabled;
    private string _syncStorageStatus = "Project folder defaults are active.";
    private string _syncHistorySearch = string.Empty;
    private string _syncHistoryPathFilter = string.Empty;
    private string _syncHistorySummary = "No Sync history loaded.";
    private readonly HashSet<string> _observedSyncDifferences = new(StringComparer.Ordinal);
    private IReadOnlyList<SyncConflictItem> _allSyncConflicts = [];
    private SyncConflictItem? _selectedSyncConflict;
    private SyncConflictBackup? _selectedSyncConflictBackup;
    private string _syncConflictSearch = string.Empty;
    private string _syncConflictRetentionDays = "30";
    private string _syncConflictSummary = "Scan synchronized folders to detect Syncthing conflict copies.";
    private bool _isSyncConflictBusy;
    private bool _isSyncthingRefreshing;
    private string _syncCommitMessage = string.Empty;
    private string _syncCommitAuthor = Environment.UserName;
    private string _syncCommitStatus = "Create a commit to publish an immutable project snapshot.";
    private SyncCommitManifest? _selectedSyncCommit;
    private SyncCommitAnalysis? _selectedSyncCommitAnalysis;
    private SyncCommitConflictViewModel? _selectedSyncCommitConflict;
    private bool _isSyncCommitBusy;
    private SyncthingSharedFolder? _selectedSharedSyncFolder;
    private string _sharedSyncFolderName = "Build versions";
    private string _sharedSyncFolderPath = string.Empty;
    private SyncthingFolderMode _selectedSharedSyncFolderMode = SyncthingFolderMode.SendReceive;
    private string _sharedSyncFolderStatus = "No independent shared folder configured.";
    private string _syncState = "Sync désactivé";
    private string _syncDetails = "Aucune instance CyRevision n'est lancée.";
    private string _peerExchangeText = string.Empty;
    private PeerRole _selectedPeerRole = PeerRole.Contributor;
    private PeerRole _selectedPeerMemberRole = PeerRole.Contributor;
    private string _peerVerificationCode = string.Empty;
    private string _assetBaselinePath = string.Empty;
    private string _assetCandidatePath = string.Empty;
    private string _assetDiffReport = "Choisissez deux fichiers, ou comparez une modification Git à HEAD.";
    private Bitmap? _assetDiffPreview;
    private string _assetExplorerSearch = string.Empty;
    private string _assetExplorerSummary = "Search or browse project files, then select one to preview it.";
    private CodeFileEntry? _selectedAssetExplorerFile;
    private IReadOnlyList<CodeFileEntry> _assetExplorerFiles = [];
    private CancellationTokenSource? _assetExplorerCancellation;
    private CancellationTokenSource? _assetExplorerPreviewCancellation;
    private bool _isAssetExplorerLoading;
    private bool _isAssetExplorerPreviewLoading;
    private int _assetExplorerPreviewVersion;
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
    private IReadOnlyList<GitRevision> _commitExplorerSourceRevisions = [];
    private GitRevision? _selectedExplorerRevision;
    private GitRevision? _selectedCommitExplorerRevision;
    private GitRevision? _selectedComparisonRevision;
    private GitCommitFileChange? _selectedExplorerFile;
    private int _explorerRevisionLoadVersion;
    private int _explorerFileLoadVersion;
    private bool _isExplorerLoading;
    private bool _isExplorerDiffLoading;
    private string _explorerSearch = string.Empty;
    private string _explorerSummary = "Sélectionnez une révision pour l'inspecter.";
    private string _explorerDiff = "Le diff de la révision apparaîtra ici.";
    private Bitmap? _explorerDiffPreviewImage;
    private string _explorerDiffPresentationSummary = string.Empty;
    private string? _comparisonFromHash;
    private string? _comparisonToHash;
    private GitRevision? _multiRestoreCommit;
    private string? _multiRestoreLoadedHash;
    private MultiRestoreFileViewModel? _selectedMultiRestoreFile;
    private GitMultiRestorePlan? _multiRestorePlan;
    private string _multiRestoreSummary = "Choose a commit to compose a safe multi-file restore.";
    private string _multiRestoreDiff = "Select a file to review the change before composing the restore.";
    private Bitmap? _multiRestoreDiffPreviewImage;
    private string _multiRestoreDiffPresentationSummary = string.Empty;
    private bool _isMultiRestoreDiffLoading;
    private int _multiRestoreDiffLoadVersion;
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
    private bool _isCherryPickDiffLoading;
    private int _cherryPickDiffLoadVersion;
    private LfsTrackedFile? _selectedLfsFile;
    private LfsFileVersion? _selectedLfsVersion;
    private LfsFileTreeNode? _selectedLfsFileTreeNode;
    private string _lfsTimelineSummary = "Sélectionnez un fichier LFS pour afficher ses versions.";
    private string _lfsLocksSummary = "Git LFS locks have not been loaded.";
    private string _lfsLockSearch = string.Empty;
    private string _lfsLockSort = "File";
    private string _allLfsLockSearch = string.Empty;
    private string _myLfsLockSearch = string.Empty;
    private string _allLfsLockSort = "File name";
    private string _myLfsLockSort = "File name";
    private string _lfsLockFilterSummary = "No locks loaded.";
    private int _selectedProjectLockCount;
    private int _selectedMyLockCount;
    private IReadOnlyList<LfsFileLock> _selectedProjectLocks = [];
    private IReadOnlyList<LfsFileLock> _selectedMyLocks = [];
    private Bitmap? _lfsPreview;
    private LfsHistoryTransferMode _selectedLfsHistoryMode = LfsHistoryTransferMode.OnDemand;
    private string _smartSyncRecentVersionCount = "3";
    private bool _smartSyncReplicateBackups;
    private string _smartSyncPlanSummary = "Plan calculé localement — aucun transfert lancé.";
    private string _peerLfsTransferSummary = "Aucun inventaire de pair vérifié pour le moment.";
    private LfsManagementProfile? _currentLfsManagementProfile;
    private LfsCleanupPlan? _lfsCleanupPlan;
    private CancellationTokenSource? _lfsAnalysisCancellation;
    private string _lfsExternalStoragePath = string.Empty;
    private string _lfsManagementArchivePath = string.Empty;
    private string _lfsCleanupRemoteName = "origin";
    private string _lfsRequiredCopies = "1";
    private string _lfsCleanupGraceDays = "7";
    private string _lfsPeerProofMaximumAgeHours = "24";
    private bool _lfsVerifyRemote = true;
    private bool _lfsRemoveOriginalAfterRelocation;
    private bool _lfsTrimRemoteBackedHistory;
    private string _lfsRecentVersionsPerFile = "3";
    private string _lfsRecentVersionExtensions = ".uasset;.umap";
    private string _lfsRemoteVerificationTimeoutSeconds = "45";
    private bool _isLfsAnalysisRunning;
    private int _lfsAnalysisPercent;
    private string _lfsAnalysisStage = "Ready for a non-destructive analysis.";
    private string _lfsRepositorySizeSummary = "Run a quick size scan to inspect working files, Git metadata, and the LFS cache.";
    private string _lfsStorageSummary = "Analyze the repository before cleaning LFS storage.";
    private string _lfsRemoteVerification = "Remote verification has not run.";
    private string _gitIgnoreSource = "Repository .gitignore";
    private string _gitIgnoreContent = string.Empty;
    private string _gitIgnoreFilePath = "No Git ignore file loaded.";
    private string _gitIgnoreSummary = "Open this tool to edit Git ignore rules.";
    private string _gitIgnoreTestPath = string.Empty;
    private string _gitIgnoreTestResult = "Enter a project-relative path to test it against Git.";
    private string _gitIgnoreTemplate = "Unreal Engine";
    private bool _isGitIgnoreDirty;
    private bool _isGitIgnoreLoading;
    private bool _suspendGitIgnoreEditing;
    private string _ignoreSuggestionSummary = "Open an ignore editor to scan project folders and file types.";
    private bool _isIgnoreSuggestionLoading;
    private Guid? _ignoreSuggestionProjectId;
    private IReadOnlyList<IgnoreSuggestionViewModel> _allIgnoreFolderSuggestions = [];
    private IReadOnlyList<IgnoreSuggestionViewModel> _allIgnoreFileTypeSuggestions = [];
    private IReadOnlyList<IgnoreSuggestionViewModel> _ignoreFolderSuggestions = [];
    private IReadOnlyList<IgnoreSuggestionViewModel> _ignoreFileTypeSuggestions = [];
    private IReadOnlyList<IgnoreSuggestionTreeNode> _ignoreFolderTree = [];
    private string _ignoreFolderSearch = string.Empty;
    private string _ignoreFileTypeSearch = string.Empty;
    private string _ignoreFolderFilter = "All";
    private string _ignoreFileTypeFilter = "All";
    private string _performanceSummary = "Open diagnostics to inspect runtime performance.";
    private VpnProjectProfile? _currentVpnProfile;
    private string _vpnState = "VPN non configuré";
    private string _vpnDetails = "WireGuard reste indépendant de Git et de Sync.";
    private string _wireGuardExecutablePath = string.Empty;
    private string _vpnNetworkCidr = string.Empty;
    private string _vpnLocalAddress = string.Empty;
    private string _vpnPublicEndpoint = string.Empty;
    private string _vpnListenPort = "51820";
    private string _vpnExchangeText = string.Empty;
    private string _vpnInvitationClientName = string.Empty;
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
    private TeamChatProfile? _currentTeamChatProfile;
    private TeamChatHost? _teamChatHost;
    private TeamChatSyncWatcher? _teamChatSyncWatcher;
    private CancellationTokenSource? _teamChatRefreshCancellation;
    private TeamChatMessage? _selectedTeamChatMessage;
    private TeamChatChannel? _selectedTeamChatChannel;
    private TeamChatTransport _selectedTeamChatTransport = TeamChatTransport.Vpn;
    private readonly Dictionary<Guid, TeamChatMessage> _teamChatMessageCache = [];
    private string _teamChatDisplayName = Environment.UserName;
    private string _teamChatListenAddress = "127.0.0.1";
    private string _teamChatPort = TeamChatDefaults.Port.ToString();
    private string _teamChatPeerEndpoint = $"127.0.0.1:{TeamChatDefaults.Port}";
    private string _teamChatAccessToken = string.Empty;
    private string _teamChatSyncFolderPath = string.Empty;
    private string _teamChatServerBaseUrl = "https://chat.example.com/";
    private string _teamChatServerApiToken = string.Empty;
    private bool _teamChatAllowPrivateServerHttp;
    private string _teamChatNewChannelName = string.Empty;
    private string _teamChatNewChannelTopic = string.Empty;
    private string _teamChatMessageText = string.Empty;
    private string _teamChatAttachmentPath = string.Empty;
    private string _teamChatStatus = "Team chat is not configured.";
    private bool _teamChatSaveConversations = true;
    private bool _teamChatEncryptStoredConversations;
    private string _teamChatRetentionDays = "365";
    private string _teamChatMaxAttachmentMb = "50";
    private bool _isTeamChatHostRunning;
    private RemoteBuildCredentials? _currentRemoteBuildCredentials;
    private RemoteBuildJobStatus? _currentRemoteBuildJob;
    private CancellationTokenSource? _remoteBuildCancellation;
    private CancellationTokenSource? _projectLoadCancellation;
    private int _projectLoadVersion;
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
    private bool _unrealAdvancedAssetInspectionEnabled;
    private bool _unrealRenderMeshThumbnails = true;
    private string _unrealAssetPreviewResolution = "512";
    private string _unrealAssetCacheBudgetGigabytes = "2";
    private string _unrealAssetInspectionSummary = "Advanced asset previews are disabled. Lightweight offline metadata remains available.";
    private UnrealProjectInspection? _unrealProjectInspection;
    private IUnrealIntegrationPlugin? _subscribedUnrealPlugin;
    private UnrealBuildDiscovery? _unrealBuildDiscovery;
    private UnrealEngineInstallation? _selectedUnrealBuildEngine;
    private UnrealEngineInstallation? _unrealBuildRangeFrom;
    private UnrealEngineInstallation? _unrealBuildRangeTo;
    private UnrealBuildTargetDescriptor? _selectedUnrealBuildTarget;
    private UnrealBuildPlatform _selectedUnrealBuildPlatform = UnrealBuildPlatform.Win64;
    private UnrealBuildConfiguration _selectedUnrealBuildConfiguration = UnrealBuildConfiguration.Development;
    private string _unrealLinuxToolchainPath = string.Empty;
    private string _unrealAndroidSdkPath = string.Empty;
    private string _unrealAndroidNdkPath = string.Empty;
    private string _unrealJavaHomePath = string.Empty;
    private string _unrealBuildOutputPath = string.Empty;
    private string _unrealBuildTimeoutMinutes = "120";
    private string _unrealBuildPresetName = "Default";
    private string _unrealBuildMaximumParallel = "1";
    private UnrealBuildProfile? _selectedUnrealBuildPreset;
    private UnrealBuildResult? _selectedUnrealBuildResult;
    private bool _unrealBuildCookAndPackage;
    private bool _unrealBuildAutoConfigureToolchains = true;
    private bool _isUnrealBuildRunning;
    private string _unrealBuildStatus = "Discover the Unreal project to list engines and buildable targets.";
    private string _unrealBuildLog = "No local Unreal build has run.";
    private CancellationTokenSource? _unrealBuildCancellation;
    private CancellationTokenSource? _codeSearchCancellation;
    private CancellationTokenSource? _codeFileFilterCancellation;
    private CancellationTokenSource? _codeWorkspaceCancellation;
    private CancellationTokenSource? _codePreviewCancellation;
    private readonly Dictionary<Guid, CachedCodeWorkspace> _codeWorkspaceCache = [];
    private readonly LinkedList<Guid> _codeWorkspaceCacheUsage = [];
    private readonly HashSet<string> _loadingCodeDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, CachedProjectSession> _projectSessionCache = [];
    private readonly LinkedList<Guid> _projectSessionCacheUsage = [];
    private readonly WorkspaceLoadCoordinator _workspaceLoadCoordinator = new();
    private readonly Dictionary<Guid, GitRepositoryStatus> _latestProjectStatuses = [];
    private readonly HashSet<Guid> _loadedProjectSessions = [];
    private bool _isRestoringProjectSession;
    private string _codeAutoRefreshFrequency = "Low · 5 min";
    private DateTimeOffset? _codeWorkspaceLastUpdated;
    private CancellationTokenSource? _aiAgentCancellation;
    private CancellationTokenSource? _codexConnectionCancellation;
    private CodeTreeNode? _selectedCodeNode;
    private CodeFileEntry? _selectedCodeFileSearchResult;
    private IReadOnlyList<CodeFileEntry> _codeFileSearchResults = [];
    private CodeSearchResult? _selectedCodeSearchResult;
    private IReadOnlyList<CodeSearchResult> _filteredCodeSearchResults = [];
    private CodeSymbol? _selectedCodeSymbol;
    private string _codeTreeFilter = string.Empty;
    private bool _codeIncludeHidden;
    private string _codeWorkspaceSummary = "Select a project to explore its code.";
    private string _codeSearchQuery = string.Empty;
    private string _codeSearchFileFilter = string.Empty;
    private string _codeFilePatterns = string.Empty;
    private bool _codeSearchMatchCase;
    private bool _codeSearchWholeWord;
    private bool _codeSearchRegex;
    private bool _isCodeSearchRunning;
    private bool _isCodeFileSearchRunning;
    private string _codeSearchSummary = "Ctrl+Shift+F searches the entire project.";
    private string _codePreviewText = string.Empty;
    private Bitmap? _codePreviewImage;
    private bool _codePreviewIsImage;
    private bool _codePreviewSupportsAiSummary;
    private string _codePreviewPath = string.Empty;
    private string _codePreviewSummary = "Select a file to preview it.";
    private string _codeSelectionSummary = "Select lines in the preview, then request their Git history.";
    private string _codeAiSummary = string.Empty;
    private bool _isCodeWorkspaceLoading;
    private bool _isAiIntegrationEnabled;
    private AiProviderDescriptor? _selectedAiProvider;
    private string _aiModel = string.Empty;
    private string _aiEndpoint = string.Empty;
    private string _aiExecutablePath = "codex";
    private string _aiApiKey = string.Empty;
    private string _aiPrompt = string.Empty;
    private string _aiResponse = "Enable the optional AI Workspace plugin to connect Codex or an API provider.";
    private string _aiStatus = "AI integration disabled.";
    private bool _isCodexDetected;
    private bool _isCodexRunning;
    private bool _isCodexChatConnected;
    private bool _isCodexChatBusy;
    private string _codexConnectionStatus = "Codex has not been scanned yet.";
    private string _codexDetectedVersion = string.Empty;
    private string _codexDetectedPath = string.Empty;
    private string _codexChatThreadId = string.Empty;
    private Guid? _codexChatProjectId;
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
    private string? _pullRequestCredentialToken;
    private PullRequestSummary? _selectedPullRequest;
    private PullRequestFile? _selectedPullRequestFile;
    private PullRequestDetails? _selectedPullRequestDetails;
    private PullRequestStateFilter _pullRequestStateFilter = PullRequestStateFilter.Open;
    private PullRequestMergeMethod _pullRequestMergeMethod = PullRequestMergeMethod.Squash;
    private PullRequestReviewAction _pullRequestReviewAction = PullRequestReviewAction.Comment;
    private string _pullRequestStatus = "Select a GitHub-backed project to manage pull requests.";
    private bool _isPullRequestLoading;
    private int _pullRequestLoadCount;
    private string _pullRequestSearch = string.Empty;
    private string _pullRequestApiBaseUrl = string.Empty;
    private string _pullRequestToken = string.Empty;
    private string _pullRequestTokenEnvironmentVariable = "GITHUB_TOKEN";
    private string _pullRequestPatch = "Select a changed file to inspect its patch.";
    private bool _isPullRequestDiffPreviewEnabled = true;
    private string _newPullRequestTitle = string.Empty;
    private string _newPullRequestBody = string.Empty;
    private string _newPullRequestHeadBranch = string.Empty;
    private string _newPullRequestBaseBranch = "main";
    private bool _newPullRequestIsDraft;
    private string _pullRequestComment = string.Empty;
    private string _pullRequestReviewBody = string.Empty;
    private int _pullRequestDetailsLoadVersion;
    private CiWorkflow? _selectedCiWorkflow;
    private CiWorkflowRun? _selectedCiRun;
    private string _ciStatus = "Select a GitHub-backed project to inspect CI workflows.";
    private string _ciRunDetails = "Select a workflow run to inspect jobs and failed steps.";
    private string _ciGitRef = string.Empty;
    private string _ciReleaseVersion = string.Empty;
    private bool _isCiLoading;
    private int _ciRunLoadVersion;
    private bool _isWorkingTreeDiffLoading;
    private CancellationTokenSource? _workingTreeDiffCancellation;
    private CancellationTokenSource? _automaticGitRefreshCancellation;
    private CancellationTokenSource? _gitCacheSaveCancellation;
    private CancellationTokenSource? _explorerRevisionCancellation;
    private CancellationTokenSource? _explorerFileCancellation;
    private readonly BoundedLruCache<string, string> _diffCache = new(
        maximumEntries: 256,
        maximumWeight: 32 * 1024 * 1024,
        getWeight: value => value.Length,
        comparer: StringComparer.Ordinal);
    private readonly BoundedLruCache<string, IReadOnlyList<GitFileRevision>> _fileHistoryCache = new(
        maximumEntries: 256,
        comparer: StringComparer.Ordinal);
    private readonly BoundedLruCache<string, GitCommitDetails> _commitDetailsCache = new(
        maximumEntries: 256,
        comparer: StringComparer.Ordinal);
    private readonly BoundedLruCache<string, CachedBranchInspection> _branchHistoryCache = new(
        maximumEntries: 128,
        comparer: StringComparer.Ordinal);
    private readonly BoundedLruCache<string, CachedCodeFileInspection> _codeFileInspectionCache = new(
        maximumEntries: 256,
        maximumWeight: 32 * 1024 * 1024,
        getWeight: value => value.Preview.Text.Length + (value.History.Count * 256L),
        comparer: StringComparer.Ordinal);
    private int _workingTreeDiffGeneration;
    private CancellationTokenSource? _repositoryConsoleCancellation;
    private readonly Dictionary<string, CancellationTokenSource> _uiDebounceOperations = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, StringBuilder> _repositoryConsoleOutputBuffers = [];
    private readonly Dictionary<Guid, string> _repositoryConsoleStatuses = [];
    private string _repositoryConsoleCommand = string.Empty;
    private string _repositoryConsoleOutput = "Select a project to open its repository console.";
    private string _repositoryConsoleStatus = "Console ready.";
    private string _selectedRepositoryShell = OperatingSystem.IsWindows() ? "PowerShell" : "Bash";
    private bool _isRepositoryConsoleRunning;
    private string _repositoryConsoleHistorySearch = string.Empty;
    private RepositoryCommandHistoryEntry? _selectedRepositoryCommand;
    private int _repositoryHistoryNavigationIndex = -1;
    private string _applicationLogSearch = string.Empty;
    private string _applicationLogLevelFilter = "All";
    private ApplicationLogEntry? _selectedApplicationLogEntry;
    private string _sidebarGroupDraft = string.Empty;
    private readonly Dictionary<string, bool> _projectGroupExpansion = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(
        IProjectCatalog projectCatalog,
        IGitRepositoryService gitService,
        ApplicationPaths applicationPaths,
        ISyncthingProfileStore syncthingProfileStore,
        SyncthingRuntimeResolver syncthingRuntimeResolver,
        SyncthingIgnoreFileService syncthingIgnoreFileService,
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
        ITeamChatProfileStore teamChatProfileStore,
        TeamChatService teamChatService,
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
        ICiWorkflowService ciWorkflowService,
        ApplicationLogService applicationLogService,
        RepositoryConsoleService repositoryConsoleService,
        string? initialProjectPath = null)
    {
        _projectCatalog = projectCatalog;
        _gitService = gitService;
        _applicationPaths = applicationPaths;
        _syncthingProfileStore = syncthingProfileStore;
        _syncthingRuntimeResolver = syncthingRuntimeResolver;
        _syncthingIgnoreFileService = syncthingIgnoreFileService;
        _syncHistoryStore = new JsonLineSyncHistoryStore(Path.Combine(applicationPaths.DataDirectory, "sync-history"));
        _syncConflictService = new SyncConflictService(Path.Combine(applicationPaths.DataDirectory, "sync-conflicts"));
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
        _teamChatProfileStore = teamChatProfileStore;
        _teamChatService = teamChatService;
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
        _filePresentationService = new FilePresentationService(pluginManager, assetDiffService);
        _codeWorkspaceService = codeWorkspaceService;
        _pullRequestService = pullRequestService;
        _ciWorkflowService = ciWorkflowService;
        _applicationLogService = applicationLogService;
        _repositoryConsoleService = repositoryConsoleService;
        _localChangePreferencesStore = new LocalChangePreferencesStore(applicationPaths.ConfigurationDirectory);
        _gitAnnotationStore = new GitAnnotationStore(applicationPaths.ConfigurationDirectory);
        _selectedProjectLicenseTemplate = _projectLicenseService.Templates.First();
        _applicationLogService.EntryWritten += OnApplicationLogEntryWritten;
        ReplaceCollection(ApplicationLogEntries, _applicationLogService.LoadRecent());
        ApplyApplicationLogFilter();
        _discordAgent.StatusChanged += OnDiscordAgentStatusChanged;
        _selectedLanguage = localization.Languages.FirstOrDefault(language =>
            string.Equals(language.Code, localization.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase));
        _initialProjectPath = initialProjectPath;
        Projects.CollectionChanged += (_, _) => RebuildProjectSidebarGroups();
        ReloadDocumentation();
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<ProjectSidebarGroupViewModel> ProjectSidebarGroups { get; } = [];

    public IReadOnlyList<string> ProjectAccentColors { get; } =
    [
        "#4E9F8A", "#4C9BE8", "#7B7FF0", "#A66DE0", "#D65C9A",
        "#E06C75", "#E89A4C", "#D9B949", "#70A95B", "#7E8799"
    ];

    public IReadOnlyList<ProjectLicenseTemplate> ProjectLicenseTemplates => _projectLicenseService.Templates;

    public ObservableCollection<GitChangeViewModel> Changes { get; } = new BatchObservableCollection<GitChangeViewModel>();

    public ObservableCollection<GitChangeViewModel> PreparedChanges { get; } = new BatchObservableCollection<GitChangeViewModel>();

    public ObservableCollection<GitChangeViewModel> FilteredVersionedChanges { get; } = new BatchObservableCollection<GitChangeViewModel>();

    public ObservableCollection<GitChangeViewModel> FilteredUnversionedChanges { get; } = new BatchObservableCollection<GitChangeViewModel>();

    public ObservableCollection<GitChangeViewModel> LocalOnlyChanges { get; } = new BatchObservableCollection<GitChangeViewModel>();

    public ObservableCollection<GitChangeViewModel> FilteredLocalOnlyChanges { get; } = new BatchObservableCollection<GitChangeViewModel>();

    public ObservableCollection<GitChangeTreeNode> ChangeTree { get; } = new BatchObservableCollection<GitChangeTreeNode>();

    public ObservableCollection<GitChangeTreeNode> FlatChangeTree { get; } = new BatchObservableCollection<GitChangeTreeNode>();

    public ObservableCollection<GitRevision> History { get; } = new BatchObservableCollection<GitRevision>();

    public ObservableCollection<GitRevision> CommitExplorerRevisions { get; } = new BatchObservableCollection<GitRevision>();

    public ObservableCollection<GitBranch> Branches { get; } = new BatchObservableCollection<GitBranch>();

    public ObservableCollection<GitBranch> FilteredBranches { get; } = new BatchObservableCollection<GitBranch>();

    public ObservableCollection<GitRevision> SelectedBranchHistory { get; } = new BatchObservableCollection<GitRevision>();

    public ObservableCollection<LfsTrackedPattern> LfsPatterns { get; } = [];

    public ObservableCollection<LfsFileLock> LfsLocks { get; } = new BatchObservableCollection<LfsFileLock>();

    public ObservableCollection<LfsFileLock> MyLfsLocks { get; } = new BatchObservableCollection<LfsFileLock>();

    public ObservableCollection<LfsFileLock> FilteredLfsLocks { get; } = new BatchObservableCollection<LfsFileLock>();

    public ObservableCollection<LfsFileLock> FilteredMyLfsLocks { get; } = new BatchObservableCollection<LfsFileLock>();

    public ObservableCollection<LfsLockTreeNode> LfsLockTree { get; } = [];

    public ObservableCollection<LfsLockTreeNode> MyLfsLockTree { get; } = [];

    public ObservableCollection<GitCommitFileChange> ExplorerFiles { get; } = new BatchObservableCollection<GitCommitFileChange>();

    public ObservableCollection<GitFileRevision> ExplorerFileHistory { get; } = new BatchObservableCollection<GitFileRevision>();

    public ObservableCollection<MultiRestoreFileViewModel> MultiRestoreFiles { get; } = [];

    public ObservableCollection<GitMultiRestoreOperation> MultiRestoreOperations { get; } = [];

    public ObservableCollection<CherryPickCommitViewModel> BranchComparisonCommits { get; } = [];

    public ObservableCollection<GitBranch> BranchCompareSources { get; } = [];

    public ObservableCollection<GitBranch> BranchCompareTargets { get; } = [];

    public ObservableCollection<LfsTrackedFile> LfsFiles { get; } = new BatchObservableCollection<LfsTrackedFile>();

    public ObservableCollection<LfsFileVersion> LfsVersions { get; } = [];
    public ObservableCollection<LfsFileTreeNode> LfsFileTree { get; } = [];

    public ObservableCollection<LfsCleanupItem> LfsCleanupItems { get; } = [];

    public ObservableCollection<RepositoryStorageArea> LfsStorageAreas { get; } = [];

    public ObservableCollection<RepositoryLargeFile> LfsLargestFiles { get; } = [];

    public ObservableCollection<GitIgnoreRule> GitIgnoreRules { get; } = new BatchObservableCollection<GitIgnoreRule>();

    public ObservableCollection<string> IgnoredFiles { get; } = new BatchObservableCollection<string>();

    public IReadOnlyList<IgnoreSuggestionViewModel> IgnoreFolderSuggestions
    {
        get => _ignoreFolderSuggestions;
        private set => SetProperty(ref _ignoreFolderSuggestions, value);
    }

    public IReadOnlyList<IgnoreSuggestionViewModel> IgnoreFileTypeSuggestions
    {
        get => _ignoreFileTypeSuggestions;
        private set => SetProperty(ref _ignoreFileTypeSuggestions, value);
    }

    public IReadOnlyList<IgnoreSuggestionTreeNode> IgnoreFolderTree
    {
        get => _ignoreFolderTree;
        private set => SetProperty(ref _ignoreFolderTree, value);
    }

    public ObservableCollection<PerformanceMetricViewModel> PerformanceMetrics { get; } = new BatchObservableCollection<PerformanceMetricViewModel>();

    public ObservableCollection<SmartSyncPlanItem> SmartSyncPlanItems { get; } = [];

    public ObservableCollection<BackupSnapshotViewModel> Backups { get; } = [];

    public ObservableCollection<GitArchiveCandidate> GitArchiveCandidates { get; } = [];

    public ObservableCollection<GitArchivedBranch> ArchivedGitBranches { get; } = [];

    public ObservableCollection<PeerMemberViewModel> PeerMembers { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> SyncthingDevices { get; } = [];

    public ObservableCollection<SyncthingDifferenceItem> SyncthingDifferences { get; } = [];

    public ObservableCollection<SyncthingLogEntry> SyncthingLogs { get; } = [];

    public ObservableCollection<SyncthingSharedFolder> SharedSyncFolders { get; } = [];

    public ObservableCollection<SyncHistoryEntry> SyncHistory { get; } = [];

    public ObservableCollection<SyncConflictItem> SyncConflicts { get; } = [];

    public ObservableCollection<SyncConflictBackup> SyncConflictBackups { get; } = [];

    public ObservableCollection<SyncCommitManifest> SyncCommits { get; } = [];

    public ObservableCollection<SyncCommitConflictViewModel> SyncCommitConflicts { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> SyncProjectMembers { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> GitProjectMembers { get; } = [];

    public ObservableCollection<ProjectParticipantViewModel> VpnProjectMembers { get; } = [];

    public ObservableCollection<TeamChatMessage> TeamChatMessages { get; } = [];

    public ObservableCollection<TeamChatParticipant> TeamChatParticipants { get; } = [];

    public ObservableCollection<TeamChatChannel> TeamChatChannels { get; } = [];

    public IReadOnlyList<TeamChatTransport> TeamChatTransports { get; } = Enum.GetValues<TeamChatTransport>();

    public ObservableCollection<AdvisoryReservationViewModel> AdvisoryReservations { get; } = [];

    public ObservableCollection<DocumentationTopic> DocumentationTopics { get; } = [];

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    public ObservableCollection<UnrealEngineInstallation> UnrealBuildEngines { get; } = [];

    public ObservableCollection<UnrealBuildTargetDescriptor> UnrealBuildTargets { get; } = [];

    public ObservableCollection<UnrealBuildResult> UnrealBuildResults { get; } = [];

    public ObservableCollection<UnrealBuildProgress> UnrealBuildLogLines { get; } = new BatchObservableCollection<UnrealBuildProgress>();

    public ObservableCollection<UnrealBuildDiagnostic> UnrealBuildDiagnostics { get; } = new BatchObservableCollection<UnrealBuildDiagnostic>();

    public ObservableCollection<UnrealBuildProfile> UnrealBuildPresets { get; } = [];

    public IReadOnlyList<UnrealBuildPlatform> UnrealBuildPlatforms { get; } = Enum.GetValues<UnrealBuildPlatform>();

    public IReadOnlyList<UnrealBuildConfiguration> UnrealBuildConfigurations { get; } =
        Enum.GetValues<UnrealBuildConfiguration>();

    public ObservableCollection<CodeTreeNode> CodeTree { get; } = new BatchObservableCollection<CodeTreeNode>();

    public ObservableCollection<CodeTreeNode> CodeFileList { get; } = new BatchObservableCollection<CodeTreeNode>();

    public IReadOnlyList<CodeFileEntry> CodeFileSearchResults
    {
        get => _codeFileSearchResults;
        private set => SetProperty(ref _codeFileSearchResults, value);
    }

    public ObservableCollection<CodeSearchResult> CodeSearchResults { get; } = new BatchObservableCollection<CodeSearchResult>();

    public IReadOnlyList<CodeSearchResult> FilteredCodeSearchResults
    {
        get => _filteredCodeSearchResults;
        private set
        {
            if (SetProperty(ref _filteredCodeSearchResults, value))
                OnPropertyChanged(nameof(CodeSearchResultFilterSummary));
        }
    }

    public string CodeSearchResultFilterSummary => string.IsNullOrWhiteSpace(CodeSearchFileFilter)
        ? $"{CodeSearchResults.Count:N0} result(s)"
        : $"{FilteredCodeSearchResults.Count:N0} visible / {CodeSearchResults.Count:N0}";

    public IReadOnlyList<CodeFileEntry> AssetExplorerFiles
    {
        get => _assetExplorerFiles;
        private set => SetProperty(ref _assetExplorerFiles, value);
    }

    public ObservableCollection<CodeHistoryEntry> CodeHistory { get; } = [];

    public ObservableCollection<CodeSymbol> CodeSymbols { get; } = [];

    public IReadOnlyList<string> CodeAutoRefreshFrequencies { get; } =
        ["Off", "Low · 5 min", "10 min", "30 min"];

    public ObservableCollection<AiProviderDescriptor> AiProviders { get; } = [];

    public ObservableCollection<AiChatMessageViewModel> AiChatMessages { get; } = [];

    public ObservableCollection<AiMcpServerViewModel> AiMcpServers { get; } = [];

    public ObservableCollection<PullRequestSummary> PullRequests { get; } = new BatchObservableCollection<PullRequestSummary>();

    public ObservableCollection<PullRequestSummary> FilteredPullRequests { get; } = new BatchObservableCollection<PullRequestSummary>();

    public ObservableCollection<PullRequestFile> PullRequestFiles { get; } = new BatchObservableCollection<PullRequestFile>();

    public ObservableCollection<PullRequestReview> PullRequestReviews { get; } = [];

    public ObservableCollection<PullRequestComment> PullRequestComments { get; } = [];

    public ObservableCollection<CiWorkflow> CiWorkflows { get; } = [];

    public ObservableCollection<CiWorkflowRun> CiRuns { get; } = new BatchObservableCollection<CiWorkflowRun>();

    public ObservableCollection<CiWorkflowJob> CiJobs { get; } = [];

    public ObservableCollection<GitRevision> PullRequestCommitRevisions { get; } = [];

    public ObservableCollection<RepositoryCommandHistoryEntry> RepositoryCommandHistory { get; } = [];

    public ObservableCollection<RepositoryCommandHistoryEntry> FilteredRepositoryCommandHistory { get; } = [];

    public ObservableCollection<ApplicationLogEntry> ApplicationLogEntries { get; } = [];

    public ObservableCollection<ApplicationLogEntry> FilteredApplicationLogEntries { get; } = new BatchObservableCollection<ApplicationLogEntry>();

    public ObservableCollection<OperationTaskViewModel> ActiveOperations { get; } = [];

    public ObservableCollection<OperationTaskViewModel> RecentOperations { get; } = [];

    public ObservableCollection<GitHistoricalWorktree> HistoricalWorktrees { get; } = [];

    public IReadOnlyList<string> RepositoryShells { get; } = OperatingSystem.IsWindows()
        ? ["PowerShell", "Command Prompt"]
        : OperatingSystem.IsMacOS() ? ["Zsh", "Bash"] : ["Bash"];

    public IReadOnlyList<string> ApplicationLogLevelFilters { get; } =
        ["All", "Debug", "Information", "Warning", "Error"];

    public IReadOnlyList<PullRequestStateFilter> PullRequestStateFilters { get; } = Enum.GetValues<PullRequestStateFilter>();

    public IReadOnlyList<PullRequestMergeMethod> PullRequestMergeMethods { get; } = Enum.GetValues<PullRequestMergeMethod>();

    public IReadOnlyList<PullRequestReviewAction> PullRequestReviewActions { get; } = Enum.GetValues<PullRequestReviewAction>();

    public IReadOnlyList<string> ChangeSortOptions { get; } = ["Checked", "Name", "State", "Lock", "Area"];

    public IReadOnlyList<string> BranchSortOptions { get; } =
        ["Name", "Status", "Last update", "Last author", "Ahead", "Behind"];

    public IReadOnlyList<string> LfsLockSortOptions { get; } = ["Owner", "File name", "Locked by", "Date", "Source", "ID"];

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

    public bool ShowSyncMemberPanel => SelectedProject?.Definition.Features.PeerSyncEnabled == true;

    public bool ShowGitMemberPanel => SelectedProject?.Definition.Features.GitEnabled == true;

    public bool ShowVpnMemberPanel => _currentVpnProfile is not null || VpnProjectMembers.Count > 0;

    public GridLength SyncMemberPanelWidth => ShowSyncMemberPanel ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public GridLength GitMemberPanelWidth => ShowGitMemberPanel ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public GridLength VpnMemberPanelWidth => ShowVpnMemberPanel ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public GridLength FirstMemberSplitterWidth => ShowSyncMemberPanel && (ShowGitMemberPanel || ShowVpnMemberPanel)
        ? new GridLength(7)
        : new GridLength(0);

    public GridLength SecondMemberSplitterWidth => ShowGitMemberPanel && ShowVpnMemberPanel
        ? new GridLength(7)
        : new GridLength(0);

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

                int loadVersion = Interlocked.Increment(ref _explorerRevisionLoadVersion);
                _ = LoadExplorerRevisionAsync(value, loadVersion);
            }
        }
    }

    public GitRevision? SelectedCommitExplorerRevision
    {
        get => _selectedCommitExplorerRevision;
        set
        {
            if (SetProperty(ref _selectedCommitExplorerRevision, value) && value is not null)
            {
                if (SelectedExplorerRevision?.Hash == value.Hash)
                {
                    int loadVersion = Interlocked.Increment(ref _explorerRevisionLoadVersion);
                    _ = LoadExplorerRevisionAsync(value, loadVersion);
                }
                else
                {
                    SelectedExplorerRevision = value;
                }
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
                int loadVersion = Interlocked.Increment(ref _explorerFileLoadVersion);
                _ = LoadExplorerFileAsync(value, loadVersion);
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
                DebounceUiAction("explorer-filter", () =>
                {
                    ApplyExplorerFilter();
                    ApplyCommitExplorerFilter();
                });
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

    public Bitmap? ExplorerDiffPreviewImage
    {
        get => _explorerDiffPreviewImage;
        private set => ReplaceBitmap(ref _explorerDiffPreviewImage, value, nameof(ExplorerDiffPreviewImage));
    }

    public string ExplorerDiffPresentationSummary
    {
        get => _explorerDiffPresentationSummary;
        private set => SetProperty(ref _explorerDiffPresentationSummary, value);
    }

    public bool IsExplorerLoading
    {
        get => _isExplorerLoading;
        private set
        {
            if (SetProperty(ref _isExplorerLoading, value)) OnPropertyChanged(nameof(IsExplorerAnyLoading));
        }
    }

    public bool IsExplorerDiffLoading
    {
        get => _isExplorerDiffLoading;
        private set
        {
            if (SetProperty(ref _isExplorerDiffLoading, value)) OnPropertyChanged(nameof(IsExplorerAnyLoading));
        }
    }

    public bool IsExplorerAnyLoading => IsExplorerLoading || IsExplorerDiffLoading;

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

    public Bitmap? MultiRestoreDiffPreviewImage
    {
        get => _multiRestoreDiffPreviewImage;
        private set => ReplaceBitmap(ref _multiRestoreDiffPreviewImage, value, nameof(MultiRestoreDiffPreviewImage));
    }

    public string MultiRestoreDiffPresentationSummary
    {
        get => _multiRestoreDiffPresentationSummary;
        private set => SetProperty(ref _multiRestoreDiffPresentationSummary, value);
    }

    public bool IsMultiRestoreDiffLoading
    {
        get => _isMultiRestoreDiffLoading;
        private set => SetProperty(ref _isMultiRestoreDiffLoading, value);
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

    public bool IsCherryPickDiffLoading
    {
        get => _isCherryPickDiffLoading;
        private set => SetProperty(ref _isCherryPickDiffLoading, value);
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

    public LfsFileTreeNode? SelectedLfsFileTreeNode
    {
        get => _selectedLfsFileTreeNode;
        set
        {
            if (!SetProperty(ref _selectedLfsFileTreeNode, value)) return;
            if (value?.File is not null) SelectedLfsFile = value.File;
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

    public string LfsLockSearch
    {
        get => _lfsLockSearch;
        set
        {
            if (SetProperty(ref _lfsLockSearch, value))
            {
                DebounceUiAction("lfs-lock-filter", ApplyLfsLockFilter);
            }
        }
    }

    public string LfsLockSort
    {
        get => _lfsLockSort;
        set
        {
            if (SetProperty(ref _lfsLockSort, value)) RunDebouncedUiActionNow("lfs-lock-filter", ApplyLfsLockFilter);
        }
    }

    public string AllLfsLockSearch
    {
        get => _allLfsLockSearch;
        set
        {
            if (SetProperty(ref _allLfsLockSearch, value)) DebounceUiAction("lfs-lock-filter", ApplyLfsLockFilter);
        }
    }

    public string MyLfsLockSearch
    {
        get => _myLfsLockSearch;
        set
        {
            if (SetProperty(ref _myLfsLockSearch, value)) DebounceUiAction("lfs-lock-filter", ApplyLfsLockFilter);
        }
    }

    public string AllLfsLockSort
    {
        get => _allLfsLockSort;
        set
        {
            if (SetProperty(ref _allLfsLockSort, value)) RunDebouncedUiActionNow("lfs-lock-filter", ApplyLfsLockFilter);
        }
    }

    public string MyLfsLockSort
    {
        get => _myLfsLockSort;
        set
        {
            if (SetProperty(ref _myLfsLockSort, value)) RunDebouncedUiActionNow("lfs-lock-filter", ApplyLfsLockFilter);
        }
    }

    public int SelectedProjectLockCount
    {
        get => _selectedProjectLockCount;
        private set
        {
            if (SetProperty(ref _selectedProjectLockCount, value))
                OnPropertyChanged(nameof(HasSelectedProjectLocks));
        }
    }

    public int SelectedMyLockCount
    {
        get => _selectedMyLockCount;
        private set
        {
            if (SetProperty(ref _selectedMyLockCount, value))
                OnPropertyChanged(nameof(HasSelectedMyLocks));
        }
    }

    public bool HasSelectedProjectLocks => SelectedProjectLockCount > 0;
    public bool HasSelectedMyLocks => SelectedMyLockCount > 0;

    public string LfsLockFilterSummary
    {
        get => _lfsLockFilterSummary;
        private set => SetProperty(ref _lfsLockFilterSummary, value);
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

    public bool LfsTrimRemoteBackedHistory
    {
        get => _lfsTrimRemoteBackedHistory;
        set => SetProperty(ref _lfsTrimRemoteBackedHistory, value);
    }

    public string LfsRecentVersionsPerFile
    {
        get => _lfsRecentVersionsPerFile;
        set => SetProperty(ref _lfsRecentVersionsPerFile, value);
    }

    public string LfsRecentVersionExtensions
    {
        get => _lfsRecentVersionExtensions;
        set => SetProperty(ref _lfsRecentVersionExtensions, value);
    }

    public string LfsRemoteVerificationTimeoutSeconds
    {
        get => _lfsRemoteVerificationTimeoutSeconds;
        set => SetProperty(ref _lfsRemoteVerificationTimeoutSeconds, value);
    }

    public bool IsLfsAnalysisRunning
    {
        get => _isLfsAnalysisRunning;
        private set
        {
            if (SetProperty(ref _isLfsAnalysisRunning, value))
                OnPropertyChanged(nameof(CanStartLfsAnalysis));
        }
    }

    public bool CanStartLfsAnalysis => !IsLfsAnalysisRunning;

    public int LfsAnalysisPercent
    {
        get => _lfsAnalysisPercent;
        private set
        {
            if (SetProperty(ref _lfsAnalysisPercent, value))
                OnPropertyChanged(nameof(LfsAnalysisPercentText));
        }
    }

    public string LfsAnalysisPercentText => $"{LfsAnalysisPercent:N0}%";

    public string LfsAnalysisStage
    {
        get => _lfsAnalysisStage;
        private set => SetProperty(ref _lfsAnalysisStage, value);
    }

    public string LfsRepositorySizeSummary
    {
        get => _lfsRepositorySizeSummary;
        private set => SetProperty(ref _lfsRepositorySizeSummary, value);
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

    public IReadOnlyList<string> GitIgnoreSources { get; } =
        ["Repository .gitignore", "Local exclude (.git/info/exclude)"];

    public IReadOnlyList<string> GitIgnoreTemplates { get; } =
    [
        "Unreal Engine",
        "Unreal plugin binaries",
        "Unreal generated project files",
        "Unity",
        "Godot",
        "JetBrains Rider",
        "Visual Studio",
        ".NET",
        "Node.js",
        "Operating-system files",
        "Build and package outputs",
        "CyRevision cache"
    ];

    public string GitIgnoreSource
    {
        get => _gitIgnoreSource;
        set
        {
            if (SelectedProject is { } project && IsGitIgnoreDirty)
                _gitIgnoreDrafts[BuildGitIgnoreDraftKey(project.Id, _gitIgnoreSource)] = GitIgnoreContent;
            if (!SetProperty(ref _gitIgnoreSource, value)) return;
            if (SelectedProject is not null) _ = LoadGitIgnoreAsync();
        }
    }

    public string GitIgnoreContent
    {
        get => _gitIgnoreContent;
        set
        {
            if (!SetProperty(ref _gitIgnoreContent, value)) return;
            if (_suspendGitIgnoreEditing) return;
            IsGitIgnoreDirty = true;
            DebounceUiAction("gitignore-parse", ParseGitIgnoreEditorContent, 120);
        }
    }

    public string GitIgnoreFilePath
    {
        get => _gitIgnoreFilePath;
        private set => SetProperty(ref _gitIgnoreFilePath, value);
    }

    public string GitIgnoreSummary
    {
        get => _gitIgnoreSummary;
        private set => SetProperty(ref _gitIgnoreSummary, value);
    }

    public string GitIgnoreTestPath
    {
        get => _gitIgnoreTestPath;
        set => SetProperty(ref _gitIgnoreTestPath, value);
    }

    public string GitIgnoreTestResult
    {
        get => _gitIgnoreTestResult;
        private set => SetProperty(ref _gitIgnoreTestResult, value);
    }

    public string GitIgnoreTemplate
    {
        get => _gitIgnoreTemplate;
        set => SetProperty(ref _gitIgnoreTemplate, value);
    }

    public bool IsGitIgnoreDirty
    {
        get => _isGitIgnoreDirty;
        private set
        {
            if (SetProperty(ref _isGitIgnoreDirty, value)) OnPropertyChanged(nameof(CanSaveGitIgnore));
        }
    }

    public bool IsGitIgnoreLoading
    {
        get => _isGitIgnoreLoading;
        private set => SetProperty(ref _isGitIgnoreLoading, value);
    }

    public bool CanSaveGitIgnore => SelectedProject?.Definition.Features.GitEnabled == true && IsGitIgnoreDirty;

    public string IgnoreSuggestionSummary
    {
        get => _ignoreSuggestionSummary;
        private set => SetProperty(ref _ignoreSuggestionSummary, value);
    }

    public bool IsIgnoreSuggestionLoading
    {
        get => _isIgnoreSuggestionLoading;
        private set => SetProperty(ref _isIgnoreSuggestionLoading, value);
    }

    public IReadOnlyList<string> IgnoreSuggestionFilters { get; } =
        ["All", "Selected", "10+ files", "100+ files", "1,000+ files"];

    public string IgnoreFolderSearch
    {
        get => _ignoreFolderSearch;
        set
        {
            if (SetProperty(ref _ignoreFolderSearch, value))
                DebounceUiAction("ignore-folder-filter", ApplyIgnoreSuggestionFilters, 120);
        }
    }

    public string IgnoreFileTypeSearch
    {
        get => _ignoreFileTypeSearch;
        set
        {
            if (SetProperty(ref _ignoreFileTypeSearch, value))
                DebounceUiAction("ignore-type-filter", ApplyIgnoreSuggestionFilters, 120);
        }
    }

    public string IgnoreFolderFilter
    {
        get => _ignoreFolderFilter;
        set
        {
            if (SetProperty(ref _ignoreFolderFilter, value)) ApplyIgnoreSuggestionFilters();
        }
    }

    public string IgnoreFileTypeFilter
    {
        get => _ignoreFileTypeFilter;
        set
        {
            if (SetProperty(ref _ignoreFileTypeFilter, value)) ApplyIgnoreSuggestionFilters();
        }
    }

    public string PerformanceSummary
    {
        get => _performanceSummary;
        private set => SetProperty(ref _performanceSummary, value);
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

    public ObservableCollection<ProjectPreset> Presets { get; } = [.. ProjectPresets.All];

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
            if (ReferenceEquals(_selectedProject, value)) return;
            if (_selectedProject is { } previousProject && _loadedProjectSessions.Contains(previousProject.Id))
                CacheCurrentProjectSession(previousProject);
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            _projectLoadCancellation?.Cancel();
            _projectLoadCancellation?.Dispose();
            _projectLoadCancellation = null;
            _codeWorkspaceCancellation?.Cancel();
            _codePreviewCancellation?.Cancel();
            _automaticGitRefreshCancellation?.Cancel();
            _gitCacheSaveCancellation?.Cancel();
            _branchHistoryCancellation?.Cancel();
            _explorerRevisionCancellation?.Cancel();
            _codexConnectionCancellation?.Cancel();
            CancelDebouncedUiActions();
            _ = ResetCodexChatForProjectAsync(value);
            int loadVersion = Interlocked.Increment(ref _projectLoadVersion);
            CurrentProjectName = value?.Name ?? "No project";
            SidebarGroupDraft = value?.Definition.SidebarGroup ?? string.Empty;
            foreach (ProjectSidebarGroupViewModel group in ProjectSidebarGroups)
                group.SelectedProject = value is not null && group.Projects.Contains(value) ? value : null;
            ApplyProjectPluginScope(value);
            OnPropertyChanged(nameof(SelectedProjectAccentColor));
            NotifyProjectOrderStateChanged();
            OnPropertyChanged(nameof(GitConnectionKind));
            OnPropertyChanged(nameof(IsGitProject));
            OnPropertyChanged(nameof(AreGitActionsEnabled));
            OnPropertyChanged(nameof(IsGitInteractionLocked));
            OnPropertyChanged(nameof(RuntimeModeSummary));
            OnPropertyChanged(nameof(ProjectDiskSummary));
            OnPropertyChanged(nameof(ProjectDiskFreePercent));
            OnPropertyChanged(nameof(ProjectDiskWarning));
            OnPropertyChanged(nameof(ProjectDiskWarningColor));
            OnPropertyChanged(nameof(StartSyncAutomatically));
            OnPropertyChanged(nameof(StartVpnAutomatically));
            NotifySynchronizationModeChanged();
            bool hasCachedSession = value is not null && _projectSessionCache.ContainsKey(value.Id);
            if (!hasCachedSession)
            {
                ClearFastGitViewForProjectSwitch(value);
            }
            bool restoredFromSession = hasCachedSession && RestoreCachedProjectSession(value!);
            if (!restoredFromSession) IncludeRemoteHistory = false;
            RestoreCodeWorkspaceForProject(value);
            RestoreRepositoryConsoleForProject(value);
            OnPropertyChanged(nameof(HasSelectedProject));
            OnPropertyChanged(nameof(CanConnectCodex));
            NotifyAiGenerationStateChanged();
            NotifyMemberPanelLayoutChanged();
            OnPropertyChanged(nameof(RepositoryConsoleWorkingDirectory));
            OnPropertyChanged(nameof(CanRunRepositoryCommand));
            LoadRepositoryConsoleHistory();
            ApplyApplicationLogFilter();
            int licenseLoadVersion = Interlocked.Increment(ref _projectLicenseLoadVersion);
            _ = LoadProjectLicenseAsync(value, licenseLoadVersion);
            _ = LoadGitAnnotationsAsync(value);
            RefreshProjectNotifications();
            OnPropertyChanged(nameof(ProjectNotificationsEnabled));
            OnPropertyChanged(nameof(ProjectLicenseTargetExists));
            OnPropertyChanged(nameof(CanSaveProjectLicense));
            if (value is not null && Directory.Exists(value.RootPath) &&
                Directory.EnumerateFiles(value.RootPath, "*.uproject", SearchOption.TopDirectoryOnly).Any())
            {
                UnrealProjectPath = value.RootPath;
                RefreshUnrealInspection();
            }
            else
            {
                UnrealProjectPath = string.Empty;
                RefreshUnrealInspection();
            }
            DetectSelectedGameEngineProjects(value);
            DetectSelectedLoreProject(value);
            if (!restoredFromSession)
            {
                CancellationTokenSource loadCancellation = new();
                _projectLoadCancellation = loadCancellation;
                _ = LoadSelectedProjectAsync(value, loadVersion, loadCancellation);
            }
        }
    }

    public string SelectedProjectAccentColor =>
        SelectedProject?.AccentColor ?? ProjectItemViewModel.DefaultAccentColor;

    public string SidebarGroupDraft
    {
        get => _sidebarGroupDraft;
        set => SetProperty(ref _sidebarGroupDraft, value);
    }

    public bool IsGitProject => SelectedProject?.Definition.Features.GitEnabled == true;

    public bool AreGitActionsEnabled => IsGitProject && !IsBusy;

    public bool IsGitInteractionLocked => IsGitProject && IsBusy;

    public string GitInteractionLockMessage => IsGitInteractionLocked
        ? "Git actions are temporarily locked while the current operation finishes. Navigation and read-only views remain available."
        : string.Empty;

    public bool StartSyncAutomatically => SelectedProject?.Definition.StartSyncAutomatically == true;

    public bool StartVpnAutomatically => SelectedProject?.Definition.StartVpnAutomatically == true;

    public double ProjectDiskFreePercent
    {
        get
        {
            (long free, long total) = GetSelectedProjectDiskSpace();
            return total <= 0 ? 0 : free * 100d / total;
        }
    }

    public string ProjectDiskSummary
    {
        get
        {
            (long free, long total) = GetSelectedProjectDiskSpace();
            return total <= 0
                ? "Disk space is unavailable for this project."
                : $"{FormatByteSize(free)} free of {FormatByteSize(total)} · {ProjectDiskFreePercent:0.#}% available";
        }
    }

    public string ProjectDiskWarning => ProjectDiskFreePercent switch
    {
        <= 0 => "Disk status unavailable",
        < 5 => "Critical: Git and LFS operations may fail. Clean or relocate data now.",
        < 10 => "Low disk space: inspect Git LFS storage and backups.",
        _ => "Disk space is healthy."
    };

    public string ProjectDiskWarningColor => ProjectDiskFreePercent switch
    {
        < 5 => "#E06C75",
        < 10 => "#E5A94D",
        _ => "#78D7B7"
    };

    public ProjectLicenseTemplate? SelectedProjectLicenseTemplate
    {
        get => _selectedProjectLicenseTemplate;
        set => SetProperty(ref _selectedProjectLicenseTemplate, value);
    }

    public string ProjectLicenseDraft
    {
        get => _projectLicenseDraft;
        set
        {
            if (SetProperty(ref _projectLicenseDraft, value)) OnPropertyChanged(nameof(CanSaveProjectLicense));
        }
    }

    public string ProjectLicenseFileName
    {
        get => _projectLicenseFileName;
        set
        {
            if (!SetProperty(ref _projectLicenseFileName, value)) return;
            OnPropertyChanged(nameof(ProjectLicenseTargetExists));
            OnPropertyChanged(nameof(CanSaveProjectLicense));
        }
    }

    public string ProjectLicenseHolder
    {
        get => _projectLicenseHolder;
        set => SetProperty(ref _projectLicenseHolder, value);
    }

    public string ProjectLicenseYear
    {
        get => _projectLicenseYear;
        set => SetProperty(ref _projectLicenseYear, value);
    }

    public string ProjectLicenseStatus
    {
        get => _projectLicenseStatus;
        private set => SetProperty(ref _projectLicenseStatus, value);
    }

    public string ProjectLicenseDetectedId
    {
        get => _projectLicenseDetectedId;
        private set => SetProperty(ref _projectLicenseDetectedId, value);
    }

    public bool IsProjectLicenseLoading
    {
        get => _isProjectLicenseLoading;
        private set => SetProperty(ref _isProjectLicenseLoading, value);
    }

    public bool ProjectLicenseTargetExists
    {
        get
        {
            try
            {
                return SelectedProject is not null &&
                       File.Exists(Path.Combine(
                           SelectedProject.RootPath,
                           ProjectLicenseService.ValidateFileName(ProjectLicenseFileName)));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public bool CanSaveProjectLicense
    {
        get
        {
            if (SelectedProject is null || string.IsNullOrWhiteSpace(ProjectLicenseDraft)) return false;
            try
            {
                ProjectLicenseService.ValidateFileName(ProjectLicenseFileName);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public bool CanMoveSelectedProjectUp =>
        SelectedProject is not null && Projects.IndexOf(SelectedProject) > 0;

    public bool CanMoveSelectedProjectDown =>
        SelectedProject is not null &&
        Projects.IndexOf(SelectedProject) is int index &&
        index >= 0 && index < Projects.Count - 1;

    public GitChangeViewModel? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (SetProperty(ref _selectedChange, value))
            {
                int diffLoadVersion = Interlocked.Increment(ref _selectedDiffLoadVersion);
                if (IsChangesDiffPreviewEnabled)
                {
                    _ = LoadSelectedDiffAsync(SelectedProject, value, diffLoadVersion);
                }
            }
        }
    }

    public GitChangeTreeNode? SelectedChangeTreeNode
    {
        get => _selectedChangeTreeNode;
        set
        {
            if (SetProperty(ref _selectedChangeTreeNode, value) && value?.Change is not null)
            {
                SelectedChange = value.Change;
            }
        }
    }

    public GitBranch? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                OnPropertyChanged(nameof(CanRemoveSelectedLocalBranch));
                _ = LoadSelectedBranchHistoryAsync(value);
            }
        }
    }

    public IReadOnlyList<GitBranch> SelectedLocalBranches => _selectedBranches
        .Where(branch => !branch.IsRemote && !branch.IsCurrent)
        .DistinctBy(branch => branch.Name, StringComparer.Ordinal)
        .ToArray();

    public int SelectedLocalBranchCount => SelectedLocalBranches.Count;

    public bool CanRemoveSelectedLocalBranch => SelectedLocalBranchCount > 0 ||
                                                SelectedBranch is { IsCurrent: false, IsRemote: false };

    public void SetSelectedBranches(IEnumerable<GitBranch> branches)
    {
        _selectedBranches = branches
            .DistinctBy(branch => (branch.Name, branch.IsRemote))
            .ToArray();
        OnPropertyChanged(nameof(SelectedLocalBranches));
        OnPropertyChanged(nameof(SelectedLocalBranchCount));
        OnPropertyChanged(nameof(CanRemoveSelectedLocalBranch));
    }

    public int LfsReclaimableObjectCount => _lfsCleanupPlan?.ReclaimableCount ?? 0;
    public long LfsReclaimableBytes => _lfsCleanupPlan?.ReclaimableBytes ?? 0;
    public string LfsReclaimableSizeText => FormatByteSize(LfsReclaimableBytes);

    public GitRevision? SelectedBranchRevision
    {
        get => _selectedBranchRevision;
        set => SetProperty(ref _selectedBranchRevision, value);
    }

    public GitHistoricalWorktree? SelectedHistoricalWorktree
    {
        get => _selectedHistoricalWorktree;
        set => SetProperty(ref _selectedHistoricalWorktree, value);
    }

    public bool CreateHistoricalBranchInWorktree
    {
        get => _createHistoricalBranchInWorktree;
        set => SetProperty(ref _createHistoricalBranchInWorktree, value);
    }

    public string HistoricalWorktreeStatus
    {
        get => _historicalWorktreeStatus;
        private set => SetProperty(ref _historicalWorktreeStatus, value);
    }

    public string ChangeSearch
    {
        get => _changeSearch;
        set
        {
            if (SetProperty(ref _changeSearch, value)) DebounceUiAction("change-filter", ApplyChangeFilter);
        }
    }

    public string ChangeSort
    {
        get => _changeSort;
        set
        {
            if (SetProperty(ref _changeSort, value)) RunDebouncedUiActionNow("change-filter", ApplyChangeFilter);
        }
    }

    public string BranchSearch
    {
        get => _branchSearch;
        set
        {
            if (SetProperty(ref _branchSearch, value)) DebounceUiAction("branch-filter", ApplyBranchFilter);
        }
    }

    public string BranchSort
    {
        get => _branchSort;
        set
        {
            if (SetProperty(ref _branchSort, value)) RunDebouncedUiActionNow("branch-filter", ApplyBranchFilter);
        }
    }

    public string SelectedBranchSummary
    {
        get => _selectedBranchSummary;
        private set => SetProperty(ref _selectedBranchSummary, value);
    }

    public GitBranchDetails? SelectedBranchDetails
    {
        get => _selectedBranchDetails;
        private set => SetProperty(ref _selectedBranchDetails, value);
    }

    public bool IsChangesDiffPreviewEnabled
    {
        get => _isChangesDiffPreviewEnabled;
        set
        {
            if (!SetProperty(ref _isChangesDiffPreviewEnabled, value)) return;
            int version = Interlocked.Increment(ref _selectedDiffLoadVersion);
            if (value)
            {
                _ = LoadSelectedDiffAsync(SelectedProject, SelectedChange, version);
            }
        }
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
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyLongOperationStateChanged();
                OnPropertyChanged(nameof(AreGitActionsEnabled));
                OnPropertyChanged(nameof(IsGitInteractionLocked));
                OnPropertyChanged(nameof(GitInteractionLockMessage));
                OnPropertyChanged(nameof(CanCommitPreparedChanges));
                NotifyAiGenerationStateChanged();
            }
        }
    }

    public bool IsProjectLoading
    {
        get => _isProjectLoading;
        private set
        {
            if (SetProperty(ref _isProjectLoading, value))
            {
                NotifyLongOperationStateChanged();
            }
        }
    }

    public double ProjectLoadProgress
    {
        get => _projectLoadProgress;
        private set
        {
            if (SetProperty(ref _projectLoadProgress, value))
            {
                OnPropertyChanged(nameof(LongOperationProgress));
            }
        }
    }

    public string ProjectLoadStage
    {
        get => _projectLoadStage;
        private set
        {
            if (SetProperty(ref _projectLoadStage, value))
            {
                OnPropertyChanged(nameof(LongOperationStage));
            }
        }
    }

    public bool HasActiveOperations => ActiveOperations.Count > 0;

    public bool HasRecentOperations => RecentOperations.Count > 0;

    public bool HasOperationAlerts => RecentOperations.Any(task => task.IsAttention);

    public bool HasActivityCenterContent =>
        !_isActivityCenterDismissed && (IsLongOperationActive || HasOperationAlerts || IsActivityCenterExpanded);

    public bool IsActivityCenterExpanded
    {
        get => _isActivityCenterExpanded;
        set
        {
            if (value && _isActivityCenterDismissed)
            {
                _isActivityCenterDismissed = false;
                OnPropertyChanged(nameof(HasActivityCenterContent));
            }
            if (SetProperty(ref _isActivityCenterExpanded, value))
                OnPropertyChanged(nameof(HasActivityCenterContent));
        }
    }

    public string ActiveOperationCountText => ActiveOperations.Count == 1
        ? "1 task running"
        : $"{ActiveOperations.Count:N0} tasks running";

    public string ActivityCenterBadgeText
    {
        get
        {
            int alerts = RecentOperations.Count(task => task.IsAttention);
            if (ActiveOperations.Count > 0 && alerts > 0) return $"{ActiveOperations.Count:N0} active · {alerts:N0} alert(s)";
            if (ActiveOperations.Count > 0) return ActiveOperationCountText;
            return alerts == 1 ? "1 alert" : $"{alerts:N0} alerts";
        }
    }

    public string OperationHistoryCountText => RecentOperations.Count == 0
        ? "Tasks"
        : $"Tasks {RecentOperations.Count:N0}";

    public string ActivityCenterHeadline => IsLongOperationActive
        ? LongOperationStage
        : RecentOperations.FirstOrDefault(task => task.IsAttention)?.Detail ?? StatusMessage;

    public bool IsLongOperationActive => IsProjectLoading || IsBusy || HasActiveOperations;

    public bool IsLongOperationIndeterminate => IsBusy && !IsProjectLoading;

    public double LongOperationProgress => IsProjectLoading ? ProjectLoadProgress : 0;

    public string LongOperationStage => IsProjectLoading ? ProjectLoadStage : StatusMessage;

    public string RuntimeModeSummary
    {
        get
        {
            if (SelectedProject is null) return "No project selected";
            List<string> modes = [];
            if (ActivePluginOperatingMode is { } pluginMode)
                modes.Add($"{pluginMode.Name} mode");
            if (SelectedProject.Definition.Features.GitEnabled)
                modes.Add(SelectedProject.Definition.Features.StandardGitRemoteEnabled ? "Git remote" : "Git local");
            if (SelectedProject.Definition.Features.PeerSyncEnabled) modes.Add("Sync enabled");
            if (SelectedProject.Definition.Features.BackupEnabled) modes.Add("Backup enabled");
            return modes.Count == 0 ? "Folder only" : string.Join(" · ", modes);
        }
    }

    public bool IsGitPeerSyncMode =>
        SelectedProject?.Definition.Features is { GitEnabled: true, PeerSyncEnabled: true };

    public bool IsVersionedSyncMode =>
        CurrentOperatingMode == ProjectPresetKind.SyncWithVersions;

    public bool IsSyncCommitMode => CurrentOperatingMode == ProjectPresetKind.SyncWithCommits;

    public bool IsPlainProjectSyncMode =>
        SelectedProject?.Definition.Features is { GitEnabled: false, PeerSyncEnabled: true, BackupEnabled: false };

    public bool IsOptionalSyncMode => SelectedProject?.Definition.Features.PeerSyncEnabled != true;

    public bool ShowProjectFolderSyncSettings => IsPlainProjectSyncMode || IsVersionedSyncMode || IsSyncCommitMode;

    public string SynchronizationOverviewTabTitle => IsGitPeerSyncMode
        ? "Git exchange"
        : IsSyncCommitMode
            ? "Commit exchange"
        : IsVersionedSyncMode
            ? "Versioned folder"
            : "Project folder";

    public string SynchronizationProfileTitle => SelectedProject?.Definition.Features switch
    {
        { GitEnabled: true, PeerSyncEnabled: true } => "Git + Sync · signed repository exchange",
        { GitEnabled: false, PeerSyncEnabled: true, BackupEnabled: true } when IsSyncCommitMode => "Sync + Commit · immutable snapshots without Git",
        { GitEnabled: false, PeerSyncEnabled: true, BackupEnabled: true } => "Sync + Versions · continuous project history",
        { GitEnabled: false, PeerSyncEnabled: true } => "Sync · current project state",
        _ => "Optional Sync · independent team folders"
    };

    public string SynchronizationProfileDescription => SelectedProject?.Definition.Features switch
    {
        { GitEnabled: true, PeerSyncEnabled: true } =>
            "CyRevision exchanges signed Git bundles, verified LFS objects and peer presence through an isolated folder. The active .git directory is never synchronized directly.",
        { GitEnabled: false, PeerSyncEnabled: true, BackupEnabled: true } when IsSyncCommitMode =>
            "The working folder stays local. Only complete compressed commits are written to the synchronized exchange, so peers never receive half-written project state.",
        { GitEnabled: false, PeerSyncEnabled: true, BackupEnabled: true } =>
            "The project folder is synchronized with Syncthing versioning, configurable retention and restoration support.",
        { GitEnabled: false, PeerSyncEnabled: true } =>
            "The current project files are synchronized without Git history or automatic version retention.",
        _ =>
            "Project synchronization is disabled. The isolated Syncthing instance can still share selected build, delivery or team folders."
    };

    public string SynchronizationScopeSummary
    {
        get
        {
            if (SelectedProject is null) return "Select a project to configure synchronization.";
            ProjectFeatures features = SelectedProject.Definition.Features;
            if (features.GitEnabled && features.PeerSyncEnabled)
                return $"Protected exchange root: {ResolveSyncExchangeDirectory(SelectedProject.Definition)}";
            if (IsSyncCommitMode)
                return $"Local source: {ResolveConfiguredSyncSourceFolder(SelectedProject.Definition)} · publish only when committing";
            if (features.PeerSyncEnabled && features.BackupEnabled)
                return $"Versioned source: {ResolveConfiguredSyncSourceFolder(SelectedProject.Definition)} · {SelectedProject.Definition.Retention.Mode}";
            if (features.PeerSyncEnabled)
                return $"Synchronized source: {ResolveConfiguredSyncSourceFolder(SelectedProject.Definition)} · current state only";
            return $"{SharedSyncFolders.Count:N0} independent folder(s) configured · project root excluded";
        }
    }

    private void NotifySynchronizationModeChanged()
    {
        OnPropertyChanged(nameof(IsGitProject));
        OnPropertyChanged(nameof(IsGitPeerSyncMode));
        OnPropertyChanged(nameof(IsVersionedSyncMode));
        OnPropertyChanged(nameof(IsSyncCommitMode));
        OnPropertyChanged(nameof(IsPlainProjectSyncMode));
        OnPropertyChanged(nameof(IsOptionalSyncMode));
        OnPropertyChanged(nameof(ShowProjectFolderSyncSettings));
        OnPropertyChanged(nameof(SynchronizationOverviewTabTitle));
        OnPropertyChanged(nameof(SynchronizationProfileTitle));
        OnPropertyChanged(nameof(SynchronizationProfileDescription));
        OnPropertyChanged(nameof(SynchronizationScopeSummary));
        OnPropertyChanged(nameof(CanCreateSyncCommit));
        OnPropertyChanged(nameof(CanAnalyzeSyncCommit));
        OnPropertyChanged(nameof(CanApplySyncCommit));
        NotifyPluginOperatingModeChanged();
    }

    private void NotifyPluginOperatingModeChanged()
    {
        OnPropertyChanged(nameof(HasActivePluginOperatingMode));
        OnPropertyChanged(nameof(ActivePluginOperatingModeName));
        OnPropertyChanged(nameof(ActivePluginOperatingModeSummary));
        OnPropertyChanged(nameof(ActivePluginOperatingModeWorkspaceTabs));
    }

    private void RefreshProjectModeCatalog()
    {
        string? selectedPluginId = SelectedPreset?.ProviderPluginId;
        string? selectedModeId = SelectedPreset?.PluginModeId;
        ProjectPresetKind? selectedBuiltInKind = SelectedPreset is { IsPluginMode: false }
            ? SelectedPreset.Kind
            : null;

        List<ProjectPreset> modes = [.. ProjectPresets.All];
        if (SelectedProject is { } project)
        {
            PluginProjectModeContext context = new(
                project.Id,
                project.Name,
                project.RootPath,
                project.Definition.EnabledPluginIds ?? []);

            foreach (IProjectModeProvider provider in _pluginManager.GetExtensions<IProjectModeProvider>())
            {
                foreach (PluginProjectModeDescriptor pluginMode in provider.ProjectModes)
                {
                    PluginProjectModeAvailability availability;
                    try
                    {
                        availability = provider.EvaluateProjectMode(pluginMode.Id, context);
                    }
                    catch (Exception exception)
                    {
                        availability = new PluginProjectModeAvailability(
                            false,
                            $"Compatibility check failed: {exception.Message}");
                    }

                    modes.Add(ToProjectPreset(provider.Descriptor.Id, pluginMode, availability));
                }
            }
        }

        ReplaceCollection(Presets, modes);

        ProjectDefinition? definition = SelectedProject?.Definition;
        ProjectPreset? restored = null;
        if (definition is not null &&
            !string.IsNullOrWhiteSpace(definition.PluginOperatingModeId) &&
            !string.IsNullOrWhiteSpace(definition.PluginOperatingModeProviderId))
        {
            restored = Presets.FirstOrDefault(preset =>
                string.Equals(preset.PluginModeId, definition.PluginOperatingModeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(preset.ProviderPluginId, definition.PluginOperatingModeProviderId, StringComparison.OrdinalIgnoreCase));
        }

        restored ??= !string.IsNullOrWhiteSpace(selectedModeId) && !string.IsNullOrWhiteSpace(selectedPluginId)
            ? Presets.FirstOrDefault(preset =>
                string.Equals(preset.PluginModeId, selectedModeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(preset.ProviderPluginId, selectedPluginId, StringComparison.OrdinalIgnoreCase))
            : null;
        restored ??= definition?.OperatingMode is { } operatingMode
            ? Presets.FirstOrDefault(preset => !preset.IsPluginMode && preset.Kind == operatingMode)
            : null;
        restored ??= selectedBuiltInKind is { } builtInKind
            ? Presets.FirstOrDefault(preset => !preset.IsPluginMode && preset.Kind == builtInKind)
            : null;
        restored ??= definition is not null
            ? Presets.FirstOrDefault(preset =>
                !preset.IsPluginMode &&
                preset.Features == definition.Features &&
                preset.Retention.Mode == definition.Retention.Mode)
            : null;

        SelectedPreset = restored ?? Presets.FirstOrDefault();
        NotifyPluginOperatingModeChanged();
    }

    private static ProjectPreset ToProjectPreset(
        string providerPluginId,
        PluginProjectModeDescriptor mode,
        PluginProjectModeAvailability availability)
    {
        ProjectFeatures features = new(
            mode.Features.GitEnabled,
            mode.Features.LfsEnabled,
            mode.Features.PeerSyncEnabled,
            mode.Features.BackupEnabled,
            mode.Features.StandardGitRemoteEnabled);
        RetentionMode retentionMode = mode.Retention.Kind switch
        {
            PluginProjectModeRetentionKind.Timeline => RetentionMode.Timeline,
            PluginProjectModeRetentionKind.Permanent => RetentionMode.Permanent,
            _ => RetentionMode.CurrentStateOnly
        };
        RetentionPolicy retention = new(
            retentionMode,
            mode.Retention.MaxVersionsPerFile,
            mode.Retention.MaximumAgeDays is { } days ? TimeSpan.FromDays(days) : null,
            mode.Retention.StorageBudgetBytes);

        return new ProjectPreset(
            ProjectPresetKind.Custom,
            mode.Name,
            mode.Description,
            features,
            retention,
            mode.Id,
            providerPluginId,
            mode.WorkspaceTabIds,
            mode.CategoryLabel,
            availability.IsAvailable,
            availability.Summary);
    }

    private ProjectPresetKind CurrentOperatingMode
    {
        get
        {
            if (SelectedProject?.Definition.OperatingMode is { } explicitMode) return explicitMode;
            ProjectFeatures? features = SelectedProject?.Definition.Features;
            if (features is null) return ProjectPresetKind.Custom;
            if (features.GitEnabled && features.PeerSyncEnabled) return ProjectPresetKind.GitWithPeerSync;
            if (features.GitEnabled) return ProjectPresetKind.GitOnly;
            if (features.PeerSyncEnabled && features.BackupEnabled) return ProjectPresetKind.SyncWithVersions;
            if (features.PeerSyncEnabled) return ProjectPresetKind.SyncOnly;
            if (features.BackupEnabled) return ProjectPresetKind.BackupOnly;
            return ProjectPresetKind.Custom;
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(LongOperationStage));
                OnPropertyChanged(nameof(ActivityCenterHeadline));
            }
        }
    }

    public string RepositoryConsoleCommand
    {
        get => _repositoryConsoleCommand;
        set
        {
            if (SetProperty(ref _repositoryConsoleCommand, value))
                OnPropertyChanged(nameof(CanRunRepositoryCommand));
        }
    }

    public string RepositoryConsoleOutput
    {
        get => _repositoryConsoleOutput;
        private set => SetProperty(ref _repositoryConsoleOutput, value);
    }

    public string RepositoryConsoleStatus
    {
        get => _repositoryConsoleStatus;
        private set => SetProperty(ref _repositoryConsoleStatus, value);
    }

    public string RepositoryConsoleWorkingDirectory =>
        SelectedProject?.RootPath ?? "No project selected";

    public string SelectedRepositoryShell
    {
        get => _selectedRepositoryShell;
        set => SetProperty(ref _selectedRepositoryShell, value);
    }

    public bool IsRepositoryConsoleRunning
    {
        get => _isRepositoryConsoleRunning;
        private set
        {
            if (!SetProperty(ref _isRepositoryConsoleRunning, value)) return;
            OnPropertyChanged(nameof(CanRunRepositoryCommand));
            OnPropertyChanged(nameof(CanStopRepositoryCommand));
        }
    }

    public bool CanRunRepositoryCommand =>
        SelectedProject is not null && !IsRepositoryConsoleRunning && !string.IsNullOrWhiteSpace(RepositoryConsoleCommand);

    public bool CanStopRepositoryCommand => IsRepositoryConsoleRunning;

    public string RepositoryConsoleHistorySearch
    {
        get => _repositoryConsoleHistorySearch;
        set
        {
            if (SetProperty(ref _repositoryConsoleHistorySearch, value))
                DebounceUiAction("console-history-filter", ApplyRepositoryConsoleHistoryFilter);
        }
    }

    public RepositoryCommandHistoryEntry? SelectedRepositoryCommand
    {
        get => _selectedRepositoryCommand;
        set => SetProperty(ref _selectedRepositoryCommand, value);
    }

    public string ApplicationLogSearch
    {
        get => _applicationLogSearch;
        set
        {
            if (SetProperty(ref _applicationLogSearch, value)) DebounceUiAction("application-log-filter", ApplyApplicationLogFilter);
        }
    }

    public string ApplicationLogLevelFilter
    {
        get => _applicationLogLevelFilter;
        set
        {
            if (SetProperty(ref _applicationLogLevelFilter, value))
                RunDebouncedUiActionNow("application-log-filter", ApplyApplicationLogFilter);
        }
    }

    public ApplicationLogEntry? SelectedApplicationLogEntry
    {
        get => _selectedApplicationLogEntry;
        set => SetProperty(ref _selectedApplicationLogEntry, value);
    }

    public string ApplicationLogDirectory => _applicationLogService.LogDirectory;

    public string CurrentApplicationLogPath => _applicationLogService.CurrentLogPath;

    public string ApplicationLogProjectSummary => SelectedProject is null
        ? "Global application events"
        : $"Project log · {SelectedProject.Name}";

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

    public string ChangeSummaryColor
    {
        get => _changeSummaryColor;
        private set => SetProperty(ref _changeSummaryColor, value);
    }

    public int GitConflictCount => Changes.Count(change => change.Change.Kind == GitChangeKind.Conflicted);

    public bool HasGitConflicts => GitConflictCount > 0;

    public string GitConflictActionText => $"Resolve {GitConflictCount:N0} conflict(s)…";

    public string CurrentProjectName
    {
        get => _currentProjectName;
        private set => SetProperty(ref _currentProjectName, value);
    }

    public string HistoryScope => IncludeRemoteHistory ? "Local + remote refs" : "Current branch";

    public bool IsLfsInventoryLoaded
    {
        get => _isLfsInventoryLoaded;
        private set => SetProperty(ref _isLfsInventoryLoaded, value);
    }

    public bool IncludeRemoteHistory
    {
        get => _includeRemoteHistory;
        private set
        {
            if (SetProperty(ref _includeRemoteHistory, value))
            {
                OnPropertyChanged(nameof(HistoryScope));
            }
        }
    }

    public string ChangePreparationSummary
    {
        get => _changePreparationSummary;
        private set => SetProperty(ref _changePreparationSummary, value);
    }

    public int IncludedChangeCount => _includedChangeCount;

    public int KeptChangeCount => _keptChangeCount;

    public int ForeignLockedIncludedCount => _foreignLockedIncludedCount;

    public bool CanCommitPreparedChanges =>
        !IsBusy && IncludedChangeCount > 0 && !string.IsNullOrWhiteSpace(CommitMessage);

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            if (SetProperty(ref _commitMessage, value))
            {
                OnPropertyChanged(nameof(CanCommitPreparedChanges));
            }
        }
    }

    public string DiffText
    {
        get => _diffText;
        private set => SetProperty(ref _diffText, value);
    }

    public Bitmap? DiffPreviewImage
    {
        get => _diffPreviewImage;
        private set => ReplaceBitmap(ref _diffPreviewImage, value, nameof(DiffPreviewImage));
    }

    public string DiffPresentationSummary
    {
        get => _diffPresentationSummary;
        private set => SetProperty(ref _diffPresentationSummary, value);
    }

    public string LfsPattern
    {
        get => _lfsPattern;
        set => SetProperty(ref _lfsPattern, value);
    }

    public string RemoteUrl
    {
        get => _remoteUrl;
        set
        {
            if (SetProperty(ref _remoteUrl, value)) OnPropertyChanged(nameof(GitConnectionKind));
        }
    }

    public string GitConnectionKind => SelectedProject?.Definition.Features.GitEnabled != true
        ? "No Git"
        : string.IsNullOrWhiteSpace(RemoteUrl) ? "Local Git" : "Git + remote";

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

    public IReadOnlyList<BackupArchiveProfile> BackupArchiveProfiles { get; } = BackupArchiveProfile.BuiltIn;

    public BackupArchiveProfile SelectedBackupArchiveProfile
    {
        get => _selectedBackupArchiveProfile;
        set
        {
            if (!SetProperty(ref _selectedBackupArchiveProfile, value)) return;
            ColdArchiveAfterDays = value.ArchiveAfterDays.ToString();
            OnPropertyChanged(nameof(BackupArchiveProfileDescription));
        }
    }

    public string BackupArchiveProfileDescription => SelectedBackupArchiveProfile.Description;

    public bool RemoveArchivedHotCopies
    {
        get => _removeArchivedHotCopies;
        set => SetProperty(ref _removeArchivedHotCopies, value);
    }

    public IReadOnlyList<GitArchiveProfile> GitArchiveProfiles { get; } = GitArchiveProfile.BuiltIn;

    public GitArchiveProfile SelectedGitArchiveProfile
    {
        get => _selectedGitArchiveProfile;
        set
        {
            if (!SetProperty(ref _selectedGitArchiveProfile, value)) return;
            OnPropertyChanged(nameof(GitArchiveProfileDescription));
        }
    }

    public string GitArchiveProfileDescription => SelectedGitArchiveProfile.Description;

    public GitArchiveCandidate? SelectedGitArchiveCandidate
    {
        get => _selectedGitArchiveCandidate;
        set
        {
            if (SetProperty(ref _selectedGitArchiveCandidate, value)) OnPropertyChanged(nameof(CanArchiveSelectedGitBranch));
        }
    }

    public GitArchivedBranch? SelectedArchivedGitBranch
    {
        get => _selectedArchivedGitBranch;
        set
        {
            if (SetProperty(ref _selectedArchivedGitBranch, value)) OnPropertyChanged(nameof(CanRestoreSelectedGitArchive));
        }
    }

    public bool RemoveGitBranchAfterArchive
    {
        get => _removeGitBranchAfterArchive;
        set => SetProperty(ref _removeGitBranchAfterArchive, value);
    }

    public string GitArchiveStatus
    {
        get => _gitArchiveStatus;
        private set => SetProperty(ref _gitArchiveStatus, value);
    }

    public bool CanArchiveSelectedGitBranch => SelectedProject?.Definition.Features.GitEnabled == true && SelectedGitArchiveCandidate is not null;
    public bool CanRestoreSelectedGitArchive => SelectedProject?.Definition.Features.GitEnabled == true && SelectedArchivedGitBranch is not null;

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
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                NotifyPluginOperatingModeChanged();
            }
        }
    }

    public bool HasActivePluginOperatingMode => ActivePluginOperatingMode is not null;

    public string ActivePluginOperatingModeName =>
        ActivePluginOperatingMode?.CategoryLabel ?? ActivePluginOperatingMode?.Name ?? "Plugin mode";

    public string ActivePluginOperatingModeSummary =>
        ActivePluginOperatingMode is { } mode
            ? $"{mode.Name} · supplied by {mode.ProviderPluginId}"
            : "No plugin-owned operating mode is active.";

    public IReadOnlyList<string> ActivePluginOperatingModeWorkspaceTabs =>
        ActivePluginOperatingMode?.WorkspaceTabIds ?? [];

    private ProjectPreset? ActivePluginOperatingMode
    {
        get
        {
            ProjectDefinition? definition = SelectedProject?.Definition;
            if (definition is null ||
                string.IsNullOrWhiteSpace(definition.PluginOperatingModeId) ||
                string.IsNullOrWhiteSpace(definition.PluginOperatingModeProviderId))
            {
                return null;
            }

            return Presets.FirstOrDefault(preset =>
                string.Equals(preset.PluginModeId, definition.PluginOperatingModeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(preset.ProviderPluginId, definition.PluginOperatingModeProviderId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsWorkspaceTabContributedByActivePluginMode(string? workspaceTabId) =>
        !string.IsNullOrWhiteSpace(workspaceTabId) &&
        ActivePluginOperatingModeWorkspaceTabs.Contains(workspaceTabId, StringComparer.OrdinalIgnoreCase);

    public string SyncthingExecutablePath
    {
        get => _syncthingExecutablePath;
        private set => SetProperty(ref _syncthingExecutablePath, value);
    }

    public string SyncthingRuntimeSummary
    {
        get => _syncthingRuntimeSummary;
        private set => SetProperty(ref _syncthingRuntimeSummary, value);
    }

    public IReadOnlyList<SyncthingFolderMode> SyncthingFolderModes { get; } =
        Enum.GetValues<SyncthingFolderMode>();

    public IReadOnlyList<string> SyncthingFolderModeNames { get; } =
        Enum.GetValues<SyncthingFolderMode>().Select(mode => mode.ToDisplayName()).ToArray();

    public SyncthingFolderMode SelectedSyncthingFolderMode
    {
        get => _selectedSyncthingFolderMode;
        set
        {
            if (SetProperty(ref _selectedSyncthingFolderMode, value))
            {
                OnPropertyChanged(nameof(SelectedSyncthingFolderModeName));
            }
        }
    }

    public string SelectedSyncthingFolderModeName
    {
        get => SelectedSyncthingFolderMode.ToDisplayName();
        set
        {
            SyncthingFolderMode mode = SyncthingFolderModeNames
                .Select((name, index) => (name, mode: SyncthingFolderModes[index]))
                .FirstOrDefault(item => string.Equals(item.name, value, StringComparison.OrdinalIgnoreCase)).mode;
            SelectedSyncthingFolderMode = mode;
        }
    }

    public string SyncthingRescanInterval
    {
        get => _syncthingRescanInterval;
        set => SetProperty(ref _syncthingRescanInterval, value);
    }

    public bool SyncthingFileWatcherEnabled
    {
        get => _syncthingFileWatcherEnabled;
        set => SetProperty(ref _syncthingFileWatcherEnabled, value);
    }

    public string SyncthingFolderSummary
    {
        get => _syncthingFolderSummary;
        private set => SetProperty(ref _syncthingFolderSummary, value);
    }

    public string SyncthingIgnoreRules
    {
        get => _syncthingIgnoreRules;
        set => SetProperty(ref _syncthingIgnoreRules, value);
    }

    public string SyncthingIgnoreStatus
    {
        get => _syncthingIgnoreStatus;
        private set => SetProperty(ref _syncthingIgnoreStatus, value);
    }

    public bool IsSyncthingRefreshing
    {
        get => _isSyncthingRefreshing;
        private set => SetProperty(ref _isSyncthingRefreshing, value);
    }

    public string SyncSourceFolderPath
    {
        get => _syncSourceFolderPath;
        set => SetProperty(ref _syncSourceFolderPath, value);
    }

    public string SyncVersionStorePath
    {
        get => _syncVersionStorePath;
        set => SetProperty(ref _syncVersionStorePath, value);
    }

    public string SyncCompressedBackupPath
    {
        get => _syncCompressedBackupPath;
        set => SetProperty(ref _syncCompressedBackupPath, value);
    }

    public bool SyncCompressedBackupEnabled
    {
        get => _syncCompressedBackupEnabled;
        set => SetProperty(ref _syncCompressedBackupEnabled, value);
    }

    public string SyncStorageStatus
    {
        get => _syncStorageStatus;
        private set => SetProperty(ref _syncStorageStatus, value);
    }

    public string SyncHistorySearch
    {
        get => _syncHistorySearch;
        set => SetProperty(ref _syncHistorySearch, value);
    }

    public string SyncHistoryPathFilter
    {
        get => _syncHistoryPathFilter;
        set => SetProperty(ref _syncHistoryPathFilter, value);
    }

    public string SyncHistorySummary
    {
        get => _syncHistorySummary;
        private set => SetProperty(ref _syncHistorySummary, value);
    }

    public SyncConflictItem? SelectedSyncConflict
    {
        get => _selectedSyncConflict;
        set
        {
            if (SetProperty(ref _selectedSyncConflict, value))
            {
                OnPropertyChanged(nameof(CanResolveSelectedSyncConflict));
            }
        }
    }

    public SyncConflictBackup? SelectedSyncConflictBackup
    {
        get => _selectedSyncConflictBackup;
        set
        {
            if (SetProperty(ref _selectedSyncConflictBackup, value))
            {
                OnPropertyChanged(nameof(CanRestoreSelectedSyncConflictBackup));
            }
        }
    }

    public string SyncConflictSearch
    {
        get => _syncConflictSearch;
        set
        {
            if (SetProperty(ref _syncConflictSearch, value)) ApplySyncConflictFilter();
        }
    }

    public string SyncConflictRetentionDays
    {
        get => _syncConflictRetentionDays;
        set => SetProperty(ref _syncConflictRetentionDays, value);
    }

    public string SyncConflictSummary
    {
        get => _syncConflictSummary;
        private set => SetProperty(ref _syncConflictSummary, value);
    }

    public bool IsSyncConflictBusy
    {
        get => _isSyncConflictBusy;
        private set
        {
            if (SetProperty(ref _isSyncConflictBusy, value))
            {
                OnPropertyChanged(nameof(CanResolveSelectedSyncConflict));
                OnPropertyChanged(nameof(CanRestoreSelectedSyncConflictBackup));
            }
        }
    }

    public bool CanResolveSelectedSyncConflict => SelectedSyncConflict is not null && !IsSyncConflictBusy;

    public bool CanRestoreSelectedSyncConflictBackup => SelectedSyncConflictBackup is not null && !IsSyncConflictBusy;

    public string SyncCommitMessage
    {
        get => _syncCommitMessage;
        set
        {
            if (SetProperty(ref _syncCommitMessage, value)) OnPropertyChanged(nameof(CanCreateSyncCommit));
        }
    }

    public string SyncCommitAuthor
    {
        get => _syncCommitAuthor;
        set
        {
            if (SetProperty(ref _syncCommitAuthor, value)) OnPropertyChanged(nameof(CanCreateSyncCommit));
        }
    }

    public string SyncCommitStatus
    {
        get => _syncCommitStatus;
        private set => SetProperty(ref _syncCommitStatus, value);
    }

    public SyncCommitManifest? SelectedSyncCommit
    {
        get => _selectedSyncCommit;
        set
        {
            if (!SetProperty(ref _selectedSyncCommit, value)) return;
            SelectedSyncCommitAnalysis = null;
            OnPropertyChanged(nameof(CanAnalyzeSyncCommit));
        }
    }

    public SyncCommitAnalysis? SelectedSyncCommitAnalysis
    {
        get => _selectedSyncCommitAnalysis;
        private set
        {
            if (!SetProperty(ref _selectedSyncCommitAnalysis, value)) return;
            OnPropertyChanged(nameof(CanApplySyncCommit));
            OnPropertyChanged(nameof(SyncCommitConflictSummary));
        }
    }

    public SyncCommitConflictViewModel? SelectedSyncCommitConflict
    {
        get => _selectedSyncCommitConflict;
        set => SetProperty(ref _selectedSyncCommitConflict, value);
    }

    public bool IsSyncCommitBusy
    {
        get => _isSyncCommitBusy;
        private set
        {
            if (!SetProperty(ref _isSyncCommitBusy, value)) return;
            OnPropertyChanged(nameof(CanCreateSyncCommit));
            OnPropertyChanged(nameof(CanAnalyzeSyncCommit));
            OnPropertyChanged(nameof(CanApplySyncCommit));
        }
    }

    public bool CanCreateSyncCommit => IsSyncCommitMode && !IsSyncCommitBusy &&
                                       !string.IsNullOrWhiteSpace(SyncCommitMessage) &&
                                       !string.IsNullOrWhiteSpace(SyncCommitAuthor);
    public bool CanAnalyzeSyncCommit => IsSyncCommitMode && !IsSyncCommitBusy && SelectedSyncCommit is not null;
    public bool CanApplySyncCommit => CanAnalyzeSyncCommit &&
                                      SyncCommitConflicts.All(item => item.Choice != SyncCommitConflictChoice.Unresolved);
    public string SyncCommitConflictSummary => SelectedSyncCommitAnalysis is null
        ? "Select an incoming commit and analyze it before applying."
        : SelectedSyncCommitAnalysis.Conflicts.Count == 0
            ? $"Safe to apply · {SelectedSyncCommitAnalysis.ChangedFiles:N0} changed · {SelectedSyncCommitAnalysis.DeletedFiles:N0} deleted"
            : $"{SelectedSyncCommitAnalysis.Conflicts.Count:N0} conflict(s) require resolution; the project was not modified.";

    public SyncthingSharedFolder? SelectedSharedSyncFolder
    {
        get => _selectedSharedSyncFolder;
        set
        {
            if (!SetProperty(ref _selectedSharedSyncFolder, value) || value is null) return;
            SharedSyncFolderName = value.Name;
            SharedSyncFolderPath = value.Path;
            SelectedSharedSyncFolderMode = value.Mode;
        }
    }

    public string SharedSyncFolderName
    {
        get => _sharedSyncFolderName;
        set => SetProperty(ref _sharedSyncFolderName, value);
    }

    public string SharedSyncFolderPath
    {
        get => _sharedSyncFolderPath;
        set => SetProperty(ref _sharedSyncFolderPath, value);
    }

    public SyncthingFolderMode SelectedSharedSyncFolderMode
    {
        get => _selectedSharedSyncFolderMode;
        set
        {
            if (SetProperty(ref _selectedSharedSyncFolderMode, value))
                OnPropertyChanged(nameof(SelectedSharedSyncFolderModeName));
        }
    }

    public string SelectedSharedSyncFolderModeName
    {
        get => SelectedSharedSyncFolderMode.ToDisplayName();
        set
        {
            int index = SyncthingFolderModeNames.ToList().FindIndex(name =>
                string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) SelectedSharedSyncFolderMode = SyncthingFolderModes[index];
        }
    }

    public string SharedSyncFolderStatus
    {
        get => _sharedSyncFolderStatus;
        private set => SetProperty(ref _sharedSyncFolderStatus, value);
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

    public string AssetExplorerSearch
    {
        get => _assetExplorerSearch;
        set
        {
            if (SetProperty(ref _assetExplorerSearch, value))
                DebounceUiAction("asset-explorer-search", ApplyAssetExplorerFilter, 180);
        }
    }

    public string AssetExplorerSummary
    {
        get => _assetExplorerSummary;
        private set => SetProperty(ref _assetExplorerSummary, value);
    }

    public bool IsAssetExplorerLoading
    {
        get => _isAssetExplorerLoading;
        private set => SetProperty(ref _isAssetExplorerLoading, value);
    }

    public bool IsAssetExplorerPreviewLoading
    {
        get => _isAssetExplorerPreviewLoading;
        private set => SetProperty(ref _isAssetExplorerPreviewLoading, value);
    }

    public CodeFileEntry? SelectedAssetExplorerFile
    {
        get => _selectedAssetExplorerFile;
        set
        {
            if (!SetProperty(ref _selectedAssetExplorerFile, value)) return;
            int version = Interlocked.Increment(ref _assetExplorerPreviewVersion);
            if (value is null)
            {
                _assetExplorerPreviewCancellation?.Cancel();
                IsAssetExplorerPreviewLoading = false;
                return;
            }

            AssetCandidatePath = value.FullPath;
            _ = PreviewSelectedAssetAsync(value, version);
        }
    }

    public PeerMemberViewModel? SelectedPeerMember
    {
        get => _selectedPeerMember;
        set
        {
            if (!SetProperty(ref _selectedPeerMember, value)) return;
            if (value is not null) SelectedPeerMemberRole = value.Certificate.Role;
            OnPropertyChanged(nameof(CanUpdateSelectedPeerRole));
        }
    }

    public PeerRole SelectedPeerMemberRole
    {
        get => _selectedPeerMemberRole;
        set
        {
            if (SetProperty(ref _selectedPeerMemberRole, value))
                OnPropertyChanged(nameof(CanUpdateSelectedPeerRole));
        }
    }

    public bool CanUpdateSelectedPeerRole =>
        SelectedPeerMember is not null && SelectedPeerMember.Certificate.Role != SelectedPeerMemberRole;

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

    public string VpnInvitationClientName
    {
        get => _vpnInvitationClientName;
        set => SetProperty(ref _vpnInvitationClientName, value);
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

    public TeamChatTransport SelectedTeamChatTransport
    {
        get => _selectedTeamChatTransport;
        set
        {
            if (!SetProperty(ref _selectedTeamChatTransport, value)) return;
            OnPropertyChanged(nameof(TeamChatUsesVpn));
            OnPropertyChanged(nameof(TeamChatUsesSyncFolder));
            OnPropertyChanged(nameof(TeamChatUsesPrivateServer));
        }
    }

    public bool TeamChatUsesVpn => SelectedTeamChatTransport == TeamChatTransport.Vpn;

    public bool TeamChatUsesSyncFolder => SelectedTeamChatTransport == TeamChatTransport.SyncFolder;

    public bool TeamChatUsesPrivateServer => SelectedTeamChatTransport == TeamChatTransport.PrivateServer;

    public string TeamChatDisplayName
    {
        get => _teamChatDisplayName;
        set => SetProperty(ref _teamChatDisplayName, value);
    }

    public string TeamChatListenAddress
    {
        get => _teamChatListenAddress;
        set => SetProperty(ref _teamChatListenAddress, value);
    }

    public string TeamChatPort
    {
        get => _teamChatPort;
        set => SetProperty(ref _teamChatPort, value);
    }

    public string TeamChatPeerEndpoint
    {
        get => _teamChatPeerEndpoint;
        set => SetProperty(ref _teamChatPeerEndpoint, value);
    }

    public string TeamChatAccessToken
    {
        get => _teamChatAccessToken;
        set => SetProperty(ref _teamChatAccessToken, value);
    }

    public string TeamChatSyncFolderPath
    {
        get => _teamChatSyncFolderPath;
        set => SetProperty(ref _teamChatSyncFolderPath, value);
    }

    public string TeamChatServerBaseUrl
    {
        get => _teamChatServerBaseUrl;
        set => SetProperty(ref _teamChatServerBaseUrl, value);
    }

    public string TeamChatServerApiToken
    {
        get => _teamChatServerApiToken;
        set => SetProperty(ref _teamChatServerApiToken, value);
    }

    public bool TeamChatAllowPrivateServerHttp
    {
        get => _teamChatAllowPrivateServerHttp;
        set => SetProperty(ref _teamChatAllowPrivateServerHttp, value);
    }

    public string TeamChatNewChannelName
    {
        get => _teamChatNewChannelName;
        set => SetProperty(ref _teamChatNewChannelName, value);
    }

    public string TeamChatNewChannelTopic
    {
        get => _teamChatNewChannelTopic;
        set => SetProperty(ref _teamChatNewChannelTopic, value);
    }

    public string TeamChatMessageText
    {
        get => _teamChatMessageText;
        set => SetProperty(ref _teamChatMessageText, value);
    }

    public string TeamChatAttachmentPath
    {
        get => _teamChatAttachmentPath;
        set => SetProperty(ref _teamChatAttachmentPath, value);
    }

    public bool TeamChatSaveConversations
    {
        get => _teamChatSaveConversations;
        set => SetProperty(ref _teamChatSaveConversations, value);
    }

    public bool TeamChatEncryptStoredConversations
    {
        get => _teamChatEncryptStoredConversations;
        set => SetProperty(ref _teamChatEncryptStoredConversations, value);
    }

    public string TeamChatRetentionDays
    {
        get => _teamChatRetentionDays;
        set => SetProperty(ref _teamChatRetentionDays, value);
    }

    public string TeamChatMaxAttachmentMb
    {
        get => _teamChatMaxAttachmentMb;
        set => SetProperty(ref _teamChatMaxAttachmentMb, value);
    }

    public TeamChatMessage? SelectedTeamChatMessage
    {
        get => _selectedTeamChatMessage;
        set => SetProperty(ref _selectedTeamChatMessage, value);
    }

    public TeamChatChannel? SelectedTeamChatChannel
    {
        get => _selectedTeamChatChannel;
        set
        {
            if (!SetProperty(ref _selectedTeamChatChannel, value)) return;
            RefreshVisibleTeamChatMessages();
            OnPropertyChanged(nameof(SelectedTeamChatChannelTitle));
            OnPropertyChanged(nameof(SelectedTeamChatChannelTopic));
        }
    }

    public string SelectedTeamChatChannelTitle => $"# {SelectedTeamChatChannel?.Name ?? "general"}";

    public string SelectedTeamChatChannelTopic => SelectedTeamChatChannel?.Topic ?? "Project-wide discussion";

    public string TeamChatStatus
    {
        get => _teamChatStatus;
        private set => SetProperty(ref _teamChatStatus, value);
    }

    public bool IsTeamChatHostRunning
    {
        get => _isTeamChatHostRunning;
        private set => SetProperty(ref _isTeamChatHostRunning, value);
    }

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
                OnPropertyChanged(nameof(IsSelectedUnrealPlugin));
                OnPropertyChanged(nameof(IsSelectedAiPlugin));
                OnPropertyChanged(nameof(IsSelectedUnityPlugin));
                OnPropertyChanged(nameof(IsSelectedGodotPlugin));
                OnPropertyChanged(nameof(IsSelectedLorePlugin));
                OnPropertyChanged(nameof(IsSelectedPerforcePlugin));
            }
        }
    }

    public bool CanEnableSelectedPlugin => SelectedProject is not null && SelectedPlugin is { IsEnabled: false };

    public bool CanDisableSelectedPlugin => SelectedProject is not null && SelectedPlugin is { IsEnabled: true };

    public bool IsSelectedUnrealPlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.unreal", StringComparison.OrdinalIgnoreCase);

    public bool IsSelectedAiPlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.ai", StringComparison.OrdinalIgnoreCase);

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

    public bool UnrealAdvancedAssetInspectionEnabled
    {
        get => _unrealAdvancedAssetInspectionEnabled;
        set => SetProperty(ref _unrealAdvancedAssetInspectionEnabled, value);
    }

    public bool UnrealRenderMeshThumbnails
    {
        get => _unrealRenderMeshThumbnails;
        set => SetProperty(ref _unrealRenderMeshThumbnails, value);
    }

    public string UnrealAssetPreviewResolution
    {
        get => _unrealAssetPreviewResolution;
        set => SetProperty(ref _unrealAssetPreviewResolution, value);
    }

    public string UnrealAssetCacheBudgetGigabytes
    {
        get => _unrealAssetCacheBudgetGigabytes;
        set => SetProperty(ref _unrealAssetCacheBudgetGigabytes, value);
    }

    public string UnrealAssetInspectionSummary
    {
        get => _unrealAssetInspectionSummary;
        private set => SetProperty(ref _unrealAssetInspectionSummary, value);
    }

    public string UnrealEditorPluginVersion => _unrealProjectInspection?.BundledPluginVersion ?? "—";

    public string UnrealInstalledPluginVersion => _unrealProjectInspection?.InstalledPluginVersion ?? "Not installed";

    public string UnrealEngineVersion => _unrealProjectInspection?.EngineVersion is { Length: > 0 } version
        ? $"Unreal Engine {version}"
        : "Unreal Engine not resolved";

    public string UnrealProjectKindSummary => _unrealProjectInspection?.ProjectKind switch
    {
        UnrealProjectKind.Cpp => "C++ project",
        UnrealProjectKind.BlueprintOnly => "Blueprint-only project",
        _ => "Project type unknown"
    };

    public string UnrealInstallModeSummary => _unrealProjectInspection?.InstallMode switch
    {
        UnrealPluginInstallMode.Source => "Source plugin",
        UnrealPluginInstallMode.Precompiled => "Exact precompiled plugin",
        _ => "No safe installation mode"
    };

    public string UnrealCompatibilityStatus => _unrealProjectInspection?.CompatibilityStatus
                                                ?? "Select an Unreal project to evaluate compatibility.";

    public string UnrealSupportedVersions => $"Supported engines: {string.Join(", ",
        _unrealProjectInspection?.SupportedEngineVersions ?? UnrealPluginCompatibility.SupportedEngineVersions)}";

    public string UnrealPrecompiledVersions
    {
        get
        {
            string platform = _unrealProjectInspection?.PrecompiledPlatform
                              ?? (OperatingSystem.IsWindows() ? "Win64" :
                                  OperatingSystem.IsMacOS() ? "Mac" :
                                  OperatingSystem.IsLinux() ? "Linux" : "Unknown");
            IReadOnlyList<string> versions = _unrealProjectInspection?.AvailablePrecompiledVersions ?? [];
            return versions.Count > 0
                ? $"Blueprint-only binaries ({platform}): {string.Join(", ", versions)}"
                : $"Blueprint-only binaries ({platform}): none bundled";
        }
    }

    public bool IsUnrealPluginCompatible => _unrealProjectInspection?.IsCompatible == true;

    public bool CanInstallUnrealEditorPlugin =>
        IsUnrealIntegrationEnabled && _unrealProjectInspection is { IsValid: true, IsCompatible: true };

    public bool CanConfigureUnrealAssetInspection =>
        IsUnrealIntegrationEnabled && _unrealProjectInspection is { IsValid: true, IsCompatible: true };

    public UnrealEngineInstallation? SelectedUnrealBuildEngine
    {
        get => _selectedUnrealBuildEngine;
        set
        {
            if (!SetProperty(ref _selectedUnrealBuildEngine, value) || value is null) return;
            if (UnrealBuildAutoConfigureToolchains)
            {
                UnrealLinuxToolchainPath = value.DetectedLinuxToolchainPath ?? string.Empty;
                UnrealAndroidSdkPath = value.DetectedAndroidSdkPath ?? string.Empty;
            }
            OnPropertyChanged(nameof(CanRunUnrealBuild));
        }
    }

    public UnrealEngineInstallation? UnrealBuildRangeFrom
    {
        get => _unrealBuildRangeFrom;
        set => SetProperty(ref _unrealBuildRangeFrom, value);
    }

    public UnrealEngineInstallation? UnrealBuildRangeTo
    {
        get => _unrealBuildRangeTo;
        set => SetProperty(ref _unrealBuildRangeTo, value);
    }

    public UnrealBuildTargetDescriptor? SelectedUnrealBuildTarget
    {
        get => _selectedUnrealBuildTarget;
        set
        {
            if (SetProperty(ref _selectedUnrealBuildTarget, value))
                OnPropertyChanged(nameof(CanRunUnrealBuild));
        }
    }

    public UnrealBuildPlatform SelectedUnrealBuildPlatform
    {
        get => _selectedUnrealBuildPlatform;
        set => SetProperty(ref _selectedUnrealBuildPlatform, value);
    }

    public UnrealBuildConfiguration SelectedUnrealBuildConfiguration
    {
        get => _selectedUnrealBuildConfiguration;
        set => SetProperty(ref _selectedUnrealBuildConfiguration, value);
    }

    public string UnrealLinuxToolchainPath
    {
        get => _unrealLinuxToolchainPath;
        set => SetProperty(ref _unrealLinuxToolchainPath, value);
    }

    public string UnrealAndroidSdkPath
    {
        get => _unrealAndroidSdkPath;
        set => SetProperty(ref _unrealAndroidSdkPath, value);
    }

    public string UnrealAndroidNdkPath
    {
        get => _unrealAndroidNdkPath;
        set => SetProperty(ref _unrealAndroidNdkPath, value);
    }

    public string UnrealJavaHomePath
    {
        get => _unrealJavaHomePath;
        set => SetProperty(ref _unrealJavaHomePath, value);
    }

    public string UnrealBuildOutputPath
    {
        get => _unrealBuildOutputPath;
        set => SetProperty(ref _unrealBuildOutputPath, value);
    }

    public string UnrealBuildTimeoutMinutes
    {
        get => _unrealBuildTimeoutMinutes;
        set => SetProperty(ref _unrealBuildTimeoutMinutes, value);
    }

    public string UnrealBuildPresetName
    {
        get => _unrealBuildPresetName;
        set => SetProperty(ref _unrealBuildPresetName, value);
    }

    public string UnrealBuildMaximumParallel
    {
        get => _unrealBuildMaximumParallel;
        set => SetProperty(ref _unrealBuildMaximumParallel, value);
    }

    public UnrealBuildProfile? SelectedUnrealBuildPreset
    {
        get => _selectedUnrealBuildPreset;
        set => SetProperty(ref _selectedUnrealBuildPreset, value);
    }

    public UnrealBuildResult? SelectedUnrealBuildResult
    {
        get => _selectedUnrealBuildResult;
        set
        {
            if (SetProperty(ref _selectedUnrealBuildResult, value))
                ReplaceCollection(UnrealBuildDiagnostics, value?.Diagnostics ?? []);
        }
    }

    public bool UnrealBuildCookAndPackage
    {
        get => _unrealBuildCookAndPackage;
        set => SetProperty(ref _unrealBuildCookAndPackage, value);
    }

    public bool UnrealBuildAutoConfigureToolchains
    {
        get => _unrealBuildAutoConfigureToolchains;
        set => SetProperty(ref _unrealBuildAutoConfigureToolchains, value);
    }

    public bool IsUnrealBuildRunning
    {
        get => _isUnrealBuildRunning;
        private set
        {
            if (SetProperty(ref _isUnrealBuildRunning, value))
                OnPropertyChanged(nameof(CanRunUnrealBuild));
        }
    }

    public bool CanRunUnrealBuild => IsUnrealIntegrationEnabled && !IsUnrealBuildRunning &&
                                      SelectedUnrealBuildEngine is not null && SelectedUnrealBuildTarget is not null;

    public string UnrealBuildStatus
    {
        get => _unrealBuildStatus;
        private set => SetProperty(ref _unrealBuildStatus, value);
    }

    public string UnrealBuildLog
    {
        get => _unrealBuildLog;
        private set => SetProperty(ref _unrealBuildLog, value);
    }

    public CodeTreeNode? SelectedCodeNode
    {
        get => _selectedCodeNode;
        set
        {
            if (SetProperty(ref _selectedCodeNode, value))
            {
                CodeAiSummary = string.Empty;
                OnPropertyChanged(nameof(CanGenerateAiCodeSummary));
                _ = LoadCodeNodeAsync(value);
            }
        }
    }

    public CodeFileEntry? SelectedCodeFileSearchResult
    {
        get => _selectedCodeFileSearchResult;
        set
        {
            bool changed = SetProperty(ref _selectedCodeFileSearchResult, value);
            if (value is not null && (changed ||
                !string.Equals(SelectedCodeNode?.FullPath, value.FullPath, StringComparison.OrdinalIgnoreCase)))
                SelectedCodeNode = value.ToTreeNode();
        }
    }

    public bool HasCodeFileSearchResults => !string.IsNullOrWhiteSpace(CodeTreeFilter);

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
        set
        {
            if (SetProperty(ref _codeTreeFilter, value))
            {
                OnPropertyChanged(nameof(HasCodeFileSearchResults));
                DebounceUiAction("code-tree-filter", ApplyCodeWorkspaceFilter, 260);
            }
        }
    }

    public bool CodeIncludeHidden
    {
        get => _codeIncludeHidden;
        set
        {
            if (!SetProperty(ref _codeIncludeHidden, value)) return;
            if (SelectedProject is not null && _codeWorkspaceCache.ContainsKey(SelectedProject.Id))
                _ = RefreshCodeWorkspaceAsync();
        }
    }

    public string CodeAutoRefreshFrequency
    {
        get => _codeAutoRefreshFrequency;
        set => SetProperty(ref _codeAutoRefreshFrequency, value);
    }

    public string CodeWorkspaceLastUpdatedText => _codeWorkspaceLastUpdated is null
        ? "Not indexed yet"
        : $"Updated {_codeWorkspaceLastUpdated.Value.ToLocalTime():t}";

    public bool HasLoadedCodeWorkspace =>
        SelectedProject is not null && _codeWorkspaceCache.ContainsKey(SelectedProject.Id);

    public string CodeWorkspaceSummary
    {
        get => _codeWorkspaceSummary;
        private set => SetProperty(ref _codeWorkspaceSummary, value);
    }

    public bool IsCodeWorkspaceLoading
    {
        get => _isCodeWorkspaceLoading;
        private set => SetProperty(ref _isCodeWorkspaceLoading, value);
    }

    public bool IsCodeFileSearchRunning
    {
        get => _isCodeFileSearchRunning;
        private set => SetProperty(ref _isCodeFileSearchRunning, value);
    }

    public string CodeSearchQuery
    {
        get => _codeSearchQuery;
        set => SetProperty(ref _codeSearchQuery, value);
    }

    public string CodeSearchFileFilter
    {
        get => _codeSearchFileFilter;
        set
        {
            if (SetProperty(ref _codeSearchFileFilter, value))
                DebounceUiAction("code-result-file-filter", ApplyCodeSearchResultFilter, 120);
        }
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

    public bool IsCodeSearchRunning
    {
        get => _isCodeSearchRunning;
        private set => SetProperty(ref _isCodeSearchRunning, value);
    }

    public string CodePreviewText
    {
        get => _codePreviewText;
        private set
        {
            if (!SetProperty(ref _codePreviewText, value)) return;
            OnPropertyChanged(nameof(CanGenerateAiCodeSummary));
        }
    }

    public Bitmap? CodePreviewImage
    {
        get => _codePreviewImage;
        private set => ReplaceBitmap(ref _codePreviewImage, value, nameof(CodePreviewImage));
    }

    public bool CodePreviewIsImage
    {
        get => _codePreviewIsImage;
        private set
        {
            if (!SetProperty(ref _codePreviewIsImage, value)) return;
            OnPropertyChanged(nameof(CodePreviewIsText));
            OnPropertyChanged(nameof(CanGenerateAiCodeSummary));
        }
    }

    public bool CodePreviewIsText => !CodePreviewIsImage;

    public string CodePreviewPath
    {
        get => _codePreviewPath;
        private set => SetProperty(ref _codePreviewPath, value);
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

    public string CodeAiSummary
    {
        get => _codeAiSummary;
        private set
        {
            if (!SetProperty(ref _codeAiSummary, value)) return;
            OnPropertyChanged(nameof(HasCodeAiSummary));
        }
    }

    public bool HasCodeAiSummary => !string.IsNullOrWhiteSpace(CodeAiSummary);

    public bool IsAiIntegrationEnabled
    {
        get => _isAiIntegrationEnabled;
        private set
        {
            if (!SetProperty(ref _isAiIntegrationEnabled, value)) return;
            OnPropertyChanged(nameof(CanConnectCodex));
        }
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
        set
        {
            if (!SetProperty(ref _aiPrompt, value)) return;
            OnPropertyChanged(nameof(CanSendAiChat));
        }
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

    public bool IsCodexDetected
    {
        get => _isCodexDetected;
        private set
        {
            if (!SetProperty(ref _isCodexDetected, value)) return;
            OnPropertyChanged(nameof(CanConnectCodex));
        }
    }

    public bool IsCodexRunning
    {
        get => _isCodexRunning;
        private set => SetProperty(ref _isCodexRunning, value);
    }

    public bool IsCodexChatConnected
    {
        get => _isCodexChatConnected;
        private set
        {
            if (!SetProperty(ref _isCodexChatConnected, value)) return;
            OnPropertyChanged(nameof(CanConnectCodex));
            OnPropertyChanged(nameof(CanSendAiChat));
            OnPropertyChanged(nameof(CodexConnectButtonText));
            NotifyAiGenerationStateChanged();
        }
    }

    public bool IsCodexChatBusy
    {
        get => _isCodexChatBusy;
        private set
        {
            if (!SetProperty(ref _isCodexChatBusy, value)) return;
            OnPropertyChanged(nameof(CanConnectCodex));
            OnPropertyChanged(nameof(CanSendAiChat));
            NotifyAiGenerationStateChanged();
        }
    }

    public string CodexConnectionStatus
    {
        get => _codexConnectionStatus;
        private set => SetProperty(ref _codexConnectionStatus, value);
    }

    public string CodexDetectedVersion
    {
        get => _codexDetectedVersion;
        private set => SetProperty(ref _codexDetectedVersion, value);
    }

    public string CodexDetectedPath
    {
        get => _codexDetectedPath;
        private set => SetProperty(ref _codexDetectedPath, value);
    }

    public string CodexChatThreadId
    {
        get => _codexChatThreadId;
        private set => SetProperty(ref _codexChatThreadId, value);
    }

    public bool CanConnectCodex =>
        IsAiIntegrationEnabled && IsCodexDetected && SelectedProject is not null && !IsCodexChatBusy;

    public bool CanSendAiChat =>
        IsCodexChatConnected && !IsCodexChatBusy && !string.IsNullOrWhiteSpace(AiPrompt);

    public bool CanGenerateAiCommitDescription =>
        !IsBusy && IsCodexChatConnected && !IsCodexChatBusy && IncludedChangeCount > 0;

    public bool CanGenerateAiPullRequestDraft =>
        IsCodexChatConnected && !IsCodexChatBusy && SelectedProject?.Definition.Features.GitEnabled == true;

    public bool CanGenerateAiCodeSummary =>
        IsCodexChatConnected && !IsCodexChatBusy &&
        SelectedCodeNode is { IsDirectory: false, IsPlaceholder: false } &&
        CodePreviewIsText && _codePreviewSupportsAiSummary && !string.IsNullOrWhiteSpace(CodePreviewText);

    public string CodexConnectButtonText => IsCodexChatConnected ? "Disconnect" : "Connect";

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
                if (IsPullRequestDiffPreviewEnabled)
                {
                    UpdatePullRequestPatch(value);
                }
            }
        }
    }

    public string PullRequestSearch
    {
        get => _pullRequestSearch;
        set
        {
            if (SetProperty(ref _pullRequestSearch, value)) DebounceUiAction("pull-request-filter", ApplyPullRequestFilter);
        }
    }

    public bool IsPullRequestDiffPreviewEnabled
    {
        get => _isPullRequestDiffPreviewEnabled;
        set
        {
            if (!SetProperty(ref _isPullRequestDiffPreviewEnabled, value)) return;
            PullRequestPatch = value ? BuildPullRequestPatch(SelectedPullRequestFile) : "Diff preview hidden.";
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

    public bool IsPullRequestLoading
    {
        get => _isPullRequestLoading;
        private set => SetProperty(ref _isPullRequestLoading, value);
    }

    public bool IsWorkingTreeDiffLoading
    {
        get => _isWorkingTreeDiffLoading;
        private set => SetProperty(ref _isWorkingTreeDiffLoading, value);
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
        : !string.IsNullOrWhiteSpace(ResolveEnvironmentPullRequestToken())
            ? $"Using environment variable {PullRequestTokenEnvironmentVariable.Trim()}"
            : !string.IsNullOrWhiteSpace(_pullRequestCredentialToken)
                ? "Using Git Credential Manager · session only"
                : "No authentication · private repositories require Git credentials or a session token";

    public string PullRequestRepositoryName => _pullRequestRepository?.FullName ?? "No supported remote detected";

    public bool HasSelectedPullRequest => SelectedPullRequest is not null;

    public bool CanMergeSelectedPullRequest =>
        SelectedPullRequest is { IsMerged: false } pull && pull.State.Equals("open", StringComparison.OrdinalIgnoreCase);

    public string PullRequestDetailsSummary => _selectedPullRequestDetails?.ChangeSummary ?? "Select a pull request.";

    public string PullRequestBody => string.IsNullOrWhiteSpace(_selectedPullRequestDetails?.Body)
        ? "No description provided."
        : _selectedPullRequestDetails.Body;

    public CiWorkflow? SelectedCiWorkflow
    {
        get => _selectedCiWorkflow;
        set => SetProperty(ref _selectedCiWorkflow, value);
    }

    public CiWorkflowRun? SelectedCiRun
    {
        get => _selectedCiRun;
        set
        {
            if (SetProperty(ref _selectedCiRun, value))
                _ = LoadSelectedCiRunAsync(value);
        }
    }

    public string CiStatus
    {
        get => _ciStatus;
        private set => SetProperty(ref _ciStatus, value);
    }

    public string CiRunDetails
    {
        get => _ciRunDetails;
        private set => SetProperty(ref _ciRunDetails, value);
    }

    public string CiGitRef
    {
        get => _ciGitRef;
        set => SetProperty(ref _ciGitRef, value);
    }

    public string CiReleaseVersion
    {
        get => _ciReleaseVersion;
        set => SetProperty(ref _ciReleaseVersion, value);
    }

    public bool IsCiLoading
    {
        get => _isCiLoading;
        private set => SetProperty(ref _isCiLoading, value);
    }

    public bool IsUnrealProjectDetected => _unrealProjectInspection?.IsValid == true;

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
        _legacyProjectPluginIds = _pluginManager.LoadedPluginIds
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        RefreshPluginCatalog();
        await RunOperationAsync("Chargement des projets…", async () =>
        {
            GitToolAvailability availability = await _gitService.GetToolAvailabilityAsync();
            ToolStatus = availability.GitAvailable
                ? $"{availability.GitVersion} · {(availability.LfsAvailable ? availability.LfsVersion : "Git LFS absent")}"
                : "Git est introuvable";

            IReadOnlyList<ProjectDefinition> definitions = await _projectCatalog.GetAllAsync();
            Projects.Clear();
            IEnumerable<ProjectDefinition> orderedDefinitions = definitions.Any(project => project.SidebarOrder is not null)
                ? definitions.OrderBy(project => project.SidebarOrder ?? int.MaxValue)
                : definitions.OrderByDescending(project => project.LastOpenedAt);
            foreach (ProjectDefinition definition in orderedDefinitions)
            {
                if (Projects.Any(project => ProjectPathsEqual(project.RootPath, definition.RootPath)))
                {
                    continue;
                }
                Projects.Add(new ProjectItemViewModel(definition));
            }

            await PersistProjectOrderAsync();

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

    public async Task EnsureCodeWorkspaceLoadedAsync(bool force = false)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;
        if (!force && _codeWorkspaceCache.TryGetValue(project.Id, out CachedCodeWorkspace? cached))
        {
            ApplyCodeWorkspaceSnapshot(cached);
            return;
        }

        await RefreshCodeWorkspaceAsync();
    }

    public async Task EnsureWorkspaceDataLoadedAsync(string workspaceName)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || string.IsNullOrWhiteSpace(workspaceName)) return;
        using WorkspaceLoadCoordinator.WorkspaceLoadLease? load =
            _workspaceLoadCoordinator.TryBegin(project.Id, workspaceName);
        if (load is null) return;

        OperationTaskViewModel task = BeginTrackedTask(
            $"Load {workspaceName}",
            project.Name,
            "Loading this tool on demand");
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            switch (workspaceName)
            {
                case "BranchesWorkspaceTab":
                    await RefreshHistoricalWorktreesAsync();
                    break;
                case "LfsLocksWorkspaceTab":
                    await LoadLfsLocksCoreAsync(expectedProjectId: project.Id);
                    break;
                case "GitIgnoreWorkspaceTab":
                    await LoadGitIgnoreAsync();
                    break;
                case "GitLfsWorkspaceTab":
                    await LoadLfsManagementCoreAsync();
                    break;
                case "BackupsWorkspaceTab":
                    await LoadBackupsCoreAsync();
                    break;
                case "SynchronizationWorkspaceTab":
                    await LoadSyncProfileCoreAsync();
                    break;
                case "VpnWorkspaceTab":
                case "SwarmWorkspaceTab":
                case "VpnFilesWorkspaceTab":
                    await LoadVpnProfileCoreAsync();
                    break;
                case "RemoteBuildsWorkspaceTab":
                    await LoadRemoteBuildCoreAsync();
                    break;
                case "DiscordWorkspaceTab":
                    await LoadDiscordProfileCoreAsync();
                    break;
                case "WorkInProgressWorkspaceTab":
                    await LoadAdvisoryReservationsCoreAsync();
                    break;
                case "AssetDiffWorkspaceTab":
                    await EnsureAssetExplorerLoadedAsync();
                    break;
                case "AiWorkspaceTab":
                    await LoadAiMcpProfileCoreAsync();
                    await DetectCodexAsync(autoConnect: true);
                    break;
                case "McpWorkspaceTab":
                    await LoadAiMcpProfileCoreAsync();
                    break;
                case "PullRequestsWorkspaceTab":
                    await ResolvePullRequestRepositoryAsync();
                    break;
                case "CiWorkspaceTab":
                    await RefreshCiAsync();
                    break;
                case "UnrealWorkspaceTab":
                    RefreshUnrealInspection();
                    break;
                case "LoreWorkspaceTab":
                    RefreshLoreInspection();
                    if (IsLoreIntegrationEnabled) await DetectLoreCliAsync();
                    break;
                case "UnrealBuildWorkspaceTab":
                    RefreshUnrealInspection();
                    if (_unrealBuildDiscovery is null && IsUnrealProjectDetected)
                        await DiscoverUnrealBuildEnvironmentAsync();
                    break;
                case "MembersWorkspaceTab":
                    await RefreshProjectMembersCoreAsync(testConnections: false);
                    break;
                case "TeamChatWorkspaceTab":
                    await LoadTeamChatAsync();
                    break;
                case "DiagnosticsWorkspaceTab":
                    RefreshPerformanceDiagnostics();
                    break;
                default:
                    CompleteTrackedTask(task, "Skipped", "This workspace has no deferred data");
                    return;
            }

            if (SelectedProject?.Id != project.Id)
            {
                CompleteTrackedTask(task, "Cancelled", "The selected project changed");
                return;
            }
            load.Complete();
            CompleteTrackedTask(task, "Completed", $"Ready in {stopwatch.Elapsed.TotalMilliseconds:N0} ms");
            RecordPerformanceMetric("Workspace", workspaceName, stopwatch.Elapsed, project.Name);
            _applicationLogService.Debug(
                "workspace",
                $"lazy load complete tab={workspaceName} duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms",
                project.RootPath);
        }
        catch (Exception exception)
        {
            CompleteTrackedTask(task, "Failed", exception.Message);
            _applicationLogService.Warning(
                "workspace",
                $"lazy load failed tab={workspaceName}: {exception.Message}",
                project.RootPath);
        }
        finally
        {
            if (ActiveOperations.Contains(task)) CompleteTrackedTask(task, "Finished", task.Detail);
        }
    }

    public bool IsCodeWorkspaceRefreshDue(TimeSpan interval)
    {
        if (SelectedProject is null) return false;
        return !_codeWorkspaceCache.TryGetValue(SelectedProject.Id, out CachedCodeWorkspace? cached) ||
               DateTimeOffset.UtcNow - cached.UpdatedAt >= interval;
    }

    public async Task RefreshCodeWorkspaceAsync(bool preserveLoadedTree = true)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null)
        {
            ClearCodeWorkspace();
            return;
        }

        _codeWorkspaceCancellation?.Cancel();
        _codeWorkspaceCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _codeWorkspaceCancellation = cancellation;
        OperationTaskViewModel trackedTask = BeginTrackedTask(
            $"Index {project.Name} solution",
            project.Name,
            "Loading top-level folders without blocking the interface");
        IsCodeWorkspaceLoading = true;
        StatusMessage = $"Indexing {project.Name} code workspace…";
        CodeWorkspaceSummary = "Indexing workspace…";
        Stopwatch stopwatch = Stopwatch.StartNew();
        _applicationLogService.Information("solution", "index started (lazy mode)", project.RootPath);
        try
        {
            CodeWorkspaceSnapshot snapshot = await _codeWorkspaceService.BuildTreeAsync(
                project.RootPath,
                string.Empty,
                CodeIncludeHidden,
                cancellation.Token);
            if (preserveLoadedTree &&
                _codeWorkspaceCache.TryGetValue(project.Id, out CachedCodeWorkspace? previousWorkspace) &&
                previousWorkspace.IncludeHidden == CodeIncludeHidden)
            {
                snapshot = snapshot with
                {
                    Roots = PreserveExistingCodeRoots(previousWorkspace.Snapshot.Roots, snapshot.Roots)
                };
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id)
            {
                StoreCodeWorkspaceCache(project.Id, new CachedCodeWorkspace(
                    snapshot,
                    DateTimeOffset.UtcNow,
                    CodeIncludeHidden));
                CompleteTrackedTask(trackedTask, "Completed", $"Cached for {project.Name}");
                return;
            }
            CachedCodeWorkspace cached = new(snapshot, DateTimeOffset.UtcNow, CodeIncludeHidden);
            StoreCodeWorkspaceCache(project.Id, cached);
            ApplyCodeWorkspaceSnapshot(cached);
            StatusMessage = $"{project.Name} code index ready";
            CompleteTrackedTask(trackedTask, "Completed", $"Top level ready in {stopwatch.Elapsed.TotalMilliseconds:N0} ms");
            _applicationLogService.Information(
                "solution",
                $"index ready duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms roots={snapshot.Roots.Count:N0} lazy=true",
                project.RootPath);
        }
        catch (OperationCanceledException)
        {
            CompleteTrackedTask(trackedTask, "Cancelled", "Superseded by a newer request");
            _applicationLogService.Warning("solution", "index cancelled", project.RootPath);
            if (SelectedProject?.Id == project.Id)
            {
                CodeWorkspaceSummary = "Code indexing cancelled.";
            }
        }
        catch (Exception exception)
        {
            CompleteTrackedTask(trackedTask, "Failed", exception.Message);
            _applicationLogService.Error("solution", "index failed", exception, project.RootPath);
            if (SelectedProject?.Id == project.Id)
            {
                CodeWorkspaceSummary = exception.Message;
                StatusMessage = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_codeWorkspaceCancellation, cancellation))
            {
                _codeWorkspaceCancellation = null;
                cancellation.Dispose();
                IsCodeWorkspaceLoading = false;
            }
        }
    }

    private void RestoreCodeWorkspaceForProject(ProjectItemViewModel? project)
    {
        if (project is not null && _codeWorkspaceCache.TryGetValue(project.Id, out CachedCodeWorkspace? cached))
        {
            TouchCacheEntry(_codeWorkspaceCacheUsage, project.Id);
            SelectedCodeNode = null;
            CodePreviewText = string.Empty;
            CodePreviewPath = string.Empty;
            CodePreviewImage = null;
            CodePreviewIsImage = false;
            SetCodePreviewSupportsAiSummary(false);
            CodePreviewSummary = "Select a file to preview it.";
            ApplyCodeWorkspaceSnapshot(cached);
            return;
        }

        CodeTree.Clear();
        CodeFileList.Clear();
        CodeFileSearchResults = [];
        AssetExplorerFiles = [];
        SelectedCodeNode = null;
        SelectedCodeFileSearchResult = null;
        SelectedAssetExplorerFile = null;
        CodePreviewText = string.Empty;
        CodePreviewPath = string.Empty;
        CodePreviewImage = null;
        CodePreviewIsImage = false;
        SetCodePreviewSupportsAiSummary(false);
        CodePreviewSummary = "Select a file to preview it.";
        _codeWorkspaceLastUpdated = null;
        CodeWorkspaceSummary = project is null
            ? "Select a project to explore its code."
            : "Open Solution Explorer to index project files.";
        OnPropertyChanged(nameof(CodeWorkspaceLastUpdatedText));
        OnPropertyChanged(nameof(HasLoadedCodeWorkspace));
    }

    private void ApplyCodeWorkspaceSnapshot(CachedCodeWorkspace cached)
    {
        _codeWorkspaceLastUpdated = cached.UpdatedAt;
        CodeWorkspaceSummary = $"{cached.Snapshot.Roots.Count:N0} top-level item(s) · lazy explorer · " +
                               $"ready in {cached.Snapshot.Elapsed.TotalMilliseconds:N0} ms" +
                               (cached.Snapshot.WasTruncated ? " · directory view limited" : string.Empty);
        ApplyCodeWorkspaceFilter();
        if (SelectedCodeNode is not null &&
            CodeFileList.All(node => node.RelativePath != SelectedCodeNode.RelativePath))
        {
            SelectedCodeNode = null;
        }
        OnPropertyChanged(nameof(CodeWorkspaceLastUpdatedText));
        OnPropertyChanged(nameof(HasLoadedCodeWorkspace));
    }

    private void StoreCodeWorkspaceCache(Guid projectId, CachedCodeWorkspace cached)
    {
        _codeWorkspaceCache[projectId] = cached;
        TouchCacheEntry(_codeWorkspaceCacheUsage, projectId);
        while (_codeWorkspaceCacheUsage.Count > 5)
        {
            Guid oldest = _codeWorkspaceCacheUsage.First!.Value;
            _codeWorkspaceCacheUsage.RemoveFirst();
            _codeWorkspaceCache.Remove(oldest);
        }
    }

    private static IReadOnlyList<CodeTreeNode> PreserveExistingCodeRoots(
        IReadOnlyList<CodeTreeNode> existing,
        IReadOnlyList<CodeTreeNode> refreshed)
    {
        Dictionary<string, CodeTreeNode> existingByPath = existing.ToDictionary(
            node => node.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        return refreshed
            .Select(node => existingByPath.TryGetValue(node.RelativePath, out CodeTreeNode? previous) &&
                            previous.IsDirectory == node.IsDirectory
                ? previous
                : node)
            .ToArray();
    }

    private void ApplyCodeWorkspaceFilter() => _ = ApplyCodeWorkspaceFilterAsync();

    private async Task ApplyCodeWorkspaceFilterAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null ||
            !_codeWorkspaceCache.TryGetValue(project.Id, out CachedCodeWorkspace? cached))
            return;

        string filter = CodeTreeFilter.Trim();
        _codeFileFilterCancellation?.Cancel();
        _codeFileFilterCancellation?.Dispose();
        _codeFileFilterCancellation = null;
        if (filter.Length == 0)
        {
            IsCodeFileSearchRunning = false;
            CodeFileSearchResults = [];
            SelectedCodeFileSearchResult = null;
            IReadOnlyList<CodeTreeNode> roots = cached.Snapshot.Roots;
            if (CodeTree.Count != roots.Count ||
                CodeTree.Where((node, index) => !ReferenceEquals(node, roots[index])).Any())
            {
                ReplaceCollection(CodeTree, roots);
            }
            ReplaceCollection(CodeFileList, FlattenCodeFiles(roots));
            CodeWorkspaceSummary = $"{cached.Snapshot.Roots.Count:N0} top-level item(s) · lazy explorer · " +
                                   $"ready in {cached.Snapshot.Elapsed.TotalMilliseconds:N0} ms";
            return;
        }

        CancellationTokenSource cancellation = new();
        _codeFileFilterCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        IsCodeFileSearchRunning = true;
        CodeWorkspaceSummary = $"Searching every project file for ‘{filter}’…";
        try
        {
            CodeFileIndex index = cached.FileIndex ?? await _codeWorkspaceService.BuildFileIndexAsync(
                project.RootPath,
                CodeIncludeHidden,
                token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id || !string.Equals(CodeTreeFilter.Trim(), filter, StringComparison.Ordinal))
                return;
            if (cached.FileIndex is null)
            {
                cached = cached with { FileIndex = index };
                _codeWorkspaceCache[project.Id] = cached;
            }

            CodeFileEntry[] matchingEntries = await Task.Run(() => index.Files
                .Where(file => CodeFilePatternMatcher.IsMatch(file.RelativePath, filter))
                .ToArray(), token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id || !string.Equals(CodeTreeFilter.Trim(), filter, StringComparison.Ordinal))
                return;
            SelectedCodeFileSearchResult = null;
            CodeFileSearchResults = matchingEntries;
            CodeWorkspaceSummary = $"{matchingEntries.Length:N0} file(s) contain ‘{filter}’ in their name or path · " +
                                   $"{index.Files.Count:N0} indexed in {index.Elapsed.TotalMilliseconds:N0} ms";
            if (SelectedCodeNode is not null && matchingEntries.All(file =>
                    !string.Equals(file.RelativePath, SelectedCodeNode.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedCodeNode = null;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer expression superseded this recursive file search.
        }
        catch (Exception exception)
        {
            if (SelectedProject?.Id == project.Id) CodeWorkspaceSummary = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_codeFileFilterCancellation, cancellation))
            {
                _codeFileFilterCancellation = null;
                cancellation.Dispose();
                IsCodeFileSearchRunning = false;
            }
        }
    }

    private static IReadOnlyList<CodeTreeNode> FlattenCodeFiles(IEnumerable<CodeTreeNode> roots)
    {
        const int maximumVisibleFiles = 5_000;
        List<CodeTreeNode> files = [];
        Stack<CodeTreeNode> pending = new(roots.Reverse());
        while (pending.Count > 0 && files.Count < maximumVisibleFiles)
        {
            CodeTreeNode node = pending.Pop();
            if (node.IsPlaceholder) continue;
            if (!node.IsDirectory)
            {
                files.Add(node);
                continue;
            }
            for (int index = node.Children.Count - 1; index >= 0; index--) pending.Push(node.Children[index]);
        }
        return files;
    }

    private void ApplyCodeSearchResultFilter()
    {
        string[] terms = CodeSearchFileFilter
            .Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        CodeSearchResult[] filtered = terms.Length == 0
            ? CodeSearchResults.ToArray()
            : CodeSearchResults.Where(result => terms.All(term =>
                    result.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    result.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    result.DirectoryPath.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        FilteredCodeSearchResults = filtered;
        if (SelectedCodeSearchResult is null || !filtered.Contains(SelectedCodeSearchResult))
            SelectedCodeSearchResult = filtered.FirstOrDefault();
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
        CancellationTokenSource cancellation = new();
        _codeSearchCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        IsCodeSearchRunning = true;
        CodeSearchResults.Clear();
        FilteredCodeSearchResults = [];
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
            ApplyCodeSearchResultFilter();
            CodeSearchSummary = $"{report.Results.Count:N0} result(s) in {report.FilesScanned:N0} file(s) · " +
                                $"{report.Elapsed.TotalMilliseconds:N0} ms · " +
                                (report.UsedRipgrep ? "ripgrep engine" : "managed fallback") +
                                (report.WasTruncated ? " · result limit reached" : string.Empty);
            SelectedCodeSearchResult = FilteredCodeSearchResults.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            CodeSearchSummary = "Search cancelled.";
        }
        catch (Exception exception)
        {
            CodeSearchSummary = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_codeSearchCancellation, cancellation))
            {
                _codeSearchCancellation = null;
                cancellation.Dispose();
                IsCodeSearchRunning = false;
            }
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
        PullRequestStatus = $"{repository.Provider} repository detected · loading remote pull requests…";
        OnPropertyChanged(nameof(PullRequestRepositoryName));
        OnPropertyChanged(nameof(PullRequestAuthenticationState));
        Guid projectId = SelectedProject.Id;
        _ = RefreshPullRequestsInBackgroundAsync(projectId);
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

    public async Task RefreshCiAsync()
    {
        if (_pullRequestRepository is null) await ResolvePullRequestRepositoryAsync();
        PullRequestRepository? repository = _pullRequestRepository;
        if (repository is null)
        {
            CiStatus = "No supported GitHub remote was detected.";
            return;
        }

        IsCiLoading = true;
        CiStatus = $"Loading workflows and runs from {repository.FullName}…";
        try
        {
            string? token = await ResolvePullRequestTokenAsync(repository);
            Task<IReadOnlyList<CiWorkflow>> workflowsTask = _ciWorkflowService.ListWorkflowsAsync(repository, token);
            Task<IReadOnlyList<CiWorkflowRun>> runsTask = _ciWorkflowService.ListRunsAsync(repository, token);
            await Task.WhenAll(workflowsTask, runsTask);
            CiWorkflow[] workflows = (await workflowsTask).ToArray();
            CiWorkflowRun[] runs = (await runsTask).ToArray();
            long? selectedWorkflowId = SelectedCiWorkflow?.Id;
            long? selectedRunId = SelectedCiRun?.Id;
            ReplaceCollection(CiWorkflows, workflows);
            ReplaceCollection(CiRuns, runs);
            SelectedCiWorkflow = CiWorkflows.FirstOrDefault(item => item.Id == selectedWorkflowId)
                                 ?? CiWorkflows.FirstOrDefault(item => item.Name.Contains("release", StringComparison.OrdinalIgnoreCase))
                                 ?? CiWorkflows.FirstOrDefault();
            SelectedCiRun = CiRuns.FirstOrDefault(item => item.Id == selectedRunId) ?? CiRuns.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(CiGitRef)) CiGitRef = CurrentBranch;
            CiStatus = $"{workflows.Length} workflow(s) · {runs.Length} recent run(s) · {repository.FullName}";
            _applicationLogService.Information("ci", $"refreshed workflows={workflows.Length} runs={runs.Length}", SelectedProject?.RootPath);
        }
        catch (Exception exception)
        {
            CiStatus = exception.Message;
            _applicationLogService.Error("ci", "refresh failed", exception, SelectedProject?.RootPath);
        }
        finally
        {
            IsCiLoading = false;
        }
    }

    public Task DispatchSelectedCiWorkflowAsync() => DispatchCiWorkflowAsync(release: false);

    public Task DispatchReleaseCiWorkflowAsync() => DispatchCiWorkflowAsync(release: true);

    private async Task DispatchCiWorkflowAsync(bool release)
    {
        if (_pullRequestRepository is null) await ResolvePullRequestRepositoryAsync();
        PullRequestRepository? repository = _pullRequestRepository;
        CiWorkflow? workflow = release
            ? CiWorkflows.FirstOrDefault(item => item.Name.Contains("release", StringComparison.OrdinalIgnoreCase)) ?? SelectedCiWorkflow
            : SelectedCiWorkflow;
        if (repository is null || workflow is null)
        {
            CiStatus = "Select a workflow after refreshing CI data.";
            return;
        }

        string? token = await GetPullRequestWriteTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            CiStatus = "A session token or Git Credential Manager identity is required to start CI.";
            return;
        }

        string gitRef = string.IsNullOrWhiteSpace(CiGitRef) ? CurrentBranch : CiGitRef.Trim();
        Dictionary<string, string> inputs = [];
        if (release)
        {
            string version = CiReleaseVersion.Trim().TrimStart('v');
            if (version.Length == 0)
            {
                CiStatus = "Enter a release version before starting the release workflow.";
                return;
            }
            inputs["version"] = version;
        }

        IsCiLoading = true;
        CiStatus = $"Starting {workflow.Name} on {gitRef}…";
        try
        {
            await _ciWorkflowService.DispatchAsync(repository, workflow, gitRef, inputs, token);
            CiStatus = $"{workflow.Name} was queued on {gitRef}. Refresh runs in a few seconds.";
            _applicationLogService.Information("ci", $"workflow dispatched name=\"{workflow.Name}\" ref=\"{gitRef}\" release={release}", SelectedProject?.RootPath);
            await Task.Delay(1200);
            await RefreshCiAsync();
        }
        catch (Exception exception)
        {
            CiStatus = exception.Message;
            _applicationLogService.Error("ci", $"dispatch failed name=\"{workflow.Name}\"", exception, SelectedProject?.RootPath);
        }
        finally
        {
            IsCiLoading = false;
        }
    }

    public async Task RerunFailedCiJobsAsync()
    {
        if (_pullRequestRepository is null || SelectedCiRun is null) return;
        string? token = await GetPullRequestWriteTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        IsCiLoading = true;
        try
        {
            await _ciWorkflowService.RerunFailedJobsAsync(_pullRequestRepository, SelectedCiRun.Id, token);
            CiStatus = $"Failed jobs for run #{SelectedCiRun.Id} were queued again.";
        }
        catch (Exception exception) { CiStatus = exception.Message; }
        finally { IsCiLoading = false; }
    }

    public async Task CancelSelectedCiRunAsync()
    {
        if (_pullRequestRepository is null || SelectedCiRun is null) return;
        string? token = await GetPullRequestWriteTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        IsCiLoading = true;
        try
        {
            await _ciWorkflowService.CancelRunAsync(_pullRequestRepository, SelectedCiRun.Id, token);
            CiStatus = $"Cancellation requested for run #{SelectedCiRun.Id}.";
        }
        catch (Exception exception) { CiStatus = exception.Message; }
        finally { IsCiLoading = false; }
    }

    private async Task LoadSelectedCiRunAsync(CiWorkflowRun? run)
    {
        int version = Interlocked.Increment(ref _ciRunLoadVersion);
        CiJobs.Clear();
        if (_pullRequestRepository is null || run is null)
        {
            CiRunDetails = "Select a workflow run to inspect jobs and failed steps.";
            return;
        }

        CiRunDetails = $"Loading jobs for {run.Name}…";
        try
        {
            string? token = await ResolvePullRequestTokenAsync(_pullRequestRepository);
            CiWorkflowRunDetails details = await _ciWorkflowService.GetRunDetailsAsync(_pullRequestRepository, run, token);
            if (version != _ciRunLoadVersion || SelectedCiRun?.Id != run.Id) return;
            ReplaceCollection(CiJobs, details.Jobs);
            CiRunDetails = details.ErrorReport;
        }
        catch (Exception exception)
        {
            if (version == _ciRunLoadVersion) CiRunDetails = exception.Message;
        }
    }

    public async Task CreatePullRequestAsync()
    {
        if (_pullRequestRepository is null) await ResolvePullRequestRepositoryAsync();
        if (_pullRequestRepository is null) return;
        string? token = await GetPullRequestWriteTokenAsync();
        if (token is null) return;
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
        string? token = await GetPullRequestWriteTokenAsync();
        if (token is null) return;
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
        string? token = await GetPullRequestWriteTokenAsync();
        if (token is null) return;
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
        string? token = await GetPullRequestWriteTokenAsync();
        if (token is null) return;
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
        string? token = await GetPullRequestWriteTokenAsync();
        if (token is null) return;
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
        BeginPullRequestLoading();
        int? previousNumber = selectNumber ?? SelectedPullRequest?.Number;
        PullRequestStatus = $"Loading pull requests from {_pullRequestRepository.FullName}…";
        try
        {
            string? token = await ResolvePullRequestTokenAsync(_pullRequestRepository);
            IReadOnlyList<PullRequestSummary> pulls = await _pullRequestService.ListAsync(
                _pullRequestRepository,
                PullRequestStateFilter,
                token);
            ReplaceCollection(PullRequests, pulls);
            ApplyPullRequestFilter();
            PullRequestStatus = $"{pulls.Count} pull request(s) · {_pullRequestRepository.FullName} · {PullRequestStateFilter}";
            SelectedPullRequest = previousNumber is int number
                ? FilteredPullRequests.FirstOrDefault(pull => pull.Number == number) ?? FilteredPullRequests.FirstOrDefault()
                : FilteredPullRequests.FirstOrDefault();
        }
        finally
        {
            EndPullRequestLoading();
        }
    }

    private async Task RefreshPullRequestsInBackgroundAsync(Guid projectId)
    {
        PullRequestRepository? repository = _pullRequestRepository;
        if (repository is null) return;
        BeginPullRequestLoading();
        try
        {
            string? token = await ResolvePullRequestTokenAsync(repository);
            IReadOnlyList<PullRequestSummary> pulls = await _pullRequestService.ListAsync(
                repository,
                PullRequestStateFilter,
                token);
            if (SelectedProject?.Id != projectId || _pullRequestRepository != repository) return;
            ReplaceCollection(PullRequests, pulls);
            ApplyPullRequestFilter();
            PullRequestStatus = $"{pulls.Count} pull request(s) · {repository.FullName} · {PullRequestStateFilter}";
            SelectedPullRequest = FilteredPullRequests.FirstOrDefault();
        }
        catch (Exception exception)
        {
            if (SelectedProject?.Id == projectId)
                PullRequestStatus = "Remote pull requests unavailable: " + exception.Message;
        }
        finally
        {
            EndPullRequestLoading();
        }
    }

    private void ApplyPullRequestFilter()
    {
        string[] terms = PullRequestSearch.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Matches(PullRequestSummary pull) => terms.Length == 0 || terms.All(term =>
            pull.NumberText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            pull.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            pull.StateText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            pull.Author.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            pull.HeadBranch.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            pull.BaseBranch.Contains(term, StringComparison.OrdinalIgnoreCase));
        ReplaceCollection(
            FilteredPullRequests,
            PullRequests.Where(Matches).OrderByDescending(pull => pull.UpdatedAt));
    }

    private void UpdatePullRequestPatch(PullRequestFile? file) => PullRequestPatch = BuildPullRequestPatch(file);

    private static string BuildPullRequestPatch(PullRequestFile? file) => file is null
        ? "Select a changed file to inspect its patch."
        : string.IsNullOrWhiteSpace(file.Patch)
            ? "The provider did not supply a text patch. The file may be binary or too large."
            : file.Patch;

    private async Task LoadSelectedPullRequestAsync(PullRequestSummary? pullRequest)
    {
        int loadVersion = Interlocked.Increment(ref _pullRequestDetailsLoadVersion);
        if (_pullRequestRepository is null || pullRequest is null)
        {
            ClearPullRequestDetails();
            return;
        }

        PullRequestStatus = $"Loading pull request #{pullRequest.Number}…";
        BeginPullRequestLoading();
        try
        {
            string? token = await ResolvePullRequestTokenAsync(_pullRequestRepository);
            PullRequestDetails details = await _pullRequestService.GetDetailsAsync(
                _pullRequestRepository,
                pullRequest.Number,
                token);
            if (loadVersion != _pullRequestDetailsLoadVersion || SelectedPullRequest?.Number != pullRequest.Number) return;
            _selectedPullRequestDetails = details;
            ReplaceCollection(PullRequestCommitRevisions, details.Commits.Select(commit => new GitRevision(
                commit.Hash,
                commit.Hash[..Math.Min(8, commit.Hash.Length)],
                commit.AuthorName,
                commit.AuthorEmail,
                commit.AuthoredAt,
                commit.Subject)));
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
        finally
        {
            EndPullRequestLoading();
        }
    }

    private void BeginPullRequestLoading()
    {
        Interlocked.Increment(ref _pullRequestLoadCount);
        IsPullRequestLoading = true;
    }

    private void EndPullRequestLoading()
    {
        int remaining = Interlocked.Decrement(ref _pullRequestLoadCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _pullRequestLoadCount, 0);
            IsPullRequestLoading = false;
        }
    }

    private string? ResolvePullRequestToken()
    {
        if (!string.IsNullOrWhiteSpace(PullRequestToken)) return PullRequestToken.Trim();
        string? environmentToken = ResolveEnvironmentPullRequestToken();
        return !string.IsNullOrWhiteSpace(environmentToken)
            ? environmentToken
            : _pullRequestCredentialToken;
    }

    private string? ResolveEnvironmentPullRequestToken()
    {
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

    private async Task<string?> ResolvePullRequestTokenAsync(PullRequestRepository repository)
    {
        string? token = ResolvePullRequestToken();
        if (!string.IsNullOrWhiteSpace(token)) return token;

        _pullRequestCredentialToken = await TryReadGitCredentialTokenAsync(repository);
        OnPropertyChanged(nameof(PullRequestAuthenticationState));
        return _pullRequestCredentialToken;
    }

    private async Task<string?> GetPullRequestWriteTokenAsync()
    {
        string? token = _pullRequestRepository is null
            ? ResolvePullRequestToken()
            : await ResolvePullRequestTokenAsync(_pullRequestRepository);
        if (!string.IsNullOrWhiteSpace(token)) return token;

        PullRequestStatus = "Authentication is required. Sign in through Git Credential Manager or provide a session token.";
        StatusMessage = PullRequestStatus;
        return null;
    }

    private static async Task<string?> TryReadGitCredentialTokenAsync(PullRequestRepository repository)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("credential");
            startInfo.ArgumentList.Add("fill");
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

            using Process process = new() { StartInfo = startInfo };
            if (!process.Start()) return null;
            await process.StandardInput.WriteLineAsync("protocol=https");
            await process.StandardInput.WriteLineAsync($"host={repository.Host}");
            await process.StandardInput.WriteLineAsync($"path={repository.Owner}/{repository.Name}.git");
            await process.StandardInput.WriteLineAsync();
            process.StandardInput.Close();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return null;

            foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("password=", StringComparison.Ordinal))
                {
                    string credential = line["password=".Length..].Trim();
                    return credential.Length == 0 ? null : credential;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Git or its credential helper is unavailable. The session-token field remains the fallback.
        }

        return null;
    }

    private void ClearPullRequestData(bool clearConnection = true)
    {
        Interlocked.Increment(ref _pullRequestDetailsLoadVersion);
        PullRequests.Clear();
        FilteredPullRequests.Clear();
        ClearPullRequestDetails();
        SelectedPullRequest = null;
        if (clearConnection)
        {
            Interlocked.Increment(ref _ciRunLoadVersion);
            CiWorkflows.Clear();
            CiRuns.Clear();
            CiJobs.Clear();
            SelectedCiWorkflow = null;
            SelectedCiRun = null;
            CiStatus = "Select a GitHub-backed project to inspect CI workflows.";
            CiRunDetails = "Select a workflow run to inspect jobs and failed steps.";
            _pullRequestRepository = null;
            _pullRequestCredentialToken = null;
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
        PullRequestCommitRevisions.Clear();
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

    public async Task DetectCodexAsync(bool autoConnect = false)
    {
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        if (plugin is null)
        {
            CodexConnectionStatus = "Enable the AI Workspace plugin before scanning for Codex.";
            return;
        }

        _codexConnectionCancellation?.Cancel();
        _codexConnectionCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _codexConnectionCancellation = cancellation;
        ProjectItemViewModel? project = SelectedProject;
        IsCodexChatBusy = true;
        CodexConnectionStatus = "Scanning the local Codex installation and running desktop processesâ€¦";
        try
        {
            AiCodexDetectionResult detection = await plugin.DetectCodexAsync(AiExecutablePath, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            IsCodexDetected = detection.IsInstalled;
            IsCodexRunning = detection.IsRunning;
            CodexDetectedPath = detection.ExecutablePath;
            CodexDetectedVersion = detection.Version;
            CodexConnectionStatus = detection.Status;
            if (detection.IsInstalled && !string.IsNullOrWhiteSpace(detection.ExecutablePath))
                AiExecutablePath = detection.ExecutablePath;

            _applicationLogService.Information(
                "ai.codex",
                $"detection installed={detection.IsInstalled} running={detection.IsRunning} version=\"{detection.Version}\" executable=\"{detection.ExecutablePath}\"",
                project?.RootPath);

            if (autoConnect && detection.IsInstalled && detection.IsRunning && project is not null &&
                SelectedProject?.Id == project.Id && _codexChatProjectId != project.Id)
            {
                await ConnectCodexChatCoreAsync(plugin, detection.ExecutablePath, project, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_codexConnectionCancellation, cancellation))
                CodexConnectionStatus = "Codex scan cancelled.";
        }
        catch (Exception exception)
        {
            CodexConnectionStatus = exception.Message;
            _applicationLogService.Error("ai.codex", "detection failed", exception, project?.RootPath);
        }
        finally
        {
            if (ReferenceEquals(_codexConnectionCancellation, cancellation))
            {
                IsCodexChatBusy = false;
                _codexConnectionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    public async Task ToggleCodexChatConnectionAsync()
    {
        if (IsCodexChatConnected)
        {
            await DisconnectCodexChatAsync(clearMessages: false);
            return;
        }

        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        ProjectItemViewModel? project = SelectedProject;
        if (plugin is null || project is null)
        {
            CodexConnectionStatus = "Enable AI Workspace and select a project first.";
            return;
        }

        if (!IsCodexDetected)
        {
            await DetectCodexAsync(autoConnect: false);
            plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
            project = SelectedProject;
            if (!IsCodexDetected || plugin is null || project is null) return;
        }

        _codexConnectionCancellation?.Cancel();
        _codexConnectionCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _codexConnectionCancellation = cancellation;
        IsCodexChatBusy = true;
        try
        {
            await ConnectCodexChatCoreAsync(plugin, AiExecutablePath, project, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            CodexConnectionStatus = "Codex connection cancelled.";
        }
        catch (Exception exception)
        {
            CodexConnectionStatus = exception.Message;
            _applicationLogService.Error("ai.codex", "connection failed", exception, project.RootPath);
        }
        finally
        {
            if (ReferenceEquals(_codexConnectionCancellation, cancellation))
            {
                IsCodexChatBusy = false;
                _codexConnectionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    public async Task SendCodexChatMessageAsync()
    {
        string message = AiPrompt.Trim();
        if (message.Length == 0) return;
        if (!IsCodexChatConnected)
        {
            await ToggleCodexChatConnectionAsync();
            if (!IsCodexChatConnected) return;
        }

        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        ProjectItemViewModel? project = SelectedProject;
        if (plugin is null || project is null) return;

        _aiAgentCancellation?.Cancel();
        _aiAgentCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _aiAgentCancellation = cancellation;

        AiChatMessageViewModel userMessage = new("user", message);
        AiChatMessageViewModel assistantMessage = new("assistant", string.Empty);
        AiChatMessages.Add(userMessage);
        AiChatMessages.Add(assistantMessage);
        AiPrompt = string.Empty;
        IsCodexChatBusy = true;
        CodexConnectionStatus = "Codex is working in this projectâ€¦";
        Progress<AiChatProgress> progress = new(update =>
        {
            if (update.Kind == "delta")
            {
                assistantMessage.Append(update.Text);
            }
            else if (update.Kind is "status" or "error")
            {
                CodexConnectionStatus = update.Text;
            }
        });

        try
        {
            AiChatTurnResult result = await plugin.SendCodexChatAsync(message, progress, cancellation.Token);
            if (string.IsNullOrWhiteSpace(assistantMessage.Text))
                assistantMessage.Append(string.IsNullOrWhiteSpace(result.Response) ? result.Diagnostic : result.Response);
            AiResponse = assistantMessage.Text;
            CodexConnectionStatus = result.Succeeded
                ? $"Codex completed the turn in {result.Duration:g}."
                : $"Codex could not complete the turn: {result.Diagnostic}";
            _applicationLogService.Information(
                "ai.codex",
                $"turn completed success={result.Succeeded} thread=\"{CodexChatThreadId}\" turn=\"{result.TurnId}\" duration={result.Duration.TotalMilliseconds:N0}ms",
                project.RootPath);
        }
        catch (OperationCanceledException)
        {
            CodexConnectionStatus = "Codex turn cancelled.";
            if (string.IsNullOrWhiteSpace(assistantMessage.Text)) assistantMessage.Append("Turn cancelled.");
        }
        catch (Exception exception)
        {
            CodexConnectionStatus = exception.Message;
            if (string.IsNullOrWhiteSpace(assistantMessage.Text)) assistantMessage.Append($"Connection error: {exception.Message}");
            _applicationLogService.Error("ai.codex", "turn failed", exception, project.RootPath);
        }
        finally
        {
            if (ReferenceEquals(_aiAgentCancellation, cancellation))
            {
                await SaveCurrentAiConversationAsync().ConfigureAwait(true);
                IsCodexChatBusy = false;
                _aiAgentCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    public async Task GenerateAiCommitDescriptionAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        GitChangeViewModel[] included = Changes
            .Where(change => change.IsIncluded && !change.IsLocalOnly)
            .ToArray();
        if (project is null || included.Length == 0)
        {
            StatusMessage = "Select at least one change before generating a commit description.";
            return;
        }

        string selectionKey = BuildAiChangeSelectionKey(included);

        StatusMessage = "AI is preparing the commit descriptionâ€¦";
        string? response = await RunConnectedAiUtilityAsync(
            $"Generate a commit description for {included.Length} selected change(s).",
            token => BuildAiCommitPromptAsync(project, included, token),
            "commit description");
        if (string.IsNullOrWhiteSpace(response)) return;

        string currentSelectionKey = BuildAiChangeSelectionKey(Changes
            .Where(change => change.IsIncluded && !change.IsLocalOnly));
        if (SelectedProject?.Id != project.Id || !string.Equals(selectionKey, currentSelectionKey, StringComparison.Ordinal))
        {
            StatusMessage = "AI commit description kept in the AI conversation because the selected changes changed.";
            return;
        }

        CommitMessage = ParseAiCommitMessage(response);
        StatusMessage = "AI commit description ready. Review it before committing.";
    }

    public async Task GenerateAiPullRequestDraftAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled)
        {
            PullRequestStatus = "Select a Git project before generating a pull request draft.";
            return;
        }

        string head = string.IsNullOrWhiteSpace(NewPullRequestHeadBranch)
            ? CurrentBranch
            : NewPullRequestHeadBranch.Trim();
        string baseBranch = string.IsNullOrWhiteSpace(NewPullRequestBaseBranch)
            ? "main"
            : NewPullRequestBaseBranch.Trim();
        if (string.IsNullOrWhiteSpace(head) || head == "â€”" || head.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
        {
            PullRequestStatus = "Enter the pull request head branch first.";
            return;
        }

        NewPullRequestHeadBranch = head;
        NewPullRequestBaseBranch = baseBranch;
        PullRequestStatus = "AI is preparing the pull request draftâ€¦";
        string? response = await RunConnectedAiUtilityAsync(
            $"Draft a pull request from {head} to {baseBranch}.",
            token => BuildAiPullRequestPromptAsync(project, head, baseBranch, token),
            "pull request draft");
        if (string.IsNullOrWhiteSpace(response)) return;

        if (SelectedProject?.Id != project.Id ||
            !string.Equals(NewPullRequestHeadBranch.Trim(), head, StringComparison.Ordinal) ||
            !string.Equals(NewPullRequestBaseBranch.Trim(), baseBranch, StringComparison.Ordinal))
        {
            PullRequestStatus = "AI pull request draft kept in the AI conversation because the selected branches changed.";
            return;
        }

        (string title, string body) = ParseAiPullRequestDraft(response);
        NewPullRequestTitle = title;
        NewPullRequestBody = body;
        PullRequestStatus = "AI pull request draft ready. Review it before creating the pull request.";
    }

    public async Task GenerateAiCodeSummaryAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        CodeTreeNode? node = SelectedCodeNode;
        if (project is null || node is not { IsDirectory: false, IsPlaceholder: false } ||
            CodePreviewIsImage || string.IsNullOrWhiteSpace(CodePreviewText))
        {
            StatusMessage = "Select a readable code file before asking AI for a summary.";
            return;
        }

        string previewText = CodePreviewText;
        string relativePath = node.RelativePath;
        CodeAiSummary = "AI is summarizing this fileâ€¦";
        string? response = await RunConnectedAiUtilityAsync(
            $"Summarize {relativePath}.",
            _ => Task.FromResult(BuildAiCodeSummaryPrompt(project, relativePath, previewText)),
            "code summary");
        if (SelectedProject?.Id != project.Id ||
            !string.Equals(SelectedCodeNode?.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            return;
        CodeAiSummary = string.IsNullOrWhiteSpace(response)
            ? "The AI assistant did not return a summary."
            : response.Trim();
    }

    public void ClearAiCodeSummary() => CodeAiSummary = string.Empty;

    public bool CanUseAiConflictResolver => IsCodexChatConnected && !IsCodexChatBusy;

    public Task<string?> GenerateAiConflictAssistanceAsync(
        GitConflictFile conflict,
        bool proposeResolution,
        CancellationToken cancellationToken = default)
    {
        string mode = proposeResolution ? "a complete candidate resolution" : "review advice only";
        return RunConnectedAiUtilityAsync(
            $"Analyze Git conflict {conflict.Path} and provide {mode}.",
            _ => Task.FromResult(BuildAiConflictPrompt(conflict, proposeResolution)),
            proposeResolution ? "conflict resolution proposal" : "conflict advice");
    }

    private static string BuildAiConflictPrompt(GitConflictFile conflict, bool proposeResolution)
    {
        static string Limit(string value) => value.Length <= 24000 ? value : value[..24000] + "\n[truncated]";
        string instruction = proposeResolution
            ? "Return a short explanation followed by exactly one fenced code block containing the complete resolved file. Preserve formatting and do not include conflict markers."
            : "Explain the intent of both sides, identify risks, and recommend which blocks to combine. Do not claim that you modified the file.";
        return $"""
               You are assisting with a local Git three-way conflict. {instruction}
               File: {conflict.Path}
               Conflict type: {conflict.ConflictType}

               BASE:
               {Limit(conflict.Base.Text ?? conflict.Base.DisplayText)}

               OURS:
               {Limit(conflict.Ours.Text ?? conflict.Ours.DisplayText)}

               INCOMING:
               {Limit(conflict.Theirs.Text ?? conflict.Theirs.DisplayText)}

               CURRENT RESULT WITH MARKERS:
               {Limit(conflict.WorkingText ?? string.Empty)}
               """;
    }

    private async Task<string?> RunConnectedAiUtilityAsync(
        string displayRequest,
        Func<CancellationToken, Task<string>> promptFactory,
        string operationName)
    {
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        ProjectItemViewModel? project = SelectedProject;
        if (!IsCodexChatConnected || IsCodexChatBusy || plugin is null || project is null)
        {
            StatusMessage = "Connect an AI assistant for this project first.";
            return null;
        }

        _aiAgentCancellation?.Cancel();
        _aiAgentCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _aiAgentCancellation = cancellation;
        AiChatMessageViewModel userMessage = new("user", displayRequest);
        AiChatMessageViewModel assistantMessage = new("assistant", string.Empty);
        AiChatMessages.Add(userMessage);
        AiChatMessages.Add(assistantMessage);
        IsCodexChatBusy = true;
        CodexConnectionStatus = $"Codex is generating the {operationName}â€¦";
        Progress<AiChatProgress> progress = new(update =>
        {
            if (update.Kind == "delta")
                assistantMessage.Append(update.Text);
            else if (update.Kind is "status" or "error")
                CodexConnectionStatus = update.Text;
        });

        try
        {
            string prompt = await promptFactory(cancellation.Token);
            AiChatTurnResult result = await plugin.SendCodexChatAsync(prompt, progress, cancellation.Token);
            if (string.IsNullOrWhiteSpace(assistantMessage.Text))
                assistantMessage.Append(string.IsNullOrWhiteSpace(result.Response) ? result.Diagnostic : result.Response);
            AiResponse = assistantMessage.Text;
            CodexConnectionStatus = result.Succeeded
                ? $"Codex generated the {operationName}."
                : $"Codex could not generate the {operationName}: {result.Diagnostic}";
            _applicationLogService.Information(
                "ai.codex",
                $"generated {operationName} success={result.Succeeded} thread=\"{CodexChatThreadId}\" duration={result.Duration.TotalMilliseconds:N0}ms",
                project.RootPath);
            return result.Succeeded ? assistantMessage.Text : null;
        }
        catch (OperationCanceledException)
        {
            CodexConnectionStatus = $"AI {operationName} cancelled.";
            if (string.IsNullOrWhiteSpace(assistantMessage.Text)) assistantMessage.Append("Generation cancelled.");
            return null;
        }
        catch (Exception exception)
        {
            CodexConnectionStatus = exception.Message;
            if (string.IsNullOrWhiteSpace(assistantMessage.Text)) assistantMessage.Append($"Generation error: {exception.Message}");
            _applicationLogService.Error("ai.codex", $"{operationName} generation failed", exception, project.RootPath);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_aiAgentCancellation, cancellation))
            {
                await SaveCurrentAiConversationAsync().ConfigureAwait(true);
                IsCodexChatBusy = false;
                _aiAgentCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private async Task<string> BuildAiCommitPromptAsync(
        ProjectItemViewModel project,
        IReadOnlyList<GitChangeViewModel> included,
        CancellationToken cancellationToken)
    {
        StringBuilder prompt = new();
        prompt.AppendLine("Act as a senior developer preparing a Git commit message.");
        prompt.AppendLine($"Repository: {project.Name}");
        prompt.AppendLine($"Branch: {CurrentBranch}");
        prompt.AppendLine($"UI language: {_localization.CurrentLanguageCode}");
        prompt.AppendLine("Only describe the selected changes listed below. Do not suggest commands and do not perform any Git action.");
        prompt.AppendLine("Return exactly this format, without a Markdown code fence:");
        prompt.AppendLine("SUBJECT: one imperative subject line, preferably 72 characters or fewer");
        prompt.AppendLine("BODY:");
        prompt.AppendLine("an optional concise body explaining the important changes; leave empty when unnecessary");
        prompt.AppendLine();
        prompt.AppendLine("SELECTED FILES:");
        foreach (GitChangeViewModel change in included.Take(120))
        {
            prompt.Append("- ").Append(change.State).Append(" | ").Append(change.Path);
            if (change.Change.IsLfsObject) prompt.Append(" | LFS");
            if (change.HasLock) prompt.Append(" | ").Append(change.LockOwner);
            prompt.AppendLine();
        }
        if (included.Count > 120) prompt.AppendLine($"- â€¦ {included.Count - 120} additional selected file(s)");

        int patchCount = 0;
        int remainingPatchCharacters = 42_000;
        foreach (GitChangeViewModel change in included.Where(change => IsLikelyTextFile(change.Path)).Take(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remainingPatchCharacters <= 0) break;
            try
            {
                string patch = change.IsUntracked
                    ? await BuildUntrackedFileDiffAsync(project.RootPath, change.Path, cancellationToken)
                    : await _gitService.GetDiffAsync(
                        project.RootPath,
                        change.Path,
                        change.Change.IsStaged,
                        cancellationToken);
                if (string.IsNullOrWhiteSpace(patch)) continue;
                int take = Math.Min(8_000, Math.Min(remainingPatchCharacters, patch.Length));
                prompt.AppendLine().AppendLine($"PATCH {change.Path}:");
                prompt.AppendLine(patch[..take]);
                if (take < patch.Length) prompt.AppendLine("[patch truncated]");
                remainingPatchCharacters -= take;
                patchCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                prompt.AppendLine($"Patch unavailable for {change.Path}: {exception.Message}");
            }
        }
        if (patchCount == 0) prompt.AppendLine().AppendLine("No textual patch was available; infer the message conservatively from the selected file list.");
        return prompt.ToString();
    }

    private async Task<string> BuildAiPullRequestPromptAsync(
        ProjectItemViewModel project,
        string head,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        StringBuilder prompt = new();
        prompt.AppendLine("Act as a senior developer drafting a pull request.");
        prompt.AppendLine($"Repository: {project.Name}");
        prompt.AppendLine($"Head branch: {head}");
        prompt.AppendLine($"Base branch: {baseBranch}");
        prompt.AppendLine($"UI language: {_localization.CurrentLanguageCode}");
        prompt.AppendLine("Do not create, update, merge, or publish anything. Produce a draft for the user to review.");
        prompt.AppendLine("Return exactly this format, without a Markdown code fence:");
        prompt.AppendLine("TITLE: concise pull request title");
        prompt.AppendLine("BODY:");
        prompt.AppendLine("Markdown description with a short Summary and Testing section. Do not invent tests.");
        prompt.AppendLine();

        try
        {
            GitBranchComparison comparison = await _gitService.CompareBranchesAsync(
                project.RootPath,
                head,
                baseBranch,
                cancellationToken);
            GitBranchComparisonCommit[] sourceCommits = comparison.Commits
                .Where(commit => commit.Presence == GitBranchCommitPresence.SourceOnly)
                .ToArray();
            prompt.AppendLine($"SOURCE-ONLY COMMITS: {sourceCommits.Length}");
            foreach (GitBranchComparisonCommit commit in sourceCommits.Take(80))
            {
                GitRevision revision = commit.Revision;
                prompt.AppendLine($"- {revision.ShortHash} | {revision.Subject} | {revision.AuthorName} | {revision.AuthoredAt:O}");
            }
            if (sourceCommits.Length > 80) prompt.AppendLine($"- â€¦ {sourceCommits.Length - 80} additional commit(s)");

            prompt.AppendLine().AppendLine("CHANGED FILES FROM REPRESENTATIVE COMMITS:");
            foreach (GitBranchComparisonCommit commit in sourceCommits.Take(8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                GitCommitDetails details = await _gitService.GetCommitDetailsAsync(
                    project.RootPath,
                    commit.Revision.Hash,
                    cancellationToken);
                prompt.AppendLine($"- {commit.Revision.ShortHash}: +{details.AddedLines}/-{details.DeletedLines}, {details.BinaryFileCount} binary");
                foreach (GitCommitFileChange file in details.Files.Take(30))
                    prompt.AppendLine($"  - {file.Kind}: {file.Path} ({file.ChangeSummary})");
                if (details.Files.Count > 30) prompt.AppendLine($"  - â€¦ {details.Files.Count - 30} additional file(s)");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            prompt.AppendLine($"Branch comparison unavailable: {exception.Message}");
            prompt.AppendLine("Recent visible history (use conservatively):");
            foreach (GitRevision revision in History.Take(40))
                prompt.AppendLine($"- {revision.ShortHash} | {revision.Subject} | {revision.AuthorName}");
        }
        return prompt.ToString();
    }

    private string BuildAiCodeSummaryPrompt(ProjectItemViewModel project, string relativePath, string content)
    {
        const int maximumCharacters = 64_000;
        string bounded = content.Length <= maximumCharacters ? content : content[..maximumCharacters];
        return $"""
               Act as a senior developer reviewing one file. Summarize it for another developer.
               Repository: {project.Name}
               File: {relativePath}
               UI language: {_localization.CurrentLanguageCode}

               Explain the file's purpose, main responsibilities, important data/control flow, dependencies, and noteworthy risks or extension points. Be concise and use readable Markdown. Do not modify the file and do not suggest Git actions.
               {(content.Length > maximumCharacters ? "The preview is truncated; explicitly mention that the summary covers only the available prefix." : string.Empty)}

               FILE CONTENT:
               {bounded}
               """;
    }

    private static string ParseAiCommitMessage(string response)
    {
        string normalized = StripMarkdownFence(response);
        string[] lines = normalized.Replace("\r\n", "\n").Split('\n');
        int subjectIndex = Array.FindIndex(lines, line => line.TrimStart().StartsWith("SUBJECT:", StringComparison.OrdinalIgnoreCase));
        int bodyIndex = Array.FindIndex(lines, line => line.Trim().Equals("BODY:", StringComparison.OrdinalIgnoreCase));
        if (subjectIndex >= 0)
        {
            string subject = lines[subjectIndex][(lines[subjectIndex].IndexOf(':') + 1)..].Trim().Trim('"');
            string body = bodyIndex >= 0
                ? string.Join(Environment.NewLine, lines.Skip(bodyIndex + 1)).Trim()
                : string.Empty;
            return string.IsNullOrWhiteSpace(body) ? subject : subject + Environment.NewLine + Environment.NewLine + body;
        }
        return normalized.Trim();
    }

    private static (string Title, string Body) ParseAiPullRequestDraft(string response)
    {
        string normalized = StripMarkdownFence(response);
        string[] lines = normalized.Replace("\r\n", "\n").Split('\n');
        int titleIndex = Array.FindIndex(lines, line => line.TrimStart().StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase));
        int bodyIndex = Array.FindIndex(lines, line => line.Trim().Equals("BODY:", StringComparison.OrdinalIgnoreCase));
        string title = titleIndex >= 0
            ? lines[titleIndex][(lines[titleIndex].IndexOf(':') + 1)..].Trim().Trim('"')
            : lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim().TrimStart('#', ' ').Trim() ?? "Pull request";
        string body = bodyIndex >= 0
            ? string.Join(Environment.NewLine, lines.Skip(bodyIndex + 1)).Trim()
            : string.Join(Environment.NewLine, lines.SkipWhile(line => string.IsNullOrWhiteSpace(line) || line.Contains(title, StringComparison.Ordinal)).SkipWhile(string.IsNullOrWhiteSpace)).Trim();
        return (title, body);
    }

    private static string StripMarkdownFence(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        int firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0) return trimmed.Trim('`').Trim();
        string withoutOpening = trimmed[(firstLineEnd + 1)..];
        int closing = withoutOpening.LastIndexOf("```", StringComparison.Ordinal);
        return (closing >= 0 ? withoutOpening[..closing] : withoutOpening).Trim();
    }

    private static bool IsLikelyTextFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() is not
            (".uasset" or ".umap" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or
             ".zip" or ".7z" or ".rar" or ".dll" or ".exe" or ".pdb" or ".so" or ".dylib" or ".a" or ".lib");
    }

    private static string BuildAiChangeSelectionKey(IEnumerable<GitChangeViewModel> changes) =>
        string.Join("\n", changes
            .Select(change => $"{change.State}|{change.Path}|{change.Change.IsStaged}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

    private void NotifyAiGenerationStateChanged()
    {
        OnPropertyChanged(nameof(CanGenerateAiCommitDescription));
        OnPropertyChanged(nameof(CanGenerateAiPullRequestDraft));
        OnPropertyChanged(nameof(CanGenerateAiCodeSummary));
    }

    public void CancelAiChat()
    {
        _aiAgentCancellation?.Cancel();
        _codexConnectionCancellation?.Cancel();
    }

    private async Task ConnectCodexChatCoreAsync(
        IAiIntegrationPlugin plugin,
        string executablePath,
        ProjectItemViewModel project,
        CancellationToken cancellationToken)
    {
        CodexConnectionStatus = $"Connecting Codex to {project.Name}â€¦";
        string workingDirectory = await ResolveAiConversationWorkspaceAsync(project, cancellationToken)
            .ConfigureAwait(true);
        AiConversationViewModel? conversation = SelectedAiConversation;
        AiChatConnectionResult connection = await plugin.ConnectCodexChatAsync(
            new AiChatConnectRequest(
                project.Name,
                project.RootPath,
                executablePath,
                AiModel,
                BuildAiWorkspacePermissions(),
                conversation?.ThreadId ?? string.Empty,
                workingDirectory,
                conversation?.PrePrompt ?? string.Empty),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (SelectedProject?.Id != project.Id)
        {
            await plugin.DisconnectCodexChatAsync(cancellationToken);
            return;
        }

        IsCodexChatConnected = connection.Connected;
        CodexChatThreadId = connection.ThreadId;
        _codexChatProjectId = connection.Connected ? project.Id : null;
        CodexConnectionStatus = connection.Status;
        if (!connection.Connected) return;

        if (conversation is not null)
        {
            conversation.ThreadId = connection.ThreadId;
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
        }
        if (AiChatMessages.Count == 0)
        {
            AiChatMessages.Add(new AiChatMessageViewModel(
                "system",
                $"Connected to local Codex. Project: {project.Name}{Environment.NewLine}Workspace: {workingDirectory}"));
        }
        await SaveCurrentAiConversationAsync(cancellationToken).ConfigureAwait(true);
        _applicationLogService.Information(
            "ai.codex",
            $"connected project=\"{project.Name}\" thread=\"{connection.ThreadId}\"",
            project.RootPath);
    }

    private async Task DisconnectCodexChatAsync(bool clearMessages)
    {
        IAiIntegrationPlugin? plugin = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        _aiAgentCancellation?.Cancel();
        if (plugin is not null)
        {
            try { await plugin.DisconnectCodexChatAsync(); }
            catch (Exception exception)
            {
                _applicationLogService.Warning("ai.codex", $"disconnect warning: {exception.Message}", SelectedProject?.RootPath);
            }
        }
        IsCodexChatConnected = false;
        CodexChatThreadId = SelectedAiConversation?.ThreadId ?? string.Empty;
        _codexChatProjectId = null;
        CodexConnectionStatus = IsCodexDetected ? "Codex detected. Connect when ready." : "Codex is disconnected.";
        if (clearMessages) AiChatMessages.Clear();
    }

    private async Task ResetCodexChatForProjectAsync(ProjectItemViewModel? project)
    {
        await SaveCurrentAiConversationAsync().ConfigureAwait(true);
        if (_codexChatProjectId is not null)
            await DisconnectCodexChatAsync(clearMessages: true).ConfigureAwait(true);
        await LoadAiConversationsForProjectAsync(project).ConfigureAwait(true);
        if (project is not null && IsAiIntegrationEnabled && SelectedProject?.Id == project.Id)
            await DetectCodexAsync(autoConnect: true).ConfigureAwait(true);
    }

    private AiWorkspacePermission BuildAiWorkspacePermissions()
    {
        AiWorkspacePermission permissions = AiWorkspacePermission.ReadRepository;
        if (AiAllowModify) permissions |= AiWorkspacePermission.ModifyFiles;
        if (AiAllowNetwork) permissions |= AiWorkspacePermission.NetworkAccess;
        if (AiStageAfterRun) permissions |= AiWorkspacePermission.StageChanges;
        if (AiCommitAfterRun) permissions |= AiWorkspacePermission.CreateCommit;
        return permissions;
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

        AiWorkspacePermission permissions = BuildAiWorkspacePermissions();
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
        _unrealBuildCancellation?.Cancel();
        _unrealBuildCancellation?.Dispose();
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
    }

    private async Task LoadCodeNodeAsync(CodeTreeNode? node)
    {
        _codePreviewCancellation?.Cancel();
        _codePreviewCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _codePreviewCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        ProjectItemViewModel? project = SelectedProject;
        CodeHistory.Clear();
        CodeSymbols.Clear();
        CodePreviewText = string.Empty;
        CodePreviewPath = string.Empty;
        CodePreviewImage = null;
        CodePreviewIsImage = false;
        SetCodePreviewSupportsAiSummary(false);
        if (project is null || node is null || node.IsPlaceholder)
        {
            CodePreviewSummary = "Select a file to preview it.";
            if (ReferenceEquals(_codePreviewCancellation, cancellation))
            {
                _codePreviewCancellation = null;
                cancellation.Dispose();
            }
            return;
        }

        try
        {
            if (node.IsDirectory)
            {
                if (node.HasUnloadedChildren)
                {
                    CodePreviewSummary = $"Loading folder · {node.RelativePath}…";
                    await LoadCodeDirectoryAsync(project, node, token);
                }
                token.ThrowIfCancellationRequested();
                if (SelectedProject?.Id != project.Id) return;
                CodePreviewSummary = $"Folder · {node.RelativePath} · {node.Children.Count} loaded item(s)";
                return;
            }

            FileInfo info = new(node.FullPath);
            FilePresentationResult? richPreview = await _filePresentationService.CreatePreviewAsync(
                new FilePreviewRequest(project.RootPath, node.RelativePath, node.FullPath, info.Length),
                token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id ||
                !ReferenceEquals(_codePreviewCancellation, cancellation)) return;
            if (richPreview is not null)
            {
                ApplyFilePresentation(node.RelativePath, richPreview);
                IReadOnlyList<CodeHistoryEntry> history = project.Definition.Features.GitEnabled
                    ? await _codeWorkspaceService.GetHistoryAsync(
                        project.RootPath, node.RelativePath, cancellationToken: token)
                    : [];
                token.ThrowIfCancellationRequested();
                if (SelectedProject?.Id != project.Id ||
                    !ReferenceEquals(_codePreviewCancellation, cancellation)) return;
                ReplaceCollection(CodeHistory, history);
                CodeSelectionSummary = richPreview.Kind == FilePresentationKind.Image
                    ? "Image preview · use file history to inspect earlier versions."
                    : "Plugin preview · file history is available below.";
                return;
            }

            string cacheKey = $"{project.Id:N}:{Volatile.Read(ref _workingTreeDiffGeneration)}:" +
                              $"{node.RelativePath}:{info.LastWriteTimeUtc.Ticks}:{info.Length}";
            CodePreviewSummary = $"Loading preview · {node.RelativePath}…";
            if (!_codeFileInspectionCache.TryGetValue(cacheKey, out CachedCodeFileInspection? cached) || cached is null)
            {
                Task<CodeFilePreview> previewTask = _codeWorkspaceService.ReadPreviewAsync(
                    project.RootPath, node.RelativePath, token);
                Task<IReadOnlyList<CodeHistoryEntry>> historyTask = project.Definition.Features.GitEnabled
                    ? _codeWorkspaceService.GetHistoryAsync(project.RootPath, node.RelativePath, cancellationToken: token)
                    : Task.FromResult<IReadOnlyList<CodeHistoryEntry>>([]);

                CodeFilePreview immediatePreview = await previewTask;
                token.ThrowIfCancellationRequested();
                if (SelectedProject?.Id != project.Id ||
                    !ReferenceEquals(_codePreviewCancellation, cancellation)) return;
                ApplyCodePreview(immediatePreview);
                CodeSelectionSummary = "File ready · loading Git history…";

                IReadOnlyList<CodeHistoryEntry> history;
                try
                {
                    history = await historyTask;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    history = [];
                    CodeSelectionSummary = $"File ready · Git history unavailable: {exception.Message}";
                    _applicationLogService.Warning(
                        "solution.preview",
                        $"history load failed path=\"{node.RelativePath}\": {exception.Message}",
                        project.RootPath);
                }
                cached = new CachedCodeFileInspection(immediatePreview, history);
                _codeFileInspectionCache.Set(cacheKey, cached);
            }

            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id ||
                !ReferenceEquals(_codePreviewCancellation, cancellation)) return;
            ApplyCodePreview(cached.Preview);
            ReplaceCollection(CodeHistory, cached.History);
            CodeSelectionSummary = "Select lines in the preview, then request their Git history.";
        }
        catch (OperationCanceledException)
        {
            // A newer selection or project superseded this preview.
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && SelectedProject?.Id == project.Id)
            {
                CodePreviewSummary = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_codePreviewCancellation, cancellation))
            {
                _codePreviewCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void ApplyCodePreview(CodeFilePreview preview)
    {
        CodePreviewImage = null;
        CodePreviewIsImage = false;
        SetCodePreviewSupportsAiSummary(!preview.IsBinary);
        CodePreviewPath = preview.RelativePath;
        CodePreviewText = preview.IsBinary
            ? $"No active CyRevision plugin provides a reader or preview for {Path.GetExtension(preview.RelativePath)} files."
            : preview.Text;
        string matchLocation = SelectedCodeSearchResult is { } result &&
                               string.Equals(
                                   preview.RelativePath.Replace('\\', '/'),
                                   result.RelativePath.Replace('\\', '/'),
                                   StringComparison.OrdinalIgnoreCase)
            ? $" · match at line {result.LineNumber}, column {result.ColumnNumber}"
            : string.Empty;
        CodePreviewSummary = $"{preview.RelativePath} · {preview.Summary}{matchLocation}";
        ReplaceCollection(CodeSymbols, preview.Symbols);
    }

    private void ApplyFilePresentation(string relativePath, FilePresentationResult presentation)
    {
        CodeSymbols.Clear();
        CodePreviewPath = relativePath;
        CodePreviewSummary = $"{relativePath} · {presentation.Summary} · {presentation.ProviderId}";
        if (presentation.Kind == FilePresentationKind.Image &&
            presentation.ImagePath is not null &&
            File.Exists(presentation.ImagePath))
        {
            CodePreviewText = string.Empty;
            CodePreviewImage = new Bitmap(presentation.ImagePath);
            CodePreviewIsImage = true;
            SetCodePreviewSupportsAiSummary(false);
            return;
        }

        CodePreviewImage = null;
        CodePreviewIsImage = false;
        SetCodePreviewSupportsAiSummary(true);
        CodePreviewText = string.IsNullOrWhiteSpace(presentation.TextContent)
            ? presentation.Summary
            : presentation.TextContent;
    }

    private void SetCodePreviewSupportsAiSummary(bool value)
    {
        if (_codePreviewSupportsAiSummary == value) return;
        _codePreviewSupportsAiSummary = value;
        OnPropertyChanged(nameof(CanGenerateAiCodeSummary));
    }

    public async Task LoadCodeDirectoryAsync(
        ProjectItemViewModel project,
        CodeTreeNode directory,
        CancellationToken cancellationToken = default)
    {
        if (!directory.IsDirectory || !directory.HasUnloadedChildren) return;
        string loadKey = $"{project.Id:N}:{directory.RelativePath}";
        if (!_loadingCodeDirectories.Add(loadKey)) return;

        OperationTaskViewModel task = BeginTrackedTask(
            $"Open {directory.Name}",
            project.Name,
            directory.RelativePath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        _applicationLogService.Debug("solution", $"directory load start path=\"{directory.RelativePath}\"", project.RootPath);
        try
        {
            IReadOnlyList<CodeTreeNode> children = await _codeWorkspaceService.LoadDirectoryAsync(
                project.RootPath,
                directory.RelativePath,
                CodeIncludeHidden,
                cancellationToken);
            if (SelectedProject?.Id != project.Id)
            {
                CompleteTrackedTask(task, "Completed", $"Cached {children.Count:N0} item(s) for {project.Name}");
                directory.ReplaceChildren(children);
                return;
            }
            directory.ReplaceChildren(children);
            ReplaceCollection(CodeFileList, FlattenCodeFiles(CodeTree));
            CompleteTrackedTask(task, "Completed", $"{children.Count:N0} item(s) in {stopwatch.Elapsed.TotalMilliseconds:N0} ms");
            _applicationLogService.Debug(
                "solution",
                $"directory load complete path=\"{directory.RelativePath}\" items={children.Count:N0} duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms",
                project.RootPath);
        }
        catch (OperationCanceledException)
        {
            CompleteTrackedTask(task, "Cancelled", "Superseded by a newer selection");
        }
        catch (Exception exception)
        {
            CompleteTrackedTask(task, "Failed", exception.Message);
            _applicationLogService.Error("solution", $"directory load failed path=\"{directory.RelativePath}\"", exception, project.RootPath);
            CodePreviewSummary = exception.Message;
        }
        finally
        {
            _loadingCodeDirectories.Remove(loadKey);
        }
    }

    private static CodeTreeNode? FindFirstFile(IEnumerable<CodeTreeNode> nodes)
    {
        foreach (CodeTreeNode node in nodes)
        {
            if (!node.IsDirectory && !node.IsPlaceholder) return node;
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
        if (SelectedPlugin is null || SelectedProject is null)
        {
            return;
        }

        string pluginId = SelectedPlugin.Id;
        ProjectItemViewModel project = SelectedProject;
        await RunOperationAsync("Enabling plugin…", async () =>
        {
            await _pluginManager.EnableAsync(pluginId);
            await SetProjectPluginStateAsync(project, pluginId, enabled: true);
        }, $"Plugin enabled for {project.Name}");
    }

    public async Task DisableSelectedPluginAsync()
    {
        if (SelectedPlugin is null || SelectedProject is null)
        {
            return;
        }

        string pluginId = SelectedPlugin.Id;
        ProjectItemViewModel project = SelectedProject;
        await RunOperationAsync("Disabling plugin…", async () =>
        {
            DetachUnrealPluginEvents();
            DetachGameEnginePluginEvents(pluginId);
            await SetProjectPluginStateAsync(project, pluginId, enabled: false);
        }, $"Plugin disabled for {project.Name}");
    }

    private void ApplyProjectPluginScope(ProjectItemViewModel? project)
    {
        if (project is null)
        {
            _pluginManager.ClearProjectScope();
            RefreshPluginCatalog();
            return;
        }

        string[] pluginIds;
        if (project.Definition.EnabledPluginIds is null)
        {
            // Migrate the former application-wide selection once, then keep it on this project only.
            pluginIds = [.. _legacyProjectPluginIds];
            ProjectDefinition migrated = project.Definition with { EnabledPluginIds = pluginIds };
            project.Update(migrated);
            _ = PersistMigratedProjectPluginScopeAsync(project, migrated);
        }
        else
        {
            pluginIds = project.Definition.EnabledPluginIds;
        }

        _pluginManager.SetProjectScope(project.Id, project.RootPath, pluginIds);
        RefreshPluginCatalog();
        _ = EnsureProjectPluginsLoadedAsync(project.Id, pluginIds);
    }

    private async Task PersistMigratedProjectPluginScopeAsync(
        ProjectItemViewModel project,
        ProjectDefinition definition)
    {
        try
        {
            await _projectCatalog.UpsertAsync(definition);
        }
        catch (Exception exception)
        {
            _applicationLogService.Error(
                "plugins",
                $"could not migrate project plugin scope project=\"{project.Name}\"",
                exception,
                project.RootPath);
        }
    }

    private async Task EnsureProjectPluginsLoadedAsync(Guid projectId, IReadOnlyCollection<string> pluginIds)
    {
        try
        {
            foreach (string pluginId in pluginIds)
            {
                PluginCatalogEntry? entry = _pluginManager.Entries.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));
                if (entry is null || entry.InstanceLoaded) continue;
                await _pluginManager.EnableAsync(pluginId);
            }

            if (SelectedProject?.Id != projectId) return;
            if (SelectedProject is { } selectedProject)
                _pluginManager.SetProjectScope(selectedProject.Id, selectedProject.RootPath, pluginIds);
            RefreshPluginCatalog();
        }
        catch (Exception exception)
        {
            _applicationLogService.Error(
                "plugins",
                $"could not load project plugin scope project={projectId:N}",
                exception,
                SelectedProject?.Id == projectId ? SelectedProject.RootPath : null);
        }
    }

    private async Task SetProjectPluginStateAsync(
        ProjectItemViewModel project,
        string pluginId,
        bool enabled)
    {
        HashSet<string> pluginIds = new(
            project.Definition.EnabledPluginIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (enabled) pluginIds.Add(pluginId);
        else pluginIds.Remove(pluginId);

        bool disablesActiveMode = !enabled &&
                                  string.Equals(
                                      project.Definition.PluginOperatingModeProviderId,
                                      pluginId,
                                      StringComparison.OrdinalIgnoreCase);
        ProjectPreset? fallback = disablesActiveMode
            ? ProjectPresets.All.FirstOrDefault(preset =>
                preset.Features == project.Definition.Features &&
                preset.Retention.Mode == project.Definition.Retention.Mode)
              ?? ProjectPresets.All.FirstOrDefault(preset => preset.Kind == ProjectPresetKind.BackupOnly)
            : null;

        ProjectDefinition definition = project.Definition with
        {
            EnabledPluginIds = pluginIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            OperatingMode = disablesActiveMode ? fallback?.Kind ?? ProjectPresetKind.Custom : project.Definition.OperatingMode,
            PluginOperatingModeId = disablesActiveMode ? null : project.Definition.PluginOperatingModeId,
            PluginOperatingModeProviderId = disablesActiveMode ? null : project.Definition.PluginOperatingModeProviderId
        };
        await _projectCatalog.UpsertAsync(definition);
        project.Update(definition);

        if (SelectedProject?.Id != project.Id) return;
        _pluginManager.SetProjectScope(project.Id, project.RootPath, definition.EnabledPluginIds);
        RefreshPluginCatalog(pluginId);
        NotifySynchronizationModeChanged();
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

    public async Task SaveUnrealAssetInspectionOptionsAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || string.IsNullOrWhiteSpace(UnrealProjectPath))
        {
            StatusMessage = "Enable the Unreal Engine Integration plugin and select an Unreal project.";
            return;
        }
        if (!int.TryParse(UnrealAssetPreviewResolution, out int resolution) || resolution is < 128 or > 2048)
        {
            StatusMessage = "Preview resolution must be between 128 and 2048 pixels.";
            return;
        }
        if (!double.TryParse(UnrealAssetCacheBudgetGigabytes, out double budgetGigabytes) || budgetGigabytes is < 0.25 or > 100)
        {
            StatusMessage = "Cache budget must be between 0.25 and 100 GB.";
            return;
        }

        await RunOperationAsync("Saving Unreal asset preview settings...", async () =>
        {
            UnrealAssetInspectionOptions options = new(
                UnrealAdvancedAssetInspectionEnabled,
                resolution,
                (long)(budgetGigabytes * 1024 * 1024 * 1024),
                UnrealRenderMeshThumbnails,
                180);
            await plugin.SaveAssetInspectionOptionsAsync(UnrealProjectPath, options);
            await RefreshUnrealAssetInspectionCacheCoreAsync(plugin);
        }, UnrealAdvancedAssetInspectionEnabled
            ? "Advanced Unreal asset previews enabled"
            : "Advanced Unreal asset previews disabled");
    }

    public async Task RefreshUnrealAssetInspectionCacheAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || string.IsNullOrWhiteSpace(UnrealProjectPath)) return;
        await RunOperationAsync("Reading Unreal asset preview cache...", async () =>
        {
            await RefreshUnrealAssetInspectionCacheCoreAsync(plugin);
        }, "Unreal asset preview cache refreshed");
    }

    public async Task ClearUnrealAssetInspectionCacheAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || string.IsNullOrWhiteSpace(UnrealProjectPath)) return;
        await RunOperationAsync("Clearing Unreal asset preview cache...", async () =>
        {
            UnrealAssetInspectionCacheStatus status = await plugin.ClearAssetInspectionCacheAsync(UnrealProjectPath);
            UnrealAssetInspectionSummary = status.Summary;
        }, "Unreal asset preview cache cleared");
    }

    public async Task DiscoverUnrealBuildEnvironmentAsync(bool forceRefresh = false)
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || SelectedProject is null || string.IsNullOrWhiteSpace(UnrealProjectPath))
        {
            UnrealBuildStatus = "Enable the Unreal integration plugin and select a valid Unreal project.";
            return;
        }

        await RunOperationAsync("Discovering Unreal build targets and toolchains...", async () =>
        {
            UnrealBuildDiscovery discovery = forceRefresh
                ? await plugin.RefreshBuildEnvironmentAsync(UnrealProjectPath)
                : await plugin.DiscoverBuildEnvironmentAsync(UnrealProjectPath);
            _unrealBuildDiscovery = discovery;
            ReplaceCollection(UnrealBuildEngines, discovery.Engines);
            ReplaceCollection(UnrealBuildTargets, discovery.Targets);

            UnrealBuildProfile? profile = await plugin.LoadBuildProfileAsync(SelectedProject.Id);
            ReplaceCollection(UnrealBuildPresets, await plugin.LoadBuildPresetsAsync(SelectedProject.Id));
            SelectedUnrealBuildPreset = UnrealBuildPresets.FirstOrDefault(item =>
                                            string.Equals(item.PresetName, profile?.PresetName, StringComparison.OrdinalIgnoreCase))
                                        ?? UnrealBuildPresets.FirstOrDefault();
            SelectedUnrealBuildEngine = UnrealBuildEngines.FirstOrDefault(engine =>
                                            string.Equals(engine.RootPath, profile?.EngineRoot, StringComparison.OrdinalIgnoreCase))
                                        ?? UnrealBuildEngines.FirstOrDefault();
            SelectedUnrealBuildTarget = UnrealBuildTargets.FirstOrDefault(target => target.Id == profile?.TargetId)
                                        ?? UnrealBuildTargets.FirstOrDefault();
            UnrealBuildRangeFrom = UnrealBuildEngines.OrderBy(engine => ParseUnrealVersion(engine.Version)).FirstOrDefault();
            UnrealBuildRangeTo = UnrealBuildEngines.OrderByDescending(engine => ParseUnrealVersion(engine.Version)).FirstOrDefault();
            if (profile is not null)
            {
                SelectedUnrealBuildPlatform = profile.Platform;
                SelectedUnrealBuildConfiguration = profile.Configuration;
                UnrealLinuxToolchainPath = profile.LinuxToolchainPath;
                UnrealAndroidSdkPath = profile.AndroidSdkPath;
                UnrealAndroidNdkPath = profile.AndroidNdkPath;
                UnrealJavaHomePath = profile.JavaHomePath;
                UnrealBuildOutputPath = profile.OutputDirectory;
                UnrealBuildCookAndPackage = profile.CookAndPackage;
                UnrealBuildAutoConfigureToolchains = profile.AutoConfigureToolchains;
                UnrealBuildTimeoutMinutes = profile.TimeoutMinutes.ToString();
                UnrealBuildPresetName = profile.PresetName;
                UnrealBuildMaximumParallel = Math.Clamp(profile.MaximumParallelBuilds, 1, 4).ToString();
            }
            else if (string.IsNullOrWhiteSpace(UnrealBuildOutputPath))
            {
                UnrealBuildOutputPath = Path.Combine(SelectedProject.RootPath, "Saved", "CyRevision", "Builds");
            }
            UnrealBuildStatus = discovery.Summary;
            OnPropertyChanged(nameof(CanRunUnrealBuild));
        }, "Unreal build environment ready");
    }

    public Task RunSelectedUnrealBuildAsync() => SelectedUnrealBuildEngine is null
        ? Task.CompletedTask
        : RunUnrealBuildMatrixAsync([SelectedUnrealBuildEngine]);

    public async Task SaveUnrealBuildPresetAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || SelectedProject is null) return;
        if (!int.TryParse(UnrealBuildTimeoutMinutes, out int timeoutMinutes) || timeoutMinutes is < 1 or > 1440)
            throw new InvalidDataException("Build timeout must be between 1 and 1440 minutes.");
        if (!int.TryParse(UnrealBuildMaximumParallel, out int maximumParallel) || maximumParallel is < 1 or > 4)
            throw new InvalidDataException("Parallel build count must be between 1 and 4.");
        UnrealBuildProfile profile = CreateUnrealBuildProfile(SelectedProject.Id, timeoutMinutes) with
        {
            PresetName = string.IsNullOrWhiteSpace(UnrealBuildPresetName) ? "Default" : UnrealBuildPresetName.Trim(),
            MaximumParallelBuilds = maximumParallel
        };
        await plugin.SaveBuildPresetAsync(profile);
        ReplaceCollection(UnrealBuildPresets, await plugin.LoadBuildPresetsAsync(SelectedProject.Id));
        SelectedUnrealBuildPreset = UnrealBuildPresets.FirstOrDefault(item =>
            item.PresetName.Equals(profile.PresetName, StringComparison.OrdinalIgnoreCase));
        UnrealBuildStatus = $"Build preset '{profile.PresetName}' saved.";
    }

    public void ApplySelectedUnrealBuildPreset()
    {
        if (SelectedUnrealBuildPreset is not { } profile) return;
        SelectedUnrealBuildEngine = UnrealBuildEngines.FirstOrDefault(item =>
            string.Equals(item.RootPath, profile.EngineRoot, StringComparison.OrdinalIgnoreCase)) ?? SelectedUnrealBuildEngine;
        SelectedUnrealBuildTarget = UnrealBuildTargets.FirstOrDefault(item => item.Id == profile.TargetId) ?? SelectedUnrealBuildTarget;
        SelectedUnrealBuildPlatform = profile.Platform;
        SelectedUnrealBuildConfiguration = profile.Configuration;
        UnrealLinuxToolchainPath = profile.LinuxToolchainPath;
        UnrealAndroidSdkPath = profile.AndroidSdkPath;
        UnrealAndroidNdkPath = profile.AndroidNdkPath;
        UnrealJavaHomePath = profile.JavaHomePath;
        UnrealBuildOutputPath = profile.OutputDirectory;
        UnrealBuildCookAndPackage = profile.CookAndPackage;
        UnrealBuildAutoConfigureToolchains = profile.AutoConfigureToolchains;
        UnrealBuildTimeoutMinutes = profile.TimeoutMinutes.ToString();
        UnrealBuildPresetName = profile.PresetName;
        UnrealBuildMaximumParallel = Math.Clamp(profile.MaximumParallelBuilds, 1, 4).ToString();
        UnrealBuildStatus = $"Build preset '{profile.PresetName}' applied.";
    }

    public async Task DeleteSelectedUnrealBuildPresetAsync()
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        if (plugin is null || SelectedProject is null || SelectedUnrealBuildPreset is null) return;
        string name = SelectedUnrealBuildPreset.PresetName;
        await plugin.DeleteBuildPresetAsync(SelectedProject.Id, name);
        ReplaceCollection(UnrealBuildPresets, await plugin.LoadBuildPresetsAsync(SelectedProject.Id));
        SelectedUnrealBuildPreset = UnrealBuildPresets.FirstOrDefault();
        UnrealBuildStatus = $"Build preset '{name}' deleted.";
    }

    public Task RunUnrealBuildRangeAsync()
    {
        if (UnrealBuildRangeFrom is null || UnrealBuildRangeTo is null) return Task.CompletedTask;
        Version from = ParseUnrealVersion(UnrealBuildRangeFrom.Version);
        Version to = ParseUnrealVersion(UnrealBuildRangeTo.Version);
        if (from > to) (from, to) = (to, from);
        UnrealEngineInstallation[] engines = UnrealBuildEngines
            .Where(engine => ParseUnrealVersion(engine.Version) >= from && ParseUnrealVersion(engine.Version) <= to)
            .OrderBy(engine => ParseUnrealVersion(engine.Version))
            .ToArray();
        return RunUnrealBuildMatrixAsync(engines);
    }

    public void CancelUnrealBuild() => _unrealBuildCancellation?.Cancel();

    private async Task RunUnrealBuildMatrixAsync(IReadOnlyList<UnrealEngineInstallation> engines)
    {
        IUnrealIntegrationPlugin? plugin = _pluginManager.GetPlugin<IUnrealIntegrationPlugin>();
        ProjectItemViewModel? project = SelectedProject;
        UnrealBuildTargetDescriptor? target = SelectedUnrealBuildTarget;
        if (plugin is null || project is null || target is null || _unrealBuildDiscovery is null || engines.Count == 0)
        {
            UnrealBuildStatus = "Discover engines and select a build target first.";
            return;
        }
        if (!int.TryParse(UnrealBuildMaximumParallel, out int maximumParallel) || maximumParallel is < 1 or > 4)
        {
            UnrealBuildStatus = "Parallel build count must be between 1 and 4.";
            return;
        }
        if (!int.TryParse(UnrealBuildTimeoutMinutes, out int timeoutMinutes) || timeoutMinutes is < 1 or > 1440)
        {
            UnrealBuildStatus = "Build timeout must be between 1 and 1440 minutes.";
            return;
        }

        _unrealBuildCancellation?.Cancel();
        _unrealBuildCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _unrealBuildCancellation = cancellation;
        IsUnrealBuildRunning = true;
        UnrealBuildResults.Clear();
        UnrealBuildDiagnostics.Clear();
        UnrealBuildLogLines.Clear();
        List<UnrealBuildProgress> logLines = [];
        object logGate = new();
        Stopwatch publishClock = Stopwatch.StartNew();
        OperationTaskViewModel trackedTask = BeginTrackedTask(
            $"Unreal build matrix ({engines.Count})", project.Name, target.DisplayName);
        trackedTask.State = "Running";
        try
        {
            UnrealBuildProfile activeProfile = CreateUnrealBuildProfile(project.Id, timeoutMinutes) with
            {
                PresetName = string.IsNullOrWhiteSpace(UnrealBuildPresetName) ? "Default" : UnrealBuildPresetName.Trim(),
                MaximumParallelBuilds = maximumParallel
            };
            await plugin.SaveBuildProfileAsync(activeProfile, cancellation.Token);
            using SemaphoreSlim buildSlots = new(maximumParallel, maximumParallel);
            ConcurrentBag<UnrealBuildResult> completed = [];

            async Task RunEngineAsync(UnrealEngineInstallation engine)
            {
                await buildSlots.WaitAsync(cancellation.Token);
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    string state = $"Building {target.DisplayName} with UE {engine.Version} for {SelectedUnrealBuildPlatform}...";
                    Dispatcher.UIThread.Post(() =>
                    {
                        UnrealBuildStatus = state;
                        trackedTask.Detail = state;
                    });
                IProgress<UnrealBuildProgress> progress = new Progress<UnrealBuildProgress>(item =>
                {
                    UnrealBuildProgress[]? snapshot = null;
                    lock (logGate)
                    {
                        logLines.Add(item);
                        if (logLines.Count > 5000) logLines.RemoveRange(0, Math.Min(500, logLines.Count - 5000));
                        if (publishClock.ElapsedMilliseconds >= 150 || item.Stream == "system")
                        {
                            publishClock.Restart();
                            snapshot = logLines.ToArray();
                        }
                    }
                    if (snapshot is not null)
                        Dispatcher.UIThread.Post(() => ReplaceCollection(UnrealBuildLogLines, snapshot), DispatcherPriority.Background);
                });
                UnrealBuildRequest request = CreateUnrealBuildRequest(project.Id, engine, target, timeoutMinutes);
                UnrealBuildResult result = await plugin.RunBuildAsync(request, progress, cancellation.Token);
                    completed.Add(result);
                    Dispatcher.UIThread.Post(() => UnrealBuildResults.Insert(0, result));
                _applicationLogService.Information(
                    "unreal-build",
                    $"engine={result.EngineVersion} target=\"{result.TargetName}\" platform={result.Platform} " +
                    $"exit={result.ExitCode} duration={result.Duration.TotalSeconds:N1}s log=\"{result.LogPath}\"",
                    project.RootPath);
                }
                finally
                {
                    buildSlots.Release();
                }
            }

            await Task.WhenAll(engines.Select(RunEngineAsync));
            UnrealBuildProgress[] finalLog;
            lock (logGate) finalLog = logLines.ToArray();
            ReplaceCollection(UnrealBuildLogLines, finalLog);
            UnrealBuildLog = string.Join(Environment.NewLine, finalLog.TakeLast(250)
                .Select(item => $"{item.Timestamp:HH:mm:ss} [{item.Stream}] {item.Text}"));
            int succeeded = completed.Count(result => result.Succeeded);
            int failed = engines.Count - succeeded;
            UnrealBuildStatus = $"Build matrix finished: {succeeded} succeeded, {failed} failed.";
            CompleteTrackedTask(trackedTask, failed == 0 ? "Completed" : "Failed", UnrealBuildStatus);
        }
        catch (OperationCanceledException)
        {
            UnrealBuildStatus = "Unreal build cancelled.";
            CompleteTrackedTask(trackedTask, "Cancelled", UnrealBuildStatus);
        }
        catch (Exception exception)
        {
            UnrealBuildStatus = exception.Message;
            CompleteTrackedTask(trackedTask, "Failed", exception.Message);
            _applicationLogService.Error("unreal-build", "local build failed", exception, project.RootPath);
        }
        finally
        {
            IsUnrealBuildRunning = false;
            if (ActiveOperations.Contains(trackedTask))
                CompleteTrackedTask(trackedTask, "Finished", UnrealBuildStatus);
            if (ReferenceEquals(_unrealBuildCancellation, cancellation)) _unrealBuildCancellation = null;
            cancellation.Dispose();
        }
    }

    private UnrealBuildProfile CreateUnrealBuildProfile(Guid projectId, int timeoutMinutes) => new(
        projectId,
        SelectedUnrealBuildEngine?.RootPath ?? string.Empty,
        SelectedUnrealBuildTarget?.Id ?? string.Empty,
        SelectedUnrealBuildPlatform,
        SelectedUnrealBuildConfiguration,
        UnrealLinuxToolchainPath,
        UnrealAndroidSdkPath,
        UnrealAndroidNdkPath,
        UnrealJavaHomePath,
        UnrealBuildOutputPath,
        UnrealBuildCookAndPackage,
        UnrealBuildAutoConfigureToolchains,
        timeoutMinutes,
        DateTimeOffset.UtcNow,
        string.IsNullOrWhiteSpace(UnrealBuildPresetName) ? "Default" : UnrealBuildPresetName.Trim(),
        int.TryParse(UnrealBuildMaximumParallel, out int maximumParallel) ? Math.Clamp(maximumParallel, 1, 4) : 1);

    private UnrealBuildRequest CreateUnrealBuildRequest(
        Guid projectId,
        UnrealEngineInstallation engine,
        UnrealBuildTargetDescriptor target,
        int timeoutMinutes) => new(
        projectId,
        _unrealBuildDiscovery?.ProjectFile ?? UnrealProjectPath,
        engine,
        target,
        SelectedUnrealBuildPlatform,
        SelectedUnrealBuildConfiguration,
        UnrealLinuxToolchainPath,
        UnrealAndroidSdkPath,
        UnrealAndroidNdkPath,
        UnrealJavaHomePath,
        UnrealBuildOutputPath,
        UnrealBuildCookAndPackage,
        UnrealBuildAutoConfigureToolchains,
        timeoutMinutes);

    private static Version ParseUnrealVersion(string value)
    {
        string normalized = string.Join('.', value.Split('.').Take(3));
        return Version.TryParse(normalized, out Version? version) ? version : new Version();
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

    public async Task<bool> CloneGitRepositoryAsync(
        string remoteUrl,
        string destinationPath,
        bool recurseSubmodules,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        bool succeeded = false;
        await RunOperationAsync("Cloning remote repository…", async () =>
        {
            await _gitService.CloneAsync(remoteUrl, fullPath, recurseSubmodules, cancellationToken);
            GitRepositoryStatus status = await _gitService.GetStatusAsync(fullPath, cancellationToken);
            ProjectDefinition definition = CreateGitProject(status.RootPath);
            await SaveAndSelectProjectAsync(definition);
            succeeded = true;
        }, "Repository cloned and added to CyRevision");
        return succeeded;
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

    public async Task RemoveSelectedProjectAsync(bool removeGeneratedCaches = false)
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

        ProjectItemViewModel projectToRemove = SelectedProject;
        Guid projectId = projectToRemove.Id;
        int previousIndex = Projects.IndexOf(projectToRemove);
        string? cacheCleanupWarning = null;
        _projectLoadCancellation?.Cancel();
        _codeWorkspaceCancellation?.Cancel();
        _codePreviewCancellation?.Cancel();
        _automaticGitRefreshCancellation?.Cancel();
        _gitCacheSaveCancellation?.Cancel();
        await RunOperationAsync("Removing project from CyRevision…", async () =>
        {
            await _projectCatalog.RemoveAsync(projectId);
            ProjectItemViewModel? item = Projects.FirstOrDefault(project => project.Id == projectId);
            if (item is not null)
            {
                Projects.Remove(item);
            }

            EvictProjectCaches(projectId);
            if (removeGeneratedCaches)
            {
                try
                {
                    await Task.Run(() => DeleteProjectGeneratedCache(projectToRemove.RootPath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    cacheCleanupWarning = exception.Message;
                    _applicationLogService.Warning(
                        "projects",
                        $"cache cleanup failed project=\"{projectToRemove.Name}\" error=\"{exception.Message}\"",
                        projectToRemove.RootPath);
                }
            }

            await PersistProjectOrderAsync();
            SelectedProject = Projects.Count == 0
                ? null
                : Projects[Math.Clamp(previousIndex, 0, Projects.Count - 1)];
            if (SelectedProject is null)
            {
                ClearRepositoryView();
            }
            NotifyProjectOrderStateChanged();
            _applicationLogService.Information(
                "projects",
                $"removed from catalog project=\"{projectToRemove.Name}\" generated-cache-removed={removeGeneratedCaches}",
                projectToRemove.RootPath);
        }, removeGeneratedCaches
            ? "Project removed from CyRevision · generated caches cleaned"
            : "Project removed from CyRevision · files on disk were not changed");

        if (cacheCleanupWarning is not null)
        {
            StatusMessage = $"Project removed, but its generated cache could not be fully cleaned: {cacheCleanupWarning}";
        }
    }

    public async Task MoveSelectedProjectAsync(int offset)
    {
        if (SelectedProject is null || offset == 0) return;
        int currentIndex = Projects.IndexOf(SelectedProject);
        int targetIndex = Math.Clamp(currentIndex + Math.Sign(offset), 0, Projects.Count - 1);
        if (currentIndex < 0 || currentIndex == targetIndex) return;

        Projects.Move(currentIndex, targetIndex);
        await PersistProjectOrderAsync();
        NotifyProjectOrderStateChanged();
        StatusMessage = $"{SelectedProject.Name} moved to position {targetIndex + 1}";
    }

    public async Task SetSelectedProjectAccentColorAsync(string? accentColor)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || string.IsNullOrWhiteSpace(accentColor) ||
            !ProjectAccentColors.Contains(accentColor, StringComparer.OrdinalIgnoreCase) ||
            string.Equals(project.AccentColor, accentColor, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ProjectDefinition updated = project.Definition with { AccentColor = accentColor };
        updated.Validate();
        project.Update(updated);
        await _projectCatalog.UpsertAsync(updated);
        OnPropertyChanged(nameof(SelectedProjectAccentColor));
        StatusMessage = $"Color saved for {project.Name}";
    }

    public async Task SetSelectedProjectSidebarGroupAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;
        string? groupName = string.IsNullOrWhiteSpace(SidebarGroupDraft)
            ? null
            : SidebarGroupDraft.Trim();
        ProjectDefinition updated = project.Definition with { SidebarGroup = groupName };
        updated.Validate();
        project.Update(updated);
        await _projectCatalog.UpsertAsync(updated);
        RebuildProjectSidebarGroups();
        StatusMessage = groupName is null
            ? $"{project.Name} moved to General"
            : $"{project.Name} moved to {groupName}";
    }

    public void RememberProjectGroupExpansion(ProjectSidebarGroupViewModel group)
    {
        _projectGroupExpansion[group.Name] = group.IsExpanded;
    }

    private void RebuildProjectSidebarGroups()
    {
        foreach (ProjectSidebarGroupViewModel existing in ProjectSidebarGroups)
            _projectGroupExpansion[existing.Name] = existing.IsExpanded;

        ProjectItemViewModel? selection = SelectedProject;
        var grouped = Projects
            .Select((project, index) => new { project, index })
            .GroupBy(item => item.project.SidebarGroup, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(item => item.index));

        ProjectSidebarGroups.Clear();
        foreach (var group in grouped)
        {
            bool expanded = !_projectGroupExpansion.TryGetValue(group.Key, out bool saved) || saved;
            ProjectSidebarGroupViewModel item = new(group.Key, group.OrderBy(entry => entry.index).Select(entry => entry.project), expanded)
            {
                SelectedProject = group.Any(entry => ReferenceEquals(entry.project, selection)) ? selection : null
            };
            ProjectSidebarGroups.Add(item);
        }
    }

    public async Task SetProjectServiceAutoStartAsync(bool? sync = null, bool? vpn = null)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;
        ProjectDefinition updated = project.Definition with
        {
            StartSyncAutomatically = sync ?? project.Definition.StartSyncAutomatically,
            StartVpnAutomatically = vpn ?? project.Definition.StartVpnAutomatically
        };
        updated.Validate();
        project.Update(updated);
        await _projectCatalog.UpsertAsync(updated);
        OnPropertyChanged(nameof(StartSyncAutomatically));
        OnPropertyChanged(nameof(StartVpnAutomatically));
        StatusMessage = "Project service startup preferences saved";
    }

    public void ApplySelectedProjectLicenseTemplate()
    {
        ProjectItemViewModel? project = SelectedProject;
        ProjectLicenseTemplate? template = SelectedProjectLicenseTemplate;
        if (project is null || template is null) return;

        int year = int.TryParse(ProjectLicenseYear, out int parsedYear) && parsedYear is >= 1 and <= 9999
            ? parsedYear
            : DateTimeOffset.Now.Year;
        ProjectLicenseYear = year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ProjectLicenseDraft = _projectLicenseService.RenderTemplate(
            template.Id,
            ProjectLicenseHolder,
            year,
            project.Name);
        ProjectLicenseStatus = $"{template.Name} template prepared locally. Review the complete text before saving.";
    }

    public async Task RefreshProjectLicenseAsync()
    {
        int loadVersion = Interlocked.Increment(ref _projectLicenseLoadVersion);
        await LoadProjectLicenseAsync(SelectedProject, loadVersion);
    }

    public async Task ImportProjectLicenseDraftAsync(string path)
    {
        try
        {
            string draft = await _projectLicenseService.ReadDraftAsync(path);
            string detectedId = ProjectLicenseService.DetectTemplateId(draft);
            ProjectLicenseDraft = draft;
            ProjectLicenseDetectedId = detectedId;
            SelectedProjectLicenseTemplate = ProjectLicenseTemplates.FirstOrDefault(template =>
                string.Equals(template.Id, detectedId, StringComparison.OrdinalIgnoreCase))
                ?? ProjectLicenseTemplates.First(template => template.Id == "Custom");
            ProjectLicenseStatus = $"Imported {Path.GetFileName(path)} into the editor. The project has not been modified.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ProjectLicenseStatus = $"Could not import the license: {exception.Message}";
        }
    }

    public async Task SaveProjectLicenseAsync(bool overwrite)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !CanSaveProjectLicense) return;

        string fileName = ProjectLicenseService.ValidateFileName(ProjectLicenseFileName);
        string draft = ProjectLicenseDraft;
        await RunOperationAsync("Saving project license…", async () =>
        {
            await _projectLicenseService.SaveAsync(project.RootPath, fileName, draft, overwrite);
            _applicationLogService.Information(
                "project.license",
                $"saved file=\"{fileName}\" detected=\"{ProjectLicenseService.DetectTemplateId(draft)}\" overwrite={overwrite}",
                project.RootPath);
            int loadVersion = Interlocked.Increment(ref _projectLicenseLoadVersion);
            await LoadProjectLicenseAsync(project, loadVersion);
        }, $"{fileName} saved for {project.Name}");
    }

    private async Task LoadProjectLicenseAsync(ProjectItemViewModel? project, int loadVersion)
    {
        if (project is null)
        {
            _projectLicenseSnapshot = null;
            ProjectLicenseDraft = string.Empty;
            ProjectLicenseFileName = "LICENSE";
            ProjectLicenseDetectedId = "None";
            ProjectLicenseStatus = "Select a project to inspect its license.";
            IsProjectLicenseLoading = false;
            OnPropertyChanged(nameof(ProjectLicenseTargetExists));
            OnPropertyChanged(nameof(CanSaveProjectLicense));
            return;
        }

        IsProjectLicenseLoading = true;
        ProjectLicenseStatus = $"Inspecting the license for {project.Name}…";
        try
        {
            ProjectLicenseSnapshot snapshot = await _projectLicenseService.InspectAsync(project.RootPath);
            if (loadVersion != _projectLicenseLoadVersion || SelectedProject?.Id != project.Id) return;

            _projectLicenseSnapshot = snapshot;
            ProjectLicenseFileName = snapshot.FileName;
            ProjectLicenseDraft = snapshot.Content;
            ProjectLicenseDetectedId = snapshot.Exists ? snapshot.DetectedTemplateId : "None";
            SelectedProjectLicenseTemplate = ProjectLicenseTemplates.FirstOrDefault(template =>
                string.Equals(template.Id, snapshot.DetectedTemplateId, StringComparison.OrdinalIgnoreCase))
                ?? ProjectLicenseTemplates.First(template => template.Id == "Custom");
            ProjectLicenseStatus = snapshot.Exists
                ? $"{snapshot.FileName} · {snapshot.DetectedTemplateId} · {FormatByteSize(snapshot.Size)} · updated {snapshot.LastModifiedAt?.ToLocalTime():g}"
                : "No root-level license file was found. Select a starter template or write custom terms.";
            OnPropertyChanged(nameof(ProjectLicenseTargetExists));
            OnPropertyChanged(nameof(CanSaveProjectLicense));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (loadVersion != _projectLicenseLoadVersion || SelectedProject?.Id != project.Id) return;
            _projectLicenseSnapshot = null;
            ProjectLicenseDetectedId = "Unknown";
            ProjectLicenseStatus = $"Could not inspect the project license: {exception.Message}";
            _applicationLogService.Warning("project.license", exception.Message, project.RootPath);
        }
        finally
        {
            if (loadVersion == _projectLicenseLoadVersion && SelectedProject?.Id == project.Id)
                IsProjectLicenseLoading = false;
        }
    }

    public Task RefreshAsync() => RunOperationAsync("Refreshing tracked and untracked files…", async () =>
    {
        await RefreshCoreAsync(includeUntrackedFiles: true);
        await LoadAdvisoryReservationsCoreAsync();
    }, "Repository, untracked files and reservations refreshed");

    public async Task RefreshDetectedChangesAsync(
        int eventCount,
        bool includeUntrackedFiles,
        bool gitMetadataChanged,
        bool watcherOverflowed)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled || IsProjectLoading)
        {
            return;
        }

        _automaticGitRefreshCancellation?.Cancel();
        _automaticGitRefreshCancellation?.Dispose();
        _gitCacheSaveCancellation?.Cancel();
        _gitCacheSaveCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _automaticGitRefreshCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        OperationTaskViewModel? trackedTask = includeUntrackedFiles
            ? BeginTrackedTask(
                $"Detect changes in {project.Name}",
                project.Name,
                watcherOverflowed ? "Watcher overflow · validating the complete working tree" : "Scanning new and removed files")
            : null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            GitRepositoryStatus detectedStatus = includeUntrackedFiles
                ? await _gitService.GetDetailedStatusAsync(project.RootPath, token)
                : await _gitService.GetQuickStatusAsync(project.RootPath, token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id) return;

            bool changesAreIdentical;
            if (includeUntrackedFiles)
            {
                changesAreIdentical = HaveSameChanges(detectedStatus.Changes, Changes);
            }
            else
            {
                changesAreIdentical = HaveSameTrackedChanges(detectedStatus.Changes, Changes);
                HashSet<string> trackedPaths = detectedStatus.Changes
                    .Select(change => NormalizeChangePath(change.Path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                GitChange[] mergedChanges = detectedStatus.Changes
                    .Concat(Changes
                        .Where(change => change.IsUntracked &&
                                         !trackedPaths.Contains(NormalizeChangePath(change.Path)))
                        .Select(change => change.Change))
                    .ToArray();
                detectedStatus = detectedStatus with { Changes = mergedChanges };
            }

            if (changesAreIdentical)
            {
                Interlocked.Increment(ref _workingTreeDiffGeneration);
            }
            ApplyPrimaryGitStatus(project, detectedStatus, rebuildChanges: !changesAreIdentical);
            CacheCurrentProjectSession(project);
            ScheduleProjectGitCacheSave(project, detectedStatus);
            if (changesAreIdentical && SelectedChange is not null)
            {
                await LoadSelectedDiffForExternalAsync();
            }
            string mode = includeUntrackedFiles ? "complete" : "tracked";
            StatusMessage = $"{project.Name} refreshed automatically · {eventCount:N0} event(s) · {mode} scan";
            _applicationLogService.Debug(
                "git.watch",
                $"refresh complete events={eventCount} mode={mode} metadata={gitMetadataChanged.ToString().ToLowerInvariant()} " +
                $"overflow={watcherOverflowed.ToString().ToLowerInvariant()} changes={detectedStatus.Changes.Count} " +
                $"duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms",
                project.RootPath);
            if (trackedTask is not null)
            {
                CompleteTrackedTask(
                    trackedTask,
                    "Completed",
                    $"{detectedStatus.Changes.Count:N0} change(s) in {stopwatch.Elapsed.TotalMilliseconds:N0} ms");
            }
        }
        catch (OperationCanceledException)
        {
            if (trackedTask is not null)
            {
                CompleteTrackedTask(trackedTask, "Cancelled", "Superseded by newer file-system events");
            }
        }
        catch (Exception exception)
        {
            if (trackedTask is not null)
            {
                CompleteTrackedTask(trackedTask, "Failed", exception.Message);
            }
            _applicationLogService.Warning(
                "git.watch",
                $"automatic refresh failed: {exception.Message}",
                project.RootPath);
        }
        finally
        {
            if (trackedTask is not null && ActiveOperations.Contains(trackedTask))
            {
                CompleteTrackedTask(trackedTask, "Cancelled", "Project changed before refresh completed");
            }
            if (ReferenceEquals(_automaticGitRefreshCancellation, cancellation))
            {
                _automaticGitRefreshCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private static bool HaveSameTrackedChanges(
        IReadOnlyList<GitChange> detectedChanges,
        IEnumerable<GitChangeViewModel> currentChanges)
    {
        GitChangeViewModel[] tracked = currentChanges.Where(change => change.IsTracked).ToArray();
        return HaveSameChanges(detectedChanges, tracked);
    }

    private static bool HaveSameChanges(
        IReadOnlyList<GitChange> detectedChanges,
        IEnumerable<GitChangeViewModel> currentChanges)
    {
        Dictionary<string, GitChange> detectedByPath = detectedChanges
            .GroupBy(change => NormalizeChangePath(change.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        int currentCount = 0;
        foreach (GitChangeViewModel current in currentChanges)
        {
            currentCount++;
            if (!detectedByPath.TryGetValue(NormalizeChangePath(current.Path), out GitChange? detected) ||
                detected != current.Change)
            {
                return false;
            }
        }
        return currentCount == detectedByPath.Count;
    }

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

    public void ShowCommitExplorerRevisions(
        IEnumerable<GitRevision> revisions,
        GitRevision? selectedRevision = null)
    {
        _commitExplorerSourceRevisions = revisions
            .GroupBy(revision => revision.Hash, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        ApplyCommitExplorerFilter();
        GitRevision? selection = selectedRevision is null
            ? CommitExplorerRevisions.FirstOrDefault()
            : CommitExplorerRevisions.FirstOrDefault(revision => revision.Hash == selectedRevision.Hash)
              ?? selectedRevision;
        SelectedCommitExplorerRevision = null;
        SelectedCommitExplorerRevision = selection;
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

    public async Task AddCommitExplorerFilesToMultiRestoreAsync(
        GitRevision revision,
        IReadOnlyCollection<GitCommitFileChange> files)
    {
        if (files.Count == 0) return;
        if (!string.Equals(_multiRestoreLoadedHash, revision.Hash, StringComparison.Ordinal))
        {
            await LoadMultiRestoreCommitAsync(revision);
        }

        HashSet<string> selectedPaths = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MultiRestoreFileViewModel item in MultiRestoreFiles)
        {
            item.IsSelected = selectedPaths.Contains(item.Path);
        }
        SelectedMultiRestoreFile = MultiRestoreFiles.FirstOrDefault(item => item.IsSelected);
        MultiRestoreSummary = $"{revision.ShortHash} · {selectedPaths.Count} file(s) added from Commit Explorer.";
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
        int loadVersion = Interlocked.Increment(ref _multiRestoreDiffLoadVersion);
        if (SelectedProject is null || MultiRestoreCommit is null || item is null)
        {
            IsMultiRestoreDiffLoading = false;
            MultiRestoreDiffPreviewImage = null;
            MultiRestoreDiffPresentationSummary = string.Empty;
            MultiRestoreDiff = "Select a file to inspect its commit patch.";
            return;
        }

        ProjectItemViewModel project = SelectedProject;
        string commitHash = MultiRestoreCommit.Hash;
        IsMultiRestoreDiffLoading = true;
        MultiRestoreDiffPreviewImage = null;
        MultiRestoreDiffPresentationSummary = string.Empty;
        MultiRestoreDiff = $"Loading diff for {item.Path}…";
        try
        {
            string diff = await _gitService.GetCommitDiffAsync(
                project.RootPath,
                commitHash,
                item.Path);
            if (loadVersion != _multiRestoreDiffLoadVersion ||
                SelectedProject?.Id != project.Id ||
                MultiRestoreCommit?.Hash != commitHash ||
                SelectedMultiRestoreFile?.Path != item.Path) return;
            MultiRestoreDiff = diff;
            if (string.IsNullOrWhiteSpace(diff))
            {
                MultiRestoreDiff = item.IsLfsObject
                    ? "Git LFS object — the safety preview verifies that the selected object exists locally."
                    : "No textual patch is available for this file.";
            }
            FilePresentationResult? presentation = await TryCreateRevisionPairPresentationAsync(
                project,
                $"{commitHash}^1",
                commitHash,
                item.Path,
                CancellationToken.None);
            if (loadVersion != _multiRestoreDiffLoadVersion ||
                SelectedProject?.Id != project.Id ||
                MultiRestoreCommit?.Hash != commitHash ||
                SelectedMultiRestoreFile?.Path != item.Path) return;
            ApplyMultiRestorePresentation(presentation);
        }
        catch (Exception exception)
        {
            if (loadVersion == _multiRestoreDiffLoadVersion) MultiRestoreDiff = exception.Message;
        }
        finally
        {
            if (loadVersion == _multiRestoreDiffLoadVersion) IsMultiRestoreDiffLoading = false;
        }
    }

    private async Task LoadCherryPickDiffAsync(CherryPickCommitViewModel? item)
    {
        int loadVersion = Interlocked.Increment(ref _cherryPickDiffLoadVersion);
        if (SelectedProject is null || item is null)
        {
            IsCherryPickDiffLoading = false;
            CherryPickDiff = "Select a commit to inspect its patch.";
            return;
        }

        ProjectItemViewModel project = SelectedProject;
        IsCherryPickDiffLoading = true;
        CherryPickDiff = $"Loading commit {item.ShortHash}…";
        try
        {
            string diff = await _gitService.GetCommitDiffAsync(project.RootPath, item.Hash);
            if (loadVersion != _cherryPickDiffLoadVersion ||
                SelectedProject?.Id != project.Id ||
                SelectedCherryPickCommit?.Hash != item.Hash) return;
            CherryPickDiff = diff;
        }
        catch (Exception exception)
        {
            if (loadVersion == _cherryPickDiffLoadVersion) CherryPickDiff = exception.Message;
        }
        finally
        {
            if (loadVersion == _cherryPickDiffLoadVersion) IsCherryPickDiffLoading = false;
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

        string[] paths = Changes
            .Where(change => change.IsIncluded && !change.IsLocalOnly && !change.Change.IsStaged)
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    public async Task ScanAllUntrackedFilesAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled)
        {
            return;
        }

        await RunOperationAsync(
            $"Scanning every untracked file in {project.Name}…",
            async () =>
            {
                GitRepositoryStatus status = await _gitService.GetDetailedStatusAsync(project.RootPath);
                if (SelectedProject?.Id != project.Id)
                {
                    return;
                }
                ApplyPrimaryGitStatus(project, status);
            },
            $"Detailed untracked scan complete for {project.Name}");
    }

    public async Task LoadLfsInventoryAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled)
        {
            return;
        }

        await RunOperationAsync(
            $"Scanning Git LFS files in {project.Name}…",
            async () =>
            {
                IReadOnlyList<LfsTrackedFile> files = await _gitService.GetLfsTrackedFilesAsync(project.RootPath);
                if (SelectedProject?.Id != project.Id)
                {
                    return;
                }
                ReplaceCollection(LfsFiles, files);
                ReplaceCollection(LfsFileTree, BuildLfsFileTree(files));
                IsLfsInventoryLoaded = true;
                LfsTrackedFile? selected = files.FirstOrDefault(file => file.Path == SelectedLfsFile?.Path)
                                           ?? files.FirstOrDefault();
                SelectedLfsFile = selected;
            },
            $"{project.Name} · {LfsFiles.Count:N0} Git LFS file(s) loaded");
    }

    private static IReadOnlyList<LfsFileTreeNode> BuildLfsFileTree(IReadOnlyList<LfsTrackedFile> files)
    {
        List<LfsFileTreeNode> roots = [];
        Dictionary<string, LfsFileTreeNode> folders = new(StringComparer.OrdinalIgnoreCase);
        foreach (LfsTrackedFile file in files.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            string normalized = file.Path.Replace('\\', '/').Trim('/');
            string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string parentPath = string.Empty;
            LfsFileTreeNode? parent = null;
            for (int index = 0; index < parts.Length - 1; index++)
            {
                string path = parentPath.Length == 0 ? parts[index] : $"{parentPath}/{parts[index]}";
                if (!folders.TryGetValue(path, out LfsFileTreeNode? folder))
                {
                    folder = new LfsFileTreeNode(parts[index], path, true);
                    folders[path] = folder;
                    if (parent is null) roots.Add(folder);
                    else parent.Children.Add(folder);
                }
                parent = folder;
                parentPath = path;
            }
            LfsFileTreeNode leaf = new(parts[^1], normalized, false, file);
            if (parent is null) roots.Add(leaf);
            else parent.Children.Add(leaf);
        }
        return roots;
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

    public void IncludeAllPreparedChanges()
    {
        SetAllPreparedChangesIncluded(true);
    }

    public void KeepAllPreparedChanges()
    {
        SetAllPreparedChangesIncluded(false);
    }

    public void DeselectVersionedChanges() => SetPreparedGroupIncluded(isTracked: true, isIncluded: false);

    public void DeselectUnversionedChanges() => SetPreparedGroupIncluded(isTracked: false, isIncluded: false);

    private void SetPreparedGroupIncluded(bool isTracked, bool isIncluded)
    {
        _suspendChangePreparationSummary = true;
        try
        {
            foreach (GitChangeViewModel change in PreparedChanges.Where(change => change.IsTracked == isTracked))
                change.IsIncluded = isIncluded;
        }
        finally
        {
            _suspendChangePreparationSummary = false;
            UpdateChangePreparationSummary();
        }
    }

    private void SetAllPreparedChangesIncluded(bool isIncluded)
    {
        _suspendChangePreparationSummary = true;
        try
        {
            foreach (GitChangeViewModel change in PreparedChanges)
            {
                change.IsIncluded = isIncluded;
            }
        }
        finally
        {
            _suspendChangePreparationSummary = false;
            if (_changePreparationSummaryPending)
            {
                _changePreparationSummaryPending = false;
                UpdateChangePreparationSummary();
            }
        }
    }

    public async Task SetChangeLocalOnlyAsync(GitChangeViewModel change, bool isLocalOnly)
    {
        ArgumentNullException.ThrowIfNull(change);
        await SetChangesLocalOnlyAsync([change], isLocalOnly);
    }

    public async Task SetChangesLocalOnlyAsync(
        IReadOnlyCollection<GitChangeViewModel> changes,
        bool isLocalOnly)
    {
        if (SelectedProject is null)
        {
            return;
        }

        GitChangeViewModel[] eligible = changes
            .Where(change => change.IsUntracked && change.IsLocalOnly != isLocalOnly)
            .DistinctBy(change => NormalizeChangePath(change.Path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (eligible.Length == 0)
        {
            StatusMessage = isLocalOnly
                ? "Select one or more untracked files to move to Local only."
                : "Select one or more Local-only files to restore to the change list.";
            return;
        }

        foreach (GitChangeViewModel change in eligible)
        {
            string path = NormalizeChangePath(change.Path);
            if (isLocalOnly) _localOnlyChangePaths.Add(path);
            else _localOnlyChangePaths.Remove(path);
        }

        string selectedPath = eligible[0].Path;
        RebuildChangePreparation(Changes.Select(item => item.Change).ToArray(), selectedPath);
        await SaveLocalChangePreferencesAsync();
        StatusMessage = isLocalOnly
            ? $"{eligible.Length:N0} file(s) moved to Local only"
            : $"{eligible.Length:N0} file(s) returned to Untracked files";
    }

    public Task SetSelectedChangeLocalOnlyAsync(bool isLocalOnly) => SelectedChange is null
        ? Task.CompletedTask
        : SetChangeLocalOnlyAsync(SelectedChange, isLocalOnly);

    public async Task RollbackChangesAsync(IReadOnlyCollection<GitChangeViewModel> changes)
    {
        if (SelectedProject is null || changes.Count == 0)
        {
            return;
        }

        GitChange[] targets = changes
            .Select(change => change.Change)
            .DistinctBy(change => NormalizeChangePath(change.Path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await RunOperationAsync("Rolling back selected changes…", async () =>
        {
            await _gitService.DiscardChangesAsync(SelectedProject.RootPath, targets);
            foreach (GitChange target in targets.Where(target => target.Kind == GitChangeKind.Untracked))
            {
                _localOnlyChangePaths.Remove(NormalizeChangePath(target.Path));
            }
            await SaveLocalChangePreferencesAsync();
            await RefreshCoreAsync();
        }, $"Rolled back {targets.Length} file(s)");
    }

    public Task RollbackSelectedChangeAsync() => SelectedChange is null
        ? Task.CompletedTask
        : RollbackChangesAsync([SelectedChange]);

    public Task RollbackIncludedChangesAsync() => RollbackChangesAsync(
        Changes.Where(change => change.IsIncluded && !change.IsLocalOnly).ToArray());

    public async Task DeleteChangesAsync(IReadOnlyCollection<GitChangeViewModel> changes)
    {
        string[] paths = changes
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await DeleteWorkingTreePathsAsync(paths);
    }

    public async Task DeleteWorkingTreePathsAsync(IReadOnlyCollection<string> paths)
    {
        if (SelectedProject is null || paths.Count == 0)
        {
            return;
        }

        string[] uniquePaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeChangePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (uniquePaths.Length == 0) return;

        await RunOperationAsync("Deleting selected working-tree files…", async () =>
        {
            await _gitService.DeleteWorkingTreePathsAsync(SelectedProject.RootPath, uniquePaths);
            _localOnlyChangePaths.RemoveWhere(localPath => uniquePaths.Any(path =>
                string.Equals(localPath, path, StringComparison.OrdinalIgnoreCase) ||
                localPath.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)));
            await SaveLocalChangePreferencesAsync();
            await RefreshCoreAsync();
        }, $"Deleted {uniquePaths.Length:N0} working-tree path(s)");
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

        string[] paths = Changes
            .Where(change => change.IsIncluded && !change.IsLocalOnly)
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            StatusMessage = "Aucune modification à enregistrer";
            return;
        }

        string[] excludedStagedPaths = Changes
            .Where(change => change.Change.IsStaged && !paths.Contains(change.Path, StringComparer.OrdinalIgnoreCase))
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string message = CommitMessage;
        await RunOperationAsync("Création de la révision…", async () =>
        {
            if (excludedStagedPaths.Length > 0)
            {
                await _gitService.UnstageAsync(SelectedProject.RootPath, excludedStagedPaths);
            }
            await _gitService.CreateRevisionAsync(SelectedProject.RootPath, message, paths);
            CommitMessage = string.Empty;
            if (_syncEngine is not null && SelectedProject.Definition.Features.PeerSyncEnabled)
            {
                await ExchangeGitCoreAsync();
            }
            await RefreshCoreAsync();
        }, "Révision créée");
    }

    private void ApplyBranchFilter()
    {
        string[] terms = BranchSearch.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Matches(GitBranch branch) => terms.Length == 0 || terms.All(term =>
            branch.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            branch.Scope.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            branch.PublicationStatus.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            branch.SyncStatus.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            branch.TipAuthorName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            branch.TipSubject.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (branch.RemoteName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));

        IOrderedEnumerable<GitBranch> ordered = BranchSort switch
        {
            "Status" => Branches.Where(Matches)
                .OrderByDescending(branch => branch.IsCurrent)
                .ThenBy(branch => branch.PublicationStatus, StringComparer.OrdinalIgnoreCase)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase),
            "Last update" => Branches.Where(Matches)
                .OrderByDescending(branch => branch.TipAuthoredAt)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase),
            "Last author" => Branches.Where(Matches)
                .OrderBy(branch => branch.TipAuthorName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(branch => branch.TipAuthoredAt)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase),
            "Ahead" => Branches.Where(Matches)
                .OrderByDescending(branch => branch.AheadBy)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase),
            "Behind" => Branches.Where(Matches)
                .OrderByDescending(branch => branch.BehindBy)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase),
            _ => Branches.Where(Matches)
                .OrderByDescending(branch => branch.IsCurrent)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
        };
        ReplaceCollection(FilteredBranches, ordered);
    }

    private async Task LoadSelectedBranchHistoryAsync(GitBranch? branch)
    {
        int version = Interlocked.Increment(ref _selectedBranchHistoryLoadVersion);
        _branchHistoryCancellation?.Cancel();
        _branchHistoryCancellation?.Dispose();
        _branchHistoryCancellation = null;
        SelectedBranchHistory.Clear();
        SelectedBranchRevision = null;
        SelectedBranchDetails = null;
        if (SelectedProject is null || branch is null)
        {
            SelectedBranchSummary = "Select a branch to inspect its commits without switching.";
            return;
        }

        ProjectItemViewModel project = SelectedProject;
        string cacheKey = $"{project.Id}:{branch.Name}:{branch.ShortCommitHash}";
        if (_branchHistoryCache.TryGetValue(cacheKey, out CachedBranchInspection? cached) && cached is not null)
        {
            ApplySelectedBranchInspection(branch, cached.History, cached.Details);
            return;
        }

        CancellationTokenSource cancellation = new();
        _branchHistoryCancellation = cancellation;
        SelectedBranchSummary = $"{branch.PublicationStatus} | {branch.SyncStatus} | loading commits...";
        try
        {
            Task<IReadOnlyList<GitRevision>> historyTask = _gitService.GetHistoryForReferenceAsync(
                project.RootPath,
                branch.Name,
                maximumCount: 150,
                cancellation.Token);
            Task<GitBranchDetails> detailsTask = _gitService.GetBranchDetailsAsync(
                project.RootPath,
                branch.Name,
                cancellation.Token);
            await Task.WhenAll(historyTask, detailsTask);
            if (version != _selectedBranchHistoryLoadVersion ||
                SelectedProject?.Id != project.Id ||
                SelectedBranch?.Name != branch.Name) return;
            IReadOnlyList<GitRevision> history = await historyTask;
            GitBranchDetails details = await detailsTask;
            _branchHistoryCache.Set(cacheKey, new CachedBranchInspection(history, details));
            ApplySelectedBranchInspection(branch, history, details);
        }
        catch (OperationCanceledException)
        {
            // A newer branch selection superseded this read-only inspection.
        }
        catch (Exception exception)
        {
            if (version == _selectedBranchHistoryLoadVersion && SelectedProject?.Id == project.Id)
            {
                SelectedBranchDetails = null;
                SelectedBranchSummary = $"Unable to read {branch.Name}: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_branchHistoryCancellation, cancellation))
            {
                _branchHistoryCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void ApplySelectedBranchInspection(
        GitBranch branch,
        IReadOnlyList<GitRevision> history,
        GitBranchDetails details)
    {
        ReplaceCollection(SelectedBranchHistory, history);
        SelectedBranchRevision = history.FirstOrDefault();
        SelectedBranchDetails = details;
        SelectedBranchSummary = $"{branch.PublicationStatus} | {branch.SyncStatus} | " +
                                $"{history.Count:N0} recent commit(s) | " +
                                $"{details.UniqueCommitText} vs {details.ComparisonBaseText} | " +
                                branch.TrackingText;
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

    private async Task RefreshHistoricalWorktreesLegacyAsync()
    {
        if (SelectedProject is null) return;
        IReadOnlyList<GitHistoricalWorktree> worktrees = await _gitService.GetHistoricalWorktreesAsync(SelectedProject.RootPath);
        GitHistoricalWorktree[] managed = worktrees.Where(item => item.IsManagedByCyRevision).ToArray();
        ReplaceCollection(HistoricalWorktrees, managed);
        SelectedHistoricalWorktree = managed.FirstOrDefault();
        HistoricalWorktreeStatus = managed.Length == 0
            ? "No isolated historical worktree."
            : $"{managed.Length:N0} isolated worktree(s) · main repository remains unchanged.";
    }

    private async Task RemoveSelectedHistoricalWorktreeLegacyAsync(bool force)
    {
        if (SelectedProject is null || SelectedHistoricalWorktree is null) return;
        string path = SelectedHistoricalWorktree.Path;
        await RunOperationAsync("Removing historical worktree...", async () =>
        {
            await _gitService.RemoveHistoricalWorktreeAsync(SelectedProject.RootPath, path, force);
            await RefreshHistoricalWorktreesAsync();
        }, "Historical worktree removed");
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
            try
            {
                await _gitService.MergeBranchAsync(SelectedProject.RootPath, branchName);
            }
            catch (GitOperationException)
            {
                await RefreshCoreAsync();
                throw;
            }
            await RefreshCoreAsync();
        }, $"Branche {branchName} intégrée");
    }

    public async Task MergeCurrentBranchIntoSelectedAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        GitBranch? target = SelectedBranch;
        GitBranch? source = Branches.FirstOrDefault(branch => branch.IsCurrent && !branch.IsRemote);
        if (project is null || target is null || source is null || target.IsCurrent || target.IsRemote)
            return;

        string targetName = target.Name;
        string sourceName = source.Name;
        await RunOperationAsync($"Merging {sourceName} into {targetName}…", async () =>
        {
            GitRepositoryStatus status = await _gitService.GetStatusAsync(project.RootPath);
            if (status.Changes.Count > 0)
                throw new GitOperationException("The working tree contains local changes. Commit, stash, or keep them before switching branches for Merge to.");
            await _gitService.CheckoutBranchAsync(project.RootPath, targetName);
            try
            {
                await _gitService.MergeBranchAsync(project.RootPath, sourceName);
            }
            catch (GitOperationException)
            {
                await RefreshCoreAsync();
                throw;
            }
            await RefreshCoreAsync();
        }, $"{sourceName} merged into {targetName}");
    }

    public async Task<GitLocalBranchRemovalAnalysis?> AnalyzeSelectedLocalBranchRemovalAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        GitBranch? branch = SelectedBranch;
        if (project is null || branch is null || branch.IsRemote)
            return null;

        GitLocalBranchRemovalAnalysis? analysis = null;
        await RunOperationAsync("Checking local branch safety…", async () =>
        {
            analysis = await _gitService.AnalyzeLocalBranchRemovalAsync(project.RootPath, branch.Name);
        }, "Local branch safety check complete");
        return analysis;
    }

    public async Task<IReadOnlyList<GitLocalBranchRemovalAnalysis>> AnalyzeSelectedLocalBranchRemovalsAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        GitBranch[] branches = SelectedLocalBranches.Count > 0
            ? SelectedLocalBranches.ToArray()
            : SelectedBranch is { IsCurrent: false, IsRemote: false } single ? [single] : [];
        if (project is null || branches.Length == 0)
            return [];

        List<GitLocalBranchRemovalAnalysis> analyses = [];
        await RunOperationAsync($"Checking {branches.Length:N0} local branch(es)…", async () =>
        {
            foreach (GitBranch branch in branches)
                analyses.Add(await _gitService.AnalyzeLocalBranchRemovalAsync(project.RootPath, branch.Name));
        }, $"Safety check complete for {branches.Length:N0} local branch(es)");
        return analyses;
    }

    public async Task<bool> RemoveLocalBranchAsync(string branchName, bool forceUnretained = false)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || string.IsNullOrWhiteSpace(branchName))
            return false;

        _lfsCleanupPlan = null;
        LfsCleanupItems.Clear();
        OnPropertyChanged(nameof(LfsReclaimableObjectCount));
        OnPropertyChanged(nameof(LfsReclaimableBytes));
        OnPropertyChanged(nameof(LfsReclaimableSizeText));
        bool removed = false;
        await RunOperationAsync($"Removing local branch {branchName}…", async () =>
        {
            await _gitService.RemoveLocalBranchAsync(project.RootPath, branchName, forceUnretained);
            removed = true;
            await RefreshCoreAsync();
        }, $"Local branch {branchName} removed; remote references were left untouched");
        return removed;
    }

    public async Task<int> RemoveLocalBranchesAsync(
        IReadOnlyCollection<GitLocalBranchRemovalAnalysis> analyses,
        bool forceUnretained)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || analyses.Count == 0)
            return 0;

        _lfsCleanupPlan = null;
        LfsCleanupItems.Clear();
        OnPropertyChanged(nameof(LfsReclaimableObjectCount));
        OnPropertyChanged(nameof(LfsReclaimableBytes));
        OnPropertyChanged(nameof(LfsReclaimableSizeText));
        int removed = 0;
        await RunOperationAsync($"Removing {analyses.Count:N0} local branch(es)…", async () =>
        {
            foreach (GitLocalBranchRemovalAnalysis analysis in analyses)
            {
                bool force = forceUnretained && !analysis.CanRemoveSafely;
                await _gitService.RemoveLocalBranchAsync(project.RootPath, analysis.BranchName, force);
                removed++;
                _applicationLogService.Information(
                    "git",
                    $"local branch removed branch=\"{analysis.BranchName}\" force={force}",
                    project.RootPath);
            }
            await RefreshCoreAsync();
        }, $"{analyses.Count:N0} local branch(es) removed; remote references were left untouched");
        return removed;
    }

    public Task FetchAsync() => RunGitNetworkOperationAsync(
        "Fetching local and remote references…",
        _gitService.FetchAsync,
        "Remote branches and history refreshed",
        includeRemoteHistory: true);

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

    public async Task LoadGitIgnoreAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project?.Definition.Features.GitEnabled != true) return;
        IsGitIgnoreLoading = true;
        try
        {
            GitIgnoreDocument document = await _gitIgnoreService.LoadAsync(
                project.RootPath,
                ResolveGitIgnoreSource());
            if (SelectedProject?.Id != project.Id) return;
            bool hasDraft = _gitIgnoreDrafts.TryGetValue(
                BuildGitIgnoreDraftKey(project.Id, GitIgnoreSource),
                out string? draft);
            _suspendGitIgnoreEditing = true;
            GitIgnoreContent = hasDraft ? draft! : document.Content;
            _suspendGitIgnoreEditing = false;
            GitIgnoreFilePath = document.FilePath;
            IReadOnlyList<GitIgnoreRule> rules = hasDraft
                ? GitIgnoreService.ParseRules(GitIgnoreContent)
                : document.Rules;
            ReplaceCollection(GitIgnoreRules, rules);
            IgnoredFiles.Clear();
            IsGitIgnoreDirty = hasDraft;
            int activeRules = rules.Count(rule => rule.Kind is not "Blank" and not "Comment");
            int warnings = rules.Count(rule => !string.IsNullOrWhiteSpace(rule.Warning));
            GitIgnoreSummary = $"{activeRules:N0} active rule(s) · {warnings:N0} warning(s) · " +
                               (hasDraft ? "unsaved draft restored" : document.Exists ? "file loaded" : "new file");
            GitIgnoreTestResult = "Enter a project-relative path to test it against Git.";
            OnPropertyChanged(nameof(CanSaveGitIgnore));
            await LoadIgnoreSuggestionsAsync();
        }
        catch (Exception exception)
        {
            GitIgnoreSummary = "Unable to load ignore rules: " + exception.Message;
            _applicationLogService.Warning("gitignore", exception.Message, project.RootPath);
        }
        finally
        {
            _suspendGitIgnoreEditing = false;
            IsGitIgnoreLoading = false;
        }
    }

    public async Task SaveGitIgnoreAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project?.Definition.Features.GitEnabled != true) return;
        await RunOperationAsync("Saving Git ignore rules…", async () =>
        {
            await _gitIgnoreService.SaveAsync(project.RootPath, ResolveGitIgnoreSource(), GitIgnoreContent);
            _gitIgnoreDrafts.Remove(BuildGitIgnoreDraftKey(project.Id, GitIgnoreSource));
            IsGitIgnoreDirty = false;
            ParseGitIgnoreEditorContent();
            _applicationLogService.Information(
                "gitignore",
                $"saved source={GitIgnoreSource} rules={GitIgnoreRules.Count:N0}",
                project.RootPath);
        }, "Git ignore rules saved");
    }

    public async Task TestGitIgnorePathAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        string path = GitIgnoreTestPath.Trim();
        if (project?.Definition.Features.GitEnabled != true || path.Length == 0)
        {
            GitIgnoreTestResult = "Enter a file or folder path relative to the repository.";
            return;
        }

        try
        {
            GitIgnoreMatch match = await _gitIgnoreService.TestPathAsync(project.RootPath, path);
            GitIgnoreTestResult = match.Summary;
        }
        catch (Exception exception)
        {
            GitIgnoreTestResult = "Unable to test path: " + exception.Message;
        }
    }

    public async Task RefreshIgnoredFilesAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project?.Definition.Features.GitEnabled != true) return;
        IsGitIgnoreLoading = true;
        try
        {
            IReadOnlyList<string> files = await _gitIgnoreService.ListIgnoredFilesAsync(project.RootPath);
            if (SelectedProject?.Id != project.Id) return;
            ReplaceCollection(IgnoredFiles, files);
            GitIgnoreSummary = $"{GitIgnoreRules.Count(rule => rule.Kind is not "Blank" and not "Comment"):N0} active rule(s) · " +
                               $"{files.Count:N0} ignored path(s) shown" +
                               (files.Count >= 2500 ? " · display limit reached" : string.Empty);
        }
        catch (Exception exception)
        {
            GitIgnoreSummary = "Unable to list ignored files: " + exception.Message;
        }
        finally
        {
            IsGitIgnoreLoading = false;
        }
    }

    public void ApplyGitIgnoreTemplate()
    {
        string block = GitIgnoreTemplate switch
        {
            "Unreal Engine" => "# Unreal Engine\n/Binaries/\n/Build/\n/DerivedDataCache/\n/Intermediate/\n/Saved/\n.vs/\n*.VC.db\n*.opensdf\n*.sdf\n*.sln\n*.suo\n",
            "Unreal plugin binaries" => "# Unreal plugin generated binaries\n/Plugins/*/Binaries/\n/Plugins/*/Intermediate/\n/Plugins/*/DerivedDataCache/\n/Plugins/*/Saved/\n",
            "Unreal generated project files" => "# Unreal generated IDE and build files\n/.vs/\n/.vscode/\n/.idea/\n/*.sln\n/*.suo\n/*.opensdf\n/*.sdf\n/*.VC.db\n/Intermediate/ProjectFiles/\n",
            "Unity" => "# Unity generated folders\n/[Ll]ibrary/\n/[Tt]emp/\n/[Oo]bj/\n/[Bb]uild/\n/[Bb]uilds/\n/[Ll]ogs/\n/[Uu]ser[Ss]ettings/\n/MemoryCaptures/\n",
            "Godot" => "# Godot generated data\n/.godot/\n/.import/\n/export.cfg\nexport_presets.cfg\n*.translation\n",
            "JetBrains Rider" => "# JetBrains Rider\n.idea/\n*.sln.iml\n_ReSharper.Caches/\n",
            "Visual Studio" => "# Visual Studio\n.vs/\n*.user\n*.suo\n*.userosscache\n*.sln.docstates\n[Bb]in/\n[Oo]bj/\n",
            ".NET" => "# .NET\n[Bb]in/\n[Oo]bj/\n*.user\n*.suo\nTestResults/\n",
            "Node.js" => "# Node.js\nnode_modules/\nnpm-debug.log*\nyarn-debug.log*\n.pnpm-store/\ndist/\n",
            "Operating-system files" => "# Operating-system metadata\n.DS_Store\n.AppleDouble\n.LSOverride\nThumbs.db\nThumbs.db:encryptable\nehthumbs.db\nDesktop.ini\n$RECYCLE.BIN/\n",
            "Build and package outputs" => "# Build and package outputs\n/dist/\n/out/\n/artifacts/\n/packages/\n*.zip\n*.7z\n*.tar\n*.tar.gz\n",
            "CyRevision cache" => "# CyRevision local cache\n.cyrevision/\n",
            _ => string.Empty
        };
        if (block.Length == 0 || GitIgnoreContent.Contains(block, StringComparison.Ordinal)) return;
        string separator = GitIgnoreContent.Length == 0 || GitIgnoreContent.EndsWith('\n') ? string.Empty : "\n";
        GitIgnoreContent += separator + (GitIgnoreContent.Length == 0 ? string.Empty : "\n") + block;
    }

    public async Task LoadIgnoreSuggestionsAsync(bool force = false)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;
        if (!force && _ignoreSuggestionProjectId == project.Id &&
            (_allIgnoreFolderSuggestions.Count > 0 || _allIgnoreFileTypeSuggestions.Count > 0)) return;

        IsIgnoreSuggestionLoading = true;
        IgnoreSuggestionSummary = "Scanning project folders and file types…";
        try
        {
            if (!_codeWorkspaceCache.TryGetValue(project.Id, out CachedCodeWorkspace? cached))
            {
                await EnsureCodeWorkspaceLoadedAsync();
                if (!_codeWorkspaceCache.TryGetValue(project.Id, out cached)) return;
            }

            CodeFileIndex index = cached.FileIndex ?? await _codeWorkspaceService.BuildFileIndexAsync(
                project.RootPath,
                CodeIncludeHidden);
            if (SelectedProject?.Id != project.Id) return;
            if (cached.FileIndex is null)
            {
                cached = cached with { FileIndex = index };
                _codeWorkspaceCache[project.Id] = cached;
            }

            (IgnoreSuggestionViewModel[] folders, IgnoreSuggestionViewModel[] types) = await Task.Run(() =>
            {
                Dictionary<string, int> folderCounts = new(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, int> extensionCounts = new(StringComparer.OrdinalIgnoreCase);
                foreach (CodeFileEntry file in index.Files)
                {
                    string? directory = Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        string current = string.Empty;
                        foreach (string part in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
                        {
                            current = current.Length == 0 ? part : $"{current}/{part}";
                            folderCounts[current] = folderCounts.GetValueOrDefault(current) + 1;
                        }
                    }

                    string extension = Path.GetExtension(file.Name).ToLowerInvariant();
                    if (extension.Length > 1)
                        extensionCounts[extension] = extensionCounts.GetValueOrDefault(extension) + 1;
                }

                IgnoreSuggestionViewModel[] folderItems = folderCounts
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new IgnoreSuggestionViewModel(
                        Path.GetFileName(item.Key),
                        $"/{item.Key}/",
                        item.Value,
                        "Folder"))
                    .ToArray();
                IgnoreSuggestionViewModel[] typeItems = extensionCounts
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new IgnoreSuggestionViewModel(
                        item.Key,
                        $"*{item.Key}",
                        item.Value,
                        "File type"))
                    .ToArray();
                return (folderItems, typeItems);
            });
            if (SelectedProject?.Id != project.Id) return;
            _allIgnoreFolderSuggestions = folders;
            _allIgnoreFileTypeSuggestions = types;
            ApplyIgnoreSuggestionFilters();
            _ignoreSuggestionProjectId = project.Id;
            IgnoreSuggestionSummary = $"{folders.Length:N0} folder(s) · {types.Length:N0} file type(s) detected";
        }
        catch (Exception exception)
        {
            if (SelectedProject?.Id == project.Id) IgnoreSuggestionSummary = exception.Message;
        }
        finally
        {
            IsIgnoreSuggestionLoading = false;
        }
    }

    public void AppendSelectedSuggestionsToGitIgnore()
    {
        string[] patterns = SelectedIgnoreSuggestionPatterns(syncthing: false);
        if (patterns.Length == 0)
        {
            GitIgnoreSummary = "Select one or more project folders or file types first.";
            return;
        }

        GitIgnoreContent = AppendUniqueIgnorePatterns(GitIgnoreContent, "# Added with CyRevision", patterns);
        ClearIgnoreSuggestionSelection();
    }

    public void AppendSelectedSuggestionsToSyncthingIgnore()
    {
        string[] patterns = SelectedIgnoreSuggestionPatterns(syncthing: true);
        if (patterns.Length == 0)
        {
            SyncthingIgnoreStatus = "Select one or more project folders or file types first.";
            return;
        }

        SyncthingIgnoreRules = AppendUniqueIgnorePatterns(
            SyncthingIgnoreRules,
            "// Added with CyRevision",
            patterns);
        SyncthingIgnoreStatus = $"{patterns.Length:N0} selected rule(s) added in the editor; save to apply them.";
        ClearIgnoreSuggestionSelection();
    }

    private string[] SelectedIgnoreSuggestionPatterns(bool syncthing)
    {
        IEnumerable<string> folders = _allIgnoreFolderSuggestions
            .Where(item => item.IsSelected)
            .Select(item => syncthing ? item.Pattern.TrimEnd('/') : item.Pattern);
        IEnumerable<string> types = _allIgnoreFileTypeSuggestions
            .Where(item => item.IsSelected)
            .Select(item => item.Pattern);
        return folders.Concat(types).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string AppendUniqueIgnorePatterns(string content, string heading, IEnumerable<string> patterns)
    {
        HashSet<string> existing = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] additions = patterns.Where(pattern => !existing.Contains(pattern)).ToArray();
        if (additions.Length == 0) return content;
        string normalized = content.TrimEnd('\r', '\n');
        string separator = normalized.Length == 0 ? string.Empty : Environment.NewLine + Environment.NewLine;
        return normalized + separator + heading + Environment.NewLine + string.Join(Environment.NewLine, additions) + Environment.NewLine;
    }

    private void ClearIgnoreSuggestionSelection()
    {
        foreach (IgnoreSuggestionViewModel item in _allIgnoreFolderSuggestions) item.IsSelected = false;
        foreach (IgnoreSuggestionViewModel item in _allIgnoreFileTypeSuggestions) item.IsSelected = false;
        ApplyIgnoreSuggestionFilters();
    }

    private void ApplyIgnoreSuggestionFilters()
    {
        IgnoreFolderSuggestions = FilterIgnoreSuggestions(
            _allIgnoreFolderSuggestions,
            IgnoreFolderSearch,
            IgnoreFolderFilter);
        IgnoreFolderTree = BuildIgnoreFolderTree(IgnoreFolderSuggestions);
        IgnoreFileTypeSuggestions = FilterIgnoreSuggestions(
            _allIgnoreFileTypeSuggestions,
            IgnoreFileTypeSearch,
            IgnoreFileTypeFilter);
    }

    private static IReadOnlyList<IgnoreSuggestionTreeNode> BuildIgnoreFolderTree(
        IReadOnlyList<IgnoreSuggestionViewModel> suggestions)
    {
        Dictionary<string, IgnoreSuggestionViewModel> suggestionsByPath = suggestions
            .Select(item => (Path: item.Pattern.Trim().Replace('\\', '/').Trim('/'), Item: item))
            .Where(item => item.Path.Length > 0)
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Item, StringComparer.OrdinalIgnoreCase);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in suggestionsByPath.Keys)
        {
            string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int length = 1; length <= segments.Length; length++)
                paths.Add(string.Join('/', segments.Take(length)));
        }

        Dictionary<string, IgnoreSuggestionTreeNode> nodes = paths.ToDictionary(
            path => path,
            path => new IgnoreSuggestionTreeNode(
                Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar)),
                path,
                suggestionsByPath.GetValueOrDefault(path)),
            StringComparer.OrdinalIgnoreCase);
        List<IgnoreSuggestionTreeNode> roots = [];
        foreach ((string path, IgnoreSuggestionTreeNode node) in nodes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            int separator = path.LastIndexOf('/');
            if (separator < 0) roots.Add(node);
            else if (nodes.TryGetValue(path[..separator], out IgnoreSuggestionTreeNode? parent)) parent.Children.Add(node);
        }

        return roots;
    }

    private static IReadOnlyList<IgnoreSuggestionViewModel> FilterIgnoreSuggestions(
        IReadOnlyList<IgnoreSuggestionViewModel> source,
        string search,
        string filter)
    {
        string value = search.Trim();
        int minimum = filter switch
        {
            "10+ files" => 10,
            "100+ files" => 100,
            "1,000+ files" => 1000,
            _ => 0
        };
        return source.Where(item =>
                (value.Length == 0 ||
                 item.Pattern.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                 item.Name.Contains(value, StringComparison.OrdinalIgnoreCase)) &&
                (filter != "Selected" || item.IsSelected) &&
                item.FileCount >= minimum)
            .ToArray();
    }

    private GitIgnoreSource ResolveGitIgnoreSource() =>
        GitIgnoreSource.StartsWith("Local", StringComparison.OrdinalIgnoreCase)
            ? CyRevision.Git.GitIgnoreSource.LocalExclude
            : CyRevision.Git.GitIgnoreSource.Repository;

    private static string BuildGitIgnoreDraftKey(Guid projectId, string source) => $"{projectId:N}:{source}";

    private void ParseGitIgnoreEditorContent()
    {
        IReadOnlyList<GitIgnoreRule> rules = GitIgnoreService.ParseRules(GitIgnoreContent);
        ReplaceCollection(GitIgnoreRules, rules);
        int activeRules = rules.Count(rule => rule.Kind is not "Blank" and not "Comment");
        int warnings = rules.Count(rule => !string.IsNullOrWhiteSpace(rule.Warning));
        GitIgnoreSummary = $"{activeRules:N0} active rule(s) · {warnings:N0} warning(s)" +
                           (IsGitIgnoreDirty ? " · unsaved changes" : string.Empty);
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
        "Refreshing Git LFS locks…",
        () => LoadLfsLocksCoreAsync(),
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
            force ? $"Force-unlocking {fileLock.Path}…" : $"Unlocking {fileLock.Path}…",
            async () =>
            {
                _applicationLogService.Information(
                    "git-lfs",
                    $"unlock request path=\"{fileLock.Path}\" lock_id={fileLock.Id} owner=\"{fileLock.OwnerName}\" force={force.ToString().ToLowerInvariant()}",
                    SelectedProject.RootPath);
                await _gitService.UnlockLfsFileAsync(SelectedProject.RootPath, fileLock.Id, force);
                _applicationLogService.Information(
                    "git-lfs",
                    $"unlock complete path=\"{fileLock.Path}\" lock_id={fileLock.Id}",
                    SelectedProject.RootPath);
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
            forceEveryLock ? "Force-unlocking every Git LFS file…" : "Unlocking all of your Git LFS files…",
            async () =>
            {
                List<string> failures = [];
                int removed = 0;
                foreach (LfsFileLock item in targets)
                {
                    try
                    {
                        _applicationLogService.Information(
                            "git-lfs",
                            $"unlock request path=\"{item.Path}\" lock_id={item.Id} owner=\"{item.OwnerName}\" force={forceEveryLock.ToString().ToLowerInvariant()}",
                            SelectedProject.RootPath);
                        await _gitService.UnlockLfsFileAsync(
                            SelectedProject.RootPath,
                            item.Id,
                            force: forceEveryLock);
                        removed++;
                        _applicationLogService.Information(
                            "git-lfs",
                            $"unlock complete path=\"{item.Path}\" lock_id={item.Id}",
                            SelectedProject.RootPath);
                    }
                    catch (Exception exception)
                    {
                        _applicationLogService.Error(
                            "git-lfs",
                            $"unlock failed path=\"{item.Path}\" lock_id={item.Id}",
                            exception,
                            SelectedProject.RootPath);
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
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled || IsLfsAnalysisRunning)
            return;
        ProjectItemViewModel project = SelectedProject;
        _lfsAnalysisCancellation?.Cancel();
        _lfsAnalysisCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _lfsAnalysisCancellation = cancellation;
        OperationTaskViewModel trackedTask = BeginTrackedTask(
            "Analyzing local LFS objects and retention evidence...",
            project.Name,
            "Preparing non-destructive LFS analysis");
        Stopwatch stopwatch = Stopwatch.StartNew();
        IsLfsAnalysisRunning = true;
        LfsAnalysisPercent = 0;
        LfsAnalysisStage = "Preparing non-destructive LFS analysis…";
        IProgress<LfsAnalysisProgress> progress = new Progress<LfsAnalysisProgress>(update =>
        {
            LfsAnalysisPercent = Math.Clamp(update.Percent, 0, 100);
            LfsAnalysisStage = $"{update.Stage} · {update.Detail}";
            trackedTask.Detail = LfsAnalysisStage;
            StatusMessage = LfsAnalysisStage;
        });
        try
        {
            LfsManagementProfile profile = BuildLfsManagementProfile();
            await _lfsManagementProfileStore.SaveAsync(profile);
            _currentLfsManagementProfile = profile;
            PeerLfsAvailabilityCache peers = await _gitPeerExchangeService.GetCachedLfsAvailabilityAsync(
                GetGitExchangeStatePath(project.Id), project.Id, cancellation.Token);
            string repositoryPath = project.RootPath;
            _lfsCleanupPlan = await Task.Run(() =>
                _lfsStorageManager.AnalyzeAsync(
                    repositoryPath,
                    profile,
                    peers,
                    progress,
                    cancellation.Token),
                cancellation.Token);
            if (SelectedProject?.Id != project.Id)
                throw new OperationCanceledException("The selected project changed during LFS analysis.");
            ReplaceCollection(LfsCleanupItems, _lfsCleanupPlan.Objects);
            LfsStorageSummary = $"{_lfsCleanupPlan.Objects.Count} local object(s) · " +
                                $"{FormatByteSize(_lfsCleanupPlan.TotalBytes)} cached · " +
                                $"{_lfsCleanupPlan.ReclaimableCount} safely reclaimable · " +
                                $"{FormatByteSize(_lfsCleanupPlan.ReclaimableBytes)} · storage {_lfsCleanupPlan.StoragePath}";
            LfsRemoteVerification = _lfsCleanupPlan.RemoteVerificationOutput;
            OnPropertyChanged(nameof(LfsReclaimableObjectCount));
            OnPropertyChanged(nameof(LfsReclaimableBytes));
            OnPropertyChanged(nameof(LfsReclaimableSizeText));
            LfsAnalysisStage = $"Complete · {_lfsCleanupPlan.ReclaimableCount:N0} safely reclaimable object(s).";
            LfsAnalysisPercent = 100;
            StatusMessage = "LFS safety analysis complete";
            CompleteTrackedTask(trackedTask, "Completed", LfsAnalysisStage);
            _applicationLogService.Information(
                "git-lfs",
                $"safety analysis complete objects={_lfsCleanupPlan.Objects.Count} reclaimable={_lfsCleanupPlan.ReclaimableCount} duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms",
                project.RootPath);
        }
        catch (OperationCanceledException)
        {
            LfsAnalysisStage = "Analysis cancelled safely. No file was changed.";
            StatusMessage = LfsAnalysisStage;
            CompleteTrackedTask(trackedTask, "Cancelled", LfsAnalysisStage);
        }
        catch (Exception exception)
        {
            LfsAnalysisStage = $"Analysis failed · {exception.Message}";
            StatusMessage = LfsAnalysisStage;
            CompleteTrackedTask(trackedTask, "Failed", exception.Message);
            _applicationLogService.Error("git-lfs", "safety analysis failed", exception, project.RootPath);
        }
        finally
        {
            IsLfsAnalysisRunning = false;
            if (ReferenceEquals(_lfsAnalysisCancellation, cancellation))
                _lfsAnalysisCancellation = null;
            cancellation.Dispose();
        }
    }

    public async Task RunNativeLfsPruneAsync(bool dryRun)
    {
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled || IsLfsAnalysisRunning)
            return;

        ProjectItemViewModel project = SelectedProject;
        _lfsAnalysisCancellation?.Cancel();
        _lfsAnalysisCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _lfsAnalysisCancellation = cancellation;
        string operationName = dryRun ? "Previewing native Git LFS prune…" : "Running verified native Git LFS prune…";
        OperationTaskViewModel trackedTask = BeginTrackedTask(operationName, project.Name, "Preparing Git LFS prune");
        IsLfsAnalysisRunning = true;
        LfsAnalysisPercent = 0;
        LfsAnalysisStage = operationName;
        IProgress<LfsAnalysisProgress> progress = new Progress<LfsAnalysisProgress>(update =>
        {
            LfsAnalysisPercent = Math.Clamp(update.Percent, 0, 100);
            LfsAnalysisStage = $"{update.Stage} · {update.Detail}";
            trackedTask.Detail = LfsAnalysisStage;
            StatusMessage = LfsAnalysisStage;
        });
        try
        {
            int timeout = int.TryParse(LfsRemoteVerificationTimeoutSeconds, out int parsed)
                ? Math.Clamp(parsed, 10, 3600)
                : 300;
            LfsPruneResult result = await _lfsStorageManager.RunNativePruneAsync(
                project.RootPath,
                dryRun,
                verifyRemote: true,
                timeout,
                progress,
                cancellation.Token);
            if (SelectedProject?.Id != project.Id)
                return;

            LfsRemoteVerification = string.IsNullOrWhiteSpace(result.Output)
                ? result.Summary
                : result.Output;
            LfsStorageSummary = dryRun
                ? "Native git lfs prune preview complete. No object was deleted; review the report before running cleanup."
                : "Native git lfs prune completed with remote verification. Run Quick size scan to measure reclaimed space.";
            LfsAnalysisPercent = 100;
            LfsAnalysisStage = result.Summary;
            StatusMessage = result.Summary;
            CompleteTrackedTask(trackedTask, "Completed", result.Summary);
            _applicationLogService.Information(
                "git-lfs",
                $"native prune complete dry_run={dryRun} verify_remote=true duration={result.Duration.TotalMilliseconds:N0}ms",
                project.RootPath);
        }
        catch (OperationCanceledException)
        {
            LfsAnalysisStage = "Git LFS prune cancelled safely.";
            CompleteTrackedTask(trackedTask, "Cancelled", LfsAnalysisStage);
        }
        catch (Exception exception)
        {
            LfsAnalysisStage = $"Git LFS prune failed · {exception.Message}";
            LfsRemoteVerification = exception.Message;
            StatusMessage = LfsAnalysisStage;
            CompleteTrackedTask(trackedTask, "Failed", exception.Message);
            _applicationLogService.Error("git-lfs", "native prune failed", exception, project.RootPath);
        }
        finally
        {
            IsLfsAnalysisRunning = false;
            if (ReferenceEquals(_lfsAnalysisCancellation, cancellation))
                _lfsAnalysisCancellation = null;
            cancellation.Dispose();
        }
    }

    public void CancelLfsStorageAnalysis()
    {
        if (!IsLfsAnalysisRunning)
            return;
        LfsAnalysisStage = "Cancelling Git LFS analysis…";
        _lfsAnalysisCancellation?.Cancel();
    }

    public async Task AnalyzeRepositoryStorageAsync()
    {
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled)
            return;
        ProjectItemViewModel project = SelectedProject;
        await RunOperationAsync("Scanning repository disk usage…", async () =>
        {
            RepositoryStorageReport report = await Task.Run(() =>
                _lfsStorageManager.AnalyzeRepositoryStorageAsync(project.RootPath));
            ReplaceCollection(LfsStorageAreas, report.Areas);
            ReplaceCollection(LfsLargestFiles, report.LargestFiles);
            RepositoryStorageArea? lfs = report.Areas.FirstOrDefault(area => area.Name == "Git LFS cache");
            LfsRepositorySizeSummary = $"{project.Name} · {report.TotalSizeText} measured · " +
                                       $"LFS {FormatByteSize(lfs?.Size ?? 0)} · " +
                                       $"{report.LargestFiles.Count:N0} largest working file(s) listed · " +
                                       $"updated {report.CreatedAt.ToLocalTime():t}";
        }, "Repository size scan complete");
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
            OnPropertyChanged(nameof(LfsReclaimableObjectCount));
            OnPropertyChanged(nameof(LfsReclaimableBytes));
            OnPropertyChanged(nameof(LfsReclaimableSizeText));
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
                        : 180,
                BackupArchiveProfile = SelectedBackupArchiveProfile.Id,
                RemoveArchivedHotBackups = RemoveArchivedHotCopies,
                GitArchiveProfile = SelectedGitArchiveProfile.Id,
                RemoveArchivedGitBranches = RemoveGitBranchAfterArchive
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
                new ColdArchivePolicy(
                    Path.GetFullPath(ColdArchivePath),
                    TimeSpan.FromDays(days),
                    SelectedBackupArchiveProfile.MinimumRecentSnapshots,
                    RemoveArchivedHotCopies));
            ColdArchiveStatus = result.EligibleSnapshots == 0
                ? $"No old snapshot is eligible. At least {SelectedBackupArchiveProfile.MinimumRecentSnapshots:N0} recent snapshot(s) remain in hot storage."
                : $"{result.ArchivedSnapshots} snapshot(s) copied · {result.ExistingSnapshots} already cold · " +
                  $"{result.CopiedObjects} object(s) added" +
                  (result.RemovedHotSnapshots > 0
                      ? $" · {result.RemovedHotSnapshots} verified hot snapshot(s) removed · {FormatByteSize(result.ReclaimedHotBytes)} reclaimed"
                      : " · hot storage retained");
        }, RemoveArchivedHotCopies
            ? "Cold migration completed after verification"
            : "Cold copy completed — hot storage retained");
    }

    public async Task AnalyzeGitArchiveCandidatesAsync()
    {
        if (SelectedProject is null || !SelectedProject.Definition.Features.GitEnabled || _gitService is not GitCliRepositoryService git)
        {
            GitArchiveStatus = "Select a local Git project to analyze stale branches.";
            return;
        }
        await RunOperationAsync("Analyzing stale Git branches…", async () =>
        {
            IReadOnlyList<GitArchiveCandidate> candidates = await git.GetArchiveCandidatesAsync(
                SelectedProject.RootPath, SelectedGitArchiveProfile);
            ReplaceCollection(GitArchiveCandidates, candidates);
            SelectedGitArchiveCandidate = GitArchiveCandidates.FirstOrDefault();
            await RefreshArchivedGitBranchesCoreAsync(git);
            GitArchiveStatus = candidates.Count == 0
                ? "No local branch matches this cold-storage profile. Current and recent branches are protected."
                : $"{candidates.Count:N0} stale local branch(es) eligible · " +
                  $"{candidates.Count(item => item.HasRemoteCopy):N0} have an identical origin copy";
        }, "Git archive analysis completed");
    }

    public async Task ArchiveSelectedGitBranchAsync()
    {
        if (SelectedProject is null || SelectedGitArchiveCandidate is null || _gitService is not GitCliRepositoryService git) return;
        GitArchiveCandidate candidate = SelectedGitArchiveCandidate;
        await RunOperationAsync("Creating and verifying Git branch archive…", async () =>
        {
            string directory = ResolveGitArchiveDirectory(SelectedProject.Definition);
            GitArchivedBranch archived = await git.ArchiveBranchAsync(
                SelectedProject.RootPath,
                candidate.Branch,
                directory,
                SelectedGitArchiveProfile,
                RemoveGitBranchAfterArchive);
            await RefreshArchivedGitBranchesCoreAsync(git);
            IReadOnlyList<GitArchiveCandidate> candidates = await git.GetArchiveCandidatesAsync(
                SelectedProject.RootPath, SelectedGitArchiveProfile);
            ReplaceCollection(GitArchiveCandidates, candidates);
            SelectedGitArchiveCandidate = GitArchiveCandidates.FirstOrDefault();
            GitArchiveStatus = archived.SourceBranchRemoved
                ? $"{archived.Branch} archived and verified; its local ref was removed by explicit opt-in. No automatic Git GC was run."
                : $"{archived.Branch} copied to verified cold storage; the hot local branch was retained.";
        }, "Verified Git branch archive created");
    }

    public async Task RestoreSelectedGitArchiveAsync()
    {
        if (SelectedProject is null || SelectedArchivedGitBranch is null || _gitService is not GitCliRepositoryService git) return;
        GitArchivedBranch archive = SelectedArchivedGitBranch;
        string restoreName = $"restored/{archive.Branch.Replace(' ', '-')}";
        await RunOperationAsync("Restoring Git branch from cold storage…", async () =>
        {
            await git.RestoreArchivedBranchAsync(SelectedProject.RootPath, archive, restoreName);
            GitArchiveStatus = $"Restored as local branch {restoreName}. The current branch was not switched.";
            await RefreshCoreAsync();
        }, "Git branch restored from verified archive");
    }

    private async Task RefreshArchivedGitBranchesCoreAsync(GitCliRepositoryService git)
    {
        if (SelectedProject is null) return;
        IReadOnlyList<GitArchivedBranch> archives = await git.ListArchivedBranchesAsync(
            ResolveGitArchiveDirectory(SelectedProject.Definition));
        ReplaceCollection(ArchivedGitBranches, archives);
        SelectedArchivedGitBranch = ArchivedGitBranches.FirstOrDefault();
    }

    private string ResolveGitArchiveDirectory(ProjectDefinition definition) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(definition.ColdArchivePath)
                ? Path.Combine(_applicationPaths.DataDirectory, "cold-archives", definition.Id.ToString("N"))
                : Path.GetFullPath(definition.ColdArchivePath),
            "git-branches");

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

    private void ApplyAssetExplorerFilter() => _ = EnsureAssetExplorerLoadedAsync();

    private async Task EnsureAssetExplorerLoadedAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;

        _assetExplorerCancellation?.Cancel();
        _assetExplorerCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _assetExplorerCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        IsAssetExplorerLoading = true;
        try
        {
            if (!_codeWorkspaceCache.TryGetValue(project.Id, out CachedCodeWorkspace? cached))
            {
                await EnsureCodeWorkspaceLoadedAsync();
                if (!_codeWorkspaceCache.TryGetValue(project.Id, out cached)) return;
            }

            CodeFileIndex index = cached.FileIndex ?? await _codeWorkspaceService.BuildFileIndexAsync(
                project.RootPath,
                CodeIncludeHidden,
                token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id) return;
            if (cached.FileIndex is null)
            {
                cached = cached with { FileIndex = index };
                _codeWorkspaceCache[project.Id] = cached;
            }

            string filter = AssetExplorerSearch.Trim();
            CodeFileEntry[] files = await Task.Run(() => (filter.Length == 0
                    ? index.Files
                    : index.Files.Where(file => file.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToArray(), token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id ||
                !string.Equals(AssetExplorerSearch.Trim(), filter, StringComparison.Ordinal)) return;
            SelectedAssetExplorerFile = null;
            AssetExplorerFiles = files;
            AssetExplorerSummary = filter.Length == 0
                ? $"{files.Length:N0} project file(s) · virtualized list"
                : $"{files.Length:N0} match(es) for ‘{filter}’ · {index.Files.Count:N0} files indexed";
        }
        catch (OperationCanceledException)
        {
            // A newer project or search expression superseded this request.
        }
        catch (Exception exception)
        {
            if (SelectedProject?.Id == project.Id) AssetExplorerSummary = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_assetExplorerCancellation, cancellation))
            {
                _assetExplorerCancellation = null;
                cancellation.Dispose();
                IsAssetExplorerLoading = false;
            }
        }
    }

    private async Task PreviewSelectedAssetAsync(CodeFileEntry file, int selectionVersion)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !File.Exists(file.FullPath)) return;

        _assetExplorerPreviewCancellation?.Cancel();
        _assetExplorerPreviewCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _assetExplorerPreviewCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        IsAssetExplorerPreviewLoading = true;
        AssetDiffReport = $"Loading preview for {file.RelativePath}…";
        try
        {
            FilePresentationResult? presentation = await _filePresentationService.CreatePreviewAsync(
                new FilePreviewRequest(project.RootPath, file.RelativePath, file.FullPath, file.Size),
                token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id || selectionVersion != _assetExplorerPreviewVersion ||
                !string.Equals(SelectedAssetExplorerFile?.FullPath, file.FullPath, StringComparison.OrdinalIgnoreCase)) return;
            if (presentation is not null)
            {
                ApplyAssetPresentation(file.RelativePath, presentation);
                return;
            }

            CodeFilePreview preview = await _codeWorkspaceService.ReadPreviewAsync(
                project.RootPath,
                file.RelativePath,
                token);
            token.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id || selectionVersion != _assetExplorerPreviewVersion ||
                !string.Equals(SelectedAssetExplorerFile?.FullPath, file.FullPath, StringComparison.OrdinalIgnoreCase)) return;
            AssetDiffPreview = null;
            AssetDiffReport = preview.IsBinary
                ? $"{file.RelativePath}{Environment.NewLine}{Environment.NewLine}" +
                  $"No active CyRevision plugin provides a reader or preview for {Path.GetExtension(file.Name)} files."
                : $"{file.RelativePath} · {preview.Summary}{Environment.NewLine}{Environment.NewLine}{preview.Text}";
        }
        catch (OperationCanceledException)
        {
            // Selection changed while its preview was loading.
        }
        catch (Exception exception)
        {
            if (SelectedProject?.Id == project.Id && selectionVersion == _assetExplorerPreviewVersion)
                AssetDiffReport = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_assetExplorerPreviewCancellation, cancellation))
            {
                _assetExplorerPreviewCancellation = null;
                cancellation.Dispose();
                if (selectionVersion == _assetExplorerPreviewVersion) IsAssetExplorerPreviewLoading = false;
            }
        }
    }

    public void UseSelectedAssetAsBaseline()
    {
        if (SelectedAssetExplorerFile is not null) AssetBaselinePath = SelectedAssetExplorerFile.FullPath;
    }

    public void UseSelectedAssetAsCandidate()
    {
        if (SelectedAssetExplorerFile is not null) AssetCandidatePath = SelectedAssetExplorerFile.FullPath;
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
            string projectRoot = SelectedProject?.RootPath ?? Path.GetDirectoryName(AssetCandidatePath) ?? string.Empty;
            FilePresentationResult? result = await _filePresentationService.CreateDiffAsync(
                new FileDiffRequest(
                    projectRoot,
                    GetAssetRelativePath(projectRoot, AssetCandidatePath),
                    AssetBaselinePath,
                    AssetCandidatePath),
                GetDiffArtifactDirectory());
            if (result is null)
            {
                AssetDiffPreview = null;
                AssetDiffReport = $"No active CyRevision plugin can compare {Path.GetExtension(AssetCandidatePath)} files.";
                return;
            }
            ApplyAssetPresentation(GetAssetRelativePath(projectRoot, AssetCandidatePath), result);
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
            FilePresentationResult? result = await _filePresentationService.CreateDiffAsync(
                new FileDiffRequest(
                    SelectedProject.RootPath,
                    SelectedChange.Path,
                    baselinePath,
                    candidatePath),
                artifactDirectory);
            if (result is null)
            {
                AssetDiffPreview = null;
                AssetDiffReport = $"No active CyRevision plugin can compare {Path.GetExtension(candidatePath)} files.";
                return;
            }
            ApplyAssetPresentation(SelectedChange.Path, result);
        }, "Comparaison avec HEAD terminée");
    }

    public async Task ApplySelectedPresetAsync()
    {
        if (SelectedProject is null || SelectedPreset is null)
        {
            return;
        }

        ProjectPreset preset = SelectedPreset;
        if (!preset.IsAvailable)
        {
            StatusMessage = string.IsNullOrWhiteSpace(preset.AvailabilitySummary)
                ? $"Mode « {preset.Name} » is not compatible with this project."
                : preset.AvailabilitySummary;
            return;
        }

        if (preset.IsPluginMode &&
            !_pluginManager.IsActiveForCurrentProject(preset.ProviderPluginId!))
        {
            StatusMessage = $"Enable plugin {preset.ProviderPluginId} for this project before applying {preset.Name}.";
            return;
        }

        await RunOperationAsync("Application du mode projet…", async () =>
        {
            if (preset.Features.GitEnabled && !Directory.Exists(Path.Combine(SelectedProject.RootPath, ".git")))
            {
                await _gitService.InitializeAsync(SelectedProject.RootPath);
            }

            ProjectDefinition updated = SelectedProject.Definition with
            {
                Features = preset.Features,
                Retention = preset.Retention,
                OperatingMode = preset.Kind,
                PluginOperatingModeId = preset.PluginModeId,
                PluginOperatingModeProviderId = preset.ProviderPluginId
            };
            await _projectCatalog.UpsertAsync(updated);
            SelectedProject.Update(updated);
            OnPropertyChanged(nameof(GitConnectionKind));
            OnPropertyChanged(nameof(RuntimeModeSummary));
            NotifySynchronizationModeChanged();
            NotifyMemberPanelLayoutChanged();
            RefreshProjectModeCatalog();
            LoadBackupSettings(updated);
            if (!preset.Features.PeerSyncEnabled &&
                _currentSyncProfile?.SharedFolders.Any(folder => folder.Enabled) != true)
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
                ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition));
            SyncthingExecutablePath = _currentSyncProfile.ExecutablePath;
            SyncState = "Sync prêt";
            SyncDetails = $"API locale isolée : {_currentSyncProfile.ApiEndpoint}";
        }, "Exécutable Syncthing configuré pour ce projet");
    }

    public async Task ConfigureSyncthingAutomaticallyAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        SyncthingRuntimeInstallation installation = _syncthingRuntimeResolver.Detect(
            _currentSyncProfile?.ExecutablePath);
        if (!installation.IsAvailable)
        {
            StatusMessage = installation.Details;
            SyncthingRuntimeSummary = installation.Details;
            return;
        }

        await SetSyncthingExecutableAsync(installation.ExecutablePath!);
        SyncthingRuntimeSummary = $"{installation.Source} · {installation.Details}";
    }

    public async Task SaveSyncthingSettingsAsync()
    {
        if (SelectedProject is null || _currentSyncProfile is null)
        {
            StatusMessage = "Configure the Syncthing runtime first.";
            return;
        }

        if (!int.TryParse(SyncthingRescanInterval, out int rescanInterval) || rescanInterval is < 0 or > 86400)
        {
            StatusMessage = "The rescan interval must be between 0 and 86400 seconds.";
            return;
        }
        if (!int.TryParse(SyncConflictRetentionDays, out int conflictRetentionDays) || conflictRetentionDays is < 1 or > 3650)
        {
            StatusMessage = "Conflict backup retention must be between 1 and 3650 days.";
            return;
        }

        await RunOperationAsync("Saving Syncthing settings…", async () =>
        {
            string projectFolder = SelectedProject.Definition.Features.GitEnabled || IsSyncCommitMode
                ? _currentSyncProfile.ExchangeDirectory
                : ResolveConfiguredSyncSourceFolder(SelectedProject.Definition);
            Directory.CreateDirectory(projectFolder);
            string? versionStore = IsVersionedSyncMode ? NormalizeOptionalDirectory(SyncVersionStorePath) : null;
            string? compressedStore = IsVersionedSyncMode ? NormalizeOptionalDirectory(SyncCompressedBackupPath) : null;
            if (versionStore is not null) Directory.CreateDirectory(versionStore);
            if (IsVersionedSyncMode && SyncCompressedBackupEnabled && compressedStore is null)
                throw new InvalidOperationException("Choose a compressed-backup destination first.");
            if (compressedStore is not null) Directory.CreateDirectory(compressedStore);
            _currentSyncProfile = await _syncthingProfileStore.SaveAsync(_currentSyncProfile with
            {
                ExchangeDirectory = projectFolder,
                FolderMode = SelectedSyncthingFolderMode,
                RescanIntervalSeconds = rescanInterval,
                FileWatcherEnabled = SyncthingFileWatcherEnabled,
                ProjectFolderPath = SelectedProject.Definition.Features.GitEnabled
                    ? null
                    : ResolveConfiguredSyncSourceFolder(SelectedProject.Definition),
                VersioningDirectory = IsVersionedSyncMode ? versionStore : null,
                CompressedBackupDirectory = IsVersionedSyncMode ? compressedStore : null,
                CompressedBackupEnabled = IsVersionedSyncMode && SyncCompressedBackupEnabled,
                ConflictBackupRetentionDays = conflictRetentionDays
            });
            SyncStorageStatus = IsVersionedSyncMode
                ? $"Source: {projectFolder} · versions: {versionStore ?? "inside the source folder"} · compressed backup: {(SyncCompressedBackupEnabled ? compressedStore : "off")}"
                : $"Synchronized source: {projectFolder}";
            if (_syncEngine?.Status.State is SyncEngineState.Running or SyncEngineState.Paused)
            {
                await ConfigureCurrentSyncFolderAsync();
                await RefreshSyncthingWorkspaceCoreAsync();
            }
        }, $"Syncthing mode saved: {SelectedSyncthingFolderMode.ToDisplayName()}");
    }

    public void SetSyncSourceFolderPath(string path) => SyncSourceFolderPath = Path.GetFullPath(path);

    public void SetSyncVersionStorePath(string path) => SyncVersionStorePath = Path.GetFullPath(path);

    public void SetSyncCompressedBackupPath(string path) => SyncCompressedBackupPath = Path.GetFullPath(path);

    public async Task CreateCompressedSyncBackupAsync()
    {
        if (SelectedProject is null || !IsVersionedSyncMode)
        {
            StatusMessage = "Compressed folder backups are available in Sync + Versions mode.";
            return;
        }

        string source = ResolveConfiguredSyncSourceFolder(SelectedProject.Definition);
        string? destination = NormalizeOptionalDirectory(SyncCompressedBackupPath);
        if (destination is null)
        {
            StatusMessage = "Choose a compressed-backup destination first.";
            return;
        }

        string sourceFull = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string destinationFull = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (destinationFull.StartsWith(sourceFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "The compressed-backup destination must be outside the synchronized source folder.";
            return;
        }

        await RunOperationAsync("Creating compressed Sync backup…", async () =>
        {
            Directory.CreateDirectory(destinationFull);
            string safeName = string.Concat(SelectedProject.Name.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            string finalPath = Path.Combine(destinationFull, $"{safeName}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.zip");
            string partialPath = finalPath + ".partial";
            try
            {
                await Task.Run(() => ZipFile.CreateFromDirectory(sourceFull, partialPath, CompressionLevel.Fastest, includeBaseDirectory: false));
                File.Move(partialPath, finalPath);
            }
            finally
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
            SyncStorageStatus = $"Compressed backup created: {finalPath}";
            await RecordSyncHistoryAsync("Project Sync", sourceFull, "Compressed backup", "Local archive", finalPath);
        }, "Compressed Sync backup created");
    }

    public async Task CreateSyncCommitAsync()
    {
        if (SelectedProject is null || !CanCreateSyncCommit) return;
        IsSyncCommitBusy = true;
        try
        {
            await RunOperationAsync("Creating and publishing Sync commit…", async () =>
            {
                string exchange = ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition);
                SyncCommitCreateResult result = await _syncCommitService.CreateCommitAsync(
                    SelectedProject.Id,
                    ResolveConfiguredSyncSourceFolder(SelectedProject.Definition),
                    exchange,
                    GetSyncCommitStateDirectory(SelectedProject.Id),
                    SyncCommitMessage,
                    SyncCommitAuthor);
                SyncCommitMessage = string.Empty;
                await RefreshSyncCommitsCoreAsync();
                SelectedSyncCommit = SyncCommits.FirstOrDefault(item => item.CommitId == result.Manifest.CommitId);
                SyncCommitStatus = $"Published {result.Manifest.ShortId} · {result.Manifest.Files.Count:N0} file(s). Syncthing can now replicate this immutable package.";
                await RecordSyncHistoryAsync("Sync + Commit", result.PackagePath, "Commit published", result.Manifest.ShortId, result.Manifest.Message);
            }, "Sync commit published");
        }
        finally
        {
            IsSyncCommitBusy = false;
        }
    }

    public async Task RefreshSyncCommitsAsync()
    {
        IsSyncCommitBusy = true;
        try { await RefreshSyncCommitsCoreAsync(); }
        finally { IsSyncCommitBusy = false; }
    }

    public async Task AnalyzeSelectedSyncCommitAsync()
    {
        if (SelectedProject is null || SelectedSyncCommit is null || !IsSyncCommitMode) return;
        IsSyncCommitBusy = true;
        try
        {
            SelectedSyncCommitAnalysis = await _syncCommitService.AnalyzeAsync(
                ResolveConfiguredSyncSourceFolder(SelectedProject.Definition),
                ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition),
                SelectedSyncCommit);
            foreach (SyncCommitConflictViewModel old in SyncCommitConflicts) old.Changed -= OnSyncCommitConflictChoiceChanged;
            SyncCommitConflicts.Clear();
            foreach (SyncCommitConflict conflict in SelectedSyncCommitAnalysis.Conflicts)
            {
                SyncCommitConflictViewModel item = new(conflict);
                item.Changed += OnSyncCommitConflictChoiceChanged;
                SyncCommitConflicts.Add(item);
            }
            SelectedSyncCommitConflict = SyncCommitConflicts.FirstOrDefault();
            SyncCommitStatus = SyncCommitConflictSummary;
            OnPropertyChanged(nameof(CanApplySyncCommit));
        }
        finally
        {
            IsSyncCommitBusy = false;
        }
    }

    public void ResolveSelectedSyncCommitConflict(SyncCommitConflictChoice choice)
    {
        if (SelectedSyncCommitConflict is null) return;
        SelectedSyncCommitConflict.Choice = choice;
        SyncCommitStatus = SyncCommitConflictSummary;
    }

    public async Task ApplySelectedSyncCommitAsync()
    {
        if (SelectedProject is null || SelectedSyncCommit is null || !CanApplySyncCommit) return;
        IsSyncCommitBusy = true;
        try
        {
            await RunOperationAsync("Backing up and applying Sync commit…", async () =>
            {
                Dictionary<string, SyncCommitConflictChoice> choices = SyncCommitConflicts.ToDictionary(
                    item => item.Path, item => item.Choice, StringComparer.OrdinalIgnoreCase);
                string backupRoot = Path.Combine(_applicationPaths.DataDirectory, "sync-commit-recovery", SelectedProject.Id.ToString("N"));
                await _syncCommitService.ApplyAsync(
                    ResolveConfiguredSyncSourceFolder(SelectedProject.Definition),
                    ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition),
                    GetSyncCommitStateDirectory(SelectedProject.Id),
                    backupRoot,
                    SelectedSyncCommit,
                    choices);
                PruneSyncCommitRecovery(backupRoot);
                await RecordSyncHistoryAsync("Sync + Commit", SelectedSyncCommit.ShortId, "Commit applied", "Protected apply", $"recovery={backupRoot}");
                SyncCommitStatus = $"Applied {SelectedSyncCommit.ShortId}. A pre-apply recovery archive was retained.";
                SelectedSyncCommitAnalysis = null;
                SyncCommitConflicts.Clear();
            }, "Sync commit applied with recovery backup");
        }
        finally
        {
            IsSyncCommitBusy = false;
        }
    }

    private async Task RefreshSyncCommitsCoreAsync()
    {
        if (SelectedProject is null || !IsSyncCommitMode)
        {
            SyncCommits.Clear();
            SyncCommitConflicts.Clear();
            SelectedSyncCommit = null;
            return;
        }
        IReadOnlyList<SyncCommitManifest> commits = await _syncCommitService.ListCommitsAsync(
            ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition));
        string? selectedId = SelectedSyncCommit?.CommitId;
        ReplaceCollection(SyncCommits, commits);
        SelectedSyncCommit = SyncCommits.FirstOrDefault(item => item.CommitId == selectedId) ?? SyncCommits.FirstOrDefault();
        SyncCommitStatus = commits.Count == 0
            ? "No Sync commit published yet. The synchronized exchange remains unchanged until you commit."
            : $"{commits.Count:N0} immutable commit package(s) available · newest first";
    }

    private void OnSyncCommitConflictChoiceChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanApplySyncCommit));
        OnPropertyChanged(nameof(SyncCommitConflictSummary));
    }

    private string GetSyncCommitStateDirectory(Guid projectId) =>
        Path.Combine(_applicationPaths.DataDirectory, "sync-commit-state", projectId.ToString("N"));

    private void PruneSyncCommitRecovery(string backupRoot)
    {
        if (!Directory.Exists(backupRoot)) return;
        int days = int.TryParse(SyncConflictRetentionDays, out int parsed) ? Math.Clamp(parsed, 1, 3650) : 30;
        DateTime threshold = DateTime.UtcNow.AddDays(-days);
        foreach (string file in Directory.EnumerateFiles(backupRoot, "*.zip").Where(path => File.GetLastWriteTimeUtc(path) < threshold))
            File.Delete(file);
    }

    public async Task RefreshSyncHistoryAsync()
    {
        if (SelectedProject is null)
        {
            SyncHistory.Clear();
            SyncHistorySummary = "No project selected.";
            return;
        }

        IReadOnlyList<SyncHistoryEntry> entries = await _syncHistoryStore.SearchAsync(
            SelectedProject.Id,
            SyncHistorySearch,
            SyncHistoryPathFilter);
        ReplaceCollection(SyncHistory, entries);
        SyncHistorySummary = entries.Count == 0
            ? "No matching Sync event. History is recorded when the isolated Sync engine observes or performs an operation."
            : $"{entries.Count:N0} event(s) · newest first · stored locally for {SelectedProject.Name}";
    }

    public async Task FilterSyncHistoryForFileAsync(string relativePath)
    {
        SyncHistoryPathFilter = relativePath.Replace('\\', '/');
        await RefreshSyncHistoryAsync();
    }

    public void ClearSyncHistoryFileFilter()
    {
        SyncHistoryPathFilter = string.Empty;
    }

    public async Task RefreshSyncConflictsAsync()
    {
        if (SelectedProject is null) return;
        IsSyncConflictBusy = true;
        try
        {
            await RunOperationAsync("Scanning synchronized folders for conflicts…", async () =>
            {
                int removed = await _syncConflictService.PruneExpiredAsync(SelectedProject.Id);
                IReadOnlyList<SyncConflictScope> scopes = BuildSyncConflictScopes();
                _allSyncConflicts = await _syncConflictService.ScanAsync(scopes);
                ApplySyncConflictFilter();
                await RefreshSyncConflictBackupsCoreAsync();
                SyncConflictSummary = $"{_allSyncConflicts.Count:N0} unresolved conflict(s) · " +
                                      $"{SyncConflictBackups.Count:N0} recoverable resolution(s)" +
                                      (removed > 0 ? $" · {removed:N0} expired backup(s) removed" : string.Empty);
            }, "Sync conflict scan completed");
        }
        finally
        {
            IsSyncConflictBusy = false;
        }
    }

    public async Task ResolveSelectedSyncConflictAsync(SyncConflictResolution resolution)
    {
        if (SelectedProject is null || SelectedSyncConflict is null) return;
        if (!int.TryParse(SyncConflictRetentionDays, out int retentionDays) || retentionDays is < 1 or > 3650)
        {
            StatusMessage = "Conflict backup retention must be between 1 and 3650 days.";
            return;
        }

        SyncConflictItem conflict = SelectedSyncConflict;
        IsSyncConflictBusy = true;
        try
        {
            await RunOperationAsync("Backing up and resolving the Sync conflict…", async () =>
            {
                if (_currentSyncProfile is not null && _currentSyncProfile.ConflictBackupRetentionDays != retentionDays)
                {
                    _currentSyncProfile = await _syncthingProfileStore.SaveAsync(
                        _currentSyncProfile with { ConflictBackupRetentionDays = retentionDays });
                }

                SyncConflictBackup backup = await _syncConflictService.ResolveAsync(
                    SelectedProject.Id,
                    conflict,
                    resolution,
                    retentionDays);
                await RecordSyncHistoryAsync(
                    conflict.Scope,
                    conflict.RelativeOriginalPath,
                    resolution == SyncConflictResolution.KeepOriginal
                        ? "Conflict resolved: original kept"
                        : "Conflict resolved: conflict version used",
                    "Protected resolution",
                    $"recovery={backup.Id:N}; expires={backup.ExpiresAt:O}");
                await RequestConflictScopeScanAsync(conflict);
                await RefreshSyncConflictsCoreAsync(pruneExpired: false);
            }, "Sync conflict resolved; recovery copy retained");
        }
        finally
        {
            IsSyncConflictBusy = false;
        }
    }

    public async Task RestoreSelectedSyncConflictBackupAsync()
    {
        if (SelectedProject is null || SelectedSyncConflictBackup is null) return;
        SyncConflictBackup backup = SelectedSyncConflictBackup;
        IsSyncConflictBusy = true;
        try
        {
            await RunOperationAsync("Restoring the pre-resolution conflict state…", async () =>
            {
                await _syncConflictService.RestoreAsync(backup);
                await RecordSyncHistoryAsync(
                    backup.Scope,
                    backup.RelativeOriginalPath,
                    "Conflict resolution restored",
                    "Recovery",
                    $"recovery={backup.Id:N}");
                await RequestConflictScopeScanAsync(new SyncConflictItem(
                    Guid.Empty,
                    backup.Scope,
                    backup.RootPath,
                    backup.RelativeConflictPath,
                    backup.RelativeOriginalPath,
                    backup.CreatedAt,
                    0,
                    0,
                    backup.OriginalExisted));
                await RefreshSyncConflictsCoreAsync(pruneExpired: false);
            }, "Conflict state restored from CyRevision recovery storage");
        }
        finally
        {
            IsSyncConflictBusy = false;
        }
    }

    public async Task CleanExpiredSyncConflictBackupsAsync()
    {
        if (SelectedProject is null) return;
        IsSyncConflictBusy = true;
        try
        {
            int removed = 0;
            await RunOperationAsync("Removing expired Sync conflict recovery copies…", async () =>
            {
                removed = await _syncConflictService.PruneExpiredAsync(SelectedProject.Id);
                await RefreshSyncConflictBackupsCoreAsync();
            }, "Expired Sync conflict recovery cleanup completed");
            SyncConflictSummary = $"{_allSyncConflicts.Count:N0} unresolved conflict(s) · " +
                                  $"{SyncConflictBackups.Count:N0} recoverable resolution(s) · {removed:N0} expired removed";
        }
        finally
        {
            IsSyncConflictBusy = false;
        }
    }

    private async Task RefreshSyncConflictsCoreAsync(bool pruneExpired)
    {
        if (SelectedProject is null) return;
        if (pruneExpired) await _syncConflictService.PruneExpiredAsync(SelectedProject.Id);
        _allSyncConflicts = await _syncConflictService.ScanAsync(BuildSyncConflictScopes());
        ApplySyncConflictFilter();
        await RefreshSyncConflictBackupsCoreAsync();
        SyncConflictSummary = $"{_allSyncConflicts.Count:N0} unresolved conflict(s) · {SyncConflictBackups.Count:N0} recoverable resolution(s)";
    }

    private async Task RefreshSyncConflictBackupsCoreAsync()
    {
        if (SelectedProject is null)
        {
            SyncConflictBackups.Clear();
            return;
        }
        IReadOnlyList<SyncConflictBackup> backups = await _syncConflictService.LoadBackupsAsync(SelectedProject.Id);
        ReplaceCollection(SyncConflictBackups, backups);
        SelectedSyncConflictBackup = SyncConflictBackups.FirstOrDefault();
    }

    private IReadOnlyList<SyncConflictScope> BuildSyncConflictScopes()
    {
        if (SelectedProject is null) return [];
        List<SyncConflictScope> scopes = [];
        if (SelectedProject.Definition.Features.PeerSyncEnabled)
        {
            string root = _currentSyncProfile?.ProjectFolderPath
                          ?? _currentSyncProfile?.ExchangeDirectory
                          ?? ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition);
            scopes.Add(new SyncConflictScope(SynchronizationOverviewTabTitle, root));
        }

        if (_currentSyncProfile is not null)
        {
            scopes.AddRange(_currentSyncProfile.SharedFolders
                .Where(folder => folder.Enabled)
                .Select(folder => new SyncConflictScope(folder.Name, folder.Path)));
        }

        return scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope.RootPath))
            .GroupBy(scope => Path.GetFullPath(scope.RootPath), OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private void ApplySyncConflictFilter()
    {
        IEnumerable<SyncConflictItem> filtered = _allSyncConflicts;
        if (!string.IsNullOrWhiteSpace(SyncConflictSearch))
        {
            string search = SyncConflictSearch.Trim();
            filtered = filtered.Where(item =>
                item.RelativeOriginalPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.RelativeConflictPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Scope.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Status.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        ReplaceCollection(SyncConflicts, filtered);
        SelectedSyncConflict = SyncConflicts.FirstOrDefault();
    }

    private async Task RequestConflictScopeScanAsync(SyncConflictItem conflict)
    {
        if (_currentSyncProfile is null ||
            _syncEngine?.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused)) return;
        string conflictRoot = Path.GetFullPath(conflict.RootPath);
        string? folderId = string.Equals(
            Path.GetFullPath(_currentSyncProfile.ExchangeDirectory),
            conflictRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? _currentSyncProfile.FolderId
            : _currentSyncProfile.SharedFolders.FirstOrDefault(folder => string.Equals(
                Path.GetFullPath(folder.Path),
                conflictRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))?.FolderId;
        if (string.IsNullOrWhiteSpace(folderId)) return;
        using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
        await api.ScanFolderAsync(folderId);
    }

    private async Task RecordSyncHistoryAsync(
        string scope,
        string path,
        string action,
        string direction,
        string detail)
    {
        if (SelectedProject is null) return;
        SyncHistoryEntry entry = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            SelectedProject.Id,
            scope,
            path.Replace('\\', '/'),
            action,
            direction,
            detail);
        await _syncHistoryStore.AppendAsync(entry);
        if (string.IsNullOrWhiteSpace(SyncHistorySearch) &&
            string.IsNullOrWhiteSpace(SyncHistoryPathFilter))
        {
            SyncHistory.Insert(0, entry);
            SyncHistorySummary = $"{SyncHistory.Count:N0} event(s) · newest first · persisted per project";
        }
    }

    public void SetSharedSyncFolderPath(string path) => SharedSyncFolderPath = Path.GetFullPath(path);

    public async Task AddOrUpdateSharedSyncFolderAsync()
    {
        if (SelectedProject is null) return;
        if (string.IsNullOrWhiteSpace(SharedSyncFolderName) || string.IsNullOrWhiteSpace(SharedSyncFolderPath))
        {
            StatusMessage = "Enter a shared-folder name and choose a local directory.";
            return;
        }

        await RunOperationAsync("Saving independent shared folder…", async () =>
        {
            if (_currentSyncProfile is null)
            {
                SyncthingRuntimeInstallation installation = _syncthingRuntimeResolver.Detect();
                if (!installation.IsAvailable) throw new FileNotFoundException(installation.Details);
                _currentSyncProfile = await _syncthingProfileStore.CreateOrUpdateAsync(
                    SelectedProject.Id,
                    installation.ExecutablePath!,
                    ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition));
                SyncthingExecutablePath = _currentSyncProfile.ExecutablePath;
                SyncthingRuntimeSummary = $"{installation.Source} · {installation.Details}";
            }

            string path = Path.GetFullPath(SharedSyncFolderPath.Trim());
            Directory.CreateDirectory(path);
            SyncthingSharedFolder? existing = SelectedSharedSyncFolder;
            Guid id = existing?.Id ?? Guid.NewGuid();
            SyncthingSharedFolder definition = new(
                id,
                SharedSyncFolderName.Trim(),
                path,
                existing?.FolderId ?? $"cyrevision-share-{SelectedProject.Id:N}-{id:N}",
                SelectedSharedSyncFolderMode,
                true,
                _currentSyncProfile.RescanIntervalSeconds,
                _currentSyncProfile.FileWatcherEnabled);
            SyncthingSharedFolder[] folders = [
                .. _currentSyncProfile.SharedFolders.Where(folder => folder.Id != id),
                definition
            ];
            _currentSyncProfile = await _syncthingProfileStore.SaveAsync(
                _currentSyncProfile with { SharedFolders = folders });
            ReplaceCollection(SharedSyncFolders, folders.OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase));
            OnPropertyChanged(nameof(SynchronizationScopeSummary));
            SelectedSharedSyncFolder = SharedSyncFolders.First(folder => folder.Id == id);
            SharedSyncFolderStatus = $"{folders.Length:N0} independent folder(s) · available in every project mode.";
            await RecordSyncHistoryAsync(definition.Name, definition.Path, "Mapping saved", definition.Mode.ToDisplayName(), definition.FolderId);
            if (_syncEngine?.Status.State is SyncEngineState.Running or SyncEngineState.Paused)
                await ConfigureCurrentSyncFolderAsync();
        }, "Independent shared folder saved");
    }

    public async Task RemoveSelectedSharedSyncFolderAsync()
    {
        if (_currentSyncProfile is null || SelectedSharedSyncFolder is null) return;
        SyncthingSharedFolder selected = SelectedSharedSyncFolder;
        await RunOperationAsync("Removing independent shared folder…", async () =>
        {
            SyncthingSharedFolder[] folders = _currentSyncProfile.SharedFolders
                .Where(folder => folder.Id != selected.Id).ToArray();
            if (_syncEngine?.Status.State is SyncEngineState.Running or SyncEngineState.Paused)
            {
                using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
                await api.DeleteFolderAsync(selected.FolderId);
            }
            _currentSyncProfile = await _syncthingProfileStore.SaveAsync(
                _currentSyncProfile with { SharedFolders = folders });
            ReplaceCollection(SharedSyncFolders, folders);
            OnPropertyChanged(nameof(SynchronizationScopeSummary));
            SelectedSharedSyncFolder = SharedSyncFolders.FirstOrDefault();
            SharedSyncFolderStatus = folders.Length == 0
                ? "No independent shared folder configured."
                : $"{folders.Length:N0} independent folder(s) configured.";
            await RecordSyncHistoryAsync(selected.Name, selected.Path, "Mapping removed", selected.Mode.ToDisplayName(), selected.FolderId);
        }, "Independent shared folder removed; local files were kept");
    }

    public async Task ScanSelectedSharedSyncFolderAsync()
    {
        if (_currentSyncProfile is null || SelectedSharedSyncFolder is null ||
            _syncEngine?.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused))
        {
            StatusMessage = "Start CyRevision Syncthing and select a shared folder first.";
            return;
        }
        SyncthingSharedFolder selected = SelectedSharedSyncFolder;
        await RunOperationAsync("Scanning independent shared folder…", async () =>
        {
            using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
            await api.ScanFolderAsync(selected.FolderId);
            await RecordSyncHistoryAsync(selected.Name, selected.Path, "Scan requested", selected.Mode.ToDisplayName(), selected.FolderId);
            await RefreshSyncthingWorkspaceCoreAsync();
        }, "Shared folder scan requested");
    }

    public Task RefreshSyncthingWorkspaceAsync() =>
        RunOperationAsync(
            "Refreshing Syncthing devices, differences, and logs…",
            RefreshSyncthingWorkspaceCoreAsync,
            "Syncthing workspace refreshed");

    public async Task ScanSyncthingFolderAsync()
    {
        if (_currentSyncProfile is null || _syncEngine?.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused))
        {
            StatusMessage = "Start Syncthing before requesting a folder scan.";
            return;
        }

        await RunOperationAsync("Scanning the synchronized folder…", async () =>
        {
            using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
            await api.ScanFolderAsync(_currentSyncProfile.FolderId);
            await RecordSyncHistoryAsync(
                SynchronizationOverviewTabTitle,
                _currentSyncProfile.ExchangeDirectory,
                "Scan requested",
                RuntimeModeSummary,
                _currentSyncProfile.FolderId);
            await RefreshSyncthingWorkspaceCoreAsync();
        }, "Syncthing folder scan completed");
    }

    public async Task LoadSyncthingIgnoreRulesAsync()
    {
        if (_currentSyncProfile is null)
        {
            SyncthingIgnoreStatus = "Configure Syncthing first.";
            return;
        }

        SyncthingIgnoreRules = await _syncthingIgnoreFileService.ReadAsync(_currentSyncProfile.ExchangeDirectory);
        SyncthingIgnoreStatus = string.IsNullOrEmpty(SyncthingIgnoreRules)
            ? "No .stignore file exists yet."
            : $"Loaded {_currentSyncProfile.ExchangeDirectory}{Path.DirectorySeparatorChar}.stignore";
        await LoadIgnoreSuggestionsAsync();
    }

    public void UseSyncthingUnrealIgnoreTemplate()
    {
        SyncthingIgnoreRules = SyncthingIgnoreFileService.UnrealTemplate;
        SyncthingIgnoreStatus = "Unreal template loaded in the editor; save to apply it.";
    }

    public async Task SaveSyncthingIgnoreRulesAsync()
    {
        if (_currentSyncProfile is null)
        {
            StatusMessage = "Configure Syncthing before saving .stignore.";
            return;
        }

        await RunOperationAsync("Saving .stignore…", async () =>
        {
            await _syncthingIgnoreFileService.WriteAsync(
                _currentSyncProfile.ExchangeDirectory,
                SyncthingIgnoreRules);
            if (_syncEngine?.Status.State is SyncEngineState.Running or SyncEngineState.Paused)
            {
                using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
                await api.ScanFolderAsync(_currentSyncProfile.FolderId);
            }
        }, ".stignore saved for this project");
        SyncthingIgnoreStatus = "Saved as UTF-8 in the synchronized folder root.";
    }

    public async Task StartSyncAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        bool hasIndependentFolders = _currentSyncProfile?.SharedFolders.Any(folder => folder.Enabled) == true;
        if (!SelectedProject.Definition.Features.PeerSyncEnabled && !hasIndependentFolders)
        {
            StatusMessage = "Enable project Sync or add an independent shared folder first.";
            return;
        }

        if (_currentSyncProfile is null || !File.Exists(_currentSyncProfile.ExecutablePath))
        {
            SyncthingRuntimeInstallation installation = _syncthingRuntimeResolver.Detect();
            if (!installation.IsAvailable)
            {
                StatusMessage = installation.Details;
                SyncthingRuntimeSummary = installation.Details;
                return;
            }

            _currentSyncProfile = await _syncthingProfileStore.CreateOrUpdateAsync(
                SelectedProject.Id,
                installation.ExecutablePath!,
                ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition));
            SyncthingExecutablePath = _currentSyncProfile.ExecutablePath;
            SyncthingRuntimeSummary = $"{installation.Source} · {installation.Details}";
        }

        if (!SelectedProject.Definition.Features.PeerSyncEnabled &&
            _currentSyncProfile?.SharedFolders.Any(folder => folder.Enabled) != true)
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
            string desiredExchangePath = ResolveConfiguredSyncExchangeDirectory(SelectedProject.Definition);
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
            if (SelectedProject.Definition.Features.PeerSyncEnabled)
                await ExchangeGitCoreAsync();
            await LoadPeerMembersCoreAsync();
            UpdateSyncStatus(_syncEngine.Status);
            await RefreshSyncthingWorkspaceCoreAsync();
            await RecordSyncHistoryAsync(
                SynchronizationOverviewTabTitle,
                _currentSyncProfile.ExchangeDirectory,
                "Engine started",
                RuntimeModeSummary,
                _syncEngine.DeviceId);
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
            await RecordSyncHistoryAsync(
                SynchronizationOverviewTabTitle,
                _currentSyncProfile?.ExchangeDirectory ?? SelectedProject?.RootPath ?? string.Empty,
                "Engine paused",
                "Local runtime",
                _syncEngine.DeviceId);
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
            await RecordSyncHistoryAsync(
                SynchronizationOverviewTabTitle,
                _currentSyncProfile?.ExchangeDirectory ?? SelectedProject?.RootPath ?? string.Empty,
                "Engine resumed",
                "Local runtime",
                _syncEngine.DeviceId);
        }, "Synchronisation reprise");
    }

    public async Task StopSyncAsync()
    {
        string scope = SynchronizationOverviewTabTitle;
        string path = _currentSyncProfile?.ExchangeDirectory ?? SelectedProject?.RootPath ?? string.Empty;
        string deviceId = _syncEngine?.DeviceId ?? "local";
        await RunOperationAsync("Arrêt de l'instance Sync CyRevision…", async () =>
        {
            await StopSyncCoreAsync();
            await RecordSyncHistoryAsync(scope, path, "Engine stopped", "Local runtime", deviceId);
        }, "Instance Sync CyRevision arrêtée");
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

    public async Task UpdateSelectedPeerRoleAsync()
    {
        if (SelectedPeerMember is null ||
            !TryGetRunningSyncContext(out ProjectDefinition? project, out _, out ManagedSyncthingEngine? engine))
        {
            StatusMessage = "Select an active member and start this project's Sync instance first.";
            return;
        }

        PeerMemberViewModel selected = SelectedPeerMember;
        PeerRole role = SelectedPeerMemberRole;
        await RunOperationAsync("Updating the signed member role…", async () =>
        {
            using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(project!, engine!.DeviceId);
            JsonPeerAdmissionService admission = CreateAdmissionService(project!.Id, identity);
            MembershipCertificate updated = await admission.UpdateDeviceRoleAsync(project.Id, selected.DeviceId, role);
            await LoadPeerMembersCoreAsync();
            SelectedPeerMember = PeerMembers.FirstOrDefault(member => member.DeviceId == updated.Device.DeviceId);
            _applicationLogService.Information(
                "security",
                $"member role updated device={updated.Device.DeviceId:N} role={updated.Role} epoch={updated.MembershipEpoch}",
                project.RootPath);
        }, $"{selected.DisplayName} is now {role}");
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
        if (string.IsNullOrWhiteSpace(VpnInvitationClientName))
        {
            StatusMessage = "Enter the client name before creating its VPN invitation.";
            return;
        }
        await RunOperationAsync("Création de l'invitation VPN signée…", async () =>
        {
            VpnProjectProfile profile = await SaveVpnFormCoreAsync();
            using FileDeviceIdentityStore identity = await OpenVpnIdentityAsync(profile);
            SignedVpnInvitation invitation = VpnPeerExchangeCodec.CreateInvitation(
                profile,
                identity,
                SelectedVpnInvitationCapability,
                TimeSpan.FromHours(24),
                VpnInvitationClientName);
            VpnExchangeText = VpnPeerExchangeCodec.ExportInvitationCode(invitation);
            VpnDetails = $"One-time signed invite for {VpnInvitationClientName.Trim()} · valid 24 h · private WireGuard keys stay local.";
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
            VpnExchangeText = VpnPeerExchangeCodec.ExportJoinResponseCode(response);
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
            "Saving the Unreal Swarm VPN session…",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            SwarmStatus = $"{profile.Role} · coordinator {profile.CoordinatorAddress} · TCP 8008/8009 over VPN";
        },
        "Unreal Swarm VPN session saved");

    public async Task DiagnoseSwarmAsync() => await RunOperationAsync(
            "Testing Swarm, VPN, DNS, firewall and coordinator ports…",
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
            "Applying Swarm Agent settings with backup…",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            SwarmOptionsUpdateResult result = await _swarmSetupService.UpdateAgentOptionsAsync(profile);
            SwarmStatus = $"Updated {string.Join(", ", result.UpdatedFields)}. Backup: {result.BackupPath}";
        },
        "Swarm Agent configuration applied");

    public async Task ApplySwarmDnsAsync() => await RunOperationAsync(
            "Applying the project-owned local Swarm DNS alias…",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            await _swarmSetupService.ApplyLocalDnsAliasAsync(profile);
            SwarmStatus = $"Local alias {profile.CoordinatorAlias} -> {profile.CoordinatorAddress} applied and DNS cache flushed.";
        },
        "Local Swarm DNS alias applied");

    public async Task RemoveSwarmDnsAsync() => await RunOperationAsync(
            "Removing only the CyRevision Swarm DNS block…",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            await _swarmSetupService.RemoveLocalDnsAliasAsync(profile);
            SwarmStatus = $"Local alias block for {profile.CoordinatorAlias} removed.";
        },
        "Local Swarm DNS alias removed");

    public async Task LaunchSwarmAgentAsync() => await RunOperationAsync(
            "Launching Swarm Agent…",
        async () =>
        {
            SwarmProjectProfile profile = await SaveSwarmFormCoreAsync();
            _swarmSetupService.LaunchAgent(profile);
            await Task.CompletedTask;
        },
        "Swarm Agent launched");

    public async Task LaunchSwarmCoordinatorAsync() => await RunOperationAsync(
            "Launching Swarm Coordinator…",
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
            "Saving secure VPN file exchange…",
        async () =>
        {
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            VpnFileStatus = $"Saved · binds only to {credentials.Profile.ListenAddress}:{credentials.Profile.Port} · project token required";
        },
        "Secure VPN file exchange saved");

    public async Task StartVpnFileExchangeAsync() => await RunOperationAsync(
            "Starting the VPN-only file endpoint…",
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
            "Stopping the VPN file endpoint…",
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
            "Testing the selected VPN file peer…",
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
            "Reading the selected peer shared folder…",
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
            "Sending and verifying the file through WireGuard…",
        async () =>
        {
            VpnPeerViewModel peer = SelectedVpnFilePeer
                                    ?? throw new InvalidOperationException("Select a VPN peer first.");
            VpnFileExchangeCredentials credentials = await SaveVpnFileExchangeFormCoreAsync();
            VpnFileTransferResult result = await _vpnFileExchangeService.SendFileAsync(
                peer.TunnelAddress, credentials.Profile.Port, credentials.AccessToken, path);
            VpnFileStatus = $"Sent {result.Name} · {result.Size:N0} bytes · SHA-256 {result.Sha256[..12]}…";
        },
        "VPN file sent and verified");

    public async Task DownloadVpnSharedFileAsync(string destinationPath) => await RunOperationAsync(
            "Downloading and verifying the shared file through WireGuard…",
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
            VpnFileStatus = $"Received {result.Name} · SHA-256 {result.Sha256[..12]}… · {result.DestinationPath}";
        },
        "Shared file downloaded and verified");

    public async Task RotateVpnFileTokenAsync() => await RunOperationAsync(
            "Rotating the project file-exchange token…",
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

    public void SetTeamChatSyncFolder(string path) => TeamChatSyncFolderPath = path;

    public void SetTeamChatAttachment(string path) => TeamChatAttachmentPath = path;

    public async Task LoadTeamChatAsync()
    {
        if (SelectedProject is null) return;
        await StopTeamChatWatcherAsync();
        TeamChatProfile profile = await _teamChatProfileStore.GetOrCreateAsync(SelectedProject.Id, Environment.UserName);
        profile = profile with { ProjectRoot = SelectedProject.RootPath };
        _currentTeamChatProfile = profile;
        ApplyTeamChatProfile(profile);
        try
        {
            await RefreshTeamChatCoreAsync(profile);
            if (profile.Transport == TeamChatTransport.SyncFolder) StartTeamChatWatcher(profile);
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException or HttpRequestException)
        {
            _teamChatMessageCache.Clear();
            ReplaceCollection(TeamChatMessages, []);
            TeamChatStatus = $"Profile loaded - conversation endpoint is not reachable yet: {exception.Message}";
        }
    }

    public async Task SaveTeamChatAsync() => await RunOperationAsync(
        "Saving team chat settings...",
        async () =>
        {
            TeamChatProfile profile = BuildTeamChatProfile();
            await _teamChatProfileStore.SaveAsync(profile);
            _currentTeamChatProfile = profile;
            await StopTeamChatWatcherAsync();
            if (profile.Transport == TeamChatTransport.SyncFolder) StartTeamChatWatcher(profile);
            TeamChatStatus = $"Saved - {profile.Transport} - {(profile.SaveConversations ? "conversation archive enabled" : "session only")}.";
        },
        "Team chat settings saved");

    public async Task StartTeamChatHostAsync() => await RunOperationAsync(
        "Starting the VPN team chat host...",
        async () =>
        {
            TeamChatProfile profile = BuildTeamChatProfile() with { Transport = TeamChatTransport.Vpn };
            await _teamChatProfileStore.SaveAsync(profile);
            if (_teamChatHost is not null) await _teamChatHost.DisposeAsync();
            _teamChatHost = await _teamChatService.StartVpnHostAsync(profile);
            _currentTeamChatProfile = profile;
            IsTeamChatHostRunning = true;
            TeamChatStatus = $"Listening on {_teamChatHost.Endpoint} - private/VPN addresses only - token authenticated.";
        },
        "VPN team chat host started");

    public async Task StopTeamChatHostAsync() => await RunOperationAsync(
        "Stopping the team chat host...",
        async () =>
        {
            if (_teamChatHost is not null)
            {
                await _teamChatHost.DisposeAsync();
                _teamChatHost = null;
            }
            IsTeamChatHostRunning = false;
            TeamChatStatus = "VPN team chat host stopped.";
        },
        "Team chat host stopped");

    public async Task SendTeamChatMessageAsync() => await RunOperationAsync(
        "Sending the team chat message...",
        async () =>
        {
            TeamChatProfile profile = BuildTeamChatProfile();
            await _teamChatProfileStore.SaveAsync(profile);
            TeamChatMessage message = profile.Transport switch
            {
                TeamChatTransport.SyncFolder => await _teamChatService.SendSyncAsync(
                    profile, TeamChatMessageText, TeamChatAttachmentPath),
                TeamChatTransport.PrivateServer => await _teamChatService.SendServerAsync(
                    profile, TeamChatMessageText, TeamChatAttachmentPath),
                _ => await _teamChatService.SendVpnAsync(profile, TeamChatMessageText, TeamChatAttachmentPath)
            };
            _currentTeamChatProfile = profile;
            TeamChatMessageText = string.Empty;
            TeamChatAttachmentPath = string.Empty;
            if (message.DeliveryState == TeamChatDeliveryState.Pending)
            {
                MergeTeamChatMessages([message]);
                TeamChatStatus = "Message queued locally; the VPN host is unavailable and CyRevision will retry.";
            }
            else
            {
                await RefreshTeamChatCoreAsync(profile);
            }
            SelectedTeamChatMessage = TeamChatMessages.FirstOrDefault(item => item.Id == message.Id)
                                      ?? TeamChatMessages.LastOrDefault();
            _applicationLogService.Information(
                "team-chat",
                $"sent transport={profile.Transport} attachment={message.AttachmentSize}B",
                SelectedProject?.RootPath);
        },
        "Team chat message sent");

    public async Task RefreshTeamChatAsync() => await RunOperationAsync(
        "Refreshing the team conversation...",
        async () =>
        {
            TeamChatProfile profile = BuildTeamChatProfile();
            await RefreshTeamChatCoreAsync(profile);
        },
        "Team conversation refreshed");

    public async Task CreateTeamChatChannelAsync() => await RunOperationAsync(
        "Creating the team chat channel...",
        async () =>
        {
            TeamChatProfile profile = BuildTeamChatProfile();
            if (profile.Transport != TeamChatTransport.PrivateServer)
                throw new InvalidOperationException("Custom persistent channels require the Private server transport.");
            TeamChatChannel channel = await _teamChatService.CreateServerChannelAsync(
                profile,
                TeamChatNewChannelName,
                TeamChatNewChannelTopic);
            if (TeamChatChannels.All(item => !string.Equals(item.Id, channel.Id, StringComparison.OrdinalIgnoreCase)))
                TeamChatChannels.Add(channel);
            SelectedTeamChatChannel = TeamChatChannels.First(item =>
                string.Equals(item.Id, channel.Id, StringComparison.OrdinalIgnoreCase));
            TeamChatNewChannelName = string.Empty;
            TeamChatNewChannelTopic = string.Empty;
            TeamChatStatus = $"Channel #{channel.Name} is ready on the private server.";
        },
        "Team chat channel created");

    public async Task RotateTeamChatTokenAsync() => await RunOperationAsync(
        "Rotating the team chat token...",
        async () =>
        {
            if (SelectedProject is null) throw new InvalidOperationException("Select a project first.");
            if (_teamChatHost is not null)
            {
                await _teamChatHost.DisposeAsync();
                _teamChatHost = null;
            }
            IsTeamChatHostRunning = false;
            TeamChatProfile profile = await _teamChatProfileStore.RotateTokenAsync(SelectedProject.Id);
            _currentTeamChatProfile = profile;
            ApplyTeamChatProfile(profile);
            TeamChatStatus = "Access token rotated. Share the new token separately with authorized teammates.";
        },
        "Team chat token rotated");

    private async Task RefreshTeamChatCoreAsync(TeamChatProfile profile)
    {
        TeamChatMessage? selected = SelectedTeamChatMessage;
        TeamChatSnapshot snapshot = profile.Transport switch
        {
            TeamChatTransport.SyncFolder => await _teamChatService.ReadSyncSnapshotAsync(profile),
            TeamChatTransport.PrivateServer => await _teamChatService.ReadServerSnapshotAsync(profile),
            _ => await _teamChatService.ReadVpnSnapshotAsync(profile)
        };
        MergeTeamChatMessages(snapshot.Messages);
        ReplaceCollection(TeamChatParticipants, snapshot.Participants);
        ApplyTeamChatChannels(snapshot.Channels ?? TeamChatDefaults.Channels, profile.SelectedChannelId);
        SelectedTeamChatMessage = selected is null
            ? TeamChatMessages.LastOrDefault()
            : TeamChatMessages.FirstOrDefault(item => item.Id == selected.Id) ?? TeamChatMessages.LastOrDefault();
        TeamChatStatus = $"{TeamChatMessages.Count:N0} message(s) · {snapshot.Participants.Count(item => item.IsOnline):N0} online · " +
                         $"{profile.Transport} · parsed {snapshot.FilesParsed:N0}/{snapshot.FilesScanned:N0}.";
    }

    public async Task<string?> PrepareSelectedTeamChatAttachmentAsync()
    {
        if (SelectedTeamChatMessage is null || string.IsNullOrWhiteSpace(SelectedTeamChatMessage.AttachmentName)) return null;
        if (!string.IsNullOrWhiteSpace(SelectedTeamChatMessage.AttachmentLocalPath) &&
            File.Exists(SelectedTeamChatMessage.AttachmentLocalPath)) return SelectedTeamChatMessage.AttachmentLocalPath;
        if (_currentTeamChatProfile is null) return null;
        TeamChatProfile profile = BuildTeamChatProfile();
        TeamChatMessage downloaded = profile.Transport switch
        {
            TeamChatTransport.SyncFolder => await _teamChatService.PrepareSyncAttachmentAsync(profile, SelectedTeamChatMessage),
            TeamChatTransport.PrivateServer => await _teamChatService.DownloadServerAttachmentAsync(profile, SelectedTeamChatMessage),
            _ => await _teamChatService.DownloadVpnAttachmentAsync(profile, SelectedTeamChatMessage)
        };
        MergeTeamChatMessages([downloaded]);
        SelectedTeamChatMessage = TeamChatMessages.FirstOrDefault(item => item.Id == downloaded.Id);
        return downloaded.AttachmentLocalPath;
    }

    private void MergeTeamChatMessages(IEnumerable<TeamChatMessage> messages)
    {
        foreach (TeamChatMessage message in messages) _teamChatMessageCache[message.Id] = message;
        if (_teamChatMessageCache.Count > 4000)
        {
            HashSet<Guid> retained = _teamChatMessageCache.Values.OrderBy(item => item.SentAt).TakeLast(4000)
                .Select(item => item.Id).ToHashSet();
            foreach (Guid id in _teamChatMessageCache.Keys.Where(id => !retained.Contains(id)).ToArray())
                _teamChatMessageCache.Remove(id);
        }
        RefreshVisibleTeamChatMessages();
    }

    private void RefreshVisibleTeamChatMessages()
    {
        string channelId = SelectedTeamChatChannel?.Id ?? "general";
        TeamChatMessage? selected = SelectedTeamChatMessage;
        ReplaceCollection(TeamChatMessages, _teamChatMessageCache.Values
            .Where(item => string.Equals(
                string.IsNullOrWhiteSpace(item.ChannelId) ? "general" : item.ChannelId,
                channelId,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SentAt)
            .TakeLast(2000));
        SelectedTeamChatMessage = selected is null
            ? TeamChatMessages.LastOrDefault()
            : TeamChatMessages.FirstOrDefault(item => item.Id == selected.Id) ?? TeamChatMessages.LastOrDefault();
    }

    private void ApplyTeamChatChannels(IEnumerable<TeamChatChannel> channels, string selectedChannelId)
    {
        string selected = SelectedTeamChatChannel?.Id ?? selectedChannelId;
        TeamChatChannel[] normalized = channels
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Position)
            .ToArray();
        if (normalized.Length == 0) normalized = TeamChatDefaults.Channels.ToArray();
        ReplaceCollection(TeamChatChannels, normalized);
        SelectedTeamChatChannel = TeamChatChannels.FirstOrDefault(item =>
                                      string.Equals(item.Id, selected, StringComparison.OrdinalIgnoreCase))
                                  ?? TeamChatChannels.FirstOrDefault(item => item.IsDefault)
                                  ?? TeamChatChannels.FirstOrDefault();
    }

    private void StartTeamChatWatcher(TeamChatProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SyncFolderPath)) return;
        _teamChatSyncWatcher = _teamChatService.WatchSync(profile);
        _teamChatSyncWatcher.ChangedAvailable += OnTeamChatSyncChanged;
    }

    private void OnTeamChatSyncChanged(object? sender, EventArgs eventArgs)
    {
        _teamChatRefreshCancellation?.Cancel();
        _teamChatRefreshCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _teamChatRefreshCancellation = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cancellation.Token);
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (_currentTeamChatProfile is { Transport: TeamChatTransport.SyncFolder } profile)
                        await RefreshTeamChatCoreAsync(profile);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _applicationLogService.Warning("team-chat", $"background refresh failed: {exception.Message}", SelectedProject?.RootPath);
            }
        }, cancellation.Token);
    }

    private async Task StopTeamChatWatcherAsync()
    {
        _teamChatRefreshCancellation?.Cancel();
        _teamChatRefreshCancellation?.Dispose();
        _teamChatRefreshCancellation = null;
        if (_teamChatSyncWatcher is null) return;
        _teamChatSyncWatcher.ChangedAvailable -= OnTeamChatSyncChanged;
        await _teamChatSyncWatcher.DisposeAsync();
        _teamChatSyncWatcher = null;
    }

    private TeamChatProfile BuildTeamChatProfile()
    {
        if (SelectedProject is null) throw new InvalidOperationException("Select a project first.");
        if (!int.TryParse(TeamChatPort, out int port) || port is < 1 or > 65535)
            throw new InvalidDataException("Team chat port is invalid.");
        if (!int.TryParse(TeamChatRetentionDays, out int retentionDays) || retentionDays is < 0 or > 36500)
            throw new InvalidDataException("Conversation retention must be between 0 and 36500 days.");
        if (!long.TryParse(TeamChatMaxAttachmentMb, out long maximumMb) || maximumMb is < 1 or > 2048)
            throw new InvalidDataException("Attachment limit must be between 1 and 2048 MB.");
        string token = string.IsNullOrWhiteSpace(TeamChatAccessToken)
            ? _currentTeamChatProfile?.AccessToken ?? throw new InvalidOperationException("Load team chat first.")
            : TeamChatAccessToken.Trim();
        return new TeamChatProfile(
            SelectedProject.Id,
            string.IsNullOrWhiteSpace(TeamChatDisplayName) ? Environment.UserName : TeamChatDisplayName.Trim(),
            SelectedTeamChatTransport,
            TeamChatListenAddress.Trim(),
            port,
            TeamChatPeerEndpoint.Trim(),
            token,
            TeamChatSyncFolderPath.Trim(),
            TeamChatSaveConversations,
            retentionDays,
            checked(maximumMb * 1024 * 1024),
            DateTimeOffset.UtcNow,
            SelectedProject.RootPath,
            TeamChatEncryptStoredConversations,
            TeamChatServerBaseUrl.Trim(),
            TeamChatServerApiToken.Trim(),
            TeamChatAllowPrivateServerHttp,
            SelectedTeamChatChannel?.Id ?? "general");
    }

    private void ApplyTeamChatProfile(TeamChatProfile profile)
    {
        SelectedTeamChatTransport = profile.Transport;
        TeamChatDisplayName = profile.DisplayName;
        TeamChatListenAddress = profile.ListenAddress;
        TeamChatPort = profile.Port.ToString();
        TeamChatPeerEndpoint = profile.PeerEndpoint;
        TeamChatAccessToken = profile.AccessToken;
        TeamChatSyncFolderPath = profile.SyncFolderPath;
        TeamChatServerBaseUrl = profile.ServerBaseUrl;
        TeamChatServerApiToken = profile.ServerApiToken;
        TeamChatAllowPrivateServerHttp = profile.AllowPrivateServerHttp;
        TeamChatSaveConversations = profile.SaveConversations;
        TeamChatEncryptStoredConversations = profile.EncryptStoredConversations;
        TeamChatRetentionDays = profile.RetentionDays.ToString();
        TeamChatMaxAttachmentMb = Math.Max(1, profile.MaxAttachmentBytes / (1024 * 1024)).ToString();
        ApplyTeamChatChannels(TeamChatDefaults.Channels, profile.SelectedChannelId);
    }

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
            "Refreshing project members…",
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
                connectivity?.LastHandshakeAt?.ToLocalTime().ToString("g") ?? "—",
                peer.Endpoint,
                online ? "#78D7B7" : peer.Peer.Enabled ? "#E5C07B" : "#7E8189",
                online));
        }

        ProjectMembersSummary =
            $"Sync {SyncProjectMembers.Count(member => member.IsOnline)}/{SyncProjectMembers.Count} online · " +
            $"Git {GitProjectMembers.Count} contributor(s) · " +
            $"VPN {VpnProjectMembers.Count(member => member.IsOnline)}/{VpnProjectMembers.Count} connected";
        NotifyMemberPanelLayoutChanged();
    }

    private void NotifyMemberPanelLayoutChanged()
    {
        OnPropertyChanged(nameof(ShowSyncMemberPanel));
        OnPropertyChanged(nameof(ShowGitMemberPanel));
        OnPropertyChanged(nameof(ShowVpnMemberPanel));
        OnPropertyChanged(nameof(SyncMemberPanelWidth));
        OnPropertyChanged(nameof(GitMemberPanelWidth));
        OnPropertyChanged(nameof(VpnMemberPanelWidth));
        OnPropertyChanged(nameof(FirstMemberSplitterWidth));
        OnPropertyChanged(nameof(SecondMemberSplitterWidth));
    }

    private async Task LoadSelectedProjectAsync(
        ProjectItemViewModel? project,
        int loadVersion,
        CancellationTokenSource cancellationSource)
    {
        CancellationToken cancellationToken = cancellationSource.Token;
        Stopwatch loadStopwatch = Stopwatch.StartNew();
        IsProjectLoading = true;
        SetProjectLoadProgress(project, loadVersion, 2, project is null ? "Closing project…" : $"Opening {project.Name}…");
        try
        {
            await _discordAgent.StopAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentProjectLoad(project, loadVersion))
            {
                return;
            }

            ClearPullRequestData();
            if (project is null)
            {
                await StopSyncCoreAsync();
                ResetDiscordView();
                ClearRepositoryView();
                return;
            }

            if (_syncEngineProjectId is not null && _syncEngineProjectId != project.Id)
            {
                SetProjectLoadProgress(project, loadVersion, 5, "Stopping the previous project services…");
                await StopSyncCoreAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            ProjectDefinition updated = project.Definition with { LastOpenedAt = DateTimeOffset.UtcNow };
            project.Update(updated);
            LoadBackupSettings(updated);
            RefreshProjectModeCatalog();
            ClearGitGraphView();

            SetProjectLoadProgress(project, loadVersion, 8, "Loading project preferences…");
            await _projectCatalog.UpsertAsync(updated, cancellationToken);
            LocalChangePreferences localPreferences = await _localChangePreferencesStore.LoadAsync(updated.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentProjectLoad(project, loadVersion))
            {
                return;
            }

            _localOnlyChangePaths.Clear();
            foreach (string path in localPreferences.LocalOnlyPaths)
            {
                _localOnlyChangePaths.Add(NormalizeChangePath(path));
            }

            if (project.Definition.Features.GitEnabled)
            {
                SetProjectLoadProgress(project, loadVersion, 10, "Restoring the local .cyrevision cache…");
                await RestorePersistentGitCacheAsync(project, loadVersion, cancellationToken);
            }

            StatusMessage = $"Loading {project.Name}…";
            SetProjectLoadProgress(project, loadVersion, 12, "Reading Git status and current branch…");
            await RefreshCoreAsync(project, loadVersion, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentProjectLoad(project, loadVersion))
            {
                return;
            }

            if (project.Definition.Features.GitEnabled)
            {
                SetProjectLoadProgress(project, loadVersion, 70, "Scanning untracked files in the background…");
                GitRepositoryStatus detailedStatus = await _gitService.GetDetailedStatusAsync(project.RootPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentProjectLoad(project, loadVersion))
                {
                    return;
                }
                ApplyPrimaryGitStatus(project, detailedStatus);
                ScheduleProjectGitCacheSave(project, detailedStatus);
            }

            if (!_codeWorkspaceCache.ContainsKey(project.Id))
                CodeWorkspaceSummary = "Open Solution Explorer to index project files.";
            SetProjectLoadProgress(project, loadVersion, 100, $"{project.Name} is ready");
            project.SetLoaded($"{Changes.Count:N0} change(s) · ready", CurrentBranch);
            StatusMessage = $"{project.Name} loaded";
            _loadedProjectSessions.Add(project.Id);
            CacheCurrentProjectSession(project);
            _ = AutoStartProjectServicesAsync(project, loadVersion);
        }
        catch (OperationCanceledException)
        {
            // Selecting another project intentionally cancels this load.
            project?.SetLoadCancelled();
        }
        catch (Exception exception)
        {
            if (IsCurrentProjectLoad(project, loadVersion))
            {
                StatusMessage = exception.Message;
                ProjectLoadStage = $"Unable to load {project?.Name ?? "project"}: {exception.Message}";
                project?.SetLoadError(exception.Message);
            }
        }
        finally
        {
            RecordPerformanceMetric(
                "Project",
                project is null ? "Close project" : "Load project",
                loadStopwatch.Elapsed,
                project?.Name ?? "No project");
            if (ReferenceEquals(_projectLoadCancellation, cancellationSource))
            {
                IsProjectLoading = false;
                _projectLoadCancellation = null;
                cancellationSource.Dispose();
            }
        }
    }

    private async Task AutoStartProjectServicesAsync(ProjectItemViewModel project, int loadVersion)
    {
        try
        {
            if (project.Definition.StartSyncAutomatically)
            {
                await LoadSyncProfileCoreAsync();
                if (IsCurrentProjectLoad(project, loadVersion))
                    await StartSyncAsync();
            }
            if (project.Definition.StartVpnAutomatically && IsCurrentProjectLoad(project, loadVersion))
            {
                await LoadVpnProfileCoreAsync();
                if (IsCurrentProjectLoad(project, loadVersion))
                    await StartVpnAsync();
            }
        }
        catch (Exception exception)
        {
            _applicationLogService.Warning(
                "startup",
                $"automatic project service startup failed: {exception.Message}",
                project.RootPath);
        }
    }

    private async Task LoadProjectSupplementalDataAsync(
        ProjectItemViewModel project,
        int loadVersion,
        CancellationToken cancellationToken)
    {
        async Task RunStageAsync(double progress, string stage, Func<Task> operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentProjectLoad(project, loadVersion))
            {
                throw new OperationCanceledException(cancellationToken);
            }
            SetProjectLoadProgress(project, loadVersion, progress, stage);
            await operation();
        }

        await RunStageAsync(68, "Loading LFS storage settings…", LoadLfsManagementCoreAsync);
        await RunStageAsync(71, "Loading remote-build settings…", LoadRemoteBuildCoreAsync);
        await RunStageAsync(74, "Loading backups…", LoadBackupsCoreAsync);
        await RunStageAsync(78, "Loading synchronization profile…", LoadSyncProfileCoreAsync);
        await RunStageAsync(82, "Loading VPN profile…", LoadVpnProfileCoreAsync);
        await RunStageAsync(85, "Loading Discord agent settings…", LoadDiscordProfileCoreAsync);
        await RunStageAsync(88, "Loading work-in-progress reservations…", LoadAdvisoryReservationsCoreAsync);
        await RunStageAsync(91, "Loading AI and MCP settings…", LoadAiMcpProfileCoreAsync);
        await RunStageAsync(94, "Detecting pull-request remote…", ResolvePullRequestRepositoryAsync);
        await RunStageAsync(97, "Building project member overview…", () => RefreshProjectMembersCoreAsync(testConnections: false));
    }

    private async Task RestorePersistentGitCacheAsync(
        ProjectItemViewModel project,
        int loadVersion,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProjectGitCacheSnapshot? cached = await _projectGitCacheStore.LoadAsync(
            project.RootPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (cached is null || !IsCurrentProjectLoad(project, loadVersion)) return;

        ApplyPrimaryGitStatus(project, cached.Status);
        ReplaceCollection(Branches, cached.Branches);
        ApplyBranchFilter();
        RefreshCompositionBranches();
        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
        _allExplorerRevisions = cached.History.ToArray();
        ApplyExplorerFilter();
        ReplaceCollection(LfsPatterns, cached.LfsPatterns);
        StatusMessage = $"{project.Name} restored from .cyrevision · validating in background";
        _applicationLogService.Debug(
            "cache",
            $"persistent Git cache restored changes={cached.Status.Changes.Count} branches={cached.Branches.Count} " +
            $"commits={cached.History.Count} age={(DateTimeOffset.UtcNow - cached.CapturedAt).TotalMinutes:N1}min " +
            $"duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms",
            project.RootPath);
    }

    private void ScheduleProjectGitCacheSave(ProjectItemViewModel project, GitRepositoryStatus status)
    {
        _gitCacheSaveCancellation?.Cancel();
        _gitCacheSaveCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _gitCacheSaveCancellation = cancellation;
        GitBranch[] branches = Branches.ToArray();
        GitRevision[] history = _allExplorerRevisions.ToArray();
        LfsTrackedPattern[] lfsPatterns = LfsPatterns.ToArray();
        _ = SaveProjectGitCacheAfterDelayAsync(
            project,
            status,
            branches,
            history,
            lfsPatterns,
            cancellation);
    }

    private async Task SaveProjectGitCacheAfterDelayAsync(
        ProjectItemViewModel project,
        GitRepositoryStatus status,
        IReadOnlyList<GitBranch> branches,
        IReadOnlyList<GitRevision> history,
        IReadOnlyList<LfsTrackedPattern> lfsPatterns,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            await _projectGitCacheStore.SaveAsync(
                project.RootPath,
                status,
                branches,
                history,
                lfsPatterns,
                cancellation.Token);
            _applicationLogService.Debug(
                "cache",
                $"persistent Git cache saved changes={status.Changes.Count} branches={branches.Count} commits={history.Count}",
                project.RootPath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _applicationLogService.Warning("cache", $"persistent Git cache save failed: {exception.Message}", project.RootPath);
        }
        finally
        {
            if (ReferenceEquals(_gitCacheSaveCancellation, cancellation)) _gitCacheSaveCancellation = null;
            cancellation.Dispose();
        }
    }

    private bool IsCurrentProjectLoad(ProjectItemViewModel? project, int loadVersion) =>
        loadVersion == _projectLoadVersion &&
        ((project is null && SelectedProject is null) ||
         (project is not null && SelectedProject?.Id == project.Id));

    private void SetProjectLoadProgress(
        ProjectItemViewModel? project,
        int loadVersion,
        double progress,
        string stage)
    {
        if (!IsCurrentProjectLoad(project, loadVersion))
        {
            return;
        }

        ProjectLoadProgress = Math.Clamp(progress, 0, 100);
        ProjectLoadStage = stage;
        project?.SetLoading(stage, progress);
    }

    private void NotifyLongOperationStateChanged()
    {
        OnPropertyChanged(nameof(IsLongOperationActive));
        OnPropertyChanged(nameof(IsLongOperationIndeterminate));
        OnPropertyChanged(nameof(LongOperationProgress));
        OnPropertyChanged(nameof(LongOperationStage));
        OnPropertyChanged(nameof(HasActivityCenterContent));
        OnPropertyChanged(nameof(ActivityCenterHeadline));
        OnPropertyChanged(nameof(ActivityCenterBadgeText));
    }

    private OperationTaskViewModel BeginTrackedTask(string title, string projectName, string detail = "")
    {
        OperationTaskViewModel task = new(title, projectName, detail);
        ActiveOperations.Insert(0, task);
        NotifyActiveOperationsChanged();
        return task;
    }

    private void CompleteTrackedTask(OperationTaskViewModel task, string state, string detail)
    {
        task.Complete(state, detail);
        PromoteCompletedOperationToNotification(task);
        ActiveOperations.Remove(task);
        RecentOperations.Remove(task);
        RecentOperations.Insert(0, task);
        while (RecentOperations.Count > 80) RecentOperations.RemoveAt(RecentOperations.Count - 1);
        if (task.IsAttention)
        {
            IsActivityCenterExpanded = true;
        }
        NotifyActiveOperationsChanged();
        RefreshProjectNotifications();
    }

    public void PublishApplicationError(string title, string detail)
    {
        OperationTaskViewModel notification = new(title, SelectedProject?.Name ?? "Application", detail);
        notification.Complete("Failed", detail);
        notification.PromoteToNotification();
        RecentOperations.Insert(0, notification);
        while (RecentOperations.Count > 80) RecentOperations.RemoveAt(RecentOperations.Count - 1);
        IsActivityCenterExpanded = true;
        NotifyActiveOperationsChanged();
        RefreshProjectNotifications();
    }

    public void DismissRecentOperation(OperationTaskViewModel task)
    {
        RecentOperations.Remove(task);
        NotifyActiveOperationsChanged();
        RefreshProjectNotifications();
    }

    public void ClearRecentOperations()
    {
        RecentOperations.Clear();
        IsActivityCenterExpanded = false;
        NotifyActiveOperationsChanged();
        RefreshProjectNotifications();
    }

    public void DismissActivityCenter()
    {
        _isActivityCenterDismissed = true;
        IsActivityCenterExpanded = false;
        OnPropertyChanged(nameof(HasActivityCenterContent));
    }

    private void NotifyActiveOperationsChanged()
    {
        OnPropertyChanged(nameof(HasActiveOperations));
        OnPropertyChanged(nameof(HasRecentOperations));
        OnPropertyChanged(nameof(HasOperationAlerts));
        OnPropertyChanged(nameof(HasActivityCenterContent));
        OnPropertyChanged(nameof(ActiveOperationCountText));
        OnPropertyChanged(nameof(ActivityCenterBadgeText));
        OnPropertyChanged(nameof(ActivityCenterHeadline));
        OnPropertyChanged(nameof(OperationHistoryCountText));
        NotifyLongOperationStateChanged();
    }

    private void ClearFastGitViewForProjectSwitch(ProjectItemViewModel? project)
    {
        CurrentProjectName = project?.Name ?? "No project";
        RepositoryPath = project?.RootPath ?? "No project selected";
        CurrentBranch = project is null
            ? "—"
            : project.Definition.Features.GitEnabled ? "Loading branch…" : "No Git";
        ChangeSummary = project is null ? "0 changes" : $"{project.Name} · loading changes…";
        ChangeSummaryColor = project is null ? "#A9ABB2" : "#61AFEF";
        foreach (GitChangeViewModel change in Changes)
        {
            change.PreparationChanged -= OnChangePreparationItemChanged;
        }
        Changes.Clear();
        NotifyGitConflictStateChanged();
        PreparedChanges.Clear();
        FilteredVersionedChanges.Clear();
        FilteredUnversionedChanges.Clear();
        LocalOnlyChanges.Clear();
        FilteredLocalOnlyChanges.Clear();
        ChangeTree.Clear();
        History.Clear();
        _allExplorerRevisions = [];
        ExplorerFiles.Clear();
        ExplorerFileHistory.Clear();
        Branches.Clear();
        FilteredBranches.Clear();
        SetSelectedBranches([]);
        SelectedBranchHistory.Clear();
        LfsPatterns.Clear();
        ClearGitIgnoreView();
        LfsFiles.Clear();
        LfsFileTree.Clear();
        IsLfsInventoryLoaded = false;
        LfsVersions.Clear();
        LfsLocks.Clear();
        MyLfsLocks.Clear();
        FilteredLfsLocks.Clear();
        FilteredMyLfsLocks.Clear();
        SelectedChange = null;
        SelectedBranch = null;
        SelectedExplorerRevision = null;
        SelectedLfsFile = null;
        LfsLockFilterSummary = "Loading locks…";
        project?.SetLoading("Waiting for Git status…", 0);
    }

    private void CacheCurrentProjectSession(ProjectItemViewModel project)
    {
        _projectSessionCache.TryGetValue(project.Id, out CachedProjectSession? previousSession);
        Dictionary<string, object?> fields = [];
        foreach (FieldInfo field in ProjectSessionFields)
        {
            fields[field.Name] = field.GetValue(this);
        }

        Dictionary<string, object?[]> collections = [];
        foreach (PropertyInfo property in ProjectSessionCollections)
        {
            if (property.GetValue(this) is not IEnumerable values) continue;
            if (values is IList list &&
                previousSession?.Collections.TryGetValue(property.Name, out object?[]? previousValues) == true &&
                CollectionSnapshotStillMatches(list, previousValues))
            {
                collections[property.Name] = previousValues;
            }
            else
            {
                collections[property.Name] = values.Cast<object?>().ToArray();
            }
        }

        _latestProjectStatuses.TryGetValue(project.Id, out GitRepositoryStatus? status);
        _projectSessionCache[project.Id] = new CachedProjectSession(
            fields,
            collections,
            status,
            _localOnlyChangePaths.ToArray(),
            DateTimeOffset.UtcNow);
        TouchCacheEntry(_projectSessionCacheUsage, project.Id);
        TrimProjectSessionCache();
    }

    private static bool CollectionSnapshotStillMatches(IList current, object?[] snapshot)
    {
        if (current.Count != snapshot.Length) return false;
        for (int index = 0; index < current.Count; index++)
        {
            if (!ReferenceEquals(current[index], snapshot[index])) return false;
        }
        return true;
    }

    private bool RestoreCachedProjectSession(ProjectItemViewModel project)
    {
        if (!_projectSessionCache.TryGetValue(project.Id, out CachedProjectSession? cached)) return false;
        TouchCacheEntry(_projectSessionCacheUsage, project.Id);

        foreach ((string fieldName, object? value) in cached.Fields)
        {
            if (ProjectSessionFieldsByName.TryGetValue(fieldName, out FieldInfo? field)) field.SetValue(this, value);
        }

        foreach ((string propertyName, object?[] values) in cached.Collections)
        {
            ProjectSessionCollectionsByName.TryGetValue(propertyName, out PropertyInfo? property);
            if (property?.GetValue(this) is not IList list) continue;
            if (list is IBatchReplaceCollection batch)
            {
                batch.ReplaceAll(values);
                continue;
            }
            list.Clear();
            foreach (object? value in values) list.Add(value);
        }

        _localOnlyChangePaths.Clear();
        foreach (string path in cached.LocalOnlyPaths) _localOnlyChangePaths.Add(path);
        _isRestoringProjectSession = true;
        try
        {
            bool restoredPreparedChanges = cached.Collections.ContainsKey(nameof(Changes));
            if (restoredPreparedChanges)
            {
                foreach (GitChangeViewModel change in Changes)
                {
                    change.PreparationChanged -= OnChangePreparationItemChanged;
                    change.PreparationChanged += OnChangePreparationItemChanged;
                }
            }
            if (cached.Status is not null)
                ApplyPrimaryGitStatus(project, cached.Status, rebuildChanges: !restoredPreparedChanges);
        }
        finally
        {
            _isRestoringProjectSession = false;
        }

        IsProjectLoading = false;
        ProjectLoadProgress = 100;
        ProjectLoadStage = $"{project.Name} restored from memory · Refresh to update";
        StatusMessage = $"{project.Name} restored from memory";
        project.SetLoaded($"{Changes.Count:N0} change(s) · cached", CurrentBranch);
        OnPropertyChanged(null);
        return true;
    }

    private void TrimProjectSessionCache()
    {
        while (_projectSessionCacheUsage.Count > 3)
        {
            Guid oldest = _projectSessionCacheUsage.First!.Value;
            _projectSessionCacheUsage.RemoveFirst();
            _projectSessionCache.Remove(oldest);
            _latestProjectStatuses.Remove(oldest);
            _workspaceLoadCoordinator.InvalidateProject(oldest);
        }
    }

    private static void TouchCacheEntry(LinkedList<Guid> usage, Guid projectId)
    {
        LinkedListNode<Guid>? existing = usage.Find(projectId);
        if (existing is not null) usage.Remove(existing);
        usage.AddLast(projectId);
    }

    private static bool ShouldCacheProjectField(FieldInfo field)
    {
        if (field.IsStatic || field.IsInitOnly || typeof(CancellationTokenSource).IsAssignableFrom(field.FieldType))
            return false;

        string name = field.Name;
        if (name.Contains("LoadVersion", StringComparison.Ordinal) ||
            name.Contains("Cancellation", StringComparison.Ordinal) ||
            (name.StartsWith("_is", StringComparison.Ordinal) && name.Contains("Loading", StringComparison.Ordinal)))
            return false;

        return name is not (
            "_selectedProject" or "_projectLoadVersion" or "_projectLoadProgress" or "_projectLoadStage" or
            "_isProjectLoading" or "_isRestoringProjectSession" or "_isBusy" or "_statusMessage" or "_isActivityCenterExpanded" or "_syncEngine" or "_syncEngineProjectId" or
            "_vpnFileExchangeHost" or "_subscribedUnrealPlugin" or "_subscribedUnityPlugin" or "_subscribedGodotPlugin" or "_currentRemoteBuildJob" or "_availableUpdate" or
            "_selectedLanguage" or "_allDocumentationTopics" or "_selectedDocumentationTopic" or
            "_documentationSearch" or "_latestApplicationVersion" or "_updateStatus" or "_updateReleaseNotes" or
            "_hasUpdateAvailable" or "_isCheckingForUpdates" or "_isDownloadingUpdate" or "_updateProgress" or
            "_selectedPlugin" or "_selectedAiConversation" or "_suppressAiConversationSelection" or
            "_isUnrealIntegrationEnabled" or "_isUnityIntegrationEnabled" or "_isGodotIntegrationEnabled" or "_discordIsRunning") &&
               !name.StartsWith("_code", StringComparison.Ordinal) &&
               !name.StartsWith("_selectedCode", StringComparison.Ordinal) &&
               !name.StartsWith("_repositoryConsole", StringComparison.Ordinal) &&
               !name.StartsWith("_applicationLog", StringComparison.Ordinal) &&
               !name.StartsWith("_performance", StringComparison.Ordinal);
    }

    private static bool ShouldCacheProjectCollection(PropertyInfo property)
    {
        if (!property.PropertyType.IsGenericType ||
            property.PropertyType.GetGenericTypeDefinition() != typeof(ObservableCollection<>)) return false;
        return property.Name is not (
            nameof(Projects) or nameof(DocumentationTopics) or nameof(Plugins) or nameof(CodeTree) or nameof(CodeFileList) or
            nameof(CodeFileSearchResults) or nameof(AssetExplorerFiles) or nameof(CodeSearchResults) or
            nameof(CodeHistory) or nameof(CodeSymbols) or nameof(IgnoreFolderSuggestions) or
            nameof(IgnoreFileTypeSuggestions) or nameof(AiProviders) or nameof(AiConversations) or
            nameof(RepositoryCommandHistory) or nameof(FilteredRepositoryCommandHistory) or
            nameof(ApplicationLogEntries) or nameof(FilteredApplicationLogEntries) or nameof(ActiveOperations) or nameof(RecentOperations) or
            nameof(PerformanceMetrics) or nameof(GitAnnotations) or nameof(FilteredGitAnnotations) or nameof(ProjectNotifications) or
            nameof(ProjectSidebarGroups));
    }

    private async Task LoadSelectedDiffAsync(
        ProjectItemViewModel? project,
        GitChangeViewModel? change,
        int diffLoadVersion)
    {
        if (project is null || change is null)
        {
            if (diffLoadVersion == _selectedDiffLoadVersion)
            {
                DiffPreviewImage = null;
                DiffPresentationSummary = string.Empty;
                DiffText = "Select a file to display its diff.";
            }
            return;
        }

        _workingTreeDiffCancellation?.Cancel();
        _workingTreeDiffCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _workingTreeDiffCancellation = cancellation;
        DiffPreviewImage = null;
        DiffPresentationSummary = string.Empty;
        string cacheKey = BuildWorkingTreeDiffCacheKey(project, change);
        if (_diffCache.TryGetValue(cacheKey, out string? cachedDiff))
        {
            DiffText = cachedDiff;
            IsWorkingTreeDiffLoading = true;
            try
            {
                FilePresentationResult? cachedPresentation = await TryCreateWorkingTreePresentationAsync(
                    project,
                    change,
                    cancellation.Token);
                if (diffLoadVersion == _selectedDiffLoadVersion &&
                    SelectedProject?.Id == project.Id &&
                    ReferenceEquals(SelectedChange, change))
                    ApplyDiffPresentation(cachedPresentation, workingTree: true);
            }
            finally
            {
                if (diffLoadVersion == _selectedDiffLoadVersion) IsWorkingTreeDiffLoading = false;
            }
            return;
        }

        Stopwatch diffStopwatch = Stopwatch.StartNew();
        try
        {
            IsWorkingTreeDiffLoading = true;
            DiffText = $"Loading diff for {change.Path}…";
            string diff = change.IsUntracked
                ? await BuildUntrackedFileDiffAsync(project.RootPath, change.Path, cancellation.Token)
                : await _gitService.GetDiffAsync(
                    project.RootPath,
                    change.Path,
                    change.Change.IsStaged,
                    cancellation.Token);
            if (diffLoadVersion != _selectedDiffLoadVersion ||
                SelectedProject?.Id != project.Id ||
                !ReferenceEquals(SelectedChange, change))
            {
                return;
            }
            string displayDiff = string.IsNullOrWhiteSpace(diff)
                ? change.Change.IsLfsObject
                    ? "Fichier LFS binaire — le diff visuel sera proposé par CyRevision Diff."
                    : "Aucun diff textuel disponible pour ce fichier."
                : diff;
            StoreDiffCache(cacheKey, displayDiff);
            DiffText = displayDiff;
            FilePresentationResult? presentation = await TryCreateWorkingTreePresentationAsync(
                project,
                change,
                cancellation.Token);
            if (diffLoadVersion != _selectedDiffLoadVersion ||
                SelectedProject?.Id != project.Id ||
                !ReferenceEquals(SelectedChange, change)) return;
            ApplyDiffPresentation(presentation, workingTree: true);
        }
        catch (OperationCanceledException)
        {
            // Rapid selection changes intentionally cancel obsolete Git processes.
        }
        catch (Exception exception)
        {
            if (diffLoadVersion == _selectedDiffLoadVersion && SelectedProject?.Id == project.Id)
            {
                DiffText = exception.Message;
            }
        }
        finally
        {
            RecordPerformanceMetric("Diff", "Working tree", diffStopwatch.Elapsed, change.Path);
            if (diffLoadVersion == _selectedDiffLoadVersion && SelectedProject?.Id == project.Id)
            {
                IsWorkingTreeDiffLoading = false;
            }
            if (ReferenceEquals(_workingTreeDiffCancellation, cancellation))
            {
                _workingTreeDiffCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private string BuildWorkingTreeDiffCacheKey(ProjectItemViewModel project, GitChangeViewModel change)
    {
        string stamp = "missing";
        try
        {
            string fullPath = Path.Combine(project.RootPath, change.Path.Replace('/', Path.DirectorySeparatorChar));
            FileInfo info = new(fullPath);
            if (info.Exists) stamp = $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return $"working:{project.Id}:{_workingTreeDiffGeneration}:{change.Change.IsStaged}:{change.Path}:{stamp}";
    }

    private async Task<FilePresentationResult?> TryCreateWorkingTreePresentationAsync(
        ProjectItemViewModel project,
        GitChangeViewModel change,
        CancellationToken cancellationToken)
    {
        string candidatePath = Path.Combine(
            project.RootPath,
            change.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!_filePresentationService.HasProviderFor(candidatePath)) return null;

        try
        {
            FileInfo candidate = new(candidatePath);
            if (change.Change.Kind is GitChangeKind.Added or GitChangeKind.Untracked || !candidate.Exists)
            {
                if (!candidate.Exists) return null;
                return await _filePresentationService.CreatePreviewAsync(
                    new FilePreviewRequest(project.RootPath, change.Path, candidatePath, candidate.Length),
                    cancellationToken);
            }

            string artifactDirectory = GetDiffArtifactDirectory();
            Directory.CreateDirectory(artifactDirectory);
            string baselinePath = Path.Combine(
                artifactDirectory,
                $"working-head-{Guid.NewGuid():N}{Path.GetExtension(change.Path)}");
            await _gitService.ExportFileFromRevisionAsync(
                project.RootPath,
                change.Path,
                "HEAD",
                baselinePath,
                cancellationToken);
            return await _filePresentationService.CreateDiffAsync(
                new FileDiffRequest(project.RootPath, change.Path, baselinePath, candidatePath),
                artifactDirectory,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _applicationLogService.Warning(
                "file.presentation",
                $"working-tree presentation unavailable path=\"{change.Path}\": {exception.Message}",
                project.RootPath);
            return null;
        }
    }

    private async Task<FilePresentationResult?> TryCreateRevisionPresentationAsync(
        ProjectItemViewModel project,
        string revision,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string probePath = Path.Combine(project.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!_filePresentationService.HasProviderFor(probePath)) return null;

        string artifactDirectory = GetDiffArtifactDirectory();
        Directory.CreateDirectory(artifactDirectory);
        string extension = Path.GetExtension(relativePath);
        string candidateRevision = _comparisonToHash ?? revision;
        string baselineRevision = _comparisonFromHash ?? $"{revision}^1";
        string candidatePath = Path.Combine(artifactDirectory, $"revision-{Guid.NewGuid():N}{extension}");
        string baselinePath = Path.Combine(artifactDirectory, $"baseline-{Guid.NewGuid():N}{extension}");
        try
        {
            await _gitService.ExportFileFromRevisionAsync(
                project.RootPath, relativePath, candidateRevision, candidatePath, cancellationToken);
            try
            {
                await _gitService.ExportFileFromRevisionAsync(
                    project.RootPath, relativePath, baselineRevision, baselinePath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                FileInfo candidate = new(candidatePath);
                return await _filePresentationService.CreatePreviewAsync(
                    new FilePreviewRequest(project.RootPath, relativePath, candidatePath, candidate.Length),
                    cancellationToken);
            }

            return await _filePresentationService.CreateDiffAsync(
                new FileDiffRequest(project.RootPath, relativePath, baselinePath, candidatePath),
                artifactDirectory,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _applicationLogService.Warning(
                "file.presentation",
                $"revision presentation unavailable path=\"{relativePath}\": {exception.Message}",
                project.RootPath);
            return null;
        }
    }

    private void ApplyDiffPresentation(FilePresentationResult? presentation, bool workingTree)
    {
        Bitmap? image = null;
        if (presentation?.ImagePath is not null && File.Exists(presentation.ImagePath))
        {
            try { image = new Bitmap(presentation.ImagePath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _applicationLogService.Warning(
                    "file.presentation",
                    $"preview image could not be decoded path=\"{presentation.ImagePath}\": {exception.Message}",
                    SelectedProject?.RootPath);
            }
        }

        if (workingTree)
        {
            DiffPreviewImage = image;
            DiffPresentationSummary = presentation?.Summary ?? string.Empty;
            if (image is null && !string.IsNullOrWhiteSpace(presentation?.TextContent))
                DiffText = presentation.TextContent;
        }
        else
        {
            ExplorerDiffPreviewImage = image;
            ExplorerDiffPresentationSummary = presentation?.Summary ?? string.Empty;
            if (image is null && !string.IsNullOrWhiteSpace(presentation?.TextContent))
                ExplorerDiff = presentation.TextContent;
        }
    }

    private async Task<FilePresentationResult?> TryCreateRevisionPairPresentationAsync(
        ProjectItemViewModel project,
        string baselineRevision,
        string candidateRevision,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string probePath = Path.Combine(project.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!_filePresentationService.HasProviderFor(probePath)) return null;
        string artifactDirectory = GetDiffArtifactDirectory();
        Directory.CreateDirectory(artifactDirectory);
        string extension = Path.GetExtension(relativePath);
        string candidatePath = Path.Combine(artifactDirectory, $"revision-{Guid.NewGuid():N}{extension}");
        string baselinePath = Path.Combine(artifactDirectory, $"baseline-{Guid.NewGuid():N}{extension}");
        try
        {
            await _gitService.ExportFileFromRevisionAsync(
                project.RootPath, relativePath, candidateRevision, candidatePath, cancellationToken);
            try
            {
                await _gitService.ExportFileFromRevisionAsync(
                    project.RootPath, relativePath, baselineRevision, baselinePath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                FileInfo candidate = new(candidatePath);
                return await _filePresentationService.CreatePreviewAsync(
                    new FilePreviewRequest(project.RootPath, relativePath, candidatePath, candidate.Length),
                    cancellationToken);
            }
            return await _filePresentationService.CreateDiffAsync(
                new FileDiffRequest(project.RootPath, relativePath, baselinePath, candidatePath),
                artifactDirectory,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _applicationLogService.Warning(
                "file.presentation",
                $"revision-pair presentation unavailable path=\"{relativePath}\": {exception.Message}",
                project.RootPath);
            return null;
        }
    }

    private void ApplyMultiRestorePresentation(FilePresentationResult? presentation)
    {
        MultiRestoreDiffPresentationSummary = presentation?.Summary ?? string.Empty;
        if (presentation?.ImagePath is not null && File.Exists(presentation.ImagePath))
        {
            try
            {
                MultiRestoreDiffPreviewImage = new Bitmap(presentation.ImagePath);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _applicationLogService.Warning(
                    "file.presentation",
                    $"multi-restore preview image could not be decoded: {exception.Message}",
                    SelectedProject?.RootPath);
            }
        }
        MultiRestoreDiffPreviewImage = null;
        if (!string.IsNullOrWhiteSpace(presentation?.TextContent))
            MultiRestoreDiff = presentation.TextContent;
    }

    private void StoreDiffCache(string key, string value)
    {
        _diffCache.Set(key, value);
    }

    public async Task<string> LoadSelectedDiffForExternalAsync()
    {
        int version = Interlocked.Increment(ref _selectedDiffLoadVersion);
        await LoadSelectedDiffAsync(SelectedProject, SelectedChange, version);
        return DiffText;
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

    private async Task LoadExplorerRevisionAsync(GitRevision revision, int loadVersion)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null)
        {
            return;
        }

        _explorerRevisionCancellation?.Cancel();
        _explorerRevisionCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _explorerRevisionCancellation = cancellation;
        _comparisonFromHash = null;
        _comparisonToHash = null;
        IsExplorerLoading = true;
        ExplorerSummary = $"Loading {revision.ShortHash} · {revision.Subject}…";
        ExplorerDiff = "Loading committed files…";
        try
        {
            string cacheKey = $"{project.Id}:{revision.Hash}";
            GitCommitDetails details;
            if (_commitDetailsCache.TryGetValue(cacheKey, out GitCommitDetails? cachedDetails) && cachedDetails is not null)
            {
                details = cachedDetails;
            }
            else
            {
                details = await _gitService.GetCommitDetailsAsync(project.RootPath, revision.Hash, cancellation.Token);
                _commitDetailsCache.Set(cacheKey, details);
            }
            if (loadVersion != _explorerRevisionLoadVersion ||
                SelectedProject?.Id != project.Id ||
                SelectedExplorerRevision?.Hash != revision.Hash)
            {
                return;
            }

            ReplaceCollection(ExplorerFiles, details.Files);
            string mergeContext = details.ParentHashes.Count > 1
                ? $" · merge vs first parent {details.ParentHashes[0][..Math.Min(8, details.ParentHashes[0].Length)]}"
                : string.Empty;
            ExplorerSummary = $"{details.Revision.ShortHash} · {details.Revision.Subject}{mergeContext} · " +
                              $"{details.Files.Count} fichier(s) · +{details.AddedLines} / -{details.DeletedLines} · " +
                              $"{details.BinaryFileCount} binaire(s)";
            ExplorerDiff = details.Files.Count == 0
                ? "This commit does not contain a file change."
                : "Select a committed file to inspect its diff.";
            SelectedExplorerFile = null;
            SelectedExplorerFile = ExplorerFiles.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // A newer commit selection superseded this inspection.
        }
        catch (Exception exception)
        {
            if (loadVersion != _explorerRevisionLoadVersion ||
                SelectedProject?.Id != project.Id ||
                SelectedExplorerRevision?.Hash != revision.Hash)
            {
                return;
            }
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
        finally
        {
            if (loadVersion == _explorerRevisionLoadVersion &&
                SelectedProject?.Id == project.Id &&
                SelectedExplorerRevision?.Hash == revision.Hash)
            {
                IsExplorerLoading = false;
            }
            if (ReferenceEquals(_explorerRevisionCancellation, cancellation))
            {
                _explorerRevisionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private async Task LoadExplorerFileAsync(GitCommitFileChange? file, int loadVersion)
    {
        ProjectItemViewModel? project = SelectedProject;
        GitRevision? revision = SelectedExplorerRevision;
        if (project is null || revision is null || file is null)
        {
            ExplorerFileHistory.Clear();
            ExplorerDiffPreviewImage = null;
            ExplorerDiffPresentationSummary = string.Empty;
            return;
        }

        _explorerFileCancellation?.Cancel();
        _explorerFileCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _explorerFileCancellation = cancellation;
        ExplorerDiffPreviewImage = null;
        ExplorerDiffPresentationSummary = string.Empty;
        string revisionHash = revision.Hash;
        string comparisonKey = _comparisonFromHash is not null && _comparisonToHash is not null
            ? $"{_comparisonFromHash}:{_comparisonToHash}"
            : revisionHash;
        string diffCacheKey = $"commit:{project.Id}:{comparisonKey}:{file.Path}";
        string historyCacheKey = $"history:{project.Id}:{file.Path}";
        IsExplorerDiffLoading = true;
        if (_diffCache.TryGetValue(diffCacheKey, out string? cachedDiff))
        {
            ExplorerDiff = cachedDiff;
            IsExplorerDiffLoading = false;
        }
        else
        {
            ExplorerDiff = $"Loading diff for {file.Path}…";
        }
        try
        {
            Task<IReadOnlyList<GitFileRevision>> historyTask = _fileHistoryCache.TryGetValue(historyCacheKey, out IReadOnlyList<GitFileRevision>? cachedHistory)
                ? Task.FromResult(cachedHistory)
                : _gitService.GetFileHistoryAsync(project.RootPath, file.Path, 100, cancellation.Token);

            if (cachedDiff is null)
            {
                string diff = _comparisonFromHash is not null && _comparisonToHash is not null
                    ? await _gitService.GetComparisonDiffAsync(
                        project.RootPath, _comparisonFromHash, _comparisonToHash, file.Path, cancellation.Token)
                    : await _gitService.GetCommitDiffAsync(project.RootPath, revisionHash, file.Path, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (loadVersion != _explorerFileLoadVersion || SelectedProject?.Id != project.Id ||
                    SelectedExplorerRevision?.Hash != revisionHash || SelectedExplorerFile?.Path != file.Path)
                    return;
                cachedDiff = string.IsNullOrWhiteSpace(diff)
                    ? file.IsLfsObject
                        ? "Git LFS object: use the LFS timeline to preview or export this version."
                        : "No textual diff is available for this file."
                    : diff;
                StoreDiffCache(diffCacheKey, cachedDiff);
                ExplorerDiff = cachedDiff;
                IsExplorerDiffLoading = false;
            }

            FilePresentationResult? presentation = await TryCreateRevisionPresentationAsync(
                project,
                revisionHash,
                file.Path,
                cancellation.Token);
            if (loadVersion != _explorerFileLoadVersion || SelectedProject?.Id != project.Id ||
                SelectedExplorerRevision?.Hash != revisionHash || SelectedExplorerFile?.Path != file.Path)
                return;
            ApplyDiffPresentation(presentation, workingTree: false);

            IReadOnlyList<GitFileRevision> history = await historyTask;
            if (loadVersion != _explorerFileLoadVersion ||
                SelectedProject?.Id != project.Id ||
                SelectedExplorerRevision?.Hash != revisionHash ||
                SelectedExplorerFile?.Path != file.Path)
            {
                return;
            }

            _fileHistoryCache.Set(historyCacheKey, history);
            ReplaceCollection(ExplorerFileHistory, history);
        }
        catch (OperationCanceledException)
        {
            // A newer file selection superseded this load.
        }
        catch (Exception exception)
        {
            if (loadVersion == _explorerFileLoadVersion &&
                SelectedProject?.Id == project.Id &&
                SelectedExplorerRevision?.Hash == revisionHash &&
                SelectedExplorerFile?.Path == file.Path)
            {
                ExplorerDiff = exception.Message;
            }
        }
        finally
        {
            if (loadVersion == _explorerFileLoadVersion &&
                SelectedProject?.Id == project.Id &&
                SelectedExplorerRevision?.Hash == revisionHash &&
                SelectedExplorerFile?.Path == file.Path)
            {
                IsExplorerDiffLoading = false;
            }
            if (ReferenceEquals(_explorerFileCancellation, cancellation))
            {
                _explorerFileCancellation = null;
                cancellation.Dispose();
            }
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

    private void ApplyCommitExplorerFilter()
    {
        string query = ExplorerSearch.Trim();
        IEnumerable<GitRevision> filtered = _commitExplorerSourceRevisions;
        if (query.Length > 0)
        {
            filtered = filtered.Where(revision =>
                revision.Hash.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                revision.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                revision.AuthorName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                revision.AuthorEmail.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceCollection(CommitExplorerRevisions, filtered);
        if (SelectedCommitExplorerRevision is not null &&
            CommitExplorerRevisions.All(revision => revision.Hash != SelectedCommitExplorerRevision.Hash))
        {
            SelectedCommitExplorerRevision = CommitExplorerRevisions.FirstOrDefault();
        }
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

    private async Task RefreshCoreAsync(
        ProjectItemViewModel? expectedProject = null,
        int? expectedLoadVersion = null,
        CancellationToken cancellationToken = default,
        bool includeUntrackedFiles = false)
    {
        ProjectItemViewModel? project = expectedProject ?? SelectedProject;
        if (project is null || !IsRefreshContextCurrent(project, expectedLoadVersion))
        {
            return;
        }

        if (!project.Definition.Features.GitEnabled)
        {
            RemoteUrl = string.Empty;
            RepositoryPath = project.RootPath;
            CurrentProjectName = project.Name;
            CurrentBranch = "No Git";
            ChangeSummary = $"{project.Name} · file synchronization / backup";
            ChangeSummaryColor = "#78D7B7";
            Changes.Clear();
            NotifyGitConflictStateChanged();
            PreparedChanges.Clear();
            FilteredVersionedChanges.Clear();
            FilteredUnversionedChanges.Clear();
            LocalOnlyChanges.Clear();
            FilteredLocalOnlyChanges.Clear();
            ChangeTree.Clear();
            UpdateChangePreparationSummary();
            History.Clear();
            Branches.Clear();
            FilteredBranches.Clear();
            SetSelectedBranches([]);
            SelectedBranchHistory.Clear();
            LfsPatterns.Clear();
            LfsLocks.Clear();
            MyLfsLocks.Clear();
            FilteredLfsLocks.Clear();
            FilteredMyLfsLocks.Clear();
            ApplyLfsLockFilter();
            LfsLocksSummary = "Git is disabled for this project.";
            ExplorerFiles.Clear();
            ExplorerFileHistory.Clear();
            LfsFiles.Clear();
            LfsFileTree.Clear();
            IsLfsInventoryLoaded = false;
            LfsVersions.Clear();
            _allExplorerRevisions = [];
            SelectedExplorerRevision = null;
            SelectedLfsFile = null;
            SelectedBranch = null;
            SelectedChange = null;
            DiffText = "Git is disabled for this project.";
            project.SetGitState("No Git", "File synchronization / backup");
            return;
        }

        string rootPath = project.RootPath;
        Task<GitRepositoryStatus> statusTask = includeUntrackedFiles
            ? _gitService.GetDetailedStatusAsync(rootPath, cancellationToken)
            : _gitService.GetQuickStatusAsync(rootPath, cancellationToken);
        Task<IReadOnlyList<GitBranch>> branchesTask = _gitService.GetBranchesAsync(rootPath, cancellationToken);
        Task<string?> remoteTask = _gitService.GetRemoteUrlAsync(rootPath);
        Task<IReadOnlyList<GitRevision>> historyTask = IncludeRemoteHistory
            ? _gitService.GetHistoryAcrossRefsAsync(rootPath, cancellationToken: cancellationToken)
            : _gitService.GetHistoryAsync(rootPath, cancellationToken: cancellationToken);
        Task<IReadOnlyList<LfsTrackedPattern>> lfsTask = _gitService.GetLfsPatternsAsync(rootPath, cancellationToken);

        await Task.WhenAll(statusTask, branchesTask, remoteTask);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRefreshContextCurrent(project, expectedLoadVersion))
        {
            return;
        }

        GitRepositoryStatus status = await statusTask;
        RemoteUrl = await remoteTask ?? string.Empty;
        ApplyPrimaryGitStatus(project, status);
        ReplaceCollection(Branches, await branchesTask);
        ApplyBranchFilter();
        RefreshCompositionBranches();
        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
        if (expectedLoadVersion is int primaryLoadVersion)
        {
            SetProjectLoadProgress(project, primaryLoadVersion, 28, "Git status ready · loading history and LFS metadata…");
        }

        await Task.WhenAll(historyTask, lfsTask);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRefreshContextCurrent(project, expectedLoadVersion))
        {
            return;
        }

        _allExplorerRevisions = (await historyTask).ToArray();
        ApplyExplorerFilter();
        ReplaceCollection(LfsPatterns, await lfsTask);
        GitRevision? firstRevision = History.FirstOrDefault();
        if (firstRevision is not null && SelectedExplorerRevision?.Hash == firstRevision.Hash)
        {
            int loadVersion = Interlocked.Increment(ref _explorerRevisionLoadVersion);
            _ = LoadExplorerRevisionAsync(firstRevision, loadVersion);
        }
        else
        {
            SelectedExplorerRevision = firstRevision;
        }

        UpdateSmartSyncPlan();
        if (expectedLoadVersion is int metadataLoadVersion)
        {
            SetProjectLoadProgress(project, metadataLoadVersion, 45, "Git history and LFS metadata ready");
        }
        if (includeUntrackedFiles)
        {
            ScheduleProjectGitCacheSave(project, status);
        }
    }

    private bool IsRefreshContextCurrent(ProjectItemViewModel project, int? expectedLoadVersion) =>
        SelectedProject?.Id == project.Id &&
        (!expectedLoadVersion.HasValue || expectedLoadVersion.Value == _projectLoadVersion);

    private void ApplyPrimaryGitStatus(
        ProjectItemViewModel project,
        GitRepositoryStatus status,
        bool rebuildChanges = true)
    {
        _latestProjectStatuses[project.Id] = status;
        if (!_isRestoringProjectSession && rebuildChanges) Interlocked.Increment(ref _workingTreeDiffGeneration);
        CurrentBranch = status.IsDetachedHead ? $"HEAD {status.CurrentBranch}" : status.CurrentBranch;
        CurrentProjectName = project.Name;
        RepositoryPath = status.RootPath;
        ChangeSummary = $"{project.Name} · {status.Changes.Count:N0} change(s) · ↑{status.AheadBy} ↓{status.BehindBy}";
        ChangeSummaryColor = status.Changes.Any(change => change.Kind == GitChangeKind.Conflicted)
            ? "#E06C75"
            : status.Changes.Count > 0 ? "#E5C07B" : "#78D7B7";
        if (rebuildChanges)
            RebuildChangePreparation(status.Changes, SelectedChange?.Path);
        project.SetGitState(CurrentBranch, $"{status.Changes.Count:N0} change(s)");
    }

    private void RebuildChangePreparation(
        IReadOnlyList<GitChange> changes,
        string? selectedPath = null)
    {
        Dictionary<string, bool> previousSelection = Changes
            .GroupBy(change => NormalizeChangePath(change.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().IsIncluded, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, GitChangeViewModel> existingChanges = Changes
            .GroupBy(change => NormalizeChangePath(change.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, LfsFileLock> locks = LfsLocks
            .GroupBy(fileLock => NormalizeChangePath(fileLock.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<GitChangeViewModel> allChanges = new(changes.Count);
        List<GitChangeViewModel> preparedChanges = new(changes.Count);
        List<GitChangeViewModel> localOnlyChanges = [];
        HashSet<GitChangeViewModel> reusedChanges = [];
        _suspendChangePreparationSummary = true;
        try
        {
            foreach (GitChange change in changes.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                string path = NormalizeChangePath(change.Path);
                bool localOnly = change.Kind == GitChangeKind.Untracked && _localOnlyChangePaths.Contains(path);
                bool included = previousSelection.TryGetValue(path, out bool wasIncluded)
                    ? wasIncluded
                    : false;
                locks.TryGetValue(path, out LfsFileLock? fileLock);
                GitChangeViewModel item;
                if (existingChanges.TryGetValue(path, out GitChangeViewModel? existing))
                {
                    item = existing;
                    item.UpdateChange(change);
                    item.IsLocalOnly = localOnly;
                    item.IsIncluded = included;
                    item.UpdateFileLock(fileLock);
                    reusedChanges.Add(item);
                }
                else
                {
                    item = new GitChangeViewModel(change, included, localOnly, fileLock);
                    item.PreparationChanged += OnChangePreparationItemChanged;
                }
                allChanges.Add(item);
                if (localOnly)
                {
                    localOnlyChanges.Add(item);
                }
                else
                {
                    preparedChanges.Add(item);
                }
            }
        }
        finally
        {
            _suspendChangePreparationSummary = false;
            _changePreparationSummaryPending = false;
        }

        foreach (GitChangeViewModel removed in existingChanges.Values.Where(change => !reusedChanges.Contains(change)))
        {
            removed.PreparationChanged -= OnChangePreparationItemChanged;
        }

        ReplaceCollection(Changes, allChanges);
        NotifyGitConflictStateChanged();
        ReplaceCollection(PreparedChanges, preparedChanges);
        ReplaceCollection(LocalOnlyChanges, localOnlyChanges);

        ApplyChangeFilter();
        SelectedChange = Changes.FirstOrDefault(change =>
                             string.Equals(change.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                         ?? PreparedChanges.FirstOrDefault()
                         ?? LocalOnlyChanges.FirstOrDefault();
        UpdateChangePreparationSummary();
    }

    private void NotifyGitConflictStateChanged()
    {
        OnPropertyChanged(nameof(GitConflictCount));
        OnPropertyChanged(nameof(HasGitConflicts));
        OnPropertyChanged(nameof(GitConflictActionText));
    }

    private void ApplyChangeFilter()
    {
        string[] terms = ChangeSearch.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool MatchesTerm(GitChangeViewModel change, string term)
        {
            if (term.IndexOfAny(['*', '?']) >= 0 || term.Contains(';'))
                return CodeFilePatternMatcher.IsMatch(change.Path, term);

            return change.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   change.State.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   change.Area.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   change.LockOwner.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        bool Matches(GitChangeViewModel change) =>
            terms.Length == 0 || terms.All(term => MatchesTerm(change, term));

        IOrderedEnumerable<GitChangeViewModel> Sort(IEnumerable<GitChangeViewModel> source) => ChangeSort switch
        {
            "Checked" => source.OrderByDescending(change => change.IsIncluded)
                .ThenBy(change => change.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase),
            "Name" or "File" => source.OrderBy(change => change.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase),
            "State" => source.OrderBy(change => change.State, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase),
            "Lock" => source.OrderByDescending(change => change.HasLock)
                .ThenBy(change => change.LockOwner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase),
            "Area" => source.OrderBy(change => change.Area, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderBy(change => change.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
        };

        GitChangeViewModel[] visible = terms.Length == 0
            ? Changes.ToArray()
            : Changes.Where(Matches).ToArray();
        IEnumerable<GitChangeViewModel> versioned = visible.Where(change => change.IsTracked);
        IEnumerable<GitChangeViewModel> unversioned = visible.Where(change => change.IsUntracked && !change.IsLocalOnly);
        IEnumerable<GitChangeViewModel> localOnly = visible.Where(change => change.IsLocalOnly);
        ReplaceCollection(FilteredVersionedChanges, Sort(versioned));
        ReplaceCollection(FilteredUnversionedChanges, Sort(unversioned));
        ReplaceCollection(FilteredLocalOnlyChanges, Sort(localOnly));
        RebuildFlatChangeTree();
        RebuildChangeTree(visible);
    }

    private void RebuildFlatChangeTree()
    {
        FlatChangeTree.Clear();
        AddFlatChangeTreeGroup("Versioned files", "tracked", FilteredVersionedChanges);
        AddFlatChangeTreeGroup("Unversioned files", "untracked", FilteredUnversionedChanges);
        AddFlatChangeTreeGroup("Local-only files", "local", FilteredLocalOnlyChanges);
    }

    private void AddFlatChangeTreeGroup(
        string title,
        string groupKind,
        IReadOnlyList<GitChangeViewModel> changes)
    {
        bool isExpanded = !string.Equals(groupKind, "local", StringComparison.OrdinalIgnoreCase);
        GitChangeTreeNode group = GitChangeTreeNode.CreateFlatGroup(title, groupKind, changes, isExpanded);
        if (isExpanded) group.EnsureChildrenLoaded();
        FlatChangeTree.Add(group);
    }

    private void RebuildChangeTree(IEnumerable<GitChangeViewModel>? source = null)
    {
        GitChangeViewModel[] visible = (source ?? Changes).ToArray();
        ChangeTree.Clear();
        AddChangeTreeGroup("Tracked changes", "tracked", visible.Where(change => change.IsTracked));
        AddChangeTreeGroup("Untracked files", "untracked", visible.Where(change => change.IsUntracked && !change.IsLocalOnly));
        AddChangeTreeGroup("Local-only files", "local", visible.Where(change => change.IsLocalOnly));
    }

    private void AddChangeTreeGroup(
        string title,
        string groupKind,
        IEnumerable<GitChangeViewModel> source)
    {
        GitChangeViewModel[] changes = source as GitChangeViewModel[] ?? source.ToArray();
        if (changes.Length == 0)
        {
            return;
        }

        bool isExpanded = !string.Equals(groupKind, "local", StringComparison.OrdinalIgnoreCase);
        GitChangeTreeNode group = GitChangeTreeNode.CreateLazyGroup(title, groupKind, changes, isExpanded);
        if (isExpanded) group.EnsureChildrenLoaded();
        ChangeTree.Add(group);
    }

    public void SetChangeTreeNodeIncluded(GitChangeTreeNode node, bool include)
    {
        ArgumentNullException.ThrowIfNull(node);
        _suspendChangePreparationSummary = true;
        _changePreparationSummaryPending = false;
        try
        {
            foreach (GitChangeViewModel change in node.ContainedChanges)
            {
                if (!change.IsLocalOnly) change.IsIncluded = include;
            }
        }
        finally
        {
            _suspendChangePreparationSummary = false;
            _changePreparationSummaryPending = false;
        }

        if (ChangeSort == "Checked") ApplyChangeFilter();
        else RefreshChangeTreeIncludedState();
        UpdateChangePreparationSummary();
    }

    private void RefreshChangeTreeIncludedState()
    {
        foreach (GitChangeTreeNode root in FlatChangeTree) root.RefreshIncludedState();
        foreach (GitChangeTreeNode root in ChangeTree) root.RefreshIncludedState();
    }

    private void OnChangePreparationItemChanged(object? sender, EventArgs e)
    {
        if (_suspendChangePreparationSummary)
        {
            _changePreparationSummaryPending = true;
            return;
        }

        if (ChangeSort == "Checked") ApplyChangeFilter();
        else RefreshChangeTreeIncludedState();
        UpdateChangePreparationSummary();
    }

    private void UpdateChangePreparationSummary()
    {
        int tracked = 0;
        int untracked = 0;
        int included = 0;
        int kept = 0;
        int foreignLockedIncluded = 0;
        int locked = 0;
        foreach (GitChangeViewModel change in Changes)
        {
            if (change.IsTracked)
            {
                tracked++;
            }
            else if (!change.IsLocalOnly)
            {
                untracked++;
            }

            if (!change.IsLocalOnly)
            {
                if (change.IsIncluded)
                {
                    included++;
                }
                else
                {
                    kept++;
                }
            }

            if (change.IsIncluded && change.HasForeignLock)
            {
                foreignLockedIncluded++;
            }

            if (change.HasLock)
            {
                locked++;
            }
        }

        int local = LocalOnlyChanges.Count;
        _includedChangeCount = included;
        _keptChangeCount = kept;
        _foreignLockedIncludedCount = foreignLockedIncluded;
        ChangePreparationSummary =
            $"{included} selected · {kept} kept · {tracked} tracked · {untracked} untracked · {local} local-only" +
            (locked > 0 ? $" · {locked} locked" : string.Empty);
        OnPropertyChanged(nameof(IncludedChangeCount));
        OnPropertyChanged(nameof(KeptChangeCount));
        OnPropertyChanged(nameof(ForeignLockedIncludedCount));
        OnPropertyChanged(nameof(CanCommitPreparedChanges));
        OnPropertyChanged(nameof(CanGenerateAiCommitDescription));
    }

    private async Task SaveLocalChangePreferencesAsync()
    {
        if (SelectedProject is not null)
        {
            await _localChangePreferencesStore.SaveAsync(SelectedProject.Id, _localOnlyChangePaths);
        }
    }

    private static async Task<string> BuildUntrackedFileDiffAsync(
        string repositoryRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison) || !File.Exists(fullPath))
        {
            return "The untracked file is no longer available.";
        }

        FileInfo file = new(fullPath);
        if (file.Length > 2 * 1024 * 1024)
        {
            return $"Untracked file · {file.Length / 1024d / 1024d:0.##} MB · text preview limited to 2 MB.";
        }

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (bytes.Contains((byte)0))
        {
            return $"Untracked binary file · {file.Length / 1024d:0.#} KB · no textual diff available.";
        }

        string[] lines = Encoding.UTF8.GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        int visibleLineCount = Math.Min(lines.Length, 5000);
        StringBuilder diff = new();
        diff.AppendLine($"diff --git a/{relativePath} b/{relativePath}");
        diff.AppendLine("new file mode 100644");
        diff.AppendLine("--- /dev/null");
        diff.AppendLine($"+++ b/{relativePath}");
        diff.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
        for (int index = 0; index < visibleLineCount; index++)
        {
            diff.Append('+').AppendLine(lines[index]);
        }
        if (visibleLineCount < lines.Length)
        {
            diff.AppendLine($"+… {lines.Length - visibleLineCount} additional line(s) hidden …");
        }
        return diff.ToString();
    }

    private static string NormalizeChangePath(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

    private async Task LoadLfsLocksCoreAsync(
        CancellationToken cancellationToken = default,
        Guid? expectedProjectId = null)
    {
        ProjectItemViewModel? project = SelectedProject;
        LfsLocks.Clear();
        MyLfsLocks.Clear();
        FilteredLfsLocks.Clear();
        FilteredMyLfsLocks.Clear();
        SetSelectedLfsLocks([], mineList: false);
        SetSelectedLfsLocks([], mineList: true);
        ApplyLfsLockFilter();
        if (project is null ||
            (expectedProjectId.HasValue && project.Id != expectedProjectId.Value) ||
            !project.Definition.Features.GitEnabled)
        {
            LfsLocksSummary = "Git is disabled for this project.";
            ApplyLfsLockFilter();
            return;
        }

        try
        {
            IReadOnlyList<LfsFileLock> locks = await _gitService.GetLfsLocksAsync(project.RootPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedProject?.Id != project.Id ||
                (expectedProjectId.HasValue && expectedProjectId.Value != project.Id))
            {
                return;
            }
            ReplaceCollection(LfsLocks, locks);
            ReplaceCollection(MyLfsLocks, locks.Where(item => item.IsOurs));
            ApplyLfsLockFilter();
            int others = locks.Count - MyLfsLocks.Count;
            bool cached = locks.Any(item => item.IsCached);
            LfsLocksSummary = $"{locks.Count} project lock(s) · {MyLfsLocks.Count} mine · {others} other user(s)" +
                              (cached ? " · cached/offline data" : " · verified with the LFS server");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LfsLocksSummary = "Unable to load Git LFS locks: " + exception.Message;
            ApplyLfsLockFilter();
        }
        finally
        {
            if (SelectedProject?.Id == project.Id && Changes.Count > 0)
            {
                ApplyLfsLocksToChanges();
            }
        }
    }

    private void ApplyLfsLocksToChanges()
    {
        Dictionary<string, LfsFileLock> locks = LfsLocks
            .GroupBy(fileLock => NormalizeChangePath(fileLock.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (GitChangeViewModel change in Changes)
        {
            locks.TryGetValue(NormalizeChangePath(change.Path), out LfsFileLock? fileLock);
            changed |= change.UpdateFileLock(fileLock);
        }

        if (!changed)
        {
            return;
        }

        if (ChangeSort == "Lock" || !string.IsNullOrWhiteSpace(ChangeSearch))
        {
            ApplyChangeFilter();
        }
        UpdateChangePreparationSummary();
    }

    private void ApplyLfsLockFilter()
    {
        static bool Matches(LfsFileLock fileLock, string search)
        {
            string[] terms = search.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return terms.Length == 0 || terms.All(term =>
                fileLock.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(fileLock.Path).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                fileLock.OwnerName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                fileLock.Ownership.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                fileLock.Source.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                fileLock.Id.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        static IOrderedEnumerable<LfsFileLock> Sort(IEnumerable<LfsFileLock> source, string sort) => sort switch
        {
            "Owner" => source.OrderBy(item => item.Ownership, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase),
            "Locked by" => source.OrderBy(item => item.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase),
            "Date" => source.OrderByDescending(item => item.LockedAt)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
            "Source" => source.OrderBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
            "ID" => source.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
        };

        ReplaceCollection(
            FilteredLfsLocks,
            Sort(LfsLocks.Where(item => Matches(item, AllLfsLockSearch)), AllLfsLockSort));
        ReplaceCollection(
            FilteredMyLfsLocks,
            Sort(MyLfsLocks.Where(item => Matches(item, MyLfsLockSearch)), MyLfsLockSort));
        RebuildLfsLockTree(LfsLockTree, FilteredLfsLocks);
        RebuildLfsLockTree(MyLfsLockTree, FilteredMyLfsLocks);
        LfsLockFilterSummary = $"All {FilteredLfsLocks.Count:N0}/{LfsLocks.Count:N0} · " +
                               $"Mine {FilteredMyLfsLocks.Count:N0}/{MyLfsLocks.Count:N0}";
    }

    private static void RebuildLfsLockTree(
        ObservableCollection<LfsLockTreeNode> target,
        IEnumerable<LfsFileLock> locks)
    {
        target.Clear();
        Dictionary<string, LfsLockTreeNode> directories = new(StringComparer.OrdinalIgnoreCase);
        foreach (LfsFileLock fileLock in locks)
        {
            string[] parts = NormalizeChangePath(fileLock.Path)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            IList<LfsLockTreeNode> level = target;
            string currentPath = string.Empty;
            for (int index = 0; index < Math.Max(0, parts.Length - 1); index++)
            {
                string part = parts[index];
                currentPath = currentPath.Length == 0 ? part : currentPath + "/" + part;
                if (!directories.TryGetValue(currentPath, out LfsLockTreeNode? directory))
                {
                    directory = new LfsLockTreeNode(part, currentPath, true);
                    directories[currentPath] = directory;
                    level.Add(directory);
                }
                directory.IncrementLeafCount();
                level = directory.Children;
            }
            level.Add(new LfsLockTreeNode(parts.LastOrDefault() ?? fileLock.Path, fileLock.Path, false, fileLock));
        }
    }

    public void SetSelectedLfsLocks(IEnumerable<LfsFileLock> locks, bool mineList)
    {
        LfsFileLock[] selected = locks.DistinctBy(item => item.Id).ToArray();
        if (mineList)
        {
            _selectedMyLocks = selected;
            SelectedMyLockCount = selected.Length;
        }
        else
        {
            _selectedProjectLocks = selected;
            SelectedProjectLockCount = selected.Length;
        }
    }

    public async Task UnlockSelectedLfsLocksAsync(bool mineList)
    {
        if (SelectedProject is null) return;
        LfsFileLock[] targets = (mineList ? _selectedMyLocks : _selectedProjectLocks).ToArray();
        if (targets.Length == 0) return;

        await RunOperationAsync(
            $"Unlocking {targets.Length:N0} selected Git LFS file(s)…",
            async () =>
            {
                List<string> failures = [];
                foreach (LfsFileLock item in targets)
                {
                    try
                    {
                        _applicationLogService.Information(
                            "git-lfs",
                            $"unlock request path=\"{item.Path}\" lock_id={item.Id} owner=\"{item.OwnerName}\" force={(!item.IsOurs).ToString().ToLowerInvariant()}",
                            SelectedProject.RootPath);
                        await _gitService.UnlockLfsFileAsync(SelectedProject.RootPath, item.Id, force: !item.IsOurs);
                        _applicationLogService.Information(
                            "git-lfs",
                            $"unlock complete path=\"{item.Path}\" lock_id={item.Id}",
                            SelectedProject.RootPath);
                    }
                    catch (Exception exception)
                    {
                        _applicationLogService.Error(
                            "git-lfs",
                            $"unlock failed path=\"{item.Path}\" lock_id={item.Id}",
                            exception,
                            SelectedProject.RootPath);
                        failures.Add($"{item.Path}: {exception.Message}");
                    }
                }
                await LoadLfsLocksCoreAsync();
                if (failures.Count > 0)
                    throw new GitOperationException(string.Join(" | ", failures.Take(3)));
            },
            $"{targets.Length:N0} selected lock(s) processed");
    }

    private async Task LoadLfsManagementCoreAsync()
    {
        _lfsAnalysisCancellation?.Cancel();
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
        LfsTrimRemoteBackedHistory = profile.TrimRemoteBackedHistory;
        LfsRecentVersionsPerFile = profile.RecentVersionsPerFile.ToString();
        LfsRecentVersionExtensions = profile.RecentVersionExtensions;
        LfsRemoteVerificationTimeoutSeconds = profile.RemoteVerificationTimeoutSeconds.ToString();
        LfsAnalysisPercent = 0;
        LfsAnalysisStage = "Ready for a non-destructive analysis.";
        LfsStorageAreas.Clear();
        LfsLargestFiles.Clear();
        LfsRepositorySizeSummary = "Run a quick size scan to inspect working files, Git metadata, and the LFS cache.";
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
            SyncthingRuntimeSummary = "No project selected.";
            SyncthingFolderSummary = "No synchronized folder selected.";
            SyncthingIgnoreRules = string.Empty;
            SyncthingIgnoreStatus = "No .stignore file loaded.";
            SyncSourceFolderPath = string.Empty;
            SyncVersionStorePath = string.Empty;
            SyncCompressedBackupPath = string.Empty;
            SyncCompressedBackupEnabled = false;
            SyncStorageStatus = "No project selected.";
            SyncthingDevices.Clear();
            SyncthingDifferences.Clear();
            SyncthingLogs.Clear();
            SharedSyncFolders.Clear();
            SyncHistory.Clear();
            SyncHistorySummary = "No project selected.";
            _allSyncConflicts = [];
            SyncConflicts.Clear();
            SyncConflictBackups.Clear();
            SelectedSyncConflict = null;
            SelectedSyncConflictBackup = null;
            SyncConflictRetentionDays = "30";
            SyncConflictSummary = "No project selected.";
            SyncCommits.Clear();
            SelectedSyncCommit = null;
            SyncCommitStatus = "No project selected.";
            SelectedSharedSyncFolder = null;
            SharedSyncFolderStatus = "No independent shared folder configured.";
            SyncState = "Sync désactivé";
            SyncDetails = "Aucun projet sélectionné.";
            return;
        }

        _currentSyncProfile = await _syncthingProfileStore.GetAsync(SelectedProject.Id);
        _observedSyncDifferences.Clear();
        ReplaceCollection(SharedSyncFolders, _currentSyncProfile?.SharedFolders ?? []);
        OnPropertyChanged(nameof(SynchronizationScopeSummary));
        SelectedSharedSyncFolder = SharedSyncFolders.FirstOrDefault();
        SharedSyncFolderStatus = SharedSyncFolders.Count == 0
            ? "No independent shared folder configured."
            : $"{SharedSyncFolders.Count:N0} independent folder(s) · usable without project Sync.";
        SyncthingRuntimeInstallation installation = _syncthingRuntimeResolver.Detect(_currentSyncProfile?.ExecutablePath);
        SyncthingExecutablePath = _currentSyncProfile?.ExecutablePath ?? string.Empty;
        SyncthingRuntimeSummary = installation.IsAvailable
            ? $"{installation.Source} · {installation.Details}"
            : installation.Details;
        SelectedSyncthingFolderMode = _currentSyncProfile?.FolderMode ?? SyncthingFolderMode.SendReceive;
        SyncthingRescanInterval = (_currentSyncProfile?.RescanIntervalSeconds ?? 60).ToString();
        SyncthingFileWatcherEnabled = _currentSyncProfile?.FileWatcherEnabled ?? true;
        SyncSourceFolderPath = _currentSyncProfile?.ProjectFolderPath ?? SelectedProject.RootPath;
        SyncVersionStorePath = _currentSyncProfile?.VersioningDirectory ?? string.Empty;
        SyncCompressedBackupPath = _currentSyncProfile?.CompressedBackupDirectory ?? string.Empty;
        SyncCompressedBackupEnabled = _currentSyncProfile?.CompressedBackupEnabled ?? false;
        SyncConflictRetentionDays = (_currentSyncProfile?.ConflictBackupRetentionDays ?? 30).ToString();
        _allSyncConflicts = [];
        SyncConflicts.Clear();
        SelectedSyncConflict = null;
        SyncStorageStatus = IsVersionedSyncMode
            ? $"Source: {SyncSourceFolderPath} · versions: {(string.IsNullOrWhiteSpace(SyncVersionStorePath) ? "inside source" : SyncVersionStorePath)} · compressed backup: {(SyncCompressedBackupEnabled ? SyncCompressedBackupPath : "off")}"
            : $"Synchronized source: {SyncSourceFolderPath}";
        await RefreshSyncCommitsCoreAsync();
        if (_currentSyncProfile is not null)
        {
            await LoadSyncthingIgnoreRulesAsync();
        }
        if (!SelectedProject.Definition.Features.PeerSyncEnabled && SharedSyncFolders.Count == 0)
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
        await RefreshSyncHistoryAsync();
        await RefreshSyncConflictBackupsCoreAsync();
        SyncConflictSummary = $"Conflict scan not run · {SyncConflictBackups.Count:N0} recoverable resolution(s) stored";
    }

    private async Task LoadVpnProfileCoreAsync()
    {
        await StopTeamChatWatcherAsync();
        if (_vpnFileExchangeHost is not null)
        {
            await _vpnFileExchangeHost.DisposeAsync();
            _vpnFileExchangeHost = null;
            OnPropertyChanged(nameof(VpnFileHostRunning));
        }
        if (_teamChatHost is not null)
        {
            await _teamChatHost.DisposeAsync();
            _teamChatHost = null;
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
        if (SelectedProject is { } project)
            project.SetVpnRuntime(status.State == VpnRuntimeState.Running);
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
        foreach (SyncthingDeviceConfiguration device in await api.GetDevicesAsync())
            deviceIds.Add(device.DeviceId);
        if (SelectedProject.Definition.Features.PeerSyncEnabled)
            await api.PutFolderAsync(CreateFolderConfiguration(_currentSyncProfile, deviceIds));
        foreach (SyncthingSharedFolder folder in _currentSyncProfile.SharedFolders.Where(folder => folder.Enabled))
        {
            await api.PutFolderAsync(new SyncthingFolderConfiguration(
                folder.FolderId,
                folder.Name,
                folder.Path,
                deviceIds.ToArray(),
                folder.Mode.ToApiValue(),
                RescanIntervalSeconds: folder.RescanIntervalSeconds,
                FileWatcherEnabled: folder.FileWatcherEnabled));
        }
    }

    private async Task RefreshSyncthingWorkspaceCoreAsync()
    {
        IsSyncthingRefreshing = true;
        try
        {
            SyncthingDevices.Clear();
            SyncthingDifferences.Clear();
            SyncthingLogs.Clear();
            if (_currentSyncProfile is null ||
                _syncEngine?.Status.State is not (SyncEngineState.Running or SyncEngineState.Paused))
            {
                SyncthingFolderSummary = "Syncthing is stopped. Start it to inspect live devices and differences.";
                return;
            }

            using SyncthingApiClient api = new(_currentSyncProfile.ApiEndpoint, _currentSyncProfile.ApiKey);
            string inspectedFolderId = SelectedProject?.Definition.Features.PeerSyncEnabled == true
                ? _currentSyncProfile.FolderId
                : SelectedSharedSyncFolder?.FolderId
                  ?? _currentSyncProfile.SharedFolders.First(folder => folder.Enabled).FolderId;
            Task<IReadOnlyList<SyncthingDeviceConfiguration>> devicesTask = api.GetDevicesAsync();
            Task<IReadOnlyList<SyncthingPeerConnectionStatus>> connectionsTask = api.GetPeerConnectionsAsync();
            Task<SyncthingFolderStatus> statusTask = api.GetFolderStatusAsync(inspectedFolderId);
            Task<IReadOnlyList<SyncthingDifferenceItem>> differencesTask = api.GetDifferencesAsync(inspectedFolderId);
            Task<IReadOnlyList<SyncthingLogEntry>> logsTask = api.GetLogsAsync();
            await Task.WhenAll(devicesTask, connectionsTask, statusTask, differencesTask, logsTask);

            IReadOnlyDictionary<string, SyncthingPeerConnectionStatus> connections =
                connectionsTask.Result.ToDictionary(item => item.DeviceId, StringComparer.OrdinalIgnoreCase);
            foreach (SyncthingDeviceConfiguration device in devicesTask.Result
                         .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                connections.TryGetValue(device.DeviceId, out SyncthingPeerConnectionStatus? connection);
                bool online = connection?.Connected == true;
                SyncthingDevices.Add(new ProjectParticipantViewModel(
                    string.IsNullOrWhiteSpace(device.Name) ? ShortDeviceId(device.DeviceId) : device.Name,
                    ShortDeviceId(device.DeviceId),
                    device.Paused ? "Paused" : "Device",
                    online ? "Connected" : "Offline",
                    connection?.LastSeenAt?.ToLocalTime().ToString("g") ?? "—",
                    connection?.Address ?? string.Join(", ", device.Addresses ?? []),
                    online ? "#78D7B7" : "#A9ABB2",
                    online));
            }

            ReplaceCollection(SyncthingDifferences, differencesTask.Result);
            if (SelectedProject is not null)
            {
                string scope = SelectedProject.Definition.Features.PeerSyncEnabled
                    ? SynchronizationOverviewTabTitle
                    : SelectedSharedSyncFolder?.Name ?? "Shared folder";
                List<SyncHistoryEntry> newEntries = [];
                HashSet<string> currentFingerprints = new(StringComparer.Ordinal);
                foreach (SyncthingDifferenceItem difference in differencesTask.Result)
                {
                    string fingerprint = $"{inspectedFolderId}|{difference.Direction}|{difference.Type}|{difference.Name}|{difference.Size}|{difference.ModifiedAt:O}|{difference.Deleted}";
                    currentFingerprints.Add(fingerprint);
                    if (_observedSyncDifferences.Contains(fingerprint)) continue;
                    newEntries.Add(new SyncHistoryEntry(
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow,
                        SelectedProject.Id,
                        scope,
                        difference.Name,
                        difference.Deleted ? "Delete observed" : "Difference observed",
                        difference.Direction,
                        $"{difference.Type} · {difference.Size:N0} bytes · folder {inspectedFolderId}"));
                }
                _observedSyncDifferences.Clear();
                _observedSyncDifferences.UnionWith(currentFingerprints);
                await _syncHistoryStore.AppendManyAsync(newEntries);
            }
            ReplaceCollection(SyncthingLogs, logsTask.Result.OrderByDescending(entry => entry.Timestamp).Take(500));
            SyncthingFolderStatus folder = statusTask.Result;
            SyncthingFolderSummary = folder.IsInSync
                ? $"Up to date · {folder.InSyncFiles:N0} file(s) · state {folder.State}"
                : $"{folder.NeededFiles:N0} incoming file(s) / {FormatByteSize(folder.NeededBytes)} · " +
                  $"{folder.ReceiveOnlyChangedFiles:N0} local change(s) / {FormatByteSize(folder.ReceiveOnlyChangedBytes)} · " +
                  $"{folder.ErrorCount:N0} error(s)";
            UpdateSyncStatus(await _syncEngine.RefreshStatusAsync());
        }
        finally
        {
            IsSyncthingRefreshing = false;
        }
    }

    private static string ShortDeviceId(string deviceId) =>
        string.IsNullOrWhiteSpace(deviceId)
            ? "Unknown"
            : deviceId.Length <= 12 ? deviceId : deviceId[..12] + "…";

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
            !SelectedProject.Definition.Features.PeerSyncEnabled ||
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
        await RecordSyncHistoryAsync(
            "Git + Sync exchange",
            _currentSyncProfile.ExchangeDirectory,
            "Signed Git exchange",
            "Bidirectional",
            $"published LFS={exported.PublishedLfsObjects}; imported transactions={imported.ImportedTransactions}; imported LFS={imported.ImportedLfsObjects}");
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
        SyncthingFolderMode folderMode = profile.FolderMode;
        string grantPath = Path.Combine(GetProjectSecurityPath(definition.Id), "membership-grant.json");
        if (File.Exists(grantPath))
        {
            PeerMembershipGrant grant = PeerExchangeCodec.ImportMembershipGrant(File.ReadAllText(grantPath));
            if (grant.Certificate.Role is PeerRole.ReadOnly or PeerRole.Backup or PeerRole.EncryptedArchive)
            {
                folderMode = SyncthingFolderMode.ReceiveOnly;
            }
        }

        return new SyncthingFolderConfiguration(
            profile.FolderId,
            definition.Name,
            profile.ExchangeDirectory,
            deviceIds.ToArray(),
            folderMode.ToApiValue(),
            versioningType,
            keepVersions,
            cleanoutDays,
            RescanIntervalSeconds: profile.RescanIntervalSeconds,
            FileWatcherEnabled: profile.FileWatcherEnabled,
            VersioningPath: string.IsNullOrWhiteSpace(profile.VersioningDirectory)
                ? null
                : profile.VersioningDirectory);
    }

    private async Task StopSyncCoreAsync(bool updateUi = true)
    {
        ManagedSyncthingEngine? engine = _syncEngine;
        _syncEngine = null;
        _syncEngineProjectId = null;
        if (engine is not null)
        {
            await engine.DisposeAsync();
        }

        if (!updateUi) return;

        SyncState = SelectedProject?.Definition.Features.PeerSyncEnabled == true || SharedSyncFolders.Count > 0
            ? "Sync prêt — arrêté"
            : "Sync désactivé";
        SyncDetails = "Seule l'instance possédée par CyRevision a été arrêtée.";
        PeerMembers.Clear();
        SyncthingDevices.Clear();
        SyncthingDifferences.Clear();
        SyncthingLogs.Clear();
        SyncthingFolderSummary = "Syncthing is stopped.";
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
            : definition.OperatingMode == ProjectPresetKind.SyncWithCommits
                ? Path.Combine(_applicationPaths.DataDirectory, "sync-commit-exchange", definition.Id.ToString("N"))
            : definition.RootPath;

    private string ResolveConfiguredSyncExchangeDirectory(ProjectDefinition definition) =>
        definition.Features.GitEnabled || definition.OperatingMode == ProjectPresetKind.SyncWithCommits
            ? ResolveSyncExchangeDirectory(definition)
            : ResolveConfiguredSyncSourceFolder(definition);

    private string ResolveConfiguredSyncSourceFolder(ProjectDefinition definition)
    {
        string candidate = !string.IsNullOrWhiteSpace(SyncSourceFolderPath)
            ? SyncSourceFolderPath
            : _currentSyncProfile?.ProjectFolderPath ?? definition.RootPath;
        return Path.GetFullPath(candidate);
    }

    private static string? NormalizeOptionalDirectory(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());

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
        if (SelectedProject is { } project)
            project.SetSyncRuntime(
                status.State is SyncEngineState.Running or SyncEngineState.Paused,
                status.State == SyncEngineState.Paused);
    }

    private (long Free, long Total) GetSelectedProjectDiskSpace()
    {
        string? rootPath = SelectedProject?.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath)) return (0, 0);
        try
        {
            string? volumeRoot = Path.GetPathRoot(Path.GetFullPath(rootPath));
            if (string.IsNullOrWhiteSpace(volumeRoot)) return (0, 0);
            DriveInfo drive = new(volumeRoot);
            return drive.IsReady ? (drive.AvailableFreeSpace, drive.TotalSize) : (0, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (0, 0);
        }
    }

    private LfsManagementProfile BuildLfsManagementProfile()
    {
        if (SelectedProject is null)
            throw new InvalidOperationException("Select a project first.");
        int copies = int.TryParse(LfsRequiredCopies, out int parsedCopies) ? parsedCopies : 1;
        int grace = int.TryParse(LfsCleanupGraceDays, out int parsedGrace) ? parsedGrace : 7;
        int peerAge = int.TryParse(LfsPeerProofMaximumAgeHours, out int parsedPeerAge) ? parsedPeerAge : 24;
        int recentVersions = int.TryParse(LfsRecentVersionsPerFile, out int parsedRecentVersions)
            ? parsedRecentVersions
            : 3;
        int remoteTimeout = int.TryParse(LfsRemoteVerificationTimeoutSeconds, out int parsedRemoteTimeout)
            ? parsedRemoteTimeout
            : 45;
        LfsManagementProfile profile = new(
            SelectedProject.Id,
            string.IsNullOrWhiteSpace(LfsExternalStoragePath) ? string.Empty : Path.GetFullPath(LfsExternalStoragePath),
            string.IsNullOrWhiteSpace(LfsManagementArchivePath) ? string.Empty : Path.GetFullPath(LfsManagementArchivePath),
            string.IsNullOrWhiteSpace(LfsCleanupRemoteName) ? "origin" : LfsCleanupRemoteName.Trim(),
            copies,
            grace,
            peerAge,
            LfsVerifyRemote,
            LfsTrimRemoteBackedHistory,
            recentVersions,
            string.IsNullOrWhiteSpace(LfsRecentVersionExtensions)
                ? ".uasset;.umap"
                : LfsRecentVersionExtensions.Trim(),
            remoteTimeout);
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

    private void ApplyAssetPresentation(string relativePath, FilePresentationResult presentation)
    {
        List<string> report = [
            relativePath,
            presentation.Summary,
            $"Provider: {presentation.ProviderId}"
        ];
        if (presentation.Metadata is { Count: > 0 })
        {
            report.Add(string.Empty);
            report.AddRange(presentation.Metadata.Select(item => $"{item.Key}: {item.Value}"));
        }
        if (!string.IsNullOrWhiteSpace(presentation.TextContent))
        {
            report.Add(string.Empty);
            report.Add(presentation.TextContent);
        }

        AssetDiffReport = string.Join(Environment.NewLine, report);
        AssetDiffPreview = presentation.Kind == FilePresentationKind.Image &&
                           presentation.ImagePath is not null &&
                           File.Exists(presentation.ImagePath)
            ? new Bitmap(presentation.ImagePath)
            : null;
    }

    private static string GetAssetRelativePath(string projectRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) return Path.GetFileName(path);
        try
        {
            string relative = Path.GetRelativePath(projectRoot, path).Replace('\\', '/');
            return relative.StartsWith("../", StringComparison.Ordinal) ? Path.GetFileName(path) : relative;
        }
        catch
        {
            return Path.GetFileName(path);
        }
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
        SelectedBackupArchiveProfile = BackupArchiveProfiles.FirstOrDefault(profile =>
            profile.Id.Equals(definition.BackupArchiveProfile, StringComparison.OrdinalIgnoreCase))
            ?? BackupArchiveProfiles[0];
        RemoveArchivedHotCopies = definition.RemoveArchivedHotBackups;
        SelectedGitArchiveProfile = GitArchiveProfiles.FirstOrDefault(profile =>
            profile.Id.Equals(definition.GitArchiveProfile, StringComparison.OrdinalIgnoreCase))
            ?? GitArchiveProfiles[0];
        RemoveGitBranchAfterArchive = definition.RemoveArchivedGitBranches;
        ColdArchiveStatus = string.IsNullOrWhiteSpace(definition.ColdArchivePath)
            ? "Archive froide facultative — aucune suppression automatique."
            : RemoveArchivedHotCopies
                ? "Cold migration enabled: only verified archived copies can leave hot storage."
                : "Cold archive configured: hot copies are retained.";
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
        ProjectItemViewModel? existing = Projects.FirstOrDefault(project => project.Id == definition.Id) ??
                                         Projects.FirstOrDefault(project =>
                                             ProjectPathsEqual(project.RootPath, definition.RootPath));
        if (existing is not null)
        {
            definition = definition with
            {
                SidebarOrder = existing.Definition.SidebarOrder,
                AccentColor = existing.Definition.AccentColor
            };
        }
        else
        {
            definition = definition with
            {
                SidebarOrder = 0,
                AccentColor = ProjectItemViewModel.DefaultAccentColor
            };
        }

        await _projectCatalog.UpsertAsync(definition);
        if (existing is null)
        {
            existing = new ProjectItemViewModel(definition);
            Projects.Insert(0, existing);
        }
        else
        {
            existing.Update(definition);
        }

        await PersistProjectOrderAsync();
        SelectedProject = existing;
        NotifyProjectOrderStateChanged();
    }

    private async Task PersistProjectOrderAsync()
    {
        for (int index = 0; index < Projects.Count; index++)
        {
            ProjectItemViewModel project = Projects[index];
            if (project.Definition.SidebarOrder == index) continue;
            ProjectDefinition updated = project.Definition with { SidebarOrder = index };
            project.Update(updated);
            await _projectCatalog.UpsertAsync(updated);
        }
    }

    private void NotifyProjectOrderStateChanged()
    {
        OnPropertyChanged(nameof(CanMoveSelectedProjectUp));
        OnPropertyChanged(nameof(CanMoveSelectedProjectDown));
    }

    private void EvictProjectCaches(Guid projectId)
    {
        _projectSessionCache.Remove(projectId);
        _projectSessionCacheUsage.Remove(projectId);
        _codeWorkspaceCache.Remove(projectId);
        _codeWorkspaceCacheUsage.Remove(projectId);
        _latestProjectStatuses.Remove(projectId);
        _loadedProjectSessions.Remove(projectId);
        _workspaceLoadCoordinator.InvalidateProject(projectId);
    }

    private static void DeleteProjectGeneratedCache(string projectRoot)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        string metadataRoot = Path.GetFullPath(Path.Combine(normalizedRoot, ".cyrevision"));
        string cacheRoot = Path.GetFullPath(Path.Combine(metadataRoot, "cache"));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string expectedPrefix = metadataRoot + Path.DirectorySeparatorChar;
        if (!cacheRoot.StartsWith(expectedPrefix, comparison))
        {
            throw new IOException("CyRevision refused to clean a cache outside the selected project's metadata folder.");
        }

        if (Directory.Exists(cacheRoot))
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
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
        string successMessage,
        bool includeRemoteHistory = false)
    {
        if (SelectedProject is null)
        {
            return;
        }

        ProjectItemViewModel project = SelectedProject;
        await RunOperationAsync(progressMessage, async () =>
        {
            await operation(project.RootPath, CancellationToken.None);
            if (SelectedProject?.Id != project.Id)
            {
                return;
            }
            if (includeRemoteHistory)
            {
                IncludeRemoteHistory = true;
            }
            await RefreshCoreAsync(project);
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

    public async Task RunRepositoryCommandAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        string command = RepositoryConsoleCommand.Trim();
        if (project is null || string.IsNullOrWhiteSpace(command) || IsRepositoryConsoleRunning) return;

        _repositoryConsoleCancellation?.Dispose();
        _repositoryConsoleCancellation = new CancellationTokenSource();
        IsRepositoryConsoleRunning = true;
        SetRepositoryConsoleStatus(project.Id, $"Running in {project.Name}…");
        AppendRepositoryConsoleOutput(project.Id, $"{Environment.NewLine}[{DateTime.Now:T}] {SelectedRepositoryShell}  {project.RootPath}{Environment.NewLine}> {command}");
        RepositoryConsoleCommand = string.Empty;
        _repositoryHistoryNavigationIndex = -1;
        _applicationLogService.Information("Console", $"Repository command started in '{project.Name}' using {SelectedRepositoryShell}.", project.RootPath);
        try
        {
            RepositoryCommandResult result = await _repositoryConsoleService.ExecuteAsync(
                project.RootPath,
                command,
                SelectedRepositoryShell,
                (line, isError) => Dispatcher.UIThread.Post(() =>
                    AppendRepositoryConsoleOutput(project.Id, isError ? $"! {line}" : line)),
                _repositoryConsoleCancellation.Token);
            string resultText = result.WasCancelled
                ? $"Cancelled after {result.Duration:g}."
                : $"Exit code {result.ExitCode} · {result.Duration:g}.";
            AppendRepositoryConsoleOutput(project.Id, resultText);
            SetRepositoryConsoleStatus(project.Id, resultText);
            if (result.WasCancelled)
                _applicationLogService.Warning("Console", $"Repository command cancelled in '{project.Name}'.", project.RootPath);
            else if (result.ExitCode == 0)
                _applicationLogService.Information("Console", $"Repository command completed in '{project.Name}' with exit code 0.", project.RootPath);
            else
                _applicationLogService.Warning("Console", $"Repository command completed in '{project.Name}' with exit code {result.ExitCode}.", project.RootPath);
        }
        catch (Exception exception)
        {
            SetRepositoryConsoleStatus(project.Id, exception.Message);
            AppendRepositoryConsoleOutput(project.Id, $"ERROR: {exception.Message}");
            _applicationLogService.Error("Console", $"Repository console failed in '{project.Name}'.", exception, project.RootPath);
        }
        finally
        {
            IsRepositoryConsoleRunning = false;
            _repositoryConsoleCancellation?.Dispose();
            _repositoryConsoleCancellation = null;
            LoadRepositoryConsoleHistory();
        }
    }

    public void StopRepositoryCommand() => _repositoryConsoleCancellation?.Cancel();

    public void ClearRepositoryConsoleOutput()
    {
        if (SelectedProject is not { } project) return;
        GetRepositoryConsoleBuffer(project.Id).Clear();
        RepositoryConsoleOutput = string.Empty;
        SetRepositoryConsoleStatus(project.Id, "Console output cleared. Persistent command history was kept.");
    }

    public void ClearRepositoryConsoleHistory()
    {
        if (SelectedProject is null) return;
        _repositoryConsoleService.ClearHistory(SelectedProject.RootPath);
        LoadRepositoryConsoleHistory();
        RepositoryConsoleStatus = "Command history cleared for this project.";
    }

    public void UseSelectedRepositoryCommand()
    {
        if (SelectedRepositoryCommand is null) return;
        RepositoryConsoleCommand = SelectedRepositoryCommand.Command;
        _repositoryHistoryNavigationIndex = -1;
    }

    public string NavigateRepositoryCommandHistory(int direction)
    {
        if (FilteredRepositoryCommandHistory.Count == 0) return RepositoryConsoleCommand;
        _repositoryHistoryNavigationIndex = direction < 0
            ? Math.Min(_repositoryHistoryNavigationIndex + 1, FilteredRepositoryCommandHistory.Count - 1)
            : Math.Max(_repositoryHistoryNavigationIndex - 1, -1);
        RepositoryConsoleCommand = _repositoryHistoryNavigationIndex < 0
            ? string.Empty
            : FilteredRepositoryCommandHistory[_repositoryHistoryNavigationIndex].Command;
        return RepositoryConsoleCommand;
    }

    public void RefreshApplicationLogs()
    {
        ReplaceCollection(ApplicationLogEntries, _applicationLogService.LoadRecent());
        ApplyApplicationLogFilter();
    }

    public void ClearApplicationLogView()
    {
        ApplicationLogEntries.Clear();
        FilteredApplicationLogEntries.Clear();
        SelectedApplicationLogEntry = null;
    }

    private void LoadRepositoryConsoleHistory()
    {
        RepositoryCommandHistory.Clear();
        if (SelectedProject is not null)
        {
            foreach (RepositoryCommandHistoryEntry entry in _repositoryConsoleService.GetHistory(SelectedProject.RootPath))
                RepositoryCommandHistory.Add(entry);
        }
        _repositoryHistoryNavigationIndex = -1;
        ApplyRepositoryConsoleHistoryFilter();
    }

    private void ApplyRepositoryConsoleHistoryFilter()
    {
        string search = RepositoryConsoleHistorySearch.Trim();
        IEnumerable<RepositoryCommandHistoryEntry> entries = RepositoryCommandHistory;
        if (search.Length > 0)
        {
            entries = entries.Where(entry =>
                entry.Command.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Shell.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        ReplaceCollection(FilteredRepositoryCommandHistory, entries);
    }

    private void RestoreRepositoryConsoleForProject(ProjectItemViewModel? project)
    {
        RepositoryConsoleOutput = project is null
            ? "Select a project to open its repository console."
            : GetRepositoryConsoleBuffer(project.Id).ToString();
        RepositoryConsoleStatus = project is not null && _repositoryConsoleStatuses.TryGetValue(project.Id, out string? status)
            ? status
            : "Console ready.";
    }

    private StringBuilder GetRepositoryConsoleBuffer(Guid projectId)
    {
        if (_repositoryConsoleOutputBuffers.TryGetValue(projectId, out StringBuilder? buffer)) return buffer;
        buffer = new StringBuilder();
        _repositoryConsoleOutputBuffers[projectId] = buffer;
        return buffer;
    }

    private void SetRepositoryConsoleStatus(Guid projectId, string status)
    {
        _repositoryConsoleStatuses[projectId] = status;
        if (SelectedProject?.Id == projectId) RepositoryConsoleStatus = status;
    }

    private void AppendRepositoryConsoleOutput(Guid projectId, string text)
    {
        const int maximumCharacters = 2_000_000;
        StringBuilder buffer = GetRepositoryConsoleBuffer(projectId);
        if (buffer.Length > 0) buffer.AppendLine();
        buffer.Append(text);
        if (buffer.Length > maximumCharacters)
            buffer.Remove(0, buffer.Length - maximumCharacters);
        if (SelectedProject?.Id == projectId) RepositoryConsoleOutput = buffer.ToString();
    }

    private void OnApplicationLogEntryWritten(object? sender, ApplicationLogEntry entry)
    {
        void AddEntry()
        {
            ApplicationLogEntries.Insert(0, entry);
            while (ApplicationLogEntries.Count > 1_000) ApplicationLogEntries.RemoveAt(ApplicationLogEntries.Count - 1);
            ApplyApplicationLogFilter();
        }
        if (Dispatcher.UIThread.CheckAccess()) AddEntry();
        else Dispatcher.UIThread.Post(AddEntry);
    }

    private void ApplyApplicationLogFilter()
    {
        string search = ApplicationLogSearch.Trim();
        IEnumerable<ApplicationLogEntry> entries = ApplicationLogEntries;
        string? projectPath = SelectedProject?.RootPath;
        entries = projectPath is null
            ? entries.Where(entry => string.IsNullOrWhiteSpace(entry.ProjectPath))
            : entries.Where(entry => entry.ProjectPath is not null && ProjectPathsEqual(entry.ProjectPath, projectPath));
        if (!string.Equals(ApplicationLogLevelFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse(ApplicationLogLevelFilter, out ApplicationLogLevel level))
        {
            entries = entries.Where(entry => entry.Level == level);
        }
        if (search.Length > 0)
        {
            entries = entries.Where(entry =>
                entry.Area.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Message.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        ReplaceCollection(FilteredApplicationLogEntries, entries);
        SelectedApplicationLogEntry = FilteredApplicationLogEntries.FirstOrDefault();
        OnPropertyChanged(nameof(ApplicationLogProjectSummary));
    }

    private async Task RunOperationAsync(string progressMessage, Func<Task> operation, string successMessage)
    {
        if (!await _operationGate.WaitAsync(0).ConfigureAwait(true))
        {
            StatusMessage = "Another operation is already running. The duplicate action was not queued.";
            return;
        }

        string? projectPath = SelectedProject?.RootPath;
        string projectName = SelectedProject?.Name ?? "Application";
        OperationTaskViewModel trackedTask = BeginTrackedTask(progressMessage, projectName);
        bool enteredGate = true;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            trackedTask.State = "Running";
            trackedTask.Detail = projectPath ?? "Application";
            IsBusy = true;
            StatusMessage = progressMessage;
            _applicationLogService.Information(
                "operation",
                $"start id={trackedTask.Id:N} task=\"{progressMessage}\"",
                projectPath);
            await operation();
            StatusMessage = successMessage;
            CompleteTrackedTask(trackedTask, "Completed", successMessage);
            _applicationLogService.Information(
                "operation",
                $"complete id={trackedTask.Id:N} duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms result=\"{successMessage}\"",
                projectPath);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Cancelled: {progressMessage}";
            CompleteTrackedTask(trackedTask, "Cancelled", "Operation cancelled");
            _applicationLogService.Warning(
                "operation",
                $"cancel id={trackedTask.Id:N} duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms task=\"{progressMessage}\"",
                projectPath);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            CompleteTrackedTask(trackedTask, "Failed", exception.Message);
            _applicationLogService.Error(
                "operation",
                $"fail id={trackedTask.Id:N} duration={stopwatch.Elapsed.TotalMilliseconds:N0}ms task=\"{progressMessage}\"",
                exception,
                projectPath);
        }
        finally
        {
            RecordPerformanceMetric("Operation", progressMessage, stopwatch.Elapsed, trackedTask.State);
            if (ActiveOperations.Contains(trackedTask))
                CompleteTrackedTask(trackedTask, "Finished", trackedTask.Detail);
            if (enteredGate)
            {
                IsBusy = false;
                _operationGate.Release();
            }
        }
    }

    private void ClearRepositoryView()
    {
        RepositoryPath = "Aucun projet ouvert";
        CurrentProjectName = "No project";
        CurrentBranch = "—";
        ChangeSummary = "0 modification";
        ChangeSummaryColor = "#A9ABB2";
        Changes.Clear();
        NotifyGitConflictStateChanged();
        PreparedChanges.Clear();
        LocalOnlyChanges.Clear();
        ChangeTree.Clear();
        _localOnlyChangePaths.Clear();
        UpdateChangePreparationSummary();
        History.Clear();
        Branches.Clear();
        FilteredBranches.Clear();
        SetSelectedBranches([]);
        SelectedBranchHistory.Clear();
        LfsPatterns.Clear();
        ClearGitIgnoreView();
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
        MultiRestoreDiffPreviewImage = null;
        MultiRestoreDiffPresentationSummary = string.Empty;
        BranchComparisonSummary = "Choose a source and target branch to compare their commits.";
        CherryPickPlanSummary = "Compare branches, then select the source-only commits to apply.";
        CherryPickDiff = "Select a commit to inspect its patch.";
        OnPropertyChanged(nameof(CanApplyMultiRestore));
        OnPropertyChanged(nameof(CanApplyCherryPick));
        LfsFiles.Clear();
        LfsFileTree.Clear();
        IsLfsInventoryLoaded = false;
        LfsVersions.Clear();
        LfsLocks.Clear();
        MyLfsLocks.Clear();
        FilteredLfsLocks.Clear();
        FilteredMyLfsLocks.Clear();
        ApplyLfsLockFilter();
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
        ExplorerDiffPreviewImage = null;
        ExplorerDiffPresentationSummary = string.Empty;
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
        DiffPreviewImage = null;
        DiffPresentationSummary = string.Empty;
        ClearGitGraphView();
        ClearCodeWorkspace();
        ClearAiMcpProfile();
        ClearPullRequestData();
    }

    private void ClearCodeWorkspace()
    {
        _codeFileFilterCancellation?.Cancel();
        _codeFileFilterCancellation?.Dispose();
        _codeFileFilterCancellation = null;
        _assetExplorerCancellation?.Cancel();
        _assetExplorerCancellation?.Dispose();
        _assetExplorerCancellation = null;
        _assetExplorerPreviewCancellation?.Cancel();
        _assetExplorerPreviewCancellation?.Dispose();
        _assetExplorerPreviewCancellation = null;
        IsCodeFileSearchRunning = false;
        CodeTree.Clear();
        CodeFileList.Clear();
        CodeFileSearchResults = [];
        AssetExplorerFiles = [];
        _allIgnoreFolderSuggestions = [];
        _allIgnoreFileTypeSuggestions = [];
        IgnoreFolderSuggestions = [];
        IgnoreFileTypeSuggestions = [];
        IgnoreFolderTree = [];
        _ignoreSuggestionProjectId = null;
        CodeSearchResults.Clear();
        CodeHistory.Clear();
        CodeSymbols.Clear();
        SelectedCodeNode = null;
        SelectedCodeFileSearchResult = null;
        SelectedAssetExplorerFile = null;
        SelectedCodeSearchResult = null;
        SelectedCodeSymbol = null;
        CodePreviewText = string.Empty;
        CodePreviewPath = string.Empty;
        CodePreviewImage = null;
        CodePreviewIsImage = false;
        SetCodePreviewSupportsAiSummary(false);
        CodeWorkspaceSummary = "Select a project to explore its code.";
        CodeSearchSummary = "Ctrl+Shift+F searches the entire project.";
        CodePreviewSummary = "Select a file to preview it.";
        CodeSelectionSummary = "Select lines in the preview, then request their Git history.";
    }

    private void ClearGitIgnoreView()
    {
        _suspendGitIgnoreEditing = true;
        GitIgnoreContent = string.Empty;
        _suspendGitIgnoreEditing = false;
        GitIgnoreRules.Clear();
        IgnoredFiles.Clear();
        GitIgnoreFilePath = "No Git ignore file loaded.";
        GitIgnoreSummary = "Open this tool to edit Git ignore rules.";
        GitIgnoreTestPath = string.Empty;
        GitIgnoreTestResult = "Enter a project-relative path to test it against Git.";
        IsGitIgnoreDirty = false;
        IsGitIgnoreLoading = false;
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
            _pluginManager.Entries.Select(entry => new PluginItemViewModel(
                entry,
                _pluginManager.IsActiveForCurrentProject(entry.Id))));
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
            UnrealAdvancedAssetInspectionEnabled = false;
            UnrealAssetInspectionSummary = "Enable the Unreal Engine Integration plugin to configure headless asset previews.";
            _unrealBuildDiscovery = null;
            ReplaceCollection(UnrealBuildEngines, []);
            ReplaceCollection(UnrealBuildTargets, []);
            UnrealBuildStatus = "Enable the Unreal integration plugin to use the build lab.";
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
        OnPropertyChanged(nameof(CanConfigureUnrealAssetInspection));
        OnPropertyChanged(nameof(CanRunUnrealBuild));
        NotifyUnrealCompatibilityChanged();

        IAiIntegrationPlugin? ai = _pluginManager.GetPlugin<IAiIntegrationPlugin>();
        IsAiIntegrationEnabled = ai is not null;
        ReplaceCollection(AiProviders, ai?.Providers ?? []);
        SelectedAiProvider = AiProviders.FirstOrDefault(provider => provider.Id == SelectedAiProvider?.Id)
                             ?? AiProviders.FirstOrDefault();
        if (ai is null)
        {
            AiStatus = "AI integration disabled.";
            AiResponse = "Enable the optional AI Workspace plugin from the Plugins tab.";
            IsCodexDetected = false;
            IsCodexRunning = false;
            IsCodexChatConnected = false;
            CodexDetectedPath = string.Empty;
            CodexDetectedVersion = string.Empty;
            CodexChatThreadId = string.Empty;
            CodexConnectionStatus = "AI integration disabled.";
            ClearAiMcpProfile();
        }
        else
        {
            AiStatus = "AI Workspace ready. Read access only by default.";
            AiResponse = "CyRevision checks for the local Codex installation automatically and starts a project-scoped chat when it is available.";
            if (!IsCodexDetected) CodexConnectionStatus = "Checking for the local Codex installation automatically.";
            _ = LoadAiMcpProfileCoreAsync();
            if (SelectedProject is not null)
                _ = DetectCodexAsync(autoConnect: true);
        }

        RefreshGameEnginePluginCatalog();
        RefreshLorePluginCatalog();
        RefreshPerforcePluginCatalog();
        RefreshProjectModeCatalog();
        OnPropertyChanged(nameof(HasWorkItemPlugins));
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
            OnPropertyChanged(nameof(CanConfigureUnrealAssetInspection));
            OnPropertyChanged(nameof(IsUnrealProjectDetected));
            NotifyUnrealCompatibilityChanged();
            return;
        }

        _unrealProjectInspection = plugin.InspectProject(UnrealProjectPath);
        UnrealPluginSummary = _unrealProjectInspection.Summary;
        OnPropertyChanged(nameof(UnrealEditorPluginVersion));
        OnPropertyChanged(nameof(UnrealInstalledPluginVersion));
        OnPropertyChanged(nameof(CanInstallUnrealEditorPlugin));
        OnPropertyChanged(nameof(CanConfigureUnrealAssetInspection));
        OnPropertyChanged(nameof(IsUnrealProjectDetected));
        NotifyUnrealCompatibilityChanged();
        _ = LoadUnrealAssetInspectionOptionsAsync(plugin, UnrealProjectPath);
    }

    private async Task LoadUnrealAssetInspectionOptionsAsync(IUnrealIntegrationPlugin plugin, string projectPath)
    {
        try
        {
            UnrealAssetInspectionOptions options = await plugin.LoadAssetInspectionOptionsAsync(projectPath);
            if (!string.Equals(projectPath, UnrealProjectPath, StringComparison.OrdinalIgnoreCase)) return;
            UnrealAdvancedAssetInspectionEnabled = options.Enabled;
            UnrealRenderMeshThumbnails = options.RenderMeshThumbnails;
            UnrealAssetPreviewResolution = options.PreviewResolution.ToString();
            UnrealAssetCacheBudgetGigabytes = (options.CacheBudgetBytes / (1024d * 1024 * 1024)).ToString("0.##");
            await RefreshUnrealAssetInspectionCacheCoreAsync(plugin);
        }
        catch (Exception exception)
        {
            UnrealAssetInspectionSummary = $"Asset preview settings could not be loaded: {exception.Message}";
        }
    }

    private async Task RefreshUnrealAssetInspectionCacheCoreAsync(IUnrealIntegrationPlugin plugin)
    {
        UnrealAssetInspectionCacheStatus status = await plugin.GetAssetInspectionCacheStatusAsync(UnrealProjectPath);
        UnrealAssetInspectionSummary = (UnrealAdvancedAssetInspectionEnabled
            ? "On-demand headless inspection enabled · "
            : "Advanced inspection disabled · ") + status.Summary;
    }

    private void NotifyUnrealCompatibilityChanged()
    {
        OnPropertyChanged(nameof(UnrealEngineVersion));
        OnPropertyChanged(nameof(UnrealProjectKindSummary));
        OnPropertyChanged(nameof(UnrealInstallModeSummary));
        OnPropertyChanged(nameof(UnrealCompatibilityStatus));
        OnPropertyChanged(nameof(UnrealSupportedVersions));
        OnPropertyChanged(nameof(UnrealPrecompiledVersions));
        OnPropertyChanged(nameof(IsUnrealPluginCompatible));
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

    public void RefreshPerformanceDiagnostics()
    {
        using Process process = Process.GetCurrentProcess();
        long managedMegabytes = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
        long workingSetMegabytes = process.WorkingSet64 / (1024 * 1024);
        PerformanceSummary =
            $"Working set {workingSetMegabytes:N0} MB · managed {managedMegabytes:N0} MB · " +
            $"{ActiveOperations.Count:N0} active task(s) · {PerformanceMetrics.Count:N0} samples · " +
            $"uptime {_applicationUptime.Elapsed:hh\\:mm\\:ss}";
    }

    public void ClearPerformanceDiagnostics()
    {
        PerformanceMetrics.Clear();
        RefreshPerformanceDiagnostics();
    }

    private void RecordPerformanceMetric(string area, string operation, TimeSpan elapsed, string detail)
    {
        void AddMetric()
        {
            PerformanceMetrics.Insert(0, new PerformanceMetricViewModel(
                DateTimeOffset.Now,
                area,
                operation,
                $"{elapsed.TotalMilliseconds:N0} ms",
                detail));
            while (PerformanceMetrics.Count > 250) PerformanceMetrics.RemoveAt(PerformanceMetrics.Count - 1);
            RefreshPerformanceDiagnostics();
        }

        if (Dispatcher.UIThread.CheckAccess()) AddMetric();
        else Dispatcher.UIThread.Post(AddMetric, DispatcherPriority.Background);
    }

    private void DebounceUiAction(string key, Action action, int delayMilliseconds = 180)
    {
        CancelDebouncedUiAction(key);
        CancellationTokenSource cancellation = new();
        _uiDebounceOperations[key] = cancellation;
        _ = RunDebouncedUiActionAsync(key, action, delayMilliseconds, cancellation);
    }

    private async Task RunDebouncedUiActionAsync(
        string key,
        Action action,
        int delayMilliseconds,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(delayMilliseconds, cancellation.Token).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (!_uiDebounceOperations.TryGetValue(key, out CancellationTokenSource? current) ||
                    !ReferenceEquals(current, cancellation)) return;
                _uiDebounceOperations.Remove(key);
                cancellation.Dispose();
                action();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke superseded this filter operation.
        }
    }

    private void RunDebouncedUiActionNow(string key, Action action)
    {
        CancelDebouncedUiAction(key);
        action();
    }

    private void CancelDebouncedUiAction(string key)
    {
        if (!_uiDebounceOperations.Remove(key, out CancellationTokenSource? cancellation)) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void CancelDebouncedUiActions()
    {
        foreach (CancellationTokenSource cancellation in _uiDebounceOperations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        _uiDebounceOperations.Clear();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        if (destination is BatchObservableCollection<T> batch)
        {
            batch.ReplaceAll(source);
            return;
        }

        destination.Clear();
        foreach (T item in source)
        {
            destination.Add(item);
        }
    }

    private void ReplaceBitmap(ref Bitmap? storage, Bitmap? value, string propertyName)
    {
        if (ReferenceEquals(storage, value)) return;
        Bitmap? previous = storage;
        storage = value;
        OnPropertyChanged(propertyName);
        previous?.Dispose();
    }

    private sealed record CachedCodeWorkspace(
        CodeWorkspaceSnapshot Snapshot,
        DateTimeOffset UpdatedAt,
        bool IncludeHidden,
        CodeFileIndex? FileIndex = null);

    private sealed record CachedProjectSession(
        IReadOnlyDictionary<string, object?> Fields,
        IReadOnlyDictionary<string, object?[]> Collections,
        GitRepositoryStatus? Status,
        IReadOnlyList<string> LocalOnlyPaths,
        DateTimeOffset CapturedAt);

    private sealed record CachedBranchInspection(
        IReadOnlyList<GitRevision> History,
        GitBranchDetails Details);

    private sealed record CachedCodeFileInspection(
        CodeFilePreview Preview,
        IReadOnlyList<CodeHistoryEntry> History);

    public async ValueTask DisposeAsync()
    {
        CancelDebouncedUiActions();
        _repositoryConsoleCancellation?.Cancel();
        _repositoryConsoleCancellation?.Dispose();
        _applicationLogService.EntryWritten -= OnApplicationLogEntryWritten;
        _remoteBuildCancellation?.Cancel();
        _projectLoadCancellation?.Cancel();
        _projectLoadCancellation?.Dispose();
        _codeWorkspaceCancellation?.Cancel();
        _codeWorkspaceCancellation?.Dispose();
        _codeFileFilterCancellation?.Cancel();
        _codeFileFilterCancellation?.Dispose();
        _codePreviewCancellation?.Cancel();
        _codePreviewCancellation?.Dispose();
        _branchHistoryCancellation?.Cancel();
        _branchHistoryCancellation?.Dispose();
        _workingTreeDiffCancellation?.Cancel();
        _workingTreeDiffCancellation?.Dispose();
        _automaticGitRefreshCancellation?.Cancel();
        _automaticGitRefreshCancellation?.Dispose();
        _explorerRevisionCancellation?.Cancel();
        _explorerRevisionCancellation?.Dispose();
        _explorerFileCancellation?.Cancel();
        _explorerFileCancellation?.Dispose();
        _codeSearchCancellation?.Cancel();
        _codeSearchCancellation?.Dispose();
        _aiAgentCancellation?.Cancel();
        _aiAgentCancellation?.Dispose();
        _codexConnectionCancellation?.Cancel();
        _codexConnectionCancellation?.Dispose();
        await StopSyncCoreAsync(updateUi: false);
        if (_vpnFileExchangeHost is not null)
        {
            await _vpnFileExchangeHost.DisposeAsync();
            _vpnFileExchangeHost = null;
        }
        await StopTeamChatWatcherAsync();
        if (_teamChatHost is not null)
        {
            await _teamChatHost.DisposeAsync();
            _teamChatHost = null;
        }
        DetachUnrealPluginEvents();
        DetachAllGameEnginePluginEvents();
        await _pluginManager.DisposeAsync();
        _discordAgent.StatusChanged -= OnDiscordAgentStatusChanged;
        await _discordAgent.DisposeAsync();
        await _pullRequestService.DisposeAsync();
        if (_ciWorkflowService is IDisposable disposableCiService) disposableCiService.Dispose();
        CodePreviewImage = null;
        DiffPreviewImage = null;
        ExplorerDiffPreviewImage = null;
        MultiRestoreDiffPreviewImage = null;
        AssetDiffPreview = null;
        LfsPreview = null;
        _updateService.Dispose();
    }
}
