using Avalonia.Media.Imaging;
using CyRevision.Code;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class BranchFileExplorerViewModel
{
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginOperation("Reading Git tree", RevisionReference, 0, cancellationToken, out CancellationToken token)) return;
        try
        {
            if (!_historyLoaded)
            {
                OperationProgress = new BranchFileOperationProgress("Loading activity history", BranchName, 0, 0);
                IReadOnlyList<BranchFileOperationHistoryItem> history = await _workspaceStore.LoadHistoryAsync(RepositoryPath, token);
                foreach (BranchFileOperationHistoryItem item in history) OperationHistory.Add(item);
                _historyLoaded = true;
            }

            await _workspaceStore.CleanupCacheAsync(RepositoryPath, GetCacheRoot(), token);
            await LoadTreeCoreAsync(token);
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
            EndOperation();
        }
    }

    public async Task RefreshRemoteAsync()
    {
        if (RemoteName is null || RemoteBranchName is null ||
            !TryBeginOperation("Fetching remote branch", $"{RemoteName}/{RemoteBranchName}", 3, default, out CancellationToken token))
            return;
        try
        {
            OperationProgress = new BranchFileOperationProgress("Fetching Git objects", $"{RemoteName}/{RemoteBranchName} → private inspection ref", 1, 3);
            RevisionReference = await _gitService.FetchRemoteBranchForInspectionAsync(RepositoryPath, RemoteName, RemoteBranchName, token);
            OperationProgress = new BranchFileOperationProgress("Reading fetched tree", RevisionReference, 2, 3);
            await LoadTreeCoreAsync(token);
            OperationProgress = new BranchFileOperationProgress("Remote inspection ready", RevisionReference, 3, 3);
            Status = $"Remote branch refreshed at {RevisionReference}. The checked-out branch was not changed.";
        }
        catch (OperationCanceledException)
        {
            Status = "Remote branch refresh cancelled.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    public Task RetrieveSelectedForPreviewAsync() => SelectedListFile is null
        ? Task.CompletedTask
        : LoadPreviewAsync(SelectedListFile, fetchMissingLfsObject: true);

    public async Task<int> RetrieveSelectedFilesAsync(string destinationDirectory)
    {
        IReadOnlyList<GitRevisionFile> files = GetEffectiveSelection();
        if (files.Count == 0 || !TryBeginOperation("Retrieving branch files", destinationDirectory, files.Count, default, out CancellationToken token))
            return 0;
        int completed = 0;
        try
        {
            string root = Path.GetFullPath(destinationDirectory);
            Directory.CreateDirectory(root);
            foreach (GitRevisionFile file in files)
            {
                token.ThrowIfCancellationRequested();
                OperationProgress = new BranchFileOperationProgress("Git/LFS retrieval", file.Path, completed, files.Count);
                string destination = ResolveInsideDirectory(root, file.Path);
                await _gitService.MaterializeFileFromRevisionAsync(
                    RepositoryPath, file.Path, RevisionReference, destination, RemoteName, true, token);
                completed++;
            }
            OperationProgress = new BranchFileOperationProgress("Retrieval complete", destinationDirectory, completed, files.Count);
            Status = $"{completed:N0} branch file(s) retrieved to {destinationDirectory}. The repository was not changed.";
            await RecordHistoryAsync("Retrieve", files.Select(file => file.Path), Status, true, token);
            return completed;
        }
        catch (OperationCanceledException)
        {
            Status = $"Retrieval cancelled after {completed:N0}/{files.Count:N0} file(s). Completed copies were kept.";
            await RecordHistoryAsync("Retrieve", files.Take(completed).Select(file => file.Path), Status, false, CancellationToken.None);
            return completed;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            await RecordHistoryAsync("Retrieve", files.Take(completed).Select(file => file.Path), exception.Message, false, CancellationToken.None);
            throw;
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<GitRevisionFileExportResult?> ExportSelectedAsync(string destinationPath)
    {
        GitRevisionFile? file = SelectedListFile;
        if (file is null || !TryBeginOperation("Exporting branch file", file.Path, 1, default, out CancellationToken token)) return null;
        try
        {
            GitRevisionFileExportResult result = await _gitService.MaterializeFileFromRevisionAsync(
                RepositoryPath, file.Path, RevisionReference, destinationPath, RemoteName, true, token);
            Status = result.IsLfsObject
                ? $"LFS file exported{(result.DownloadedLfsObject ? " after targeted retrieval" : string.Empty)}: {destinationPath}"
                : $"File exported: {destinationPath}";
            OperationProgress = new BranchFileOperationProgress("Export complete", file.Path, 1, 1);
            await RecordHistoryAsync("Export", [file.Path], Status, true, token);
            return result;
        }
        catch (OperationCanceledException)
        {
            Status = "Export cancelled.";
            return null;
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<string?> RestoreSelectedToWorkingTreeAsync()
    {
        GitRevisionFile? file = SelectedListFile;
        if (file is null || !TryBeginOperation("Preparing safe restore", file.Path, 3, default, out CancellationToken token)) return null;
        string destination = ResolveInsideRepository(file.Path);
        string stagingRoot = ResolveInsideRepository($".cyrevision/cache/branch-file-restore-stage/{Guid.NewGuid():N}");
        string stagedFile = ResolveInsideDirectory(stagingRoot, file.Path);
        string? backupPath = null;
        try
        {
            OperationProgress = new BranchFileOperationProgress("Retrieving revision", file.Path, 1, 3);
            await _gitService.MaterializeFileFromRevisionAsync(
                RepositoryPath, file.Path, RevisionReference, stagedFile, RemoteName, true, token);
            if (File.Exists(destination))
            {
                OperationProgress = new BranchFileOperationProgress("Backing up working file", file.Path, 2, 3);
                string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                backupPath = ResolveInsideRepository($".cyrevision/backups/branch-file-restore/{stamp}/{file.Path}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(destination, backupPath, overwrite: false);
            }

            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(stagedFile, destination, overwrite: true);
            OperationProgress = new BranchFileOperationProgress("Restore complete", file.Path, 3, 3);
            Status = backupPath is null
                ? $"{file.Path} restored as a working-tree file. It was not staged."
                : $"{file.Path} restored and not staged. Previous file backed up to {Path.GetRelativePath(RepositoryPath, backupPath)}.";
            await RecordHistoryAsync("Restore", [file.Path], Status, true, token);
            return backupPath;
        }
        catch (OperationCanceledException)
        {
            Status = "Restore cancelled before replacing the working file.";
            await RecordHistoryAsync("Restore", [file.Path], Status, false, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            await RecordHistoryAsync("Restore", [file.Path], exception.Message, false, CancellationToken.None);
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
            EndOperation();
        }
    }

    public async Task LoadDiffAsync()
    {
        GitRevisionFile? file = SelectedListFile;
        if (file is null || !TryBeginOperation("Preparing comparison", file.Path, 4, default, out CancellationToken token)) return;
        IsDiffLoading = true;
        ClearDiffPresentation();
        DiffSummary = $"Loading {BranchName} ↔ HEAD for {file.Path}…";
        DiffText = DiffSummary;
        try
        {
            string root = GetCacheRoot("comparisons");
            string baseline = ResolveInsideDirectory(Path.Combine(root, "head"), file.Path);
            string candidate = ResolveInsideDirectory(Path.Combine(root, "branch"), file.Path);
            OperationProgress = new BranchFileOperationProgress("Retrieving current HEAD", file.Path, 1, 4);
            await _gitService.MaterializeFileFromRevisionAsync(RepositoryPath, file.Path, "HEAD", baseline, RemoteName, true, token);
            OperationProgress = new BranchFileOperationProgress("Retrieving selected branch", file.Path, 2, 4);
            await _gitService.MaterializeFileFromRevisionAsync(RepositoryPath, file.Path, RevisionReference, candidate, RemoteName, true, token);
            OperationProgress = new BranchFileOperationProgress("Plugin-aware semantic comparison", file.Path, 3, 4);
            FilePresentationResult? presentation = await _filePresentationService.CreateDiffAsync(
                new FileDiffRequest(RepositoryPath, file.Path, baseline, candidate),
                Path.Combine(root, "artifacts"),
                token);
            if (presentation is not null)
            {
                ApplyDiffPresentation(presentation);
            }
            else
            {
                DiffText = await _gitService.GetComparisonDiffAsync(RepositoryPath, RevisionReference, "HEAD", file.Path, token);
                if (string.IsNullOrWhiteSpace(DiffText)) DiffText = "The selected file is identical in this branch and HEAD.";
                DiffSummary = "Git textual comparison";
            }
            OperationProgress = new BranchFileOperationProgress("Comparison ready", file.Path, 4, 4);
        }
        catch (OperationCanceledException)
        {
            DiffSummary = "Comparison cancelled.";
            DiffText = DiffSummary;
        }
        catch (Exception exception)
        {
            DiffSummary = "Unable to compare the selected file.";
            DiffText = exception.Message;
        }
        finally
        {
            IsDiffLoading = false;
            EndOperation();
        }
    }

    private async Task LoadTreeCoreAsync(CancellationToken token)
    {
        OperationProgress = new BranchFileOperationProgress("Reading Git tree", RevisionReference, 0, 0);
        IReadOnlyList<GitRevisionFile> files = await _gitService.GetRevisionFilesAsync(RepositoryPath, RevisionReference, token);
        OperationProgress = new BranchFileOperationProgress("Building folder index", $"{files.Count:N0} files", 1, 2);
        IReadOnlyList<GitRevisionTreeNode> tree = await Task.Run(() => GitRevisionTreeNode.Build(files), token);
        token.ThrowIfCancellationRequested();
        _allFiles = files;
        TreeRoots = tree;
        ApplyFilter();
        Summary = $"{files.Count:N0} file(s) at {RevisionReference}";
        OperationProgress = new BranchFileOperationProgress("Branch index ready", RevisionReference, 2, 2);
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
            string cacheRoot = GetCacheRoot("previews");
            string cachePath = ResolveInsideDirectory(cacheRoot, file.Path);
            GitRevisionFileExportResult export = await _gitService.MaterializeFileFromRevisionAsync(
                RepositoryPath, file.Path, RevisionReference, cachePath, RemoteName, fetchMissingLfsObject, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentPreview(cancellation, file)) return;
            FilePresentationResult? presentation = await _filePresentationService.CreatePreviewAsync(
                new FilePreviewRequest(RepositoryPath, file.Path, cachePath, export.Size), token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentPreview(cancellation, file)) return;
            if (presentation is not null)
            {
                PreviewSummary = $"{file.Path} · {presentation.Summary} · {presentation.ProviderId}";
                if (presentation.Kind == FilePresentationKind.Image && TryLoadBitmap(presentation.ImagePath) is { } image)
                    PreviewImage = image;
                else
                    PreviewText = string.IsNullOrWhiteSpace(presentation.TextContent) ? presentation.Summary : presentation.TextContent;
                return;
            }

            CodeFilePreview preview = await _codeWorkspaceService.ReadPreviewAsync(cacheRoot, file.Path, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentPreview(cancellation, file)) return;
            PreviewSummary = $"{file.Path} · {preview.Summary}";
            PreviewText = preview.IsBinary
                ? $"No active project plugin provides a preview for {Path.GetExtension(file.Path)} files."
                : preview.Text;
        }
        catch (OperationCanceledException) { }
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

    private void ApplyDiffPresentation(FilePresentationResult presentation)
    {
        DiffSummary = $"{presentation.Summary} · {presentation.ProviderId}";
        DiffText = string.IsNullOrWhiteSpace(presentation.TextContent) ? presentation.Summary : presentation.TextContent;
        DiffBaselineImage = TryLoadBitmap(presentation.BaselineImagePath);
        DiffCandidateImage = TryLoadBitmap(presentation.CandidateImagePath ?? presentation.ImagePath);
        DiffHeatmapImage = TryLoadBitmap(presentation.DifferenceImagePath);
    }

    private void ClearDiffPresentation()
    {
        DiffBaselineImage = null;
        DiffCandidateImage = null;
        DiffHeatmapImage = null;
    }

    private static Bitmap? TryLoadBitmap(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new Bitmap(path) : null;

    private bool IsCurrentPreview(CancellationTokenSource cancellation, GitRevisionFile file) =>
        ReferenceEquals(_previewCancellation, cancellation) && SelectedListFile?.Path == file.Path;

    private IReadOnlyList<GitRevisionFile> GetEffectiveSelection() =>
        _selectedFiles.Count > 0 ? _selectedFiles : SelectedListFile is null ? [] : [SelectedListFile];

    private bool TryBeginOperation(string stage, string detail, int total, CancellationToken externalToken, out CancellationToken token)
    {
        token = default;
        if (IsOperationActive) return false;
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        IsOperationActive = true;
        OperationProgress = new BranchFileOperationProgress(stage, detail, 0, total);
        Status = detail.Length == 0 ? stage : $"{stage}: {detail}";
        token = _operationCancellation.Token;
        return true;
    }

    private void EndOperation()
    {
        IsOperationActive = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    private async Task RecordHistoryAsync(string operation, IEnumerable<string> paths, string result, bool succeeded, CancellationToken cancellationToken)
    {
        BranchFileOperationHistoryItem item = new(DateTimeOffset.Now, operation, BranchName, paths.ToArray(), result, succeeded);
        OperationHistory.Insert(0, item);
        while (OperationHistory.Count > 200) OperationHistory.RemoveAt(OperationHistory.Count - 1);
        await _workspaceStore.SaveHistoryAsync(RepositoryPath, OperationHistory, cancellationToken);
    }

    private static string ResolveInsideDirectory(string root, string relativePath)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("The selected path is outside the destination directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
