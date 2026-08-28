using CyRevision.Desktop.Services;

namespace CyRevision.Core.Tests;

public sealed class BranchFileWorkspaceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-branch-cache-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Cleanup_uses_explicit_cache_root_and_preserves_active_inspection()
    {
        string cacheRoot = Path.Combine(_root, "global-cache", "branch-files", "repository");
        string expiredDirectory = Path.Combine(cacheRoot, "expired");
        string protectedDirectory = Path.Combine(cacheRoot, "active");
        Directory.CreateDirectory(expiredDirectory);
        Directory.CreateDirectory(protectedDirectory);
        string expired = Path.Combine(expiredDirectory, "old.bin");
        string active = Path.Combine(protectedDirectory, "active.bin");
        File.WriteAllText(expired, "old");
        File.WriteAllText(active, "active");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-30));

        await new BranchFileWorkspaceStore().CleanupCacheAsync(cacheRoot, protectedDirectory);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(active));
        Assert.False(Directory.Exists(Path.Combine(_root, ".cyrevision")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}