using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionSyncHistoryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HistoryIsPersistentNewestFirstAndSearchable()
    {
        JsonLineSyncHistoryStore store = new(_root);
        Guid projectId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await store.AppendManyAsync([
            new SyncHistoryEntry(Guid.NewGuid(), now.AddMinutes(-1), projectId, "Builds", "Windows/app.zip", "Difference observed", "Incoming", "42 bytes"),
            new SyncHistoryEntry(Guid.NewGuid(), now, projectId, "Project folder", "Source/Game.cpp", "Difference observed", "Local change", "120 bytes")
        ]);

        IReadOnlyList<SyncHistoryEntry> all = await store.SearchAsync(projectId);
        IReadOnlyList<SyncHistoryEntry> byText = await store.SearchAsync(projectId, "incoming");
        IReadOnlyList<SyncHistoryEntry> byPath = await store.SearchAsync(projectId, pathFilter: "Source/Game.cpp");

        Assert.Equal(2, all.Count);
        Assert.Equal("Source/Game.cpp", all[0].Path);
        Assert.Equal("Windows/app.zip", Assert.Single(byText).Path);
        Assert.Equal("Project folder", Assert.Single(byPath).Scope);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
