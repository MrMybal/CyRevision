using CyRevision.Core.Configuration;

namespace CyRevision.Backup;

public sealed record BackupSnapshot(
    Guid SnapshotId,
    Guid ProjectId,
    DateTimeOffset CreatedAt,
    long LogicalSizeBytes,
    long StoredSizeBytes,
    string ManifestHash);

public interface IBackupService
{
    Task<BackupSnapshot> CreateSnapshotAsync(
        Guid projectId,
        string sourcePath,
        RetentionPolicy retention,
        CancellationToken cancellationToken = default);

    Task RestoreFileAsync(
        Guid snapshotId,
        string relativePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

