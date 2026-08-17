using System.Text.Json;

namespace CyRevision.Sync;

public sealed record SyncConflictScope(string Name, string RootPath);

public sealed record SyncConflictItem(
    Guid Id,
    string Scope,
    string RootPath,
    string RelativeConflictPath,
    string RelativeOriginalPath,
    DateTimeOffset ModifiedAt,
    long ConflictSize,
    long OriginalSize,
    bool OriginalExists)
{
    public string FileName => Path.GetFileName(RelativeOriginalPath);
    public string Folder => Path.GetDirectoryName(RelativeOriginalPath)?.Replace('\\', '/') ?? string.Empty;
    public string Status => OriginalExists ? "Original and conflict available" : "Original missing";
}

public enum SyncConflictResolution
{
    KeepOriginal,
    UseConflict
}

public sealed record SyncConflictBackup(
    Guid Id,
    Guid ProjectId,
    string Scope,
    string RootPath,
    string RelativeConflictPath,
    string RelativeOriginalPath,
    SyncConflictResolution Resolution,
    bool OriginalExisted,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RestoredAt = null)
{
    public string FileName => Path.GetFileName(RelativeOriginalPath);
    public string RetentionStatus => ExpiresAt <= DateTimeOffset.UtcNow
        ? "Expired"
        : $"Kept until {ExpiresAt.LocalDateTime:g}";
}

public sealed class SyncConflictService
{
    private const string ManifestName = "manifest.json";
    private const string OriginalPayloadName = "original.bin";
    private const string ConflictPayloadName = "conflict.bin";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _backupRoot;

    public SyncConflictService(string backupRoot)
    {
        _backupRoot = Path.GetFullPath(backupRoot);
    }

    public Task<IReadOnlyList<SyncConflictItem>> ScanAsync(
        IReadOnlyCollection<SyncConflictScope> scopes,
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SyncConflictItem>>(() =>
        {
            List<SyncConflictItem> conflicts = [];
            foreach (SyncConflictScope scope in scopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string root = Path.GetFullPath(scope.RootPath);
                if (!Directory.Exists(root)) continue;
                foreach (string path in EnumerateConflictFiles(root, cancellationToken))
                {
                    if (!TryGetOriginalPath(path, out string? originalPath)) continue;
                    FileInfo conflict = new(path);
                    FileInfo original = new(originalPath);
                    conflicts.Add(new SyncConflictItem(
                        Guid.NewGuid(),
                        scope.Name,
                        root,
                        Path.GetRelativePath(root, path).Replace('\\', '/'),
                        Path.GetRelativePath(root, originalPath).Replace('\\', '/'),
                        conflict.LastWriteTimeUtc,
                        conflict.Exists ? conflict.Length : 0,
                        original.Exists ? original.Length : 0,
                        original.Exists));
                }
            }

            return conflicts
                .OrderByDescending(item => item.ModifiedAt)
                .ThenBy(item => item.RelativeOriginalPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);

    public async Task<SyncConflictBackup> ResolveAsync(
        Guid projectId,
        SyncConflictItem conflict,
        SyncConflictResolution resolution,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project ID is required.", nameof(projectId));
        retentionDays = Math.Clamp(retentionDays, 1, 3650);
        string root = Path.GetFullPath(conflict.RootPath);
        string conflictPath = ResolveUnderRoot(root, conflict.RelativeConflictPath);
        string originalPath = ResolveUnderRoot(root, conflict.RelativeOriginalPath);
        if (!File.Exists(conflictPath)) throw new FileNotFoundException("The Syncthing conflict copy no longer exists.", conflictPath);
        bool originalExists = File.Exists(originalPath);
        if (resolution == SyncConflictResolution.KeepOriginal && !originalExists)
            throw new InvalidOperationException("The original file is missing. Use the conflict version or restore the original first.");

        Guid backupId = Guid.NewGuid();
        string backupDirectory = GetBackupDirectory(projectId, backupId);
        Directory.CreateDirectory(backupDirectory);
        try
        {
            if (originalExists)
                await CopyFileAsync(originalPath, Path.Combine(backupDirectory, OriginalPayloadName), cancellationToken);
            await CopyFileAsync(conflictPath, Path.Combine(backupDirectory, ConflictPayloadName), cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            SyncConflictBackup backup = new(
                backupId,
                projectId,
                conflict.Scope,
                root,
                conflict.RelativeConflictPath,
                conflict.RelativeOriginalPath,
                resolution,
                originalExists,
                now,
                now.AddDays(retentionDays));
            await WriteManifestAsync(backupDirectory, backup, cancellationToken);

            if (resolution == SyncConflictResolution.UseConflict)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
                string temporaryPath = originalPath + $".cyrevision-resolve-{backupId:N}.tmp";
                try
                {
                    await CopyFileAsync(conflictPath, temporaryPath, cancellationToken);
                    File.Move(temporaryPath, originalPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }

            File.Delete(conflictPath);
            return backup;
        }
        catch
        {
            if (Directory.Exists(backupDirectory) && !File.Exists(Path.Combine(backupDirectory, ManifestName)))
                Directory.Delete(backupDirectory, true);
            throw;
        }
    }

    public async Task RestoreAsync(SyncConflictBackup backup, CancellationToken cancellationToken = default)
    {
        string backupDirectory = GetBackupDirectory(backup.ProjectId, backup.Id);
        string root = Path.GetFullPath(backup.RootPath);
        string originalPath = ResolveUnderRoot(root, backup.RelativeOriginalPath);
        string conflictPath = ResolveUnderRoot(root, backup.RelativeConflictPath);
        string conflictPayload = Path.Combine(backupDirectory, ConflictPayloadName);
        if (!File.Exists(conflictPayload)) throw new FileNotFoundException("The saved conflict payload is missing.", conflictPayload);

        if (backup.OriginalExisted)
        {
            string originalPayload = Path.Combine(backupDirectory, OriginalPayloadName);
            if (!File.Exists(originalPayload)) throw new FileNotFoundException("The saved original payload is missing.", originalPayload);
            await ReplaceFileAtomicallyAsync(originalPayload, originalPath, backup.Id, cancellationToken);
        }
        else if (File.Exists(originalPath))
        {
            File.Delete(originalPath);
        }

        await ReplaceFileAtomicallyAsync(conflictPayload, conflictPath, backup.Id, cancellationToken);
        await WriteManifestAsync(backupDirectory, backup with { RestoredAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public async Task<IReadOnlyList<SyncConflictBackup>> LoadBackupsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string projectRoot = GetProjectBackupRoot(projectId);
        if (!Directory.Exists(projectRoot)) return [];
        List<SyncConflictBackup> backups = [];
        foreach (string manifestPath in Directory.EnumerateFiles(projectRoot, ManifestName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = File.OpenRead(manifestPath);
                SyncConflictBackup? backup = await JsonSerializer.DeserializeAsync<SyncConflictBackup>(stream, JsonOptions, cancellationToken);
                if (backup is not null && backup.ProjectId == projectId) backups.Add(backup);
            }
            catch (JsonException)
            {
                // A damaged manifest does not hide the other recoverable conflict backups.
            }
        }

        return backups.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public Task<int> PruneExpiredAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            IReadOnlyList<SyncConflictBackup> backups = await LoadBackupsAsync(projectId, cancellationToken);
            int removed = 0;
            foreach (SyncConflictBackup backup in backups.Where(item => item.ExpiresAt <= DateTimeOffset.UtcNow))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = GetBackupDirectory(projectId, backup.Id);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                    removed++;
                }
            }
            return removed;
        }, cancellationToken);

    private IEnumerable<string> EnumerateConflictFiles(string root, CancellationToken cancellationToken)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory, "*.sync-conflict-*");
                directories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string file in files) yield return file;
            foreach (string child in directories)
            {
                string name = Path.GetFileName(child);
                if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ".cyrevision", StringComparison.OrdinalIgnoreCase) ||
                    IsUnderRoot(child, _backupRoot))
                    continue;
                pending.Push(child);
            }
        }
    }

    private static bool TryGetOriginalPath(string conflictPath, out string originalPath)
    {
        string fileName = Path.GetFileName(conflictPath);
        int marker = fileName.IndexOf(".sync-conflict-", StringComparison.OrdinalIgnoreCase);
        if (marker <= 0)
        {
            originalPath = string.Empty;
            return false;
        }

        int extension = fileName.IndexOf('.', marker + ".sync-conflict-".Length);
        string originalName = fileName[..marker] + (extension >= 0 ? fileName[extension..] : string.Empty);
        originalPath = Path.Combine(Path.GetDirectoryName(conflictPath)!, originalName);
        return true;
    }

    private string GetProjectBackupRoot(Guid projectId) => Path.Combine(_backupRoot, projectId.ToString("N"));
    private string GetBackupDirectory(Guid projectId, Guid backupId) => Path.Combine(GetProjectBackupRoot(projectId), backupId.ToString("N"));

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        string resolved = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(resolved, root)) throw new InvalidOperationException("The conflict path escapes its synchronized root.");
        return resolved;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 128, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task ReplaceFileAtomicallyAsync(
        string source,
        string destination,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporaryPath = destination + $".cyrevision-restore-{operationId:N}.tmp";
        try
        {
            await CopyFileAsync(source, temporaryPath, cancellationToken);
            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task WriteManifestAsync(string directory, SyncConflictBackup backup, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, ManifestName);
        await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, cancellationToken);
    }
}
