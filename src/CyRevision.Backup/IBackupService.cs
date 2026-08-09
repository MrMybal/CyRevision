using CyRevision.Core.Configuration;

namespace CyRevision.Backup;

public sealed record BackupSnapshot(
    Guid SnapshotId,
    Guid ProjectId,
    DateTimeOffset CreatedAt,
    long LogicalSizeBytes,
    long StoredSizeBytes,
    string ManifestHash);

public sealed record BackupFileEntry(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTime,
    string ContentHash);

public sealed record BackupManifest(
    BackupSnapshot Snapshot,
    IReadOnlyList<BackupFileEntry> Files);

public interface IBackupService
{
    Task<BackupSnapshot> CreateSnapshotAsync(
        Guid projectId,
        string sourcePath,
        RetentionPolicy retention,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<BackupManifest> GetManifestAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default);

    Task RestoreFileAsync(
        Guid snapshotId,
        string relativePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task RestoreSnapshotAsync(
        Guid snapshotId,
        string destinationDirectory,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    Task ApplyRetentionAsync(
        Guid projectId,
        RetentionPolicy retention,
        CancellationToken cancellationToken = default);
}
