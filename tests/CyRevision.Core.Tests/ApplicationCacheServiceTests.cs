using CyRevision.Desktop.SystemIntegration;

namespace CyRevision.Core.Tests;

public sealed class ApplicationCacheServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "cyrevision-cache-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Measure_and_purge_only_manage_items_inside_cache_root()
    {
        string cache = Path.Combine(_root, "cache");
        string nested = Path.Combine(cache, "previews");
        Directory.CreateDirectory(nested);
        await File.WriteAllBytesAsync(Path.Combine(cache, "one.bin"), new byte[128]);
        await File.WriteAllBytesAsync(Path.Combine(nested, "two.bin"), new byte[256]);

        ApplicationCacheUsage usage = await ApplicationCacheService.MeasureAsync(cache);
        ApplicationCacheOperationResult result = await ApplicationCacheService.PurgeAsync(cache);

        Assert.Equal(384, usage.Bytes);
        Assert.Equal(2, usage.Files);
        Assert.True(result.Succeeded);
        Assert.Equal(384, result.Bytes);
        Assert.True(Directory.Exists(cache));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cache));
    }

    [Fact]
    public void Complete_pending_move_verifies_destination_before_removing_source_files()
    {
        string source = Path.Combine(_root, "old-cache");
        string destination = Path.Combine(_root, "new-cache");
        string relative = Path.Combine("git", "objects", "sample.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(source, relative))!);
        File.WriteAllBytes(Path.Combine(source, relative), new byte[] { 1, 2, 3, 4 });
        ApplicationPreferences preferences = ApplicationPreferences.Default with
        {
            CacheDirectory = destination,
            PendingCacheMoveSource = source
        };

        ApplicationPreferences completed = ApplicationCacheService.CompletePendingMove(
            preferences,
            Path.Combine(_root, "fallback"),
            out ApplicationCacheOperationResult result);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, completed.PendingCacheMoveSource);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(destination, relative)));
        Assert.False(File.Exists(Path.Combine(source, relative)));
    }

    [Fact]
    public void Drive_root_is_never_accepted_as_a_cache()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(_root))!;

        Assert.Throws<IOException>(() => ApplicationCacheService.NormalizeSafeRoot(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}