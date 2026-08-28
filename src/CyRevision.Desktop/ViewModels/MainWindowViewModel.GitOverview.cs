using System.Collections.ObjectModel;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private GitRemoteInfo? _selectedGitRemote;
    private Guid? _gitRemotesProjectId;
    private string _gitRemoteNameDraft = "origin";
    private string _gitRemoteFetchUrlDraft = string.Empty;
    private string _gitRemotePushUrlDraft = string.Empty;
    private string _gitOverviewRemoteStatus = "No remote configuration loaded.";
    private string _gitOverviewRepositoryState = "Waiting for repository status.";
    private string _gitOverviewTrackingState = "No tracking information loaded.";
    private string _gitOverviewHeadSummary = "No commit loaded.";
    private bool _isGitRemoteEditorDirty;
    private bool _suppressGitRemoteEditorChanges;

    public ObservableCollection<GitRemoteInfo> GitRemotes { get; } = [];

    public GitRemoteInfo? SelectedGitRemote
    {
        get => _selectedGitRemote;
        set
        {
            if (!SetProperty(ref _selectedGitRemote, value))
            {
                return;
            }

            if (value is not null)
            {
                PopulateGitRemoteEditor(value);
            }

            NotifyGitRemoteCommandStateChanged();
        }
    }

    public string GitRemoteNameDraft
    {
        get => _gitRemoteNameDraft;
        set
        {
            if (!SetProperty(ref _gitRemoteNameDraft, value)) return;
            MarkGitRemoteEditorDirty();
        }
    }

    public string GitRemoteFetchUrlDraft
    {
        get => _gitRemoteFetchUrlDraft;
        set
        {
            if (!SetProperty(ref _gitRemoteFetchUrlDraft, value)) return;
            MarkGitRemoteEditorDirty();
        }
    }

    public string GitRemotePushUrlDraft
    {
        get => _gitRemotePushUrlDraft;
        set
        {
            if (!SetProperty(ref _gitRemotePushUrlDraft, value)) return;
            MarkGitRemoteEditorDirty();
        }
    }

    public string GitOverviewRemoteStatus
    {
        get => _gitOverviewRemoteStatus;
        private set => SetProperty(ref _gitOverviewRemoteStatus, value);
    }

    public string GitOverviewRepositoryState
    {
        get => _gitOverviewRepositoryState;
        private set => SetProperty(ref _gitOverviewRepositoryState, value);
    }

    public string GitOverviewTrackingState
    {
        get => _gitOverviewTrackingState;
        private set => SetProperty(ref _gitOverviewTrackingState, value);
    }

    public string GitOverviewHeadSummary
    {
        get => _gitOverviewHeadSummary;
        private set => SetProperty(ref _gitOverviewHeadSummary, value);
    }

    public bool IsGitRemoteEditorDirty
    {
        get => _isGitRemoteEditorDirty;
        private set
        {
            if (!SetProperty(ref _isGitRemoteEditorDirty, value)) return;
            NotifyGitRemoteCommandStateChanged();
        }
    }

    public bool IsEditingGitRemote => SelectedGitRemote is not null;

    public string GitRemoteSaveAction => SelectedGitRemote is null ? "Add remote" : "Save remote";

    public string SelectedGitRemoteWebUrl =>
        SelectedGitRemote?.WebUrl ?? "No browser page is available for this remote URL.";

    public bool CanSaveGitRemote =>
        IsGitProject &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(GitRemoteNameDraft) &&
        !string.IsNullOrWhiteSpace(GitRemoteFetchUrlDraft);

    public bool CanRemoveSelectedGitRemote => IsGitProject && !IsBusy && SelectedGitRemote is not null;

    public bool CanOpenSelectedGitRemote =>
        IsGitProject &&
        !IsBusy &&
        SelectedGitRemote?.CanOpenWebPage == true;

    public void BeginNewGitRemote()
    {
        _selectedGitRemote = null;
        OnPropertyChanged(nameof(SelectedGitRemote));
        SetGitRemoteEditor("origin", string.Empty, string.Empty, dirty: false);
        GitOverviewRemoteStatus = "Enter a remote name and its fetch URL. The push URL is optional.";
        NotifyGitRemoteCommandStateChanged();
    }

    public async Task RefreshGitOverviewRemotesAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled || IsBusy)
        {
            return;
        }

        await RunOperationAsync(
            "Refreshing Git remotes...",
            () => RefreshGitRemoteSnapshotCoreAsync(project, CancellationToken.None, preserveEditor: true),
            "Git remote configuration refreshed");
    }

    public async Task SaveGitRemoteAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        string name = GitRemoteNameDraft.Trim();
        string fetchUrl = GitRemoteFetchUrlDraft.Trim();
        string pushUrl = string.IsNullOrWhiteSpace(GitRemotePushUrlDraft)
            ? fetchUrl
            : GitRemotePushUrlDraft.Trim();
        if (project is null || !project.Definition.Features.GitEnabled ||
            IsBusy || name.Length == 0 || fetchUrl.Length == 0)
        {
            return;
        }

        await RunOperationAsync(
            SelectedGitRemote is null ? "Adding Git remote..." : "Updating Git remote...",
            async () =>
            {
                await _gitService.AddOrUpdateRemoteAsync(project.RootPath, name, fetchUrl);
                await _gitService.SetRemotePushUrlAsync(project.RootPath, name, pushUrl);
                IsGitRemoteEditorDirty = false;
                await RefreshGitRemoteSnapshotCoreAsync(project, CancellationToken.None, preserveEditor: false, name);
            },
            $"Remote {name} configured");
    }

    public async Task RemoveSelectedGitRemoteAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        GitRemoteInfo? remote = SelectedGitRemote;
        if (project is null || remote is null || IsBusy)
        {
            return;
        }

        await RunOperationAsync(
            $"Removing remote {remote.Name}...",
            async () =>
            {
                await _gitService.RemoveRemoteAsync(project.RootPath, remote.Name);
                IsGitRemoteEditorDirty = false;
                await RefreshGitRemoteSnapshotCoreAsync(project, CancellationToken.None, preserveEditor: false);
            },
            $"Remote {remote.Name} removed locally");
    }

    private async Task RefreshGitRemoteSnapshotCoreAsync(
        ProjectItemViewModel project,
        CancellationToken cancellationToken,
        bool preserveEditor,
        string? preferredRemoteName = null)
    {
        IReadOnlyList<GitRemoteInfo> remotes = await _gitService.GetRemotesAsync(
            project.RootPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (SelectedProject?.Id != project.Id)
        {
            return;
        }

        ApplyGitRemoteSnapshot(project, remotes, preserveEditor, preferredRemoteName);
    }

    private void ApplyGitRemoteSnapshot(
        ProjectItemViewModel project,
        IReadOnlyList<GitRemoteInfo> remotes,
        bool preserveEditor = true,
        string? preferredRemoteName = null)
    {
        string? previousName = preferredRemoteName ?? SelectedGitRemote?.Name;
        bool keepDraft = preserveEditor && IsGitRemoteEditorDirty;
        string draftName = GitRemoteNameDraft;
        string draftFetch = GitRemoteFetchUrlDraft;
        string draftPush = GitRemotePushUrlDraft;

        ReplaceCollection(GitRemotes, remotes);
        _gitRemotesProjectId = project.Id;
        GitRemoteInfo? selected = remotes.FirstOrDefault(remote =>
                                      remote.Name.Equals(previousName, StringComparison.Ordinal))
                                  ?? remotes.FirstOrDefault(remote =>
                                      remote.Name.Equals("origin", StringComparison.Ordinal))
                                  ?? remotes.FirstOrDefault();
        SelectedGitRemote = selected;

        if (keepDraft)
        {
            SetGitRemoteEditor(draftName, draftFetch, draftPush, dirty: true);
        }
        else if (selected is null)
        {
            SetGitRemoteEditor("origin", string.Empty, string.Empty, dirty: false);
        }

        GitRemoteInfo? origin = remotes.FirstOrDefault(remote =>
            remote.Name.Equals("origin", StringComparison.Ordinal));
        RemoteUrl = origin?.FetchUrl ?? string.Empty;
        GitOverviewRemoteStatus = remotes.Count == 0
            ? "No remote configured. Add origin or another remote below."
            : $"{remotes.Count:N0} remote(s) configured. Select one to inspect or edit it.";
        NotifyGitRemoteCommandStateChanged();
    }

    private void PopulateGitRemoteEditor(GitRemoteInfo remote) =>
        SetGitRemoteEditor(remote.Name, remote.FetchUrl, remote.PushUrl, dirty: false);

    private void SetGitRemoteEditor(string name, string fetchUrl, string pushUrl, bool dirty)
    {
        _suppressGitRemoteEditorChanges = true;
        try
        {
            GitRemoteNameDraft = name;
            GitRemoteFetchUrlDraft = fetchUrl;
            GitRemotePushUrlDraft = pushUrl;
        }
        finally
        {
            _suppressGitRemoteEditorChanges = false;
        }

        IsGitRemoteEditorDirty = dirty;
    }

    private void MarkGitRemoteEditorDirty()
    {
        if (!_suppressGitRemoteEditorChanges)
        {
            IsGitRemoteEditorDirty = true;
        }

        NotifyGitRemoteCommandStateChanged();
    }

    private void UpdateGitOverviewFromStatus(GitRepositoryStatus status)
    {
        int conflicts = status.Changes.Count(change => change.Kind == GitChangeKind.Conflicted);
        GitOverviewRepositoryState = conflicts > 0
            ? $"{conflicts:N0} conflicted file(s) require resolution"
            : status.Changes.Count == 0
                ? "Working tree clean"
                : $"{status.Changes.Count:N0} pending change(s)";
        GitOverviewTrackingState = !status.HasRemote
            ? "Local repository - no remote configured"
            : status.AheadBy == 0 && status.BehindBy == 0
                ? "Up to date with the tracked branch"
                : $"Tracking difference: {status.AheadBy:N0} ahead / {status.BehindBy:N0} behind";
    }

    private void UpdateGitOverviewHead(GitRevision? revision)
    {
        GitOverviewHeadSummary = revision is null
            ? "Repository has no commit yet."
            : $"{revision.ShortHash} - {revision.Subject} - {revision.AuthorName} - {revision.AuthoredAt.ToLocalTime():g}";
    }

    private void ClearGitOverview()
    {
        GitRemotes.Clear();
        _gitRemotesProjectId = null;
        _selectedGitRemote = null;
        OnPropertyChanged(nameof(SelectedGitRemote));
        SetGitRemoteEditor("origin", string.Empty, string.Empty, dirty: false);
        bool gitEnabled = IsGitProject;
        GitOverviewRemoteStatus = gitEnabled
            ? "Loading remote configuration..."
            : "Git is disabled for this project.";
        GitOverviewRepositoryState = gitEnabled
            ? "Waiting for repository status."
            : "Git is disabled for this project.";
        GitOverviewTrackingState = gitEnabled
            ? "Waiting for tracking information."
            : "No Git tracking information.";
        GitOverviewHeadSummary = gitEnabled
            ? "Loading latest commit..."
            : "No Git commit loaded.";
        NotifyGitRemoteCommandStateChanged();
    }

    private void NotifyGitRemoteCommandStateChanged()
    {
        OnPropertyChanged(nameof(IsEditingGitRemote));
        OnPropertyChanged(nameof(GitRemoteSaveAction));
        OnPropertyChanged(nameof(SelectedGitRemoteWebUrl));
        OnPropertyChanged(nameof(CanSaveGitRemote));
        OnPropertyChanged(nameof(CanRemoveSelectedGitRemote));
        OnPropertyChanged(nameof(CanOpenSelectedGitRemote));
    }
}
