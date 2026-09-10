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
        if (IsInside(stateRoot, sourceRoot))
            throw new InvalidOperationException("The Sync + Commit state folder must be outside the project folder.");

        Directory.CreateDirectory(exchangeRoot);
        Directory.CreateDirectory(stateRoot);
        using FileStream operationLock = new(Path.Combine(stateRoot, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        IReadOnlyList<SyncCommitFile> files = await ScanAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        string? parent = await ReadHeadAsync(stateRoot, cancellationToken).ConfigureAwait(false);
        if (parent is not null && !(await ListCommitsAsync(exchangeRoot, cancellationToken).ConfigureAwait(false))
            .Any(commit => commit.CommitId == parent && commit.ProjectId == projectId))
            throw new InvalidDataException("The local HEAD belongs to a missing revision or a different shared project.");
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        string commitId = ComputeCommitId(projectId, parent, message.Trim(), author.Trim(), createdAt, files);
        SyncCommitManifest manifest = new(commitId, parent, projectId, message.Trim(), author.Trim(), createdAt, files);
        ValidateManifest(manifest);
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
        CancellationToken cancellationToken = default,
        string? stateDirectory = null)
    {
        ValidateManifest(incoming);
        IReadOnlyList<SyncCommitFile> localFiles = await ScanAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> local = localFiles.ToDictionary(item => item.Path, item => item.Sha256, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> next = incoming.Files.ToDictionary(item => item.Path, item => item.Sha256, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> parent = new(StringComparer.OrdinalIgnoreCase);
        string? baseId = incoming.ParentCommitId;
        string? localHead = stateDirectory is null ? null : await ReadHeadAsync(stateDirectory, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SyncCommitManifest>? history = null;
        if (localHead is not null && localHead != incoming.ParentCommitId)
        {
            history = await ListCommitsAsync(exchangeDirectory, cancellationToken).ConfigureAwait(false);
            Dictionary<string, SyncCommitManifest> commits = history.Where(commit => commit.ProjectId == incoming.ProjectId)
                .GroupBy(commit => commit.CommitId).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            commits[incoming.CommitId] = incoming;
            HashSet<string> localAncestors = Ancestors(localHead, commits).ToHashSet(StringComparer.Ordinal);
            baseId = Ancestors(incoming.CommitId, commits).FirstOrDefault(localAncestors.Contains);
        }
        if (!string.IsNullOrWhiteSpace(baseId))
        {
            SyncCommitManifest? parentManifest = (history ?? await ListCommitsAsync(exchangeDirectory, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.CommitId.Equals(baseId, StringComparison.OrdinalIgnoreCase));
            if (parentManifest is null || parentManifest.ProjectId != incoming.ProjectId)
                throw new InvalidDataException("The parent revision is missing or belongs to another project. Wait for synchronization before applying this commit.");
            ValidateManifest(parentManifest);
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
            if (incomingChanged && !StringComparer.Ordinal.Equals(localHash, incomingHash))
            {
                changed++;
                pathsToChange.Add(path);
            }
            if (incomingChanged && incomingHash is null && localHash is not null) deleted++;
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
        if (IsInside(Path.GetFullPath(stateDirectory), Path.GetFullPath(sourceDirectory)) ||
            IsInside(Path.GetFullPath(backupDirectory), Path.GetFullPath(sourceDirectory)))
            throw new InvalidOperationException("State and recovery folders must be outside the synchronized source.");
        Directory.CreateDirectory(stateDirectory);
        using FileStream operationLock = new(Path.Combine(stateDirectory, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SyncCommitAnalysis analysis = await AnalyzeAsync(sourceDirectory, exchangeDirectory, incoming, cancellationToken, stateDirectory)
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
        HashSet<string> keepLocal = analysis.Conflicts
            .Where(conflict => conflictChoices[conflict.Path] == SyncCommitConflictChoice.KeepLocal)
            .Select(conflict => conflict.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(stateDirectory);
        string staging = Path.Combine(Path.GetFullPath(stateDirectory), "apply-" + Guid.NewGuid().ToString("N"));
        string[] paths = analysis.PathsToChange.Where(path => !keepLocal.Contains(path)).ToArray();
        try
        {
            // Validate the entire package before touching the working tree or HEAD.
            await Task.Run(() => StageVerifiedPackage(package, staging, incoming, paths.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken), cancellationToken).ConfigureAwait(false);
            // A slow download/validation must not silently apply a now-obsolete conflict decision.
            SyncCommitAnalysis latest = await AnalyzeAsync(sourceRoot, exchangeDirectory, incoming, cancellationToken, stateDirectory).ConfigureAwait(false);
            if (!analysis.Conflicts.SequenceEqual(latest.Conflicts) || !analysis.PathsToChange.SequenceEqual(latest.PathsToChange))
                throw new InvalidOperationException("Local files changed during validation. Analyze the revision again.");
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, $"before-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{incoming.ShortId}-{Guid.NewGuid():N}.zip");
            await Task.Run(() => ApplyVerifiedPlan(sourceRoot, staging, backup, paths, cancellationToken), cancellationToken).ConfigureAwait(false);
            string head = Path.Combine(stateDirectory, "HEAD");
            string temporaryHead = head + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryHead, incoming.CommitId, Encoding.UTF8, CancellationToken.None).ConfigureAwait(false);
                File.Move(temporaryHead, head, true);
            }
            finally { File.Delete(temporaryHead); }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
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
                    if (!ExcludedDirectoryNames.Contains(Path.GetFileName(child)))
                    {
                        RejectLink(child);
                        directories.Push(child);
                    }
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileInfo info = new(file);
                    RejectLink(file);
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
            // Hash the bytes actually archived, not only the earlier directory scan.
            using Stream input = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
            using Stream output = archive.CreateEntry("files/" + file.Path, CompressionLevel.Optimal).Open();
            CopyVerified(input, output, file, cancellationToken);
        }
    }

    private static void ApplyVerifiedPlan(string sourceRoot, string staging, string backupPath, string[] paths, CancellationToken cancellationToken)
    {
        HashSet<string> existed = new(StringComparer.OrdinalIgnoreCase);
        using (ZipArchive archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string absolute = SafeCombine(sourceRoot, path);
                if (Directory.Exists(absolute))
                    throw new IOException($"A local directory occupies the incoming file path '{path}'. Resolve it before applying.");
                if (File.Exists(absolute))
                {
                    archive.CreateEntryFromFile(absolute, path, CompressionLevel.Optimal);
                    existed.Add(path);
                }
            }
            using Stream recovery = archive.CreateEntry(".cyrevision/recovery.json").Open();
            JsonSerializer.Serialize(recovery, new { Paths = paths, PreviouslyPresent = existed }, JsonOptions);
        }
        List<string> touched = [];
        try
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = SafeCombine(sourceRoot, path);
                string staged = SafeCombine(staging, path);
                if (File.Exists(staged))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".cycommit-tmp";
                    try
                    {
                        File.Copy(staged, temporary);
                        File.Move(temporary, destination, true);
                    }
                    finally { File.Delete(temporary); }
                }
                else File.Delete(destination);
                touched.Add(path);
            }
        }
        catch
        {
            using ZipArchive archive = ZipFile.OpenRead(backupPath);
            foreach (string path in touched.AsEnumerable().Reverse())
            {
                string destination = SafeCombine(sourceRoot, path);
                if (existed.Contains(path)) archive.GetEntry(path)!.ExtractToFile(destination, true);
                else File.Delete(destination);
            }
            throw;
        }
    }

    private static void StageVerifiedPackage(string package, string staging, SyncCommitManifest incoming, IReadOnlySet<string> changedPaths, CancellationToken token)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        if (archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count)
            throw new InvalidDataException("The package contains duplicate entries.");
        using Stream manifestStream = (archive.GetEntry(ManifestEntryName) ?? throw new InvalidDataException("Missing manifest.")).Open();
        SyncCommitManifest actual = JsonSerializer.Deserialize<SyncCommitManifest>(manifestStream, JsonOptions)
            ?? throw new InvalidDataException("Invalid manifest.");
        ValidateManifest(actual);
        if (JsonSerializer.Serialize(actual, JsonOptions) != JsonSerializer.Serialize(incoming, JsonOptions))
            throw new InvalidDataException("The package manifest changed since it was selected.");
        Directory.CreateDirectory(staging);
        foreach (SyncCommitFile file in incoming.Files)
        {
            token.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = archive.GetEntry("files/" + file.Path) ?? throw new InvalidDataException($"Missing entry '{file.Path}'.");
            if (entry.Length != file.Length) throw new InvalidDataException($"Invalid size for '{file.Path}'.");
            string destination = SafeCombine(staging, file.Path);
            using Stream input = entry.Open();
            if (changedPaths.Contains(file.Path)) Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream output = changedPaths.Contains(file.Path) ? File.Create(destination) : Stream.Null;
            CopyVerified(input, output, file, token);
        }
    }

    private static void CopyVerified(Stream input, Stream output, SyncCommitFile file, CancellationToken token)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long length = 0;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            length += count;
            if (length > file.Length) throw new InvalidDataException($"File '{file.Path}' changed size.");
            hash.AppendData(buffer, 0, count);
            output.Write(buffer, 0, count);
        }
        if (length != file.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 verification failed for '{file.Path}'. No revision was published or applied.");
    }

    private static void ValidateManifest(SyncCommitManifest manifest)
    {
        if (manifest.ProjectId == Guid.Empty || manifest.Files is null || manifest.Files.Any(file =>
            file.Length < 0 || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("Invalid commit manifest.");
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (SyncCommitFile file in manifest.Files)
        {
            string[] parts = file.Path.Split('/');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || ExcludedDirectoryNames.Contains(part) ||
                    part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['\\', ':']) >= 0) || !paths.Add(file.Path))
                throw new InvalidDataException($"Unsafe or duplicate commit path '{file.Path}'.");
        }
        if (paths.Any(path => path.Split('/').SkipLast(1).Aggregate(new List<string>(), (parents, part) =>
            { parents.Add(parents.Count == 0 ? part : parents[^1] + "/" + part); return parents; }).Any(paths.Contains)))
            throw new InvalidDataException("A commit path cannot be both a file and a directory.");
        if (ComputeCommitId(manifest.ProjectId, manifest.ParentCommitId, manifest.Message, manifest.Author, manifest.CreatedAt, manifest.Files) != manifest.CommitId)
            throw new InvalidDataException("Invalid commit identity.");
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Sync + Commit does not follow symbolic links or junctions: {path}");
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

    private static IReadOnlyList<string> Ancestors(string head, IReadOnlyDictionary<string, SyncCommitManifest> commits)
    {
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (string? id = head; id is not null;)
        {
            if (!seen.Add(id) || !commits.TryGetValue(id, out SyncCommitManifest? commit))
                throw new InvalidDataException("The Sync commit ancestry is incomplete or cyclic. Wait for all parent packages.");
            ValidateManifest(commit);
            result.Add(id);
            id = commit.ParentCommitId;
        }
        return result;
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
        for (string? component = full; component is not null && IsInside(component, fullRoot); component = Path.GetDirectoryName(component))
            if (File.Exists(component) || Directory.Exists(component)) RejectLink(component);
        return full;
    }

    private static bool IsInside(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
