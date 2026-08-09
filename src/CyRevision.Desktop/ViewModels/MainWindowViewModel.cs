using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CyRevision.Backup;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Desktop.Localization;
using CyRevision.Diff;
using CyRevision.Git;
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
    private readonly LocalizationService _localization;
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
    private LfsTrackedFile? _selectedLfsFile;
    private LfsFileVersion? _selectedLfsVersion;
    private string _lfsTimelineSummary = "Sélectionnez un fichier LFS pour afficher ses versions.";
    private Bitmap? _lfsPreview;
    private LfsHistoryTransferMode _selectedLfsHistoryMode = LfsHistoryTransferMode.OnDemand;
    private string _smartSyncRecentVersionCount = "3";
    private bool _smartSyncReplicateBackups;
    private string _smartSyncPlanSummary = "Plan calculé localement — aucun transfert lancé.";
    private string _peerLfsTransferSummary = "Aucun inventaire de pair vérifié pour le moment.";
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
    private LanguageOption? _selectedLanguage;

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
        LocalizationService localization,
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
        _localization = localization;
        _selectedLanguage = localization.Languages.FirstOrDefault(language =>
            string.Equals(language.Code, localization.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase));
        _initialProjectPath = initialProjectPath;
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<GitChangeViewModel> Changes { get; } = [];

    public ObservableCollection<GitRevision> History { get; } = [];

    public ObservableCollection<GitBranch> Branches { get; } = [];

    public ObservableCollection<LfsTrackedPattern> LfsPatterns { get; } = [];

    public ObservableCollection<GitCommitFileChange> ExplorerFiles { get; } = [];

    public ObservableCollection<GitFileRevision> ExplorerFileHistory { get; } = [];

    public ObservableCollection<LfsTrackedFile> LfsFiles { get; } = [];

    public ObservableCollection<LfsFileVersion> LfsVersions { get; } = [];

    public ObservableCollection<SmartSyncPlanItem> SmartSyncPlanItems { get; } = [];

    public ObservableCollection<BackupSnapshotViewModel> Backups { get; } = [];

    public ObservableCollection<PeerMemberViewModel> PeerMembers { get; } = [];

    public ObservableCollection<AdvisoryReservationViewModel> AdvisoryReservations { get; } = [];

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

    public ObservableCollection<VpnPeerViewModel> VpnPeers { get; } = [];

    public IReadOnlyList<RetentionMode> RetentionModes { get; } = Enum.GetValues<RetentionMode>();

    public IReadOnlyList<ProjectPreset> Presets { get; } = ProjectPresets.All;

    public IReadOnlyList<PeerRole> PeerRoles { get; } = Enum.GetValues<PeerRole>();

    public IReadOnlyList<VpnNodeCapabilities> VpnCapabilities { get; } =
        Enum.GetValues<VpnNodeCapabilities>().Where(value => value != VpnNodeCapabilities.None).ToArray();

    public IReadOnlyList<LanguageOption> Languages => _localization.Languages;

    public IReadOnlyList<LfsHistoryTransferMode> LfsHistoryModes { get; } = Enum.GetValues<LfsHistoryTransferMode>();

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && value is not null)
            {
                _localization.SetLanguage(value.Code);
            }
        }
    }

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
        set => SetProperty(ref _vpnNetworkCidr, value);
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
        set => SetProperty(ref _vpnListenPort, value);
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

    public async Task InitializeAsync()
    {
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
            Task<UnrealDependencyGraph> unrealTask = _assetDiffService.ScanUnrealDependenciesAsync(
                SelectedProject.RootPath,
                Math.Min(1000, Math.Max(fileLimit * 5, 100)));
            await Task.WhenAll(commitsTask, filesTask, insightsTask, unrealTask);
            IReadOnlyList<GitGraphCommit> commits = await commitsTask;
            GitFileActivityGraph files = await filesTask;
            GitRepositoryInsights insights = await insightsTask;
            UnrealDependencyGraph unreal = await unrealTask;
            GitGraphCommits = commits.ToArray();
            GitFileActivities = files.Files.ToArray();
            GitFileRelations = files.Relations.ToArray();
            GitContributors = insights.Contributors.ToArray();
            GitDailyActivity = insights.DailyActivity.ToArray();
            GitHotFiles = insights.HotFiles.ToArray();
            GitInsightsSummary = $"{insights.CommitCount} commits · {insights.ContributorCount} contributeur(s) · " +
                                 $"{insights.FileCount} fichier(s) · +{insights.AddedLines} / -{insights.DeletedLines} · " +
                                 $"{insights.BinaryChanges} changement(s) binaire(s)";
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
            WireGuardInstallation installation = _wireGuardKeyService.DetectInstallation();
            if (!installation.CanGenerateKeys)
            {
                throw new FileNotFoundException(
                    "WireGuard est introuvable. Installez l'application officielle puis relancez la détection.");
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

    private async Task LoadSelectedProjectAsync()
    {
        if (SelectedProject is null)
        {
            await StopSyncCoreAsync();
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
            await LoadBackupsCoreAsync();
            await LoadSyncProfileCoreAsync();
            await LoadVpnProfileCoreAsync();
            await LoadAdvisoryReservationsCoreAsync();
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
            WireGuardInstallation detected = _wireGuardKeyService.DetectInstallation();
            WireGuardExecutablePath = detected.WireGuardExecutablePath ?? detected.WgExecutablePath ?? string.Empty;
            VpnState = detected.CanGenerateKeys ? "WireGuard détecté — à configurer" : "WireGuard non détecté";
            VpnDetails = "Le VPN peut être utilisé seul, sans lancer Git ni Syncthing.";
            return;
        }

        ApplyVpnProfile(_currentVpnProfile);
        await RefreshVpnStatusCoreAsync();
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
        WireGuardExecutablePath = profile.WireGuardExecutablePath ?? profile.WgExecutablePath ?? string.Empty;
        VpnNetworkCidr = profile.NetworkCidr;
        VpnLocalAddress = profile.LocalAddress;
        VpnPublicEndpoint = profile.PublicEndpoint ?? string.Empty;
        VpnListenPort = profile.ListenPort.ToString();
        SelectedVpnCapability = profile.LocalCapabilities;
        ReplaceCollection(VpnPeers, profile.Peers.Select(peer => new VpnPeerViewModel(peer)));
        SelectedVpnPeer = VpnPeers.FirstOrDefault();
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
        if (SelectedProject is null || _syncEngine is null || string.IsNullOrWhiteSpace(_syncEngine.DeviceId))
        {
            PeerMembers.Clear();
            SelectedPeerMember = null;
            return;
        }

        using FileDeviceIdentityStore identity = await OpenLocalDeviceIdentityAsync(
            SelectedProject.Definition,
            _syncEngine.DeviceId);
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
        string objectPath = Path.Combine(archiveRoot, "objects", oidSha256[..2], oidSha256);
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
        LfsFiles.Clear();
        LfsVersions.Clear();
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
        DiffText = "Sélectionnez un projet Git.";
        ClearGitGraphView();
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
        await StopSyncCoreAsync();
        AssetDiffPreview = null;
        LfsPreview = null;
    }
}
