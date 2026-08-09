using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CyRevision.Backup;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Diff;
using CyRevision.Git;
using CyRevision.Security;
using CyRevision.Sync;

namespace CyRevision.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IProjectCatalog _projectCatalog;
    private readonly IGitRepositoryService _gitService;
    private readonly ApplicationPaths _applicationPaths;
    private readonly ISyncthingProfileStore _syncthingProfileStore;
    private readonly IGitPeerExchangeService _gitPeerExchangeService;
    private readonly IAssetDiffService _assetDiffService;
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

    public MainWindowViewModel(
        IProjectCatalog projectCatalog,
        IGitRepositoryService gitService,
        ApplicationPaths applicationPaths,
        ISyncthingProfileStore syncthingProfileStore,
        IGitPeerExchangeService gitPeerExchangeService,
        IAssetDiffService assetDiffService,
        string? initialProjectPath = null)
    {
        _projectCatalog = projectCatalog;
        _gitService = gitService;
        _applicationPaths = applicationPaths;
        _syncthingProfileStore = syncthingProfileStore;
        _gitPeerExchangeService = gitPeerExchangeService;
        _assetDiffService = assetDiffService;
        _initialProjectPath = initialProjectPath;
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<GitChangeViewModel> Changes { get; } = [];

    public ObservableCollection<GitRevision> History { get; } = [];

    public ObservableCollection<GitBranch> Branches { get; } = [];

    public ObservableCollection<LfsTrackedPattern> LfsPatterns { get; } = [];

    public ObservableCollection<BackupSnapshotViewModel> Backups { get; } = [];

    public ObservableCollection<PeerMemberViewModel> PeerMembers { get; } = [];

    public ObservableCollection<AdvisoryReservationViewModel> AdvisoryReservations { get; } = [];

    public IReadOnlyList<RetentionMode> RetentionModes { get; } = Enum.GetValues<RetentionMode>();

    public IReadOnlyList<ProjectPreset> Presets { get; } = ProjectPresets.All;

    public IReadOnlyList<PeerRole> PeerRoles { get; } = Enum.GetValues<PeerRole>();

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
                BackupStorePath = storePath
            };
            updated.Validate();
            await _projectCatalog.UpsertAsync(updated);
            SelectedProject.Update(updated);
            BackupStorePath = storePath;
            await GetBackupService(updated).ApplyRetentionAsync(updated.Id, retention);
            await LoadBackupsCoreAsync();
        }, "Stratégie de sauvegarde enregistrée");
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
        try
        {
            await _projectCatalog.UpsertAsync(updated);
            StatusMessage = "Chargement du dépôt…";
            await RefreshCoreAsync();
            await LoadBackupsCoreAsync();
            await LoadSyncProfileCoreAsync();
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
        await Task.WhenAll(statusTask, historyTask, branchesTask, lfsTask);

        GitRepositoryStatus status = await statusTask;
        CurrentBranch = status.IsDetachedHead ? $"HEAD {status.CurrentBranch}" : status.CurrentBranch;
        RepositoryPath = status.RootPath;
        ChangeSummary = $"{status.Changes.Count} modification(s) · ↑{status.AheadBy} ↓{status.BehindBy}";

        ReplaceCollection(Changes, status.Changes.Select(change => new GitChangeViewModel(change)));
        ReplaceCollection(History, await historyTask);
        ReplaceCollection(Branches, await branchesTask);
        ReplaceCollection(LfsPatterns, await lfsTask);
        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
        SelectedChange = Changes.FirstOrDefault();
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
        Guid? exported = await _gitPeerExchangeService.ExportAsync(
            SelectedProject.Id,
            SelectedProject.RootPath,
            _currentSyncProfile.ExchangeDirectory,
            localIdentity);
        GitPeerExchangeResult imported = await _gitPeerExchangeService.ImportAsync(
            SelectedProject.Id,
            SelectedProject.RootPath,
            _currentSyncProfile.ExchangeDirectory,
            Path.Combine(_applicationPaths.DataDirectory, "git-exchange-state", SelectedProject.Id.ToString("N")),
            authorizedDevices,
            localIdentity.Identity.DeviceId);
        SyncDetails = $"Transaction {(exported is null ? "non créée" : exported.Value.ToString("N")[..8])} · " +
                      $"{imported.ImportedTransactions} transaction(s) reçue(s) · " +
                      $"{imported.ImportedLfsObjects} objet(s) LFS importé(s)";
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
        Backups.Clear();
        SelectedBackup = null;
        BackupStorePath = string.Empty;
        SelectedPreset = null;
        _currentSyncProfile = null;
        SyncthingExecutablePath = string.Empty;
        SyncState = "Sync désactivé";
        SyncDetails = "Aucun projet sélectionné.";
        PeerMembers.Clear();
        SelectedPeerMember = null;
        AdvisoryReservations.Clear();
        ReservationSummary = "Aucun projet sélectionné.";
        DiffText = "Sélectionnez un projet Git.";
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
    }
}
