using System.Text.Json;

namespace CyRevision.Backup;

public sealed record ColdArchivePolicy(
    string ArchivePath,
    TimeSpan ArchiveAfter,
    int MinimumRecentSnapshots = 5)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ArchivePath);
        if (!Path.IsPathFullyQualified(ArchivePath))
        {
            throw new InvalidOperationException("The cold archive path must be absolute.");
        }

        if (ArchiveAfter <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The archive age must be greater than zero.");
        }

        if (MinimumRecentSnapshots < 0)
        {
            throw new InvalidOperationException("The recent snapshot count cannot be negative.");
        }
    }
}

public sealed record ColdArchiveResult(
    int EligibleSnapshots,
    int ArchivedSnapshots,
    int ExistingSnapshots,
    int CopiedObjects,
    long CopiedBytes);

public interface IColdArchiveService
{
    Task<ColdArchiveResult> ArchiveEligibleAsync(
        Guid projectId,
        string sourceStorePath,
        ColdArchivePolicy policy,
        CancellationToken cancellationToken = default);
}

public sealed class FileSystemColdArchiveService : IColdArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ColdArchiveResult> ArchiveEligibleAsync(
        Guid projectId,
        string sourceStorePath,
        ColdArchivePolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceStorePath);
        policy.Validate();
        string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceStorePath));
        string archiveRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(policy.ArchivePath));
        if (string.Equals(sourceRoot, archiveRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The active backup store and cold archive must be different locations.");
        }

        FileSystemBackupService source = new(new BackupStoreOptions(sourceRoot));
        IReadOnlyList<BackupSnapshot> snapshots = await source.GetSnapshotsAsync(projectId, cancellationToken);
        DateTimeOffset threshold = DateTimeOffset.UtcNow - policy.ArchiveAfter;
        BackupSnapshot[] eligible = snapshots
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .Skip(policy.MinimumRecentSnapshots)
            .Where(snapshot => snapshot.CreatedAt <= threshold)
            .ToArray();
        int archived = 0;
        int existing = 0;
        int copiedObjects = 0;
        long copiedBytes = 0;
        foreach (BackupSnapshot snapshot in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackupManifest manifest = await source.GetManifestAsync(snapshot.SnapshotId, cancellationToken);
            foreach (BackupFileEntry entry in manifest.Files)
            {
                string sourceObject = Path.Combine(sourceRoot, "objects", entry.ContentHash[..2], entry.ContentHash);
                string archiveObject = Path.Combine(archiveRoot, "objects", entry.ContentHash[..2], entry.ContentHash);
                if (File.Exists(archiveObject))
                {
                    continue;
                }

                if (!File.Exists(sourceObject))
                {
                    throw new InvalidDataException($"Backup object '{entry.ContentHash}' is missing from the active store.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(archiveObject)!);
                string temporaryObject = archiveObject + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await CopyFileAsync(sourceObject, temporaryObject, cancellationToken);
                    if (!File.Exists(archiveObject))
                    {
                        File.Move(temporaryObject, archiveObject);
                        copiedObjects++;
                        copiedBytes += entry.Length;
                    }
                }
                finally
                {
                    File.Delete(temporaryObject);
                }
            }

            string archiveManifest = Path.Combine(
                archiveRoot,
                "manifests",
                projectId.ToString("N"),
                snapshot.SnapshotId.ToString("N") + ".json");
            bool alreadyArchived = File.Exists(archiveManifest);
            Directory.CreateDirectory(Path.GetDirectoryName(archiveManifest)!);
            string temporaryManifest = archiveManifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream output = new(temporaryManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(output, manifest, JsonOptions, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }
                File.Move(temporaryManifest, archiveManifest, true);
            }
            finally
            {
                File.Delete(temporaryManifest);
            }

            if (alreadyArchived)
            {
                existing++;
            }
            else
            {
                archived++;
            }
        }

        return new ColdArchiveResult(eligible.Length, archived, existing, copiedObjects, copiedBytes);
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }
}
