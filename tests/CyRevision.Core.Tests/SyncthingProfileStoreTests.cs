using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncthingProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionProfileTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProfileIsIsolatedPersistentAndKeepsItsSecret()
    {
        string exchange = Path.Combine(_root, "project");
        string executable = Path.Combine(_root, "tools", "syncthing.exe");
        Directory.CreateDirectory(exchange);
        JsonSyncthingProfileStore store = new(Path.Combine(_root, "managed"));
        Guid projectId = Guid.NewGuid();

        SyncthingProfile created = await store.CreateOrUpdateAsync(projectId, executable, exchange);
        SyncthingProfile updated = await store.CreateOrUpdateAsync(projectId, executable, exchange);
        SyncthingProfile? loaded = await store.GetAsync(projectId);

        Assert.NotNull(loaded);
        Assert.Equal(created.ApiEndpoint, updated.ApiEndpoint);
        Assert.Equal(created.ApiKey, updated.ApiKey);
        Assert.Equal(created.ListenPort, updated.ListenPort);
        Assert.NotEqual(created.ApiEndpoint.Port, created.ListenPort);
        Assert.Equal(created, loaded);
        created.ToIsolationOptions().Validate();
        Assert.NotEqual(Path.GetFullPath(exchange), Path.GetFullPath(created.ConfigurationDirectory));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, true);
    }
}
