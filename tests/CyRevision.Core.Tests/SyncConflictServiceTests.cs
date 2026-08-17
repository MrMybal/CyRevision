using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncConflictServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionConflictTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolveWithConflictVersionCanRestoreExactPreviousState()
    {
        string source = Path.Combine(_root, "source");
        string backupRoot = Path.Combine(_root, "recovery");
        string folder = Path.Combine(source, "Content");
        Directory.CreateDirectory(folder);
        string original = Path.Combine(folder, "Test.uasset");
        string conflict = Path.Combine(folder, "Test.sync-conflict-20260817-120000-DEVICE.uasset");
        await File.WriteAllTextAsync(original, "original version");
        await File.WriteAllTextAsync(conflict, "conflict version");
        SyncConflictService service = new(backupRoot);
        Guid projectId = Guid.NewGuid();

        SyncConflictItem detected = Assert.Single(await service.ScanAsync([new SyncConflictScope("Project", source)]));
        SyncConflictBackup backup = await service.ResolveAsync(
            projectId,
            detected,
            SyncConflictResolution.UseConflict,
            30);

        Assert.Equal("conflict version", await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(conflict));
        Assert.Equal(backup.Id, Assert.Single(await service.LoadBackupsAsync(projectId)).Id);

        await service.RestoreAsync(backup);

        Assert.Equal("original version", await File.ReadAllTextAsync(original));
        Assert.Equal("conflict version", await File.ReadAllTextAsync(conflict));
        Assert.NotNull(Assert.Single(await service.LoadBackupsAsync(projectId)).RestoredAt);
    }

    [Fact]
    public async Task KeepingOriginalArchivesAndRemovesOnlyConflictCopy()
    {
        string source = Path.Combine(_root, "second-source");
        Directory.CreateDirectory(source);
        string original = Path.Combine(source, "notes.txt");
        string conflict = Path.Combine(source, "notes.sync-conflict-20260817-120000-DEVICE.txt");
        await File.WriteAllTextAsync(original, "keep me");
        await File.WriteAllTextAsync(conflict, "other copy");
        SyncConflictService service = new(Path.Combine(_root, "second-recovery"));

        SyncConflictItem detected = Assert.Single(await service.ScanAsync([new SyncConflictScope("Shared", source)]));
        await service.ResolveAsync(Guid.NewGuid(), detected, SyncConflictResolution.KeepOriginal, 7);

        Assert.Equal("keep me", await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(conflict));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
