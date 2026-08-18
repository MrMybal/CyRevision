using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Sync;

public sealed record SyncCommitFile(string Path, long Length, string Sha256);

public sealed record SyncCommitManifest(
    string CommitId,
    string? ParentCommitId,
    Guid ProjectId,
    string Message,
    string Author,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SyncCommitFile> Files)
{
    public string ShortId => CommitId.Length > 10 ? CommitId[..10] : CommitId;
    public string SizeText => $"{Files.Count:N0} file(s)";
}

public sealed record SyncCommitConflict(
    string Path,
    string? BaseHash,
    string? LocalHash,
    string? IncomingHash)
{
    public string State => BaseHash is null ? "Both added" : IncomingHash is null ? "Incoming deleted" : "Both modified";
}

public enum SyncCommitConflictChoice
{
    Unresolved,
    KeepLocal,
    UseIncoming
}

public sealed record SyncCommitAnalysis(
    SyncCommitManifest Incoming,
    IReadOnlyList<SyncCommitConflict> Conflicts,
    IReadOnlyList<string> PathsToChange,
    int ChangedFiles,
    int DeletedFiles)
{
    public bool CanApply => Conflicts.Count == 0;
}

public sealed record SyncCommitCreateResult(SyncCommitManifest Manifest, string PackagePath);

/// <summary>
/// Creates immutable, compressed project snapshots in an exchange folder. The exchange folder is only
/// changed by CreateCommitAsync, so a file watcher/Syncthing instance publishes data at commit time.
/// </summary>
public sealed class SyncCommitService
{
    private const string ManifestEntryName = ".cyrevision/commit.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cyrevision", ".vs", ".idea", "node_modules", "Binaries", "Intermediate",
        "DerivedDataCache", "Saved", "bin", "obj"
    };

    public async Task<SyncCommitCreateResult> CreateCommitAsync(
        Guid projectId,
        string sourceDirectory,
        string exchangeDirectory,
        string stateDirectory,
        string message,
        string author,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project ID is required.", nameof(projectId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        string exchangeRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exchangeDirectory));
        string stateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateDirectory));
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException(sourceRoot);
        if (IsInside(exchangeRoot, sourceRoot))
            throw new InvalidOperationException("The Sync + Commit exchange folder must be outside the project folder.");

        Directory.CreateDirectory(exchangeRoot);
        Directory.CreateDirectory(stateRoot);
        IReadOnlyList<SyncCommitFile> files = await ScanAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        string? parent = await ReadHeadAsync(stateRoot, cancellationToken).ConfigureAwait(false);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        string commitId = ComputeCommitId(projectId, parent, message.Trim(), author.Trim(), createdAt, files);
        SyncCommitManifest manifest = new(commitId, parent, projectId, message.Trim(), author.Trim(), createdAt, files);
        string packagePath = Path.Combine(exchangeRoot, $"{createdAt:yyyyMMdd-HHmmss}-{commitId}.cycommit");
        string temporaryPath = packagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(() => WritePackage(sourceRoot, temporaryPath, manifest, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, packagePath);
            await File.WriteAllTextAsync(Path.Combine(stateRoot, "HEAD"), commitId, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return new SyncCommitCreateResult(manifest, packagePath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async Task<IReadOnlyList<SyncCommitManifest>> ListCommitsAsync(
        string exchangeDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(exchangeDirectory)) return [];
        List<SyncCommitManifest> manifests = [];
        foreach (string package in Directory.EnumerateFiles(exchangeDirectory, "*.cycommit", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncCommitManifest? manifest = await ReadManifestAsync(package, cancellationToken).ConfigureAwait(false);
            if (manifest is not null) manifests.Add(manifest);
        }
        return manifests.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task<SyncCommitAnalysis> AnalyzeAsync(
        string sourceDirectory,
        string exchangeDirectory,
        SyncCommitManifest incoming,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SyncCommitFile> localFiles = await ScanAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> local = localFiles.ToDictionary(item => item.Path, item => item.Sha256, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> next = incoming.Files.ToDictionary(item => item.Path, item => item.Sha256, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> parent = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(incoming.ParentCommitId))
        {
            SyncCommitManifest? parentManifest = (await ListCommitsAsync(exchangeDirectory, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.CommitId.Equals(incoming.ParentCommitId, StringComparison.OrdinalIgnoreCase));
            if (parentManifest is not null)
                parent = parentManifest.Files.ToDictionary(item => item.Path, item => item.Sha256, StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> paths = new(parent.Keys, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(local.Keys);
        paths.UnionWith(next.Keys);
        List<SyncCommitConflict> conflicts = [];
        List<string> pathsToChange = [];
        int changed = 0;
        int deleted = 0;
        foreach (string path in paths)
        {
            parent.TryGetValue(path, out string? baseHash);
            local.TryGetValue(path, out string? localHash);
            next.TryGetValue(path, out string? incomingHash);
            bool localChanged = !StringComparer.Ordinal.Equals(baseHash, localHash);
            bool incomingChanged = !StringComparer.Ordinal.Equals(baseHash, incomingHash);
            if (incomingChanged)
            {
                changed++;
                pathsToChange.Add(path);
            }
            if (incomingChanged && incomingHash is null) deleted++;
            if (localChanged && incomingChanged && !StringComparer.Ordinal.Equals(localHash, incomingHash))
                conflicts.Add(new SyncCommitConflict(path, baseHash, localHash, incomingHash));
        }
        return new SyncCommitAnalysis(incoming, conflicts, pathsToChange, changed, deleted);
    }

    public async Task ApplyAsync(
        string sourceDirectory,
        string exchangeDirectory,
        string stateDirectory,
        string backupDirectory,
        SyncCommitManifest incoming,
        IReadOnlyDictionary<string, SyncCommitConflictChoice>? conflictChoices = null,
        CancellationToken cancellationToken = default)
    {
        SyncCommitAnalysis analysis = await AnalyzeAsync(sourceDirectory, exchangeDirectory, incoming, cancellationToken)
            .ConfigureAwait(false);
        conflictChoices ??= new Dictionary<string, SyncCommitConflictChoice>(StringComparer.OrdinalIgnoreCase);
        SyncCommitConflict[] unresolved = analysis.Conflicts.Where(conflict =>
            !conflictChoices.TryGetValue(conflict.Path, out SyncCommitConflictChoice choice) ||
            choice == SyncCommitConflictChoice.Unresolved).ToArray();
        if (unresolved.Length > 0)
            throw new InvalidOperationException($"The commit has {unresolved.Length:N0} unresolved conflict(s).");
        string? package = FindPackage(exchangeDirectory, incoming.CommitId);
        if (package is null) throw new FileNotFoundException("The selected Sync + Commit package is missing.");
        string sourceRoot = Path.GetFullPath(sourceDirectory);
        Directory.CreateDirectory(backupDirectory);
        string backup = Path.Combine(backupDirectory, $"before-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{incoming.ShortId}.zip");
        await Task.Run(() => BackupChangedFiles(sourceRoot, backup, analysis, cancellationToken), cancellationToken).ConfigureAwait(false);
        HashSet<string> keepLocal = analysis.Conflicts
            .Where(conflict => conflictChoices[conflict.Path] == SyncCommitConflictChoice.KeepLocal)
            .Select(conflict => conflict.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() => ExtractPackage(sourceRoot, package, incoming, keepLocal, cancellationToken), cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(stateDirectory, "HEAD"), incoming.CommitId, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<SyncCommitManifest?> ReadManifestAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, false);
        ZipArchiveEntry? entry = archive.GetEntry(ManifestEntryName);
        if (entry is null) return null;
        await using Stream manifestStream = entry.Open();
        return await JsonSerializer.DeserializeAsync<SyncCommitManifest>(manifestStream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SyncCommitFile>> ScanAsync(string root, CancellationToken cancellationToken) =>
        await Task.Run(() =>
        {
            List<SyncCommitFile> files = [];
            Stack<string> directories = new();
            directories.Push(Path.GetFullPath(root));
            while (directories.TryPop(out string? directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (string child in Directory.EnumerateDirectories(directory))
                    if (!ExcludedDirectoryNames.Contains(Path.GetFileName(child))) directories.Push(child);
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileInfo info = new(file);
                    using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    string hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
                    files.Add(new SyncCommitFile(Path.GetRelativePath(root, file).Replace('\\', '/'), info.Length, hash));
                }
            }
            return (IReadOnlyList<SyncCommitFile>)files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        }, cancellationToken).ConfigureAwait(false);

    private static void WritePackage(string sourceRoot, string packagePath, SyncCommitManifest manifest, CancellationToken cancellationToken)
    {
        using FileStream stream = new(packagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using (Stream output = manifestEntry.Open()) JsonSerializer.Serialize(output, manifest, JsonOptions);
        foreach (SyncCommitFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string absolute = SafeCombine(sourceRoot, file.Path);
            archive.CreateEntryFromFile(absolute, "files/" + file.Path, CompressionLevel.Optimal);
        }
    }

    private static void BackupChangedFiles(string sourceRoot, string backupPath, SyncCommitAnalysis analysis, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
        foreach (string path in analysis.PathsToChange.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string absolute = SafeCombine(sourceRoot, path);
            if (File.Exists(absolute)) archive.CreateEntryFromFile(absolute, path, CompressionLevel.Optimal);
        }
    }

    private static void ExtractPackage(
        string sourceRoot,
        string packagePath,
        SyncCommitManifest incoming,
        IReadOnlySet<string> keepLocal,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        HashSet<string> incomingPaths = incoming.Files.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (SyncCommitFile existing in ScanAsync(sourceRoot, cancellationToken).GetAwaiter().GetResult())
        {
            if (!incomingPaths.Contains(existing.Path) && !keepLocal.Contains(existing.Path))
                File.Delete(SafeCombine(sourceRoot, existing.Path));
        }
        foreach (SyncCommitFile file in incoming.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (keepLocal.Contains(file.Path)) continue;
            ZipArchiveEntry entry = archive.GetEntry("files/" + file.Path)
                ?? throw new InvalidDataException($"Package entry '{file.Path}' is missing.");
            string destination = SafeCombine(sourceRoot, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static string? FindPackage(string exchangeDirectory, string commitId) =>
        Directory.Exists(exchangeDirectory)
            ? Directory.EnumerateFiles(exchangeDirectory, $"*-{commitId}.cycommit").FirstOrDefault()
            : null;

    private static async Task<string?> ReadHeadAsync(string stateDirectory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(stateDirectory, "HEAD");
        return File.Exists(path) ? (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim() : null;
    }

    private static string ComputeCommitId(Guid projectId, string? parent, string message, string author, DateTimeOffset createdAt, IReadOnlyList<SyncCommitFile> files)
    {
        StringBuilder payload = new StringBuilder().Append(projectId).Append('\n').Append(parent).Append('\n').Append(message)
            .Append('\n').Append(author).Append('\n').Append(createdAt.ToUnixTimeMilliseconds()).Append('\n');
        foreach (SyncCommitFile file in files) payload.Append(file.Path).Append('\0').Append(file.Sha256).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()))).ToLowerInvariant();
    }

    private static string SafeCombine(string root, string relativePath)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(full, fullRoot)) throw new InvalidDataException($"Unsafe package path '{relativePath}'.");
        return full;
    }

    private static bool IsInside(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
