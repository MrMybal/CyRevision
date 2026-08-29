using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void ReportFileHistoryIssue(string message)
    {
        StatusMessage = message;
    }

    public Task<IReadOnlyList<GitFileRevision>> LoadProjectFileHistoryAsync(
        ProjectItemViewModel project,
        string relativePath,
        int maximumCount = 300,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Definition.Features.GitEnabled)
        {
            throw new InvalidOperationException("Git is not active for this project.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A file path is required.", nameof(relativePath));
        }

        string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        string cacheKey = $"history:{project.Id}:{normalizedPath}";
        if (!forceRefresh &&
            _fileHistoryCache.TryGetValue(cacheKey, out IReadOnlyList<GitFileRevision>? cachedHistory))
        {
            return Task.FromResult(cachedHistory);
        }

        return LoadAndCacheProjectFileHistoryAsync(
            project,
            normalizedPath,
            Math.Clamp(maximumCount, 1, 1_000),
            cacheKey,
            cancellationToken);
    }

    private async Task<IReadOnlyList<GitFileRevision>> LoadAndCacheProjectFileHistoryAsync(
        ProjectItemViewModel project,
        string normalizedPath,
        int maximumCount,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GitFileRevision> history = await _gitService.GetFileHistoryAsync(
            project.RootPath,
            normalizedPath,
            maximumCount,
            cancellationToken);
        _fileHistoryCache.Set(cacheKey, history);
        return history;
    }
}