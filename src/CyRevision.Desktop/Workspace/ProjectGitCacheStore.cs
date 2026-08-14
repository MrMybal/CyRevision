using System.Text.Json;
using CyRevision.Git;

namespace CyRevision.Desktop.Workspace;

internal sealed record ProjectGitCacheSnapshot(
    int FormatVersion,
    DateTimeOffset CapturedAt,
    GitRepositoryStatus Status,
    IReadOnlyList<GitBranch> Branches,
    IReadOnlyList<GitRevision> History,
    IReadOnlyList<LfsTrackedPattern> LfsPatterns)
{
    public const int CurrentFormatVersion = 1;
}

internal sealed class ProjectGitCacheStore
{
    private const long MaximumCacheBytes = 64L * 1024 * 1024;
    private const string CacheDirectoryName = ".cyrevision";
    private const string CacheFileName = "git-state-v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProjectGitCacheSnapshot?> LoadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        string path = GetCachePath(repositoryRoot);
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumCacheBytes) return null;
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ProjectGitCacheSnapshot? snapshot = await JsonSerializer.DeserializeAsync<ProjectGitCacheSnapshot>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return snapshot?.FormatVersion == ProjectGitCacheSnapshot.CurrentFormatVersion ? snapshot : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        string repositoryRoot,
        GitRepositoryStatus status,
        IReadOnlyList<GitBranch> branches,
        IReadOnlyList<GitRevision> history,
        IReadOnlyList<LfsTrackedPattern> lfsPatterns,
        CancellationToken cancellationToken = default)
    {
        string cacheDirectory = GetCacheDirectory(repositoryRoot);
        Directory.CreateDirectory(cacheDirectory);
        await EnsurePrivateIgnoreFileAsync(cacheDirectory, cancellationToken).ConfigureAwait(false);
        string destination = Path.Combine(cacheDirectory, "cache", CacheFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ProjectGitCacheSnapshot snapshot = new(
                ProjectGitCacheSnapshot.CurrentFormatVersion,
                DateTimeOffset.UtcNow,
                status,
                branches,
                history,
                lfsPatterns);
            await using (FileStream stream = new(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (new FileInfo(temporary).Length > MaximumCacheBytes) return;
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task EnsurePrivateIgnoreFileAsync(
        string cacheDirectory,
        CancellationToken cancellationToken)
    {
        string ignorePath = Path.Combine(cacheDirectory, ".gitignore");
        if (File.Exists(ignorePath)) return;
        await File.WriteAllTextAsync(ignorePath, "*\n", cancellationToken).ConfigureAwait(false);
    }

    private static string GetCachePath(string repositoryRoot) => Path.Combine(
        GetCacheDirectory(repositoryRoot),
        "cache",
        CacheFileName);

    private static string GetCacheDirectory(string repositoryRoot) => Path.Combine(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot)),
        CacheDirectoryName);
}
