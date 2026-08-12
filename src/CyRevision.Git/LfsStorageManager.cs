using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyRevision.Git;

public sealed class LfsStorageManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex ShaRegex = new("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])", RegexOptions.Compiled);
    private readonly ProcessRunner _runner = new();
    private readonly string _gitExecutable;

    public LfsStorageManager(string gitExecutable = "git") => _gitExecutable = gitExecutable;

    public async Task<LfsCleanupPlan> AnalyzeAsync(
        string repositoryPath,
        LfsManagementProfile profile,
        PeerLfsAvailabilityCache? peerAvailability = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        string repository = Path.GetFullPath(repositoryPath);
        LfsStoragePaths paths = await LfsStoragePathResolver.ResolveAsync(
            _runner, _gitExecutable, repository, cancellationToken);
        IReadOnlyList<LocalObject> localObjects = EnumerateLocalObjects(paths.ObjectsDirectory);
        HashSet<string> referenced = await ReadProtectedOidsAsync(repository, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        (HashSet<string> remoteOids, string remoteOutput) = profile.VerifyRemote
            ? await VerifyRemoteCandidatesAsync(repository, profile.RemoteName, cancellationToken)
            : (new HashSet<string>(StringComparer.OrdinalIgnoreCase), "Remote verification disabled.");
        Dictionary<string, List<LfsRetentionEvidence>> evidence = new(StringComparer.OrdinalIgnoreCase);
        foreach (string oid in remoteOids)
            AddEvidence(evidence, oid, new LfsRetentionEvidence(
                LfsRetentionEvidenceKind.Remote, profile.RemoteName, $"Remote: {profile.RemoteName}", now));

        DateTimeOffset peerCutoff = now.AddHours(-profile.PeerProofMaximumAgeHours);
        if (peerAvailability?.ProjectId == profile.ProjectId)
        {
            foreach (PeerLfsObjectAvailability item in peerAvailability.Objects)
            foreach (PeerLfsAvailabilityLocation peer in item.Peers.Where(peer =>
                         peer.PublishedToExchange && peer.LastSeenAt >= peerCutoff))
                AddEvidence(evidence, item.OidSha256, new LfsRetentionEvidence(
                    LfsRetentionEvidenceKind.Peer,
                    peer.DeviceId.ToString("N"),
                    $"Peer: {peer.DisplayName}",
                    peer.LastSeenAt));
        }

        LfsArchiveManifest archive = await ReadArchiveManifestAsync(profile.ArchivePath, cancellationToken);
        foreach (LfsArchiveEntry entry in archive.Objects)
        {
            string path = GetArchiveObjectPath(profile.ArchivePath, entry.OidSha256);
            if (File.Exists(path) && new FileInfo(path).Length == entry.Size)
                AddEvidence(evidence, entry.OidSha256, new LfsRetentionEvidence(
                    LfsRetentionEvidenceKind.Archive,
                    Path.GetFullPath(profile.ArchivePath),
                    "Verified archive",
                    entry.VerifiedAt));
        }

        DateTimeOffset graceCutoff = now.AddDays(-profile.GracePeriodDays);
        LfsCleanupItem[] items = localObjects
            .Select(item => new LfsCleanupItem(
                item.Oid,
                item.Size,
                item.LastModifiedAt,
                item.Path,
                referenced.Contains(item.Oid),
                item.LastModifiedAt > graceCutoff,
                evidence.TryGetValue(item.Oid, out List<LfsRetentionEvidence>? copies)
                    ? copies.DistinctBy(copy => (copy.Kind, copy.LocationId)).ToArray()
                    : [],
                profile.RequiredVerifiedCopies))
            .OrderBy(item => item.CanDelete ? 0 : 1)
            .ThenByDescending(item => item.Size)
            .ToArray();

        return new LfsCleanupPlan(
            Guid.NewGuid(), repository, paths.StorageDirectory, now,
            profile.RequiredVerifiedCopies, items, remoteOutput);
    }

    public async Task<LfsArchiveResult> ArchiveUnreferencedAsync(
        LfsCleanupPlan plan,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ValidateFreshPlan(plan);
        string archiveRoot = ValidateArchivePath(archivePath);
        Directory.CreateDirectory(archiveRoot);
        LfsArchiveManifest existing = await ReadArchiveManifestAsync(archiveRoot, cancellationToken);
        Dictionary<string, LfsArchiveEntry> entries = existing.Objects.ToDictionary(
            entry => entry.OidSha256, StringComparer.OrdinalIgnoreCase);
        int count = 0;
        long bytes = 0;
        foreach (LfsCleanupItem item in plan.Objects.Where(item => !item.IsReferencedLocally && !item.IsInsideGracePeriod))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(item.LocalPath))
                continue;
            await EnsureHashAsync(item.LocalPath, item.OidSha256, cancellationToken);
            string destination = GetArchiveObjectPath(archiveRoot, item.OidSha256);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                string temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
                await CopyAsync(item.LocalPath, temporary, cancellationToken);
                await EnsureHashAsync(temporary, item.OidSha256, cancellationToken);
                File.Move(temporary, destination, false);
                count++;
                bytes += item.Size;
            }
            else
            {
                await EnsureHashAsync(destination, item.OidSha256, cancellationToken);
            }

            entries[item.OidSha256] = new LfsArchiveEntry(item.OidSha256, item.Size, DateTimeOffset.UtcNow);
        }

        await WriteArchiveManifestAsync(archiveRoot, new LfsArchiveManifest(entries.Values
            .OrderBy(entry => entry.OidSha256, StringComparer.Ordinal).ToArray()), cancellationToken);
        return new LfsArchiveResult(archiveRoot, count, bytes);
    }

    public async Task<LfsCleanupResult> ExecuteAsync(
        LfsCleanupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ValidateFreshPlan(plan);
        HashSet<string> protectedNow = await ReadProtectedOidsAsync(plan.RepositoryPath, cancellationToken);
        int deleted = 0;
        int skipped = 0;
        long bytes = 0;
        List<object> auditItems = [];
        foreach (LfsCleanupItem item in plan.Objects.Where(item => item.CanDelete))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (protectedNow.Contains(item.OidSha256) || !File.Exists(item.LocalPath))
            {
                skipped++;
                continue;
            }

            if (item.Evidence.Count < plan.RequiredCopies)
                throw new InvalidOperationException($"LFS object {item.OidSha256} no longer has enough retention evidence.");
            await EnsureHashAsync(item.LocalPath, item.OidSha256, cancellationToken);
            foreach (LfsRetentionEvidence archive in item.Evidence.Where(copy => copy.Kind == LfsRetentionEvidenceKind.Archive))
            {
                string archivedObject = GetArchiveObjectPath(archive.LocationId, item.OidSha256);
                await EnsureHashAsync(archivedObject, item.OidSha256, cancellationToken);
            }

            File.Delete(item.LocalPath);
            deleted++;
            bytes += item.Size;
            auditItems.Add(new
            {
                item.OidSha256,
                item.Size,
                Evidence = item.Evidence,
                DeletedAt = DateTimeOffset.UtcNow
            });
            RemoveEmptyObjectDirectories(item.LocalPath, plan.StoragePath);
        }

        string auditDirectory = Path.Combine(plan.RepositoryPath, ".git", "cyrevision", "lfs-cleanup");
        Directory.CreateDirectory(auditDirectory);
        string auditPath = Path.Combine(auditDirectory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{plan.PlanId:N}.json");
        await File.WriteAllBytesAsync(auditPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            plan.PlanId,
            plan.RepositoryPath,
            plan.StoragePath,
            DeletedObjects = deleted,
            ReclaimedBytes = bytes,
            SkippedObjects = skipped,
            Objects = auditItems
        }, JsonOptions), cancellationToken);
        return new LfsCleanupResult(plan.PlanId, deleted, bytes, skipped, auditPath);
    }

    public async Task<LfsRelocationResult> RelocateAsync(
        string repositoryPath,
        string destinationPath,
        bool removeOriginalObjectsAfterVerification,
        CancellationToken cancellationToken = default)
    {
        string repository = Path.GetFullPath(repositoryPath);
        string destination = Path.GetFullPath(destinationPath);
        LfsStoragePaths current = await LfsStoragePathResolver.ResolveAsync(
            _runner, _gitExecutable, repository, cancellationToken);
        if (PathsEqual(current.StorageDirectory, destination))
            throw new InvalidOperationException("The selected directory is already the active LFS storage.");
        if (IsInside(destination, current.StorageDirectory) || IsInside(current.StorageDirectory, destination))
            throw new InvalidOperationException("The destination and current LFS stores must not contain one another.");
        if (IsInside(destination, repository) && !IsInside(destination, current.GitCommonDirectory))
            throw new InvalidOperationException("Choose an external storage directory or a directory inside .git, not the working tree.");

        Directory.CreateDirectory(destination);
        string ownerPath = Path.Combine(destination, ".cyrevision-lfs-owner.json");
        if (File.Exists(ownerPath))
        {
            LfsStorageOwner? owner = JsonSerializer.Deserialize<LfsStorageOwner>(
                await File.ReadAllBytesAsync(ownerPath, cancellationToken), JsonOptions);
            if (owner is null || !PathsEqual(owner.RepositoryPath, repository))
                throw new InvalidOperationException("This LFS directory belongs to another repository. Shared lfs.storage directories are intentionally refused.");
        }
        else if (Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new InvalidOperationException("The destination must be empty or already owned by this repository.");
        }

        await File.WriteAllBytesAsync(ownerPath, JsonSerializer.SerializeToUtf8Bytes(
            new LfsStorageOwner(repository, DateTimeOffset.UtcNow), JsonOptions), cancellationToken);
        int objectCount = 0;
        long copiedBytes = 0;
        if (Directory.Exists(current.StorageDirectory))
        {
            foreach (string source in Directory.EnumerateFiles(current.StorageDirectory, "*", SafeRecursiveEnumeration()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(current.StorageDirectory, source);
                if (string.Equals(relative, ".cyrevision-lfs-owner.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                    await CopyAsync(source, target, cancellationToken);
                string name = Path.GetFileName(source);
                if (IsSha256(name) && relative.StartsWith("objects" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    await EnsureHashAsync(target, name, cancellationToken);
                    objectCount++;
                    copiedBytes += new FileInfo(source).Length;
                }
            }
        }

        ProcessResult configure = await RunGitAsync(repository,
            ["config", "--local", "lfs.storage", destination], cancellationToken);
        EnsureSuccess(configure, "Unable to activate the external LFS storage.");
        ProcessResult validation = await RunGitAsync(repository, ["lfs", "env"], cancellationToken);
        if (!validation.Succeeded)
        {
            await RunGitAsync(repository, ["config", "--local", "--unset", "lfs.storage"], cancellationToken);
            EnsureSuccess(validation, "Git LFS rejected the relocated storage.");
        }

        bool removed = false;
        if (removeOriginalObjectsAfterVerification && Directory.Exists(current.ObjectsDirectory))
        {
            Directory.Delete(current.ObjectsDirectory, true);
            removed = true;
        }

        return new LfsRelocationResult(current.StorageDirectory, destination, objectCount, copiedBytes, removed);
    }

    private async Task<HashSet<string>> ReadProtectedOidsAsync(string repository, CancellationToken cancellationToken)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string[] arguments in new[]
                 {
                     new[] { "lfs", "ls-files", "--all", "--long" },
                     new[] { "lfs", "ls-files", "--long" }
                 })
        {
            ProcessResult command = await RunGitAsync(repository, arguments, cancellationToken);
            if (command.Succeeded)
                AddOids(command.StandardOutput, result);
        }

        ProcessResult worktrees = await RunGitAsync(repository, ["worktree", "list", "--porcelain"], cancellationToken);
        if (worktrees.Succeeded)
        {
            foreach (string line in worktrees.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                         .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal)))
            {
                string path = line["worktree ".Length..];
                ProcessResult current = await RunGitAsync(path, ["lfs", "ls-files", "--long"], cancellationToken);
                if (current.Succeeded)
                    AddOids(current.StandardOutput, result);
            }
        }

        return result;
    }

    private async Task<(HashSet<string> Oids, string Output)> VerifyRemoteCandidatesAsync(
        string repository,
        string remoteName,
        CancellationToken cancellationToken)
    {
        ProcessResult remote = await RunGitAsync(repository, ["remote", "get-url", remoteName], cancellationToken);
        if (!remote.Succeeded)
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                $"Remote '{remoteName}' is unavailable; no remote retention proof was accepted.");

        ProcessResult prune = await RunGitAsync(repository,
            ["-c", $"lfs.pruneremotetocheck={remoteName}", "lfs", "prune", "--dry-run", "--recent",
                "--verify-remote", "--verify-unreachable", "--when-unverified=continue", "--verbose"],
            cancellationToken);
        HashSet<string> verified = new(StringComparer.OrdinalIgnoreCase);
        if (prune.Succeeded)
            AddOids(prune.StandardOutput, verified);
        string output = string.Join(Environment.NewLine, new[] { prune.StandardOutput, prune.StandardError }
            .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return (verified, string.IsNullOrWhiteSpace(output) ? "Remote verification completed." : output);
    }

    private static IReadOnlyList<LocalObject> EnumerateLocalObjects(string objectsDirectory)
    {
        if (!Directory.Exists(objectsDirectory))
            return [];
        return Directory.EnumerateFiles(objectsDirectory, "*", SafeRecursiveEnumeration())
            .Where(path => IsSha256(Path.GetFileName(path)))
            .Select(path => new FileInfo(path))
            .Select(file => new LocalObject(
                file.Name.ToLowerInvariant(), file.FullName, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .GroupBy(item => item.Oid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddOids(string output, HashSet<string> target)
    {
        foreach (Match match in ShaRegex.Matches(output))
            target.Add(match.Value.ToLowerInvariant());
    }

    private static void AddEvidence(
        Dictionary<string, List<LfsRetentionEvidence>> evidence,
        string oid,
        LfsRetentionEvidence item)
    {
        if (!evidence.TryGetValue(oid, out List<LfsRetentionEvidence>? entries))
            evidence[oid] = entries = [];
        entries.Add(item);
    }

    private async Task<ProcessResult> RunGitAsync(
        string repository,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken) =>
        await _runner.RunAsync(_gitExecutable, arguments, Path.GetFullPath(repository), cancellationToken);

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task EnsureHashAsync(string path, string expectedOid, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Required LFS retention copy is missing: {path}");
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(actual, expectedOid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"LFS object {expectedOid} failed SHA-256 verification at '{path}'.");
    }

    private static async Task<LfsArchiveManifest> ReadArchiveManifestAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return new LfsArchiveManifest([]);
        string path = Path.Combine(Path.GetFullPath(archivePath), "lfs-archive-manifest.json");
        if (!File.Exists(path))
            return new LfsArchiveManifest([]);
        try
        {
            return JsonSerializer.Deserialize<LfsArchiveManifest>(
                       await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions)
                   ?? new LfsArchiveManifest([]);
        }
        catch (JsonException)
        {
            return new LfsArchiveManifest([]);
        }
    }

    private static async Task WriteArchiveManifestAsync(
        string archiveRoot,
        LfsArchiveManifest manifest,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(archiveRoot, "lfs-archive-manifest.json");
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static string GetArchiveObjectPath(string archiveRoot, string oid) =>
        Path.Combine(Path.GetFullPath(archiveRoot), "objects", oid[..2], oid.Substring(2, 2), oid);

    private static string ValidateArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new InvalidOperationException("Choose an archive directory before creating LFS retention copies.");
        return Path.GetFullPath(archivePath);
    }

    private static void ValidateFreshPlan(LfsCleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (DateTimeOffset.UtcNow - plan.CreatedAt > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("The LFS cleanup plan is stale. Analyze the repository again.");
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsInside(string path, string parent)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
                                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RemoveEmptyObjectDirectories(string objectPath, string storageRoot)
    {
        string? directory = Path.GetDirectoryName(objectPath);
        string stop = Path.Combine(Path.GetFullPath(storageRoot), "objects");
        while (directory is not null && IsInside(directory, stop) && !PathsEqual(directory, stop))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                break;
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    private static EnumerationOptions SafeRecursiveEnumeration() => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.Succeeded)
            return;
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Exit code {result.ExitCode}."
            : result.StandardError.Trim();
        throw new GitOperationException($"{message} {detail}");
    }

    private sealed record LocalObject(string Oid, string Path, long Size, DateTimeOffset LastModifiedAt);
    private sealed record LfsStorageOwner(string RepositoryPath, DateTimeOffset CreatedAt);
    private sealed record LfsArchiveEntry(string OidSha256, long Size, DateTimeOffset VerifiedAt);
    private sealed record LfsArchiveManifest(IReadOnlyList<LfsArchiveEntry> Objects);
}
