using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Backup;

public sealed record ColdArchivePolicy(
    string ArchivePath,
    TimeSpan ArchiveAfter,
    int MinimumRecentSnapshots = 5,
    bool RemoveFromHotStoreAfterVerification = false)
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
    long CopiedBytes,
    int RemovedHotSnapshots = 0,
    int RemovedHotObjects = 0,
    long ReclaimedHotBytes = 0);

public sealed record BackupArchiveProfile(
    string Id,
    string Name,
    string Description,
    int ArchiveAfterDays,
    int MinimumRecentSnapshots,
    bool RemoveFromHotStoreAfterVerification = false)
{
    public static IReadOnlyList<BackupArchiveProfile> BuiltIn { get; } =
    [
        new("safe", "Safe cold copy", "Copy snapshots older than 180 days to verified cold storage. Hot data is never removed.", 180, 10),
        new("balanced", "Balanced cold copy", "Copy snapshots older than 90 days and keep at least 5 recent snapshots hot. Hot data is never removed.", 90, 5),
        new("space", "Space saver (opt-in)", "Copy snapshots older than 30 days. Removing verified hot copies is a separate explicit option and is off by default.", 30, 2)
    ];
}

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
        int removedHotSnapshots = 0;
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
            if (policy.RemoveFromHotStoreAfterVerification)
            {
                VerifyArchivedManifest(archiveRoot, manifest);
                string hotManifest = Path.Combine(sourceRoot, "manifests", projectId.ToString("N"), snapshot.SnapshotId.ToString("N") + ".json");
                if (File.Exists(hotManifest))
                {
                    File.Delete(hotManifest);
                    removedHotSnapshots++;
                }
            }
        }
        (int removedObjects, long reclaimedBytes) = policy.RemoveFromHotStoreAfterVerification
            ? await RemoveUnreferencedHotObjectsAsync(sourceRoot, cancellationToken)
            : (0, 0L);
        return new ColdArchiveResult(
            eligible.Length, archived, existing, copiedObjects, copiedBytes,
            removedHotSnapshots, removedObjects, reclaimedBytes);
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static void VerifyArchivedManifest(string archiveRoot, BackupManifest manifest)
    {
        string manifestPath = Path.Combine(archiveRoot, "manifests", manifest.Snapshot.ProjectId.ToString("N"), manifest.Snapshot.SnapshotId.ToString("N") + ".json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("The cold archive manifest could not be verified.");
        foreach (BackupFileEntry entry in manifest.Files)
        {
            string objectPath = Path.Combine(archiveRoot, "objects", entry.ContentHash[..2], entry.ContentHash);
            if (!File.Exists(objectPath) || new FileInfo(objectPath).Length != entry.Length)
                throw new InvalidDataException($"Cold archive object '{entry.ContentHash}' could not be verified.");
            using FileStream stream = new(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actualHash.Equals(entry.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Cold archive object '{entry.ContentHash}' failed SHA-256 verification.");
        }
    }

    private static async Task<(int Removed, long Bytes)> RemoveUnreferencedHotObjectsAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        HashSet<string> retained = new(StringComparer.OrdinalIgnoreCase);
        // Backup objects are content-addressed and may be shared by several projects.
        // Scan every remaining hot manifest before removing one object; limiting the
        // scan to the project being archived could corrupt another project's backup.
        string manifestDirectory = Path.Combine(sourceRoot, "manifests");
        if (Directory.Exists(manifestDirectory))
        {
            foreach (string manifestPath in Directory.EnumerateFiles(
                         manifestDirectory, "*.json", SearchOption.AllDirectories))
            {
                await using FileStream stream = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                BackupManifest? manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken);
                if (manifest is not null) retained.UnionWith(manifest.Files.Select(file => file.ContentHash));
            }
        }
        int removed = 0;
        long bytes = 0;
        string objectRoot = Path.Combine(sourceRoot, "objects");
        if (!Directory.Exists(objectRoot)) return (0, 0);
        foreach (string objectPath in Directory.EnumerateFiles(objectRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string hash = Path.GetFileName(objectPath);
            if (retained.Contains(hash)) continue;
            long length = new FileInfo(objectPath).Length;
            File.Delete(objectPath);
            removed++;
            bytes += length;
        }
        return (removed, bytes);
    }
}
