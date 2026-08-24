using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using CyRevision.Code;
using CyRevision.Desktop.Plugins;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitRevisionTreeNode
{
    private readonly List<GitRevisionTreeNode> _mutableChildren = [];
    private int _fileCount;

    private GitRevisionTreeNode(string name, string path, bool isDirectory, GitRevisionFile? file)
    {
        Name = name;
        Path = path;
        IsDirectory = isDirectory;
        File = file;
        _fileCount = isDirectory ? 0 : 1;
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public GitRevisionFile? File { get; }
    public IReadOnlyList<GitRevisionTreeNode> Children => _mutableChildren;
    public string Icon => IsDirectory ? "▸" : File?.IsSubmodule == true ? "SUB" : FileIcon(Name);
    public string AccentColor => IsDirectory ? "#D7BA7D" : FileColor(Name);
    public string Detail => IsDirectory ? $"{_fileCount:N0} file(s)" : File?.SizeText ?? string.Empty;

    public static IReadOnlyList<GitRevisionTreeNode> Build(IReadOnlyList<GitRevisionFile> files)
    {
        GitRevisionTreeNode root = new(string.Empty, string.Empty, true, null);
        Dictionary<string, GitRevisionTreeNode> directories = new(StringComparer.Ordinal)
        {
            [string.Empty] = root
        };

        foreach (GitRevisionFile file in files)
        {
            string[] segments = file.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            string parentPath = string.Empty;
            GitRevisionTreeNode parent = root;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                string path = parentPath.Length == 0 ? segments[index] : parentPath + "/" + segments[index];
                if (!directories.TryGetValue(path, out GitRevisionTreeNode? directory))
                {
                    directory = new GitRevisionTreeNode(segments[index], path, true, null);
                    directories[path] = directory;
                    parent._mutableChildren.Add(directory);
                }

                parent = directory;
                parentPath = path;
            }

            parent._mutableChildren.Add(new GitRevisionTreeNode(segments[^1], file.Path, false, file));
        }

        SortRecursively(root);
        ComputeFileCounts(root);
        return root._mutableChildren;
    }

    private static int ComputeFileCounts(GitRevisionTreeNode node)
    {
        if (!node.IsDirectory) return node._fileCount;
        node._fileCount = node._mutableChildren.Sum(ComputeFileCounts);
        return node._fileCount;
    }

    private static void SortRecursively(GitRevisionTreeNode node)
    {
        node._mutableChildren.Sort((left, right) =>
        {
            int directoriesFirst = right.IsDirectory.CompareTo(left.IsDirectory);
            return directoriesFirst != 0
                ? directoriesFirst
                : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });
        foreach (GitRevisionTreeNode child in node._mutableChildren.Where(child => child.IsDirectory))
            SortRecursively(child);
    }

    private static string FileIcon(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "C#",
        ".cpp" or ".h" or ".hpp" => "C+",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => "IMG",
        ".uasset" or ".umap" => "UE",
        ".json" => "{}",
        ".md" => "M↓",
        _ => "·"
    };

    private static string FileColor(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "#B77FDB",
        ".cpp" or ".h" or ".hpp" => "#5DADE2",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => "#E06C75",
        ".uasset" or ".umap" => "#C678DD",
        ".json" => "#9CDC8C",
        ".md" => "#61AFEF",
        _ => "#A9B7C6"
    };
}

public sealed class BranchFileExplorerViewModel : ObservableObject, IDisposable
{
    private readonly IGitRepositoryService _gitService;
    private readonly CodeWorkspaceService _codeWorkspaceService;
    private readonly FilePresentationService _filePresentationService;
    private readonly GitBranch _branch;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _previewCancellation;
    private IReadOnlyList<GitRevisionFile> _allFiles = [];
    private IReadOnlyList<GitRevisionTreeNode> _treeRoots = [];
    private IReadOnlyList<GitRevisionFile> _filteredFiles = [];
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
    private string _diffText = "Select a file, then load its difference against HEAD.";
    private bool _isLoading;
    private bool _isPreviewLoading;
    private bool _isDiffLoading;
    private bool _treeView = true;

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
        _filePresentationService = filePresentationService;
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

    public GitRevisionTreeNode? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (!SetProperty(ref _selectedTreeNode, value) || value?.File is null) return;
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
            if (value is not null) _ = LoadPreviewAsync(value, fetchMissingLfsObject: false);
        }
    }

    public bool HasSelectedFile => SelectedListFile is not null;
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

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        private set
        {
            Bitmap? previous = _previewImage;
            if (!SetProperty(ref _previewImage, value)) return;
            previous?.Dispose();
            OnPropertyChanged(nameof(PreviewIsImage));
            OnPropertyChanged(nameof(PreviewIsText));
        }
    }

    public bool PreviewIsImage => PreviewImage is not null;
    public bool PreviewIsText => PreviewImage is null;
    public string DiffText { get => _diffText; private set => SetProperty(ref _diffText, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsPreviewLoading { get => _isPreviewLoading; private set => SetProperty(ref _isPreviewLoading, value); }
    public bool IsDiffLoading { get => _isDiffLoading; private set => SetProperty(ref _isDiffLoading, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _loadCancellation.Token;
        IsLoading = true;
        Status = $"Reading the tree at {RevisionReference}…";
        try
        {
            IReadOnlyList<GitRevisionFile> files = await _gitService.GetRevisionFilesAsync(
                RepositoryPath, RevisionReference, token);
            IReadOnlyList<GitRevisionTreeNode> tree = await Task.Run(() => GitRevisionTreeNode.Build(files), token);
            token.ThrowIfCancellationRequested();
            _allFiles = files;
            TreeRoots = tree;
            ApplyFilter();
            Summary = $"{files.Count:N0} file(s) at {RevisionReference}";
            Status = "Ready · read-only branch inspection · no checkout, merge, index, or working file changed.";
        }
        catch (OperationCanceledException)
        {
            Status = "Branch inspection cancelled.";
        }
        catch (Exception exception)
        {
            Summary = "Unable to read branch files.";
            Status = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshRemoteAsync()
    {
        if (RemoteName is null || RemoteBranchName is null) return;
        IsLoading = true;
        Status = $"Refreshing only {RemoteName}/{RemoteBranchName} into a private inspection reference…";
        try
        {
            RevisionReference = await _gitService.FetchRemoteBranchForInspectionAsync(
                RepositoryPath, RemoteName, RemoteBranchName);
            await LoadAsync();
            Status = $"Remote branch refreshed at {RevisionReference}. The checked-out branch was not changed.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            IsLoading = false;
        }
    }

    public Task RetrieveSelectedForPreviewAsync() => SelectedListFile is null
        ? Task.CompletedTask
        : LoadPreviewAsync(SelectedListFile, fetchMissingLfsObject: true);

    public async Task LoadDiffAsync()
    {
        GitRevisionFile? file = SelectedListFile;
        if (file is null) return;
        IsDiffLoading = true;
        DiffText = $"Loading {BranchName} ↔ HEAD for {file.Path}…";
        try
        {
            DiffText = await _gitService.GetComparisonDiffAsync(
                RepositoryPath, RevisionReference, "HEAD", file.Path);
            if (string.IsNullOrWhiteSpace(DiffText))
                DiffText = "The selected file is identical in this branch and HEAD.";
        }
        catch (Exception exception)
        {
            DiffText = exception.Message;
        }
        finally
        {
            IsDiffLoading = false;
        }
    }

    public async Task<GitRevisionFileExportResult?> ExportSelectedAsync(string destinationPath)
    {
        GitRevisionFile? file = SelectedListFile;
        if (file is null) return null;
        IsPreviewLoading = true;
        Status = $"Exporting {file.Path}…";
        try
        {
            GitRevisionFileExportResult result = await _gitService.MaterializeFileFromRevisionAsync(
                RepositoryPath,
                file.Path,
                RevisionReference,
                destinationPath,
                RemoteName,
                fetchMissingLfsObject: true);
            Status = result.IsLfsObject
                ? $"LFS file exported{(result.DownloadedLfsObject ? " after targeted retrieval" : string.Empty)}: {destinationPath}"
                : $"File exported: {destinationPath}";
            return result;
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    public async Task<string?> RestoreSelectedToWorkingTreeAsync()
    {
        GitRevisionFile? file = SelectedListFile;
        if (file is null) return null;
        string destination = ResolveInsideRepository(file.Path);
        string stagingRoot = ResolveInsideRepository(
            $".cyrevision/cache/branch-file-restore-stage/{Guid.NewGuid():N}");
        string stagedFile = Path.GetFullPath(Path.Combine(
            stagingRoot,
            file.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!stagedFile.StartsWith(stagingRoot + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidOperationException("The selected path is outside the restore staging directory.");

        string? backupPath = null;
        Status = $"Preparing {file.Path} from {BranchName}…";
        try
        {
            await _gitService.MaterializeFileFromRevisionAsync(
                RepositoryPath,
                file.Path,
                RevisionReference,
                stagedFile,
                RemoteName,
                fetchMissingLfsObject: true);

            if (File.Exists(destination))
            {
                string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                backupPath = ResolveInsideRepository(
                    $".cyrevision/backups/branch-file-restore/{stamp}/{file.Path}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(destination, backupPath, overwrite: false);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(stagedFile, destination, overwrite: true);
            Status = backupPath is null
                ? $"{file.Path} restored as a working-tree file. It was not staged."
                : $"{file.Path} restored and not staged. Previous file backed up to {Path.GetRelativePath(RepositoryPath, backupPath)}.";
            return backupPath;
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private async Task LoadPreviewAsync(GitRevisionFile file, bool fetchMissingLfsObject)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _previewCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        IsPreviewLoading = true;
        PreviewPath = file.Path;
        PreviewSummary = $"Loading {file.Path}…";
        PreviewText = string.Empty;
        PreviewImage = null;
        try
        {
            string cacheRoot = GetCacheRoot();
            string cachePath = Path.GetFullPath(Path.Combine(cacheRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!cachePath.StartsWith(cacheRoot + Path.DirectorySeparatorChar, PathComparison))
                throw new InvalidOperationException("The branch path is outside the preview cache.");
            GitRevisionFileExportResult export = await _gitService.MaterializeFileFromRevisionAsync(
                RepositoryPath,
                file.Path,
                RevisionReference,
                cachePath,
                RemoteName,
                fetchMissingLfsObject,
                token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_previewCancellation, cancellation) || SelectedListFile?.Path != file.Path) return;

            FilePresentationResult? presentation = await _filePresentationService.CreatePreviewAsync(
                new FilePreviewRequest(RepositoryPath, file.Path, cachePath, export.Size), token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_previewCancellation, cancellation) || SelectedListFile?.Path != file.Path) return;
            if (presentation is not null)
            {
                PreviewSummary = $"{file.Path} · {presentation.Summary} · {presentation.ProviderId}";
                if (presentation.Kind == FilePresentationKind.Image && presentation.ImagePath is not null && File.Exists(presentation.ImagePath))
                {
                    PreviewImage = new Bitmap(presentation.ImagePath);
                    PreviewText = string.Empty;
                }
                else
                {
                    PreviewText = string.IsNullOrWhiteSpace(presentation.TextContent)
                        ? presentation.Summary
                        : presentation.TextContent;
                }
                return;
            }

            CodeFilePreview preview = await _codeWorkspaceService.ReadPreviewAsync(cacheRoot, file.Path, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_previewCancellation, cancellation) || SelectedListFile?.Path != file.Path) return;
            PreviewSummary = $"{file.Path} · {preview.Summary}";
            PreviewText = preview.IsBinary
                ? $"No active project plugin provides a preview for {Path.GetExtension(file.Path)} files."
                : preview.Text;
        }
        catch (OperationCanceledException)
        {
            // A newer file selection superseded this preview.
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && SelectedListFile?.Path == file.Path)
            {
                PreviewSummary = file.Path;
                PreviewText = exception.Message + Environment.NewLine + Environment.NewLine +
                              "Use ‘Retrieve selected’ to download only this missing LFS object when available on the remote.";
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
                cancellation.Dispose();
                IsPreviewLoading = false;
            }
        }
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

    private static bool MatchesFilter(string path, string filter)
    {
        if (!filter.Contains('*') && !filter.Contains('?'))
            return path.Contains(filter, StringComparison.OrdinalIgnoreCase);
        return CodeFilePatternMatcher.IsMatch(path, filter);
    }

    private string GetCacheRoot()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(RevisionReference));
        string referenceKey = Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        string root = Path.Combine(RepositoryPath, ".cyrevision", "cache", "branch-files", referenceKey);
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

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        PreviewImage = null;
    }
}
