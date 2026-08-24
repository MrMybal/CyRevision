using Avalonia.Media.Imaging;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
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
}
