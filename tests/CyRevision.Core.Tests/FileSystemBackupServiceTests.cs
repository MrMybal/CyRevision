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

    [Fact]
    public async Task ColdArchiveCopiesOldSnapshotsAndDeduplicatedObjectsWithoutDeletingSource()
    {
        string source = Path.Combine(_root, "archive-source");
        string store = Path.Combine(_root, "active-store");
        string archive = Path.Combine(_root, "cold-store");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "asset.bin");
        Guid projectId = Guid.NewGuid();
        FileSystemBackupService service = new(new BackupStoreOptions(store));
        await File.WriteAllTextAsync(file, "first");
        BackupSnapshot first = await service.CreateSnapshotAsync(projectId, source, RetentionPolicy.KeepForever);
        await File.WriteAllTextAsync(file, "second");
        await service.CreateSnapshotAsync(projectId, source, RetentionPolicy.KeepForever);
        await Task.Delay(20);

        ColdArchiveResult result = await new FileSystemColdArchiveService().ArchiveEligibleAsync(
            projectId,
            store,
            new ColdArchivePolicy(archive, TimeSpan.FromMilliseconds(1), MinimumRecentSnapshots: 0));

        Assert.Equal(2, result.ArchivedSnapshots);
        Assert.True(result.CopiedObjects >= 2);
        Assert.Equal(2, (await service.GetSnapshotsAsync(projectId)).Count);
        FileSystemBackupService archivedService = new(new BackupStoreOptions(archive));
        Assert.Equal(2, (await archivedService.GetSnapshotsAsync(projectId)).Count);
        string restore = Path.Combine(_root, "cold-restore");
        await archivedService.RestoreSnapshotAsync(first.SnapshotId, restore);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(restore, "asset.bin")));
    }

    [Fact]
    public async Task ExplicitColdReclaimNeverDeletesObjectsReferencedByAnotherProject()
    {
        string sourceOne = Path.Combine(_root, "shared-source-one");
        string sourceTwo = Path.Combine(_root, "shared-source-two");
        string store = Path.Combine(_root, "shared-active-store");
        string archive = Path.Combine(_root, "shared-cold-store");
        Directory.CreateDirectory(sourceOne);
        Directory.CreateDirectory(sourceTwo);
        await File.WriteAllTextAsync(Path.Combine(sourceOne, "shared.bin"), "shared content");
        await File.WriteAllTextAsync(Path.Combine(sourceTwo, "shared.bin"), "shared content");
        Guid projectOne = Guid.NewGuid();
        Guid projectTwo = Guid.NewGuid();
        FileSystemBackupService service = new(new BackupStoreOptions(store));
        await service.CreateSnapshotAsync(projectOne, sourceOne, RetentionPolicy.KeepForever);
        BackupSnapshot secondProjectSnapshot = await service.CreateSnapshotAsync(projectTwo, sourceTwo, RetentionPolicy.KeepForever);
        await Task.Delay(20);

        ColdArchiveResult result = await new FileSystemColdArchiveService().ArchiveEligibleAsync(
            projectOne,
            store,
            new ColdArchivePolicy(
                archive,
                TimeSpan.FromMilliseconds(1),
                MinimumRecentSnapshots: 0,
                RemoveFromHotStoreAfterVerification: true));

        Assert.Equal(1, result.RemovedHotSnapshots);
        Assert.Empty(await service.GetSnapshotsAsync(projectOne));
        Assert.Single(await service.GetSnapshotsAsync(projectTwo));
        string restore = Path.Combine(_root, "shared-restore");
        await service.RestoreSnapshotAsync(secondProjectSnapshot.SnapshotId, restore);
        Assert.Equal("shared content", await File.ReadAllTextAsync(Path.Combine(restore, "shared.bin")));
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
