using CyRevision.Backup;
using CyRevision.Core.Configuration;

namespace CyRevision.Core.Tests;

public sealed class FileSystemBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionBackupTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SnapshotsAreDeduplicatedListedAndRestored()
    {
        string source = Path.Combine(_root, "source");
        string store = Path.Combine(_root, "store");
        Directory.CreateDirectory(Path.Combine(source, "Content"));
        Directory.CreateDirectory(Path.Combine(source, "Intermediate"));
        await File.WriteAllTextAsync(Path.Combine(source, "Content", "Asset.txt"), "version one");
        await File.WriteAllTextAsync(Path.Combine(source, "Shared.txt"), "unchanged");
        await File.WriteAllTextAsync(Path.Combine(source, "Intermediate", "cache.bin"), "ignored");

        Guid projectId = Guid.NewGuid();
        FileSystemBackupService service = new(new BackupStoreOptions(store));
        BackupSnapshot first = await service.CreateSnapshotAsync(projectId, source, RetentionPolicy.KeepForever);

        await File.WriteAllTextAsync(Path.Combine(source, "Content", "Asset.txt"), "version two");
        BackupSnapshot second = await service.CreateSnapshotAsync(projectId, source, RetentionPolicy.KeepForever);
        IReadOnlyList<BackupSnapshot> snapshots = await service.GetSnapshotsAsync(projectId);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(second.SnapshotId, snapshots[0].SnapshotId);
        Assert.True(second.StoredSizeBytes < second.LogicalSizeBytes);

        string restore = Path.Combine(_root, "restore");
        await service.RestoreSnapshotAsync(first.SnapshotId, restore);
        Assert.Equal("version one", await File.ReadAllTextAsync(Path.Combine(restore, "Content", "Asset.txt")));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(Path.Combine(restore, "Shared.txt")));
        Assert.False(File.Exists(Path.Combine(restore, "Intermediate", "cache.bin")));
    }

    [Fact]
    public async Task CurrentStateRetentionKeepsOnlyNewestSnapshot()
    {
        string source = Path.Combine(_root, "retention-source");
        string store = Path.Combine(_root, "retention-store");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "data.txt");
        Guid projectId = Guid.NewGuid();
        FileSystemBackupService service = new(new BackupStoreOptions(store));

        await File.WriteAllTextAsync(file, "old");
        BackupSnapshot oldSnapshot = await service.CreateSnapshotAsync(projectId, source, RetentionPolicy.KeepForever);
        await File.WriteAllTextAsync(file, "new");
        BackupSnapshot currentSnapshot = await service.CreateSnapshotAsync(projectId, source, RetentionPolicy.CurrentStateOnly);

        IReadOnlyList<BackupSnapshot> snapshots = await service.GetSnapshotsAsync(projectId);
        Assert.Single(snapshots);
        Assert.Equal(currentSnapshot.SnapshotId, snapshots[0].SnapshotId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetManifestAsync(oldSnapshot.SnapshotId));
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
