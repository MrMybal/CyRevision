using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using CyRevision.Code;
using CyRevision.Desktop.Plugins;
using CyRevision.Desktop.Services;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class BranchFileExplorerViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly IGitRepositoryService _gitService;
    private readonly CodeWorkspaceService _codeWorkspaceService;
    private readonly FilePresentationService _filePresentationService;
    private readonly BranchFileWorkspaceStore _workspaceStore = new();
    private readonly GitBranch _branch;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _previewCancellation;
    private IReadOnlyList<GitRevisionFile> _allFiles = [];
    private IReadOnlyList<GitRevisionTreeNode> _treeRoots = [];
    private IReadOnlyList<GitRevisionFile> _filteredFiles = [];
    private IReadOnlyList<GitRevisionFile> _selectedFiles = [];
    private GitRevisionTreeNode? _selectedTreeNode;
    private GitRevisionFile? _selectedListFile;
    private string _search = string.Empty;
    private string _revisionReference;
    private string _summary = "Loading branch files…";
    private string _status = "Read-only inspection. The current branch and index are untouched.";
    private string _previewSummary = "Select a file to preview it.";
    private string _previewText = string.Empty;
    private string _previewPath = string.Empty;
    private Bitmap? _previewImage;
    private string _diffSummary = "Select a file, then load its difference against HEAD.";
    private string _diffText = "Select a file, then load its difference against HEAD.";
    private Bitmap? _diffBaselineImage;
    private Bitmap? _diffCandidateImage;
    private Bitmap? _diffHeatmapImage;
    private BranchFileOperationProgress _operationProgress = new("Ready", string.Empty, 0, 0);
    private bool _isOperationActive;
    private bool _isPreviewLoading;
    private bool _isDiffLoading;
    private bool _treeView = true;
    private bool _historyLoaded;
    private bool _disposed;

    public BranchFileExplorerViewModel(
        IGitRepositoryService gitService,
        CodeWorkspaceService codeWorkspaceService,
        FilePresentationService filePresentationService,
        string projectName,
        string repositoryPath,
        GitBranch branch)
    {
        _gitService = gitService;
        _codeWorkspaceService = codeWorkspaceService;
        _filePresentationService = filePresentationService.CreateProjectSnapshot();
        ProjectName = projectName;
        RepositoryPath = Path.GetFullPath(repositoryPath);
        _branch = branch;
        _revisionReference = branch.Name;
        (RemoteName, RemoteBranchName) = ResolveRemote(branch);
    }

    public string ProjectName { get; }
    public string RepositoryPath { get; }
    public string BranchName => _branch.Name;
    public string? RemoteName { get; }
    public string? RemoteBranchName { get; }
    public bool CanRefreshRemote => RemoteName is not null && RemoteBranchName is not null;
    public string WindowTitle => $"{ProjectName} — Branch Files — {BranchName}";
    public IReadOnlyList<GitRevisionTreeNode> TreeRoots { get => _treeRoots; private set => SetProperty(ref _treeRoots, value); }
    public IReadOnlyList<GitRevisionFile> FilteredFiles { get => _filteredFiles; private set => SetProperty(ref _filteredFiles, value); }
    public ObservableCollection<BranchFileOperationHistoryItem> OperationHistory { get; } = [];

    public GitRevisionTreeNode? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (!SetProperty(ref _selectedTreeNode, value) || value?.File is null) return;
            SetSelectedFiles([value.File]);
            SelectedListFile = value.File;
        }
    }

    public GitRevisionFile? SelectedListFile
    {
        get => _selectedListFile;
        set
        {
            if (!SetProperty(ref _selectedListFile, value)) return;
            OnPropertyChanged(nameof(HasSelectedFile));
            OnPropertyChanged(nameof(CanUseSelectedFile));
            OnPropertyChanged(nameof(CanLoadPreviewObject));
            if (value is not null) _ = LoadPreviewAsync(value, fetchMissingLfsObject: false);
        }
    }

    public bool HasSelectedFile => SelectedListFile is not null;
    public bool HasSelectedFiles => _selectedFiles.Count > 0 || HasSelectedFile;
    public bool CanRetrieveSelected => CanStartOperation && HasSelectedFiles;
    public bool CanUseSelectedFile => CanStartOperation && HasSelectedFile;
    public bool CanLoadPreviewObject => CanUseSelectedFile && !IsPreviewLoading;
    public bool HasCancelableOperation => IsOperationActive || IsPreviewLoading || IsDiffLoading;
    public int SelectedFileCount => _selectedFiles.Count > 0 ? _selectedFiles.Count : HasSelectedFile ? 1 : 0;
    public string SelectedFileSummary => SelectedFileCount == 1 ? "1 file selected" : $"{SelectedFileCount:N0} files selected";

    public void SetSelectedFiles(IEnumerable<GitRevisionFile> files)
    {
        _selectedFiles = files
            .GroupBy(file => file.Path, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (_selectedFiles.Count > 0 &&
            (_selectedListFile is null || !_selectedFiles.Any(file => file.Path == _selectedListFile.Path)))
            SelectedListFile = _selectedFiles[0];
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(CanRetrieveSelected));
        OnPropertyChanged(nameof(SelectedFileCount));
        OnPropertyChanged(nameof(SelectedFileSummary));
    }

    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value ?? string.Empty)) return;
            ApplyFilter();
            OnPropertyChanged(nameof(ShowTree));
            OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool TreeView
    {
        get => _treeView;
        set
        {
            if (!SetProperty(ref _treeView, value)) return;
            OnPropertyChanged(nameof(ListView));
            OnPropertyChanged(nameof(ShowTree));
            OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool ListView { get => !TreeView; set { if (value) TreeView = false; } }
    public bool ShowTree => TreeView && string.IsNullOrWhiteSpace(Search);
    public bool ShowList => !TreeView || !string.IsNullOrWhiteSpace(Search);
    public string RevisionReference { get => _revisionReference; private set => SetProperty(ref _revisionReference, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PreviewSummary { get => _previewSummary; private set => SetProperty(ref _previewSummary, value); }
    public string PreviewText { get => _previewText; private set => SetProperty(ref _previewText, value); }
    public string PreviewPath { get => _previewPath; private set => SetProperty(ref _previewPath, value); }
    public bool IsPreviewLoading { get => _isPreviewLoading; private set { if (!SetProperty(ref _isPreviewLoading, value)) return; OnPropertyChanged(nameof(CanLoadPreviewObject)); OnPropertyChanged(nameof(HasCancelableOperation)); } }
    public bool IsDiffLoading { get => _isDiffLoading; private set { if (!SetProperty(ref _isDiffLoading, value)) return; OnPropertyChanged(nameof(HasCancelableOperation)); } }

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        private set => ReplaceBitmap(ref _previewImage, value, nameof(PreviewImage), nameof(PreviewIsImage), nameof(PreviewIsText));
    }

    public bool PreviewIsImage => PreviewImage is not null;
    public bool PreviewIsText => PreviewImage is null;
    public string DiffSummary { get => _diffSummary; private set => SetProperty(ref _diffSummary, value); }
    public string DiffText { get => _diffText; private set => SetProperty(ref _diffText, value); }
    public Bitmap? DiffBaselineImage { get => _diffBaselineImage; private set => ReplaceBitmap(ref _diffBaselineImage, value, nameof(DiffBaselineImage), nameof(HasSideBySideImages), nameof(HasVisualDiff)); }
    public Bitmap? DiffCandidateImage { get => _diffCandidateImage; private set => ReplaceBitmap(ref _diffCandidateImage, value, nameof(DiffCandidateImage), nameof(HasSideBySideImages), nameof(HasVisualDiff)); }
    public Bitmap? DiffHeatmapImage { get => _diffHeatmapImage; private set => ReplaceBitmap(ref _diffHeatmapImage, value, nameof(DiffHeatmapImage), nameof(HasHeatmapImage), nameof(HasVisualDiff)); }
    public bool HasSideBySideImages => DiffBaselineImage is not null && DiffCandidateImage is not null;
    public bool HasHeatmapImage => DiffHeatmapImage is not null;
    public bool HasVisualDiff => HasSideBySideImages || HasHeatmapImage;

    public BranchFileOperationProgress OperationProgress { get => _operationProgress; private set { if (!SetProperty(ref _operationProgress, value)) return; OnPropertyChanged(nameof(OperationSummary)); OnPropertyChanged(nameof(OperationDetail)); OnPropertyChanged(nameof(OperationPercent)); OnPropertyChanged(nameof(OperationIsIndeterminate)); } }
    public string OperationSummary => OperationProgress.Summary;
    public string OperationDetail => OperationProgress.Detail;
    public double OperationPercent => OperationProgress.Percent;
    public bool OperationIsIndeterminate => OperationProgress.IsIndeterminate;
    public bool IsOperationActive { get => _isOperationActive; private set { if (!SetProperty(ref _isOperationActive, value)) return; OnPropertyChanged(nameof(CanStartOperation)); OnPropertyChanged(nameof(CanRetrieveSelected)); OnPropertyChanged(nameof(CanUseSelectedFile)); OnPropertyChanged(nameof(CanLoadPreviewObject)); OnPropertyChanged(nameof(HasCancelableOperation)); } }
    public bool CanStartOperation => !IsOperationActive;

    public void CancelOperation()
    {
        _operationCancellation?.Cancel();
        _previewCancellation?.Cancel();
        Status = "Cancelling the active branch operation…";
    }

    private void ApplyFilter()
    {
        string filter = Search.Trim();
        FilteredFiles = filter.Length == 0
            ? _allFiles
            : _allFiles.Where(file => MatchesFilter(file.Path, filter)).ToArray();
        if (_allFiles.Count > 0)
            Summary = filter.Length == 0
                ? $"{_allFiles.Count:N0} file(s) at {RevisionReference}"
                : $"{FilteredFiles.Count:N0} match(es) · {_allFiles.Count:N0} file(s) at {RevisionReference}";
    }

    private static bool MatchesFilter(string path, string filter) =>
        !filter.Contains('*') && !filter.Contains('?')
            ? path.Contains(filter, StringComparison.OrdinalIgnoreCase)
            : CodeFilePatternMatcher.IsMatch(path, filter);

    private string GetCacheRoot(string? category = null)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(RevisionReference));
        string referenceKey = Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        string root = Path.Combine(RepositoryPath, ".cyrevision", "cache", "branch-files", referenceKey);
        if (!string.IsNullOrWhiteSpace(category)) root = Path.Combine(root, category);
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    private string ResolveInsideRepository(string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(RepositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(RepositoryPath + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidOperationException("The selected path is outside the repository.");
        return path;
    }

    private static (string? Remote, string? Branch) ResolveRemote(GitBranch branch)
    {
        string? remoteReference = branch.IsRemote ? branch.Name : branch.RemoteName;
        if (string.IsNullOrWhiteSpace(remoteReference)) return (null, null);
        int separator = remoteReference.IndexOf('/');
        return separator <= 0 || separator >= remoteReference.Length - 1
            ? (null, null)
            : (remoteReference[..separator], remoteReference[(separator + 1)..]);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private void ReplaceBitmap(ref Bitmap? field, Bitmap? value, string propertyName, params string[] dependentProperties)
    {
        if (ReferenceEquals(field, value)) return;
        Bitmap? previous = field;
        field = value;
        previous?.Dispose();
        OnPropertyChanged(propertyName);
        foreach (string dependent in dependentProperties) OnPropertyChanged(dependent);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        PreviewImage = null;
        DiffBaselineImage = null;
        DiffCandidateImage = null;
        DiffHeatmapImage = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        string inspectionReference = RevisionReference;
        Dispose();
        using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(10));
        if (inspectionReference.StartsWith("refs/cyrevision/inspect/", StringComparison.Ordinal))
        {
            try
            {
                await _gitService.DeleteInspectionReferenceAsync(RepositoryPath, inspectionReference, cleanupTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _ = exception;
            }
        }
        try
        {
            await _workspaceStore.CleanupCacheAsync(RepositoryPath, null, cleanupTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _ = exception;
        }
    }
}
