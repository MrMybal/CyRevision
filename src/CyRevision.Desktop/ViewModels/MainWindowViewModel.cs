using System.Collections.ObjectModel;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IProjectCatalog _projectCatalog;
    private readonly IGitRepositoryService _gitService;
    private ProjectItemViewModel? _selectedProject;
    private GitChangeViewModel? _selectedChange;
    private GitBranch? _selectedBranch;
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

    public MainWindowViewModel(IProjectCatalog projectCatalog, IGitRepositoryService gitService)
    {
        _projectCatalog = projectCatalog;
        _gitService = gitService;
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<GitChangeViewModel> Changes { get; } = [];

    public ObservableCollection<GitRevision> History { get; } = [];

    public ObservableCollection<GitBranch> Branches { get; } = [];

    public ObservableCollection<LfsTrackedPattern> LfsPatterns { get; } = [];

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
                Projects.Add(new ProjectItemViewModel(definition));
            }

            if (Projects.Count > 0)
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

    public Task RefreshAsync() => RunOperationAsync("Actualisation…", RefreshCoreAsync, "Dépôt actualisé");

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
            await _gitService.CheckoutBranchAsync(SelectedProject.RootPath, branchName);
            await RefreshCoreAsync();
        }, $"Branche {branchName} active");
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

    private async Task LoadSelectedProjectAsync()
    {
        if (SelectedProject is null)
        {
            ClearRepositoryView();
            return;
        }

        ProjectDefinition updated = SelectedProject.Definition with { LastOpenedAt = DateTimeOffset.UtcNow };
        SelectedProject.Update(updated);
        try
        {
            await _projectCatalog.UpsertAsync(updated);
            StatusMessage = "Chargement du dépôt…";
            await RefreshCoreAsync();
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

    private async Task SaveAndSelectProjectAsync(ProjectDefinition definition)
    {
        await _projectCatalog.UpsertAsync(definition);
        ProjectItemViewModel? existing = Projects.FirstOrDefault(project => project.Id == definition.Id) ??
                                         Projects.FirstOrDefault(project =>
                                             string.Equals(project.RootPath, definition.RootPath, StringComparison.OrdinalIgnoreCase));
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
            string.Equals(project.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
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
}
