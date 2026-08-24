using System.Text.Json;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Services;

public sealed class BranchFileWorkspaceStore
{
    private const int MaximumHistoryItems = 200;
    private static readonly TimeSpan CacheMaximumAge = TimeSpan.FromDays(14);
    private const long CacheMaximumBytes = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<BranchFileOperationHistoryItem>> LoadHistoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string path = GetHistoryPath(repositoryPath);
        if (!File.Exists(path)) return [];
        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<BranchFileOperationHistoryItem>>(
                       stream,
                       JsonOptions,
                       cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task SaveHistoryAsync(
        string repositoryPath,
        IEnumerable<BranchFileOperationHistoryItem> history,
        CancellationToken cancellationToken = default)
    {
        string path = GetHistoryPath(repositoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    history.Take(MaximumHistoryItems).ToArray(),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public Task CleanupCacheAsync(
        string repositoryPath,
        string? protectedDirectory = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CleanupCache(repositoryPath, protectedDirectory, cancellationToken), cancellationToken);

    private static void CleanupCache(
        string repositoryPath,
        string? protectedDirectory,
        CancellationToken cancellationToken)
    {
        string root = Path.Combine(repositoryPath, ".cyrevision", "cache", "branch-files");
        if (!Directory.Exists(root)) return;
        string? protectedPath = string.IsNullOrWhiteSpace(protectedDirectory)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedDirectory));
        DateTime cutoff = DateTime.UtcNow - CacheMaximumAge;
        List<FileInfo> retained = [];

        foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = new(filePath);
            if (IsInside(file.FullName, protectedPath) || file.LastWriteTimeUtc >= cutoff)
            {
                retained.Add(file);
                continue;
            }

            TryDelete(file.FullName);
        }

        long retainedBytes = retained.Sum(file => file.Exists ? file.Length : 0);
        foreach (FileInfo file in retained
                     .Where(file => !IsInside(file.FullName, protectedPath))
                     .OrderBy(file => file.LastAccessTimeUtc))
        {
            if (retainedBytes <= CacheMaximumBytes) break;
            cancellationToken.ThrowIfCancellationRequested();
            long length = file.Exists ? file.Length : 0;
            TryDelete(file.FullName);
            retainedBytes -= length;
        }

        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string GetHistoryPath(string repositoryPath) =>
        Path.Combine(repositoryPath, ".cyrevision", "history", "branch-file-operations.json");

    private static bool IsInside(string path, string? directory) =>
        directory is not null && Path.GetFullPath(path).StartsWith(
            directory + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
