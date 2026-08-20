using System.Collections.ObjectModel;
using CyRevision.Desktop.Workspace;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private GitAnnotation? _selectedGitAnnotation;
    private string _gitAnnotationSearch = string.Empty;
    private string _gitAnnotationKindFilter = "All";
    private string _gitAnnotationTarget = string.Empty;
    private string _gitAnnotationTitle = string.Empty;
    private string _gitAnnotationNote = string.Empty;
    private string _gitAnnotationTags = string.Empty;
    private string _gitAnnotationEditorKind = "Commit";
    private string _gitAnnotationStatus = "Annotations are local to CyRevision and never modify the repository.";
    private int _gitAnnotationLoadVersion;

    public ObservableCollection<GitAnnotation> GitAnnotations { get; } = [];
    public ObservableCollection<GitAnnotation> FilteredGitAnnotations { get; } = [];
    public IReadOnlyList<string> GitAnnotationKindFilters { get; } = ["All", "Commit", "Branch"];
    public IReadOnlyList<string> GitAnnotationEditorKinds { get; } = ["Commit", "Branch"];

    public GitAnnotation? SelectedGitAnnotation
    {
        get => _selectedGitAnnotation;
        set
        {
            if (!SetProperty(ref _selectedGitAnnotation, value) || value is null) return;
            GitAnnotationEditorKind = value.KindText;
            GitAnnotationTarget = value.Target;
            GitAnnotationTitle = value.Title;
            GitAnnotationNote = value.Note;
            GitAnnotationTags = value.Tags;
        }
    }

    public string GitAnnotationSearch
    {
        get => _gitAnnotationSearch;
        set
        {
            if (SetProperty(ref _gitAnnotationSearch, value)) ApplyGitAnnotationFilter();
        }
    }

    public string GitAnnotationKindFilter
    {
        get => _gitAnnotationKindFilter;
        set
        {
            if (SetProperty(ref _gitAnnotationKindFilter, value)) ApplyGitAnnotationFilter();
        }
    }

    public string GitAnnotationEditorKind
    {
        get => _gitAnnotationEditorKind;
        set => SetProperty(ref _gitAnnotationEditorKind, value);
    }

    public string GitAnnotationTarget
    {
        get => _gitAnnotationTarget;
        set => SetProperty(ref _gitAnnotationTarget, value);
    }

    public string GitAnnotationTitle
    {
        get => _gitAnnotationTitle;
        set => SetProperty(ref _gitAnnotationTitle, value);
    }

    public string GitAnnotationNote
    {
        get => _gitAnnotationNote;
        set => SetProperty(ref _gitAnnotationNote, value);
    }

    public string GitAnnotationTags
    {
        get => _gitAnnotationTags;
        set => SetProperty(ref _gitAnnotationTags, value);
    }

    public string GitAnnotationStatus
    {
        get => _gitAnnotationStatus;
        private set => SetProperty(ref _gitAnnotationStatus, value);
    }

    public void UseSelectedBranchForAnnotation()
    {
        GitAnnotationEditorKind = "Branch";
        GitAnnotationTarget = SelectedBranch?.Name ?? CurrentBranch;
        GitAnnotationTitle = SelectedBranch is null ? CurrentBranch : SelectedBranch.Name;
    }

    public void UseSelectedCommitForAnnotation()
    {
        GitRevision? revision = SelectedBranchRevision ?? SelectedExplorerRevision ?? History.FirstOrDefault();
        if (revision is null)
        {
            GitAnnotationStatus = "Select a commit in History or Branches first.";
            return;
        }
        GitAnnotationEditorKind = "Commit";
        GitAnnotationTarget = revision.Hash;
        GitAnnotationTitle = revision.Subject;
    }

    public void NewGitAnnotation()
    {
        SelectedGitAnnotation = null;
        GitAnnotationTarget = string.Empty;
        GitAnnotationTitle = string.Empty;
        GitAnnotationNote = string.Empty;
        GitAnnotationTags = string.Empty;
        GitAnnotationStatus = "New local annotation.";
    }

    public async Task SaveGitAnnotationAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || string.IsNullOrWhiteSpace(GitAnnotationTarget))
        {
            GitAnnotationStatus = "Choose a project and a branch or commit target first.";
            return;
        }
        if (!Enum.TryParse(GitAnnotationEditorKind, true, out GitAnnotationTargetKind kind))
            kind = GitAnnotationTargetKind.Commit;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        GitAnnotation annotation = new(
            SelectedGitAnnotation?.Id ?? Guid.NewGuid(), project.Id, kind,
            GitAnnotationTarget.Trim(), GitAnnotationTitle.Trim(), GitAnnotationNote.Trim(), GitAnnotationTags.Trim(),
            SelectedGitAnnotation?.CreatedAt ?? now, now);
        annotation = await _gitAnnotationStore.SaveAsync(annotation);
        await LoadGitAnnotationsAsync(project);
        SelectedGitAnnotation = GitAnnotations.FirstOrDefault(item => item.Id == annotation.Id);
        GitAnnotationStatus = $"Saved locally at {now.ToLocalTime():g}. The repository was not modified.";
        _applicationLogService.Information("git-annotation", $"saved kind={kind} target={annotation.Target}", project.RootPath);
    }

    public async Task DeleteSelectedGitAnnotationAsync()
    {
        GitAnnotation? selected = SelectedGitAnnotation;
        if (selected is null) return;
        await _gitAnnotationStore.DeleteAsync(selected.Id);
        if (SelectedProject is { } project) await LoadGitAnnotationsAsync(project);
        NewGitAnnotation();
        GitAnnotationStatus = "Annotation removed from local CyRevision storage.";
    }

    public async Task LoadGitAnnotationsAsync(ProjectItemViewModel? project)
    {
        int version = Interlocked.Increment(ref _gitAnnotationLoadVersion);
        if (project is null)
        {
            GitAnnotations.Clear();
            FilteredGitAnnotations.Clear();
            return;
        }
        IReadOnlyList<GitAnnotation> annotations = await _gitAnnotationStore.LoadAsync(project.Id);
        if (version != _gitAnnotationLoadVersion || SelectedProject?.Id != project.Id) return;
        ReplaceCollection(GitAnnotations, annotations);
        ApplyGitAnnotationFilter();
        GitAnnotationStatus = annotations.Count == 0
            ? "No local annotation for this project. Annotations never modify Git."
            : $"{annotations.Count:N0} local annotation(s) loaded for {project.Name}.";
    }

    private void ApplyGitAnnotationFilter()
    {
        string search = GitAnnotationSearch.Trim();
        IEnumerable<GitAnnotation> annotations = GitAnnotations;
        if (!string.Equals(GitAnnotationKindFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse(GitAnnotationKindFilter, true, out GitAnnotationTargetKind kind))
            annotations = annotations.Where(item => item.Kind == kind);
        if (search.Length > 0)
            annotations = annotations.Where(item => item.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        ReplaceCollection(FilteredGitAnnotations, annotations.OrderByDescending(item => item.UpdatedAt));
    }
}
