using System.Security.Cryptography;
using System.Text.Json;
using CyRevision.Core.Configuration;

namespace CyRevision.Backup;

public sealed class FileSystemBackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BackupStoreOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSystemBackupService(BackupStoreOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StorePath);
        _options = options with { StorePath = Path.GetFullPath(options.StorePath) };
    }

    public async Task<BackupSnapshot> CreateSnapshotAsync(
        Guid projectId,
        string sourcePath,
        RetentionPolicy retention,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        retention.Validate();
        string sourceRoot = Path.GetFullPath(sourcePath);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(sourceRoot);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(ObjectsPath);
            Directory.CreateDirectory(ManifestsPath);

            Guid snapshotId = Guid.NewGuid();
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            List<BackupFileEntry> entries = [];
            long logicalSize = 0;
            long storedSize = 0;

            foreach (string filePath in EnumerateSourceFiles(sourceRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo file = new(filePath);
                string contentHash = await ComputeHashAsync(filePath, cancellationToken);
                string objectPath = GetObjectPath(contentHash);

                logicalSize += file.Length;
                if (!File.Exists(objectPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
                    string temporaryPath = objectPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await CopyFileAsync(filePath, temporaryPath, cancellationToken);
                        if (!File.Exists(objectPath))
                        {
                            File.Move(temporaryPath, objectPath);
                            storedSize += file.Length;
                        }
                    }
                    finally
                    {
                        File.Delete(temporaryPath);
                    }
                }

                entries.Add(new BackupFileEntry(
                    NormalizeRelativePath(Path.GetRelativePath(sourceRoot, filePath)),
                    file.Length,
                    file.LastWriteTimeUtc,
                    contentHash));
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
            BackupSnapshot provisional = new(
                snapshotId,
                projectId,
                createdAt,
                logicalSize,
                storedSize,
                string.Empty);
            BackupManifest provisionalManifest = new(provisional, entries);
            byte[] provisionalBytes = JsonSerializer.SerializeToUtf8Bytes(provisionalManifest, JsonOptions);
            string manifestHash = Convert.ToHexString(SHA256.HashData(provisionalBytes)).ToLowerInvariant();
            BackupSnapshot snapshot = provisional with { ManifestHash = manifestHash };
            BackupManifest manifest = provisionalManifest with { Snapshot = snapshot };

            string projectManifestDirectory = GetProjectManifestDirectory(projectId);
            Directory.CreateDirectory(projectManifestDirectory);
            await WriteJsonAtomicallyAsync(GetManifestPath(projectId, snapshotId), manifest, cancellationToken);
            await ApplyRetentionCoreAsync(projectId, retention, cancellationToken);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<BackupManifest> manifests = await ReadProjectManifestsAsync(projectId, cancellationToken);
            return manifests
                .Select(manifest => manifest.Snapshot)
                .OrderByDescending(snapshot => snapshot.CreatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupManifest> GetManifestAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await FindManifestAsync(snapshotId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreFileAsync(
        Guid snapshotId,
        string relativePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            BackupManifest manifest = await FindManifestAsync(snapshotId, cancellationToken);
            string normalizedPath = NormalizeRelativePath(relativePath);
            BackupFileEntry entry = manifest.Files.FirstOrDefault(file =>
                string.Equals(file.RelativePath, normalizedPath, StringComparison.Ordinal))
                ?? throw new FileNotFoundException($"The snapshot does not contain '{relativePath}'.", relativePath);
            await RestoreEntryAsync(entry, Path.GetFullPath(destinationPath), overwrite: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreSnapshotAsync(
        Guid snapshotId,
        string destinationDirectory,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            BackupManifest manifest = await FindManifestAsync(snapshotId, cancellationToken);
            Directory.CreateDirectory(destinationRoot);
            foreach (BackupFileEntry entry in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destinationPath = ResolveContainedPath(destinationRoot, entry.RelativePath);
                await RestoreEntryAsync(entry, destinationPath, overwrite, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyRetentionAsync(
        Guid projectId,
        RetentionPolicy retention,
        CancellationToken cancellationToken = default)
    {
        retention.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await ApplyRetentionCoreAsync(projectId, retention, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ObjectsPath => Path.Combine(_options.StorePath, "objects");

    private string ManifestsPath => Path.Combine(_options.StorePath, "manifests");

    private IEnumerable<string> EnumerateSourceFiles(string sourceRoot)
    {
        Stack<string> directories = new();
        directories.Push(sourceRoot);
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            foreach (string childDirectory in Directory.EnumerateDirectories(directory))
            {
                if (!_options.EffectiveExcludedDirectoryNames.Contains(Path.GetFileName(childDirectory)) &&
                    !PathsEqual(childDirectory, _options.StorePath))
                {
                    directories.Push(childDirectory);
                }
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }
        }
    }

    private async Task ApplyRetentionCoreAsync(
        Guid projectId,
        RetentionPolicy retention,
        CancellationToken cancellationToken)
    {
        List<BackupManifest> manifests = (await ReadProjectManifestsAsync(projectId, cancellationToken))
            .OrderByDescending(manifest => manifest.Snapshot.CreatedAt)
            .ToList();
        if (manifests.Count <= 1 || retention.Mode == RetentionMode.Permanent)
        {
            return;
        }

        HashSet<Guid> keep = manifests.Select(manifest => manifest.Snapshot.SnapshotId).ToHashSet();
        if (retention.Mode == RetentionMode.CurrentStateOnly)
        {
            keep = [manifests[0].Snapshot.SnapshotId];
        }

        if (retention.Mode == RetentionMode.LimitedVersions && retention.MaxVersionsPerFile is { } maxVersions)
        {
            keep.IntersectWith(manifests.Take(maxVersions).Select(manifest => manifest.Snapshot.SnapshotId));
        }

        if (retention.MaximumAge is { } maximumAge)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - maximumAge;
            keep.IntersectWith(manifests
                .Where((manifest, index) => index == 0 || manifest.Snapshot.CreatedAt >= cutoff)
                .Select(manifest => manifest.Snapshot.SnapshotId));
        }

        if (retention.StorageBudgetBytes is { } budget)
        {
            long accumulated = 0;
            HashSet<Guid> withinBudget = [];
            foreach (BackupManifest manifest in manifests)
            {
                if (withinBudget.Count == 0 || accumulated + manifest.Snapshot.StoredSizeBytes <= budget)
                {
                    withinBudget.Add(manifest.Snapshot.SnapshotId);
                    accumulated += manifest.Snapshot.StoredSizeBytes;
                }
            }

            keep.IntersectWith(withinBudget);
        }

        keep.Add(manifests[0].Snapshot.SnapshotId);
        foreach (BackupManifest manifest in manifests.Where(manifest => !keep.Contains(manifest.Snapshot.SnapshotId)))
        {
            File.Delete(GetManifestPath(projectId, manifest.Snapshot.SnapshotId));
        }

        await GarbageCollectObjectsAsync(cancellationToken);
    }

    private async Task GarbageCollectObjectsAsync(CancellationToken cancellationToken)
    {
        HashSet<string> referencedHashes = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(ManifestsPath))
        {
            foreach (string manifestPath in Directory.EnumerateFiles(ManifestsPath, "*.json", SearchOption.AllDirectories))
            {
                BackupManifest? manifest = await ReadManifestAsync(manifestPath, cancellationToken);
                if (manifest is not null)
                {
                    referencedHashes.UnionWith(manifest.Files.Select(file => file.ContentHash));
                }
            }
        }

        if (!Directory.Exists(ObjectsPath))
        {
            return;
        }

        foreach (string objectPath in Directory.EnumerateFiles(ObjectsPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!referencedHashes.Contains(Path.GetFileName(objectPath)))
            {
                File.Delete(objectPath);
            }
        }
    }

    private async Task<BackupManifest> FindManifestAsync(Guid snapshotId, CancellationToken cancellationToken)
    {
        if (snapshotId == Guid.Empty || !Directory.Exists(ManifestsPath))
        {
            throw new KeyNotFoundException($"Snapshot '{snapshotId}' was not found.");
        }

        string fileName = snapshotId.ToString("N") + ".json";
        string? path = Directory.EnumerateFiles(ManifestsPath, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (path is null)
        {
            throw new KeyNotFoundException($"Snapshot '{snapshotId}' was not found.");
        }

        return await ReadManifestAsync(path, cancellationToken)
               ?? throw new InvalidDataException($"Snapshot manifest '{path}' is invalid.");
    }

    private async Task<IReadOnlyList<BackupManifest>> ReadProjectManifestsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        string directory = GetProjectManifestDirectory(projectId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<BackupManifest> manifests = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            BackupManifest? manifest = await ReadManifestAsync(path, cancellationToken);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    private static async Task<BackupManifest?> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken);
    }

    private async Task RestoreEntryAsync(
        BackupFileEntry entry,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        string objectPath = GetObjectPath(entry.ContentHash);
        if (!File.Exists(objectPath))
        {
            throw new InvalidDataException($"Backup object '{entry.ContentHash}' is missing.");
        }

        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"The destination file already exists: {destinationPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await CopyFileAsync(objectPath, destinationPath, cancellationToken);
        File.SetLastWriteTimeUtc(destinationPath, entry.LastWriteTime.UtcDateTime);
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string GetProjectManifestDirectory(Guid projectId) =>
        Path.Combine(ManifestsPath, projectId.ToString("N"));

    private string GetManifestPath(Guid projectId, Guid snapshotId) =>
        Path.Combine(GetProjectManifestDirectory(projectId), snapshotId.ToString("N") + ".json");

    private string GetObjectPath(string hash) => Path.Combine(ObjectsPath, hash[..2], hash);

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveContainedPath(string root, string relativePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string destinationPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!destinationPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The backup entry escapes the restore directory: {relativePath}");
        }

        return destinationPath;
    }
}
