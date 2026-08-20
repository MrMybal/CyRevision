using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace CyRevision.Git;

public sealed class LfsStorageManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex ShaRegex = new("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])", RegexOptions.Compiled);
    private static readonly Regex LfsHistoryLineRegex = new(
        "^(?<oid>[0-9a-fA-F]{64})\\s+[*-]\\s+(?<path>.+?)(?:\\s+\\([^)]*\\))?$",
        RegexOptions.Compiled);
    private readonly ProcessRunner _runner = new();
    private readonly string _gitExecutable;

    public LfsStorageManager(string gitExecutable = "git") => _gitExecutable = gitExecutable;

    public async Task<LfsCleanupPlan> AnalyzeAsync(
        string repositoryPath,
        LfsManagementProfile profile,
        PeerLfsAvailabilityCache? peerAvailability = null,
        IProgress<LfsAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        string repository = Path.GetFullPath(repositoryPath);
        progress?.Report(new LfsAnalysisProgress("Preparing", 2, "Resolving Git and LFS storage paths…"));
        LfsStoragePaths paths = await LfsStoragePathResolver.ResolveAsync(
            _runner, _gitExecutable, repository, cancellationToken);
        progress?.Report(new LfsAnalysisProgress("Local objects", 5, "Reading the local LFS object index…"));
        IReadOnlyList<LocalObject> localObjects = EnumerateLocalObjects(
            paths.ObjectsDirectory, cancellationToken, progress);
        progress?.Report(new LfsAnalysisProgress(
            "LFS history", 24, $"{localObjects.Count:N0} local object(s) indexed; reading Git LFS history…"));
        IReadOnlyList<LfsHistoryEntry> history = await ReadLfsHistoryAsync(repository, cancellationToken);
        Dictionary<string, LocalObject> localByOid = localObjects.ToDictionary(
            item => item.Oid, StringComparer.OrdinalIgnoreCase);
        HashSet<string> recentVersions = BuildRecentVersionSet(history, localByOid, profile);
        progress?.Report(new LfsAnalysisProgress(
            "Protected branches", 36, $"Protecting current files and every retained local branch ({history.Count:N0} history entries)…"));
        HashSet<string> referenced = await ReadProtectedOidsAsync(
            repository, profile, history, recentVersions, progress, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        (HashSet<string> remoteOids, string remoteOutput) = profile.VerifyRemote
            ? await VerifyRemoteCandidatesAsync(repository, profile, progress, cancellationToken)
            : (new HashSet<string>(StringComparer.OrdinalIgnoreCase), "Remote verification disabled.");
        progress?.Report(new LfsAnalysisProgress(
            "Retention evidence", 90, "Reading verified peer and archive evidence…"));
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
        Dictionary<string, string[]> pathsByOid = history
            .GroupBy(item => item.Oid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                StringComparer.OrdinalIgnoreCase);
        LfsCleanupItem[] items = localObjects
            .Select(item =>
            {
                string[] repositoryPaths = pathsByOid.GetValueOrDefault(item.Oid) ?? [];
                string extension = repositoryPaths
                    .Select(Path.GetExtension)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Unknown";
                return new LfsCleanupItem(
                    item.Oid,
                    item.Size,
                    item.LastModifiedAt,
                    item.Path,
                    referenced.Contains(item.Oid),
                    item.LastModifiedAt > graceCutoff,
                    evidence.TryGetValue(item.Oid, out List<LfsRetentionEvidence>? copies)
                        ? copies.DistinctBy(copy => (copy.Kind, copy.LocationId)).ToArray()
                        : [],
                    profile.RequiredVerifiedCopies,
                    repositoryPaths,
                    extension,
                    recentVersions.Contains(item.Oid));
            })
            .OrderBy(item => item.CanDelete ? 0 : 1)
            .ThenByDescending(item => item.Size)
            .ToArray();

        LfsCleanupPlan plan = new(
            Guid.NewGuid(), repository, paths.StorageDirectory, now,
            profile.RequiredVerifiedCopies, items, remoteOutput, profile);
        progress?.Report(new LfsAnalysisProgress(
            "Complete", 100,
            $"{plan.Objects.Count:N0} object(s) classified; {plan.ReclaimableCount:N0} safely reclaimable."));
        return plan;
    }

    public async Task<RepositoryStorageReport> AnalyzeRepositoryStorageAsync(
        string repositoryPath,
        int largestFileLimit = 150,
        CancellationToken cancellationToken = default)
    {
        string repository = Path.GetFullPath(repositoryPath);
        LfsStoragePaths paths = await LfsStoragePathResolver.ResolveAsync(
            _runner, _gitExecutable, repository, cancellationToken);
        string cyRevisionCache = Path.Combine(repository, ".cyrevision");
        DirectorySize working = MeasureDirectory(
            repository,
            [paths.GitCommonDirectory, cyRevisionCache],
            cancellationToken);
        DirectorySize git = MeasureDirectory(
            paths.GitCommonDirectory,
            [paths.StorageDirectory],
            cancellationToken);
        DirectorySize lfs = MeasureDirectory(paths.StorageDirectory, [], cancellationToken);
        DirectorySize cache = MeasureDirectory(cyRevisionCache, [], cancellationToken);
        RepositoryStorageArea[] areas =
        [
            new("Working tree", repository, working.Bytes, working.Files),
            new("Git metadata", paths.GitCommonDirectory, git.Bytes, git.Files),
            new("Git LFS cache", paths.StorageDirectory, lfs.Bytes, lfs.Files),
            new("CyRevision cache", cyRevisionCache, cache.Bytes, cache.Files)
        ];
        RepositoryLargeFile[] largest = EnumerateMeasuredFiles(repository, [paths.GitCommonDirectory, cyRevisionCache], cancellationToken)
            .Select(file => new RepositoryLargeFile(
                Path.GetRelativePath(repository, file.Path),
                "Working tree",
                string.IsNullOrWhiteSpace(Path.GetExtension(file.Path)) ? "No extension" : Path.GetExtension(file.Path),
                file.Size))
            .OrderByDescending(file => file.Size)
            .Take(Math.Clamp(largestFileLimit, 10, 1000))
            .ToArray();
        return new RepositoryStorageReport(repository, DateTimeOffset.UtcNow, areas, largest);
    }

    public async Task<LfsPruneResult> RunNativePruneAsync(
        string repositoryPath,
        bool dryRun,
        bool verifyRemote = true,
        int timeoutSeconds = 300,
        IProgress<LfsAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds is < 10 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "The Git LFS prune timeout must be between 10 and 3600 seconds.");

        string repository = Path.GetFullPath(repositoryPath);
        List<string> arguments = ["lfs", "prune", "--verbose"];
        if (dryRun) arguments.Add("--dry-run");
        if (verifyRemote) arguments.Add("--verify-remote");

        progress?.Report(new LfsAnalysisProgress(
            "Git LFS prune",
            5,
            dryRun
                ? "Starting the native non-destructive prune preview…"
                : "Verifying the remote before removing eligible local LFS objects…"));

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProcessResult result;
        try
        {
            result = await RunGitAsync(repository, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitOperationException(
                $"Git LFS prune exceeded the {timeoutSeconds}-second safety limit and was stopped.",
                exception);
        }

        string output = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput.Trim(), result.StandardError.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!result.Succeeded)
            throw new GitOperationException(string.IsNullOrWhiteSpace(output)
                ? "Git LFS prune failed without returning details."
                : output);

        progress?.Report(new LfsAnalysisProgress(
            "Git LFS prune",
            100,
            dryRun ? "Preview complete; no object was deleted." : "Native prune completed successfully."));
        return new LfsPruneResult(dryRun, verifyRemote, stopwatch.Elapsed, output);
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
        LfsManagementProfile policy = plan.Policy
            ?? LfsManagementProfile.CreateDefault(Guid.NewGuid()) with
            {
                RequiredVerifiedCopies = plan.RequiredCopies,
                VerifyRemote = false
            };
        IReadOnlyList<LfsHistoryEntry> history = await ReadLfsHistoryAsync(plan.RepositoryPath, cancellationToken);
        Dictionary<string, LocalObject> localByOid = plan.Objects.ToDictionary(
            item => item.OidSha256,
            item => new LocalObject(item.OidSha256, item.LocalPath, item.Size, item.LastModifiedAt),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> recentVersions = BuildRecentVersionSet(history, localByOid, policy);
        HashSet<string> protectedNow = await ReadProtectedOidsAsync(
            plan.RepositoryPath, policy, history, recentVersions, progress: null, cancellationToken);
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

    private async Task<HashSet<string>> ReadProtectedOidsAsync(
        string repository,
        LfsManagementProfile profile,
        IReadOnlyList<LfsHistoryEntry> history,
        IReadOnlySet<string> recentVersions,
        IProgress<LfsAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        ProcessResult currentIndex = await RunGitAsync(
            repository,
            ["lfs", "ls-files", "--long"],
            cancellationToken);
        if (currentIndex.Succeeded)
            AddOids(currentIndex.StandardOutput, result);

        // Every LFS object present at the tip of a retained local branch stays hot. The cached
        // inventory is keyed by branch commit, so unchanged branches are not rescanned.
        result.UnionWith(await ReadLocalBranchTipOidsAsync(repository, progress, cancellationToken));

        // Tags, stashes and notes remain explicit hot-storage protections.
        string[] protectedNamespaces = ["refs/tags", "refs/stash", "refs/notes"];
        ProcessResult refs = await RunGitAsync(
            repository,
            ["for-each-ref", "--format=%(refname)", .. protectedNamespaces],
            cancellationToken);
        if (refs.Succeeded)
        {
            string[] references = refs.StandardOutput.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            progress?.Report(new LfsAnalysisProgress(
                "Protected tags and refs", 63, $"Scanning {references.Length:N0} tag, stash, or note ref(s)…"));
            ConcurrentDictionary<string, byte> protectedRefOids = new(StringComparer.OrdinalIgnoreCase);
            int completedRefs = 0;
            await Parallel.ForEachAsync(
                references,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 4 },
                async (reference, token) =>
                {
                ProcessResult referenced = await RunGitAsync(
                    repository,
                    ["lfs", "ls-files", "--long", reference],
                        token);
                if (referenced.Succeeded)
                    {
                        foreach (Match match in ShaRegex.Matches(referenced.StandardOutput))
                            protectedRefOids.TryAdd(match.Value.ToLowerInvariant(), 0);
                    }
                    int currentRef = Interlocked.Increment(ref completedRefs);
                    int percent = references.Length == 0
                        ? 66
                        : 63 + (int)Math.Round(3d * currentRef / references.Length);
                    progress?.Report(new LfsAnalysisProgress(
                        "Protected tags and refs",
                        Math.Clamp(percent, 63, 66),
                        $"Scanned {currentRef:N0}/{references.Length:N0} protected ref(s)."));
                });
            result.UnionWith(protectedRefOids.Keys);
        }

        if (profile.TrimRemoteBackedHistory)
        {
            IReadOnlySet<string> managedExtensions = profile.ParseManagedExtensions();
            foreach (IGrouping<string, LfsHistoryEntry> objectHistory in history.GroupBy(
                         item => item.Oid, StringComparer.OrdinalIgnoreCase))
            {
                bool hasUnmanagedPath = objectHistory.Any(item =>
                    !managedExtensions.Contains(Path.GetExtension(item.Path)));
                if (hasUnmanagedPath || recentVersions.Contains(objectHistory.Key))
                    result.Add(objectHistory.Key);
            }
        }

        ProcessResult worktrees = await RunGitAsync(repository, ["worktree", "list", "--porcelain"], cancellationToken);
        if (worktrees.Succeeded)
        {
            string[] worktreePaths = worktrees.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal))
                .Select(line => line["worktree ".Length..])
                .ToArray();
            for (int index = 0; index < worktreePaths.Length; index++)
            {
                string path = worktreePaths[index];
                progress?.Report(new LfsAnalysisProgress(
                    "Protected worktrees",
                    worktreePaths.Length == 0 ? 69 : 67 + (int)Math.Round(2d * index / worktreePaths.Length),
                    $"Checking worktree {index + 1:N0}/{worktreePaths.Length:N0}: {Path.GetFileName(path)}"));
                ProcessResult current = await RunGitAsync(path, ["lfs", "ls-files", "--long"], cancellationToken);
                if (current.Succeeded)
                    AddOids(current.StandardOutput, result);
            }
        }

        progress?.Report(new LfsAnalysisProgress(
            "Local protection complete", 69, $"{result.Count:N0} local LFS object(s) are protected before remote checks."));

        return result;
    }

    private async Task<HashSet<string>> ReadLocalBranchTipOidsAsync(
        string repository,
        IProgress<LfsAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ProcessResult refs = await RunGitAsync(
            repository,
            ["for-each-ref", "--format=%(refname)%09%(objectname)", "refs/heads"],
            cancellationToken);
        if (!refs.Succeeded)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> branches = refs.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        LfsStoragePaths storagePaths = await LfsStoragePathResolver.ResolveAsync(
            _runner, _gitExecutable, repository, cancellationToken);
        string cacheDirectory = Path.Combine(storagePaths.GitCommonDirectory, ".cyrevision", "cache");
        string cachePath = Path.Combine(cacheDirectory, "lfs-branch-tips.json");
        string branchFingerprintSource = string.Join('\n', branches
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}\t{item.Value}"));
        string branchFingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(branchFingerprintSource))).ToLowerInvariant();
        LfsBranchTipCache cached = await ReadBranchTipCacheAsync(cachePath, cancellationToken);
        if (cached.Version == 2 &&
            string.Equals(cached.BranchFingerprint, branchFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new LfsAnalysisProgress(
                "Protected branches",
                62,
                $"Protected {branches.Count:N0} retained local branch tip(s) from the deduplicated cache ({cached.Oids.Count:N0} object(s))."));
            return cached.Oids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        string[] uniqueTips = branches.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        progress?.Report(new LfsAnalysisProgress(
            "Protected branches",
            42,
            $"Scanning {uniqueTips.Length:N0} unique tip(s) for {branches.Count:N0} retained local branch(es)…"));
        ConcurrentDictionary<string, byte> protectedOids = new(StringComparer.OrdinalIgnoreCase);
        int completed = 0;

        await Parallel.ForEachAsync(
            uniqueTips,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4)
            },
            async (commit, token) =>
            {
                ProcessResult listed = await RunGitAsync(repository, ["lfs", "ls-files", "--long", commit], token);
                if (listed.Succeeded)
                {
                    foreach (Match match in ShaRegex.Matches(listed.StandardOutput))
                        protectedOids.TryAdd(match.Value.ToLowerInvariant(), 0);
                }
                int current = Interlocked.Increment(ref completed);
                int percent = uniqueTips.Length == 0
                    ? 62
                    : 42 + (int)Math.Round(20d * current / uniqueTips.Length);
                progress?.Report(new LfsAnalysisProgress(
                    "Protected branches",
                    Math.Clamp(percent, 42, 62),
                    $"Scanned {current:N0}/{uniqueTips.Length:N0} unique tip(s); {protectedOids.Count:N0} LFS object(s) protected."));
            });
        string[] protectedOidArray = protectedOids.Keys.Order(StringComparer.Ordinal).ToArray();
        progress?.Report(new LfsAnalysisProgress(
            "Finalizing branch protection",
            63,
            $"Writing one deduplicated inventory for {branches.Count:N0} branches ({protectedOidArray.Length:N0} unique object(s))…"));

        LfsBranchTipCache updated = new(
            2,
            DateTimeOffset.UtcNow,
            branchFingerprint,
            protectedOidArray);
        Directory.CreateDirectory(cacheDirectory);
        string temporary = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(updated, JsonOptions), cancellationToken);
        File.Move(temporary, cachePath, true);
        return protectedOids.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<LfsBranchTipCache> ReadBranchTipCacheAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 32L * 1024 * 1024)
            return LfsBranchTipCache.Empty;
        try
        {
            return JsonSerializer.Deserialize<LfsBranchTipCache>(
                       await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions)
                   ?? LfsBranchTipCache.Empty;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return LfsBranchTipCache.Empty;
        }
    }

    private async Task<(HashSet<string> Oids, string Output)> VerifyRemoteCandidatesAsync(
        string repository,
        LfsManagementProfile profile,
        IProgress<LfsAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        string remoteName = profile.RemoteName;
        ProcessResult remote = await RunGitAsync(repository, ["remote", "get-url", remoteName], cancellationToken);
        if (!remote.Succeeded)
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                $"Remote '{remoteName}' is unavailable; no remote retention proof was accepted.");

        int timeoutSeconds = profile.RemoteVerificationTimeoutSeconds;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        Stopwatch stopwatch = Stopwatch.StartNew();
        progress?.Report(new LfsAnalysisProgress(
            "Remote verification", 70,
            $"Checking '{remoteName}' with a {timeoutSeconds:N0} second safety budget…"));
        Task<ProcessResult> verification = RunGitAsync(repository,
            ["-c", $"lfs.pruneremotetocheck={remoteName}", "lfs", "prune", "--dry-run", "--recent",
                "--verify-remote", "--verify-unreachable", "--when-unverified=continue", "--verbose"],
            timeout.Token);
        try
        {
            while (!verification.IsCompleted)
            {
                Task pulse = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                if (await Task.WhenAny(verification, pulse) == verification)
                    break;
                int elapsed = Math.Min(timeoutSeconds, (int)stopwatch.Elapsed.TotalSeconds);
                int percent = 70 + (int)Math.Round(17d * elapsed / timeoutSeconds);
                progress?.Report(new LfsAnalysisProgress(
                    "Remote verification",
                    Math.Clamp(percent, 70, 87),
                    $"Remote proof scan: {elapsed:N0}/{timeoutSeconds:N0} s. Cancel is always safe; no files are changed."));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                $"Remote verification stopped after the {timeoutSeconds:N0} second safety budget. " +
                "No remote retention proof was accepted and no object is eligible on remote evidence alone. " +
                "Increase the budget explicitly or use a verified archive for very large LFS stores.");
        }

        ProcessResult prune;
        try
        {
            prune = await verification;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                $"Remote verification stopped after the {timeoutSeconds:N0} second safety budget. " +
                "No remote retention proof was accepted and no object is eligible on remote evidence alone. " +
                "Increase the budget explicitly or use a verified archive for very large LFS stores.");
        }
        HashSet<string> verified = new(StringComparer.OrdinalIgnoreCase);
        if (prune.Succeeded)
            AddOids(prune.StandardOutput, verified);
        string output = string.Join(Environment.NewLine, new[] { prune.StandardOutput, prune.StandardError }
            .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return (verified, string.IsNullOrWhiteSpace(output) ? "Remote verification completed." : output);
    }

    private static IReadOnlyList<LocalObject> EnumerateLocalObjects(
        string objectsDirectory,
        CancellationToken cancellationToken,
        IProgress<LfsAnalysisProgress>? progress)
    {
        if (!Directory.Exists(objectsDirectory))
            return [];
        Dictionary<string, LocalObject> objects = new(StringComparer.OrdinalIgnoreCase);
        int inspected = 0;
        foreach (string path in Directory.EnumerateFiles(objectsDirectory, "*", SafeRecursiveEnumeration()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            inspected++;
            string name = Path.GetFileName(path);
            if (IsSha256(name) && !objects.ContainsKey(name))
            {
                FileInfo file = new(path);
                objects[name] = new LocalObject(
                    name.ToLowerInvariant(),
                    file.FullName,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
            }

            if (inspected % 1_000 == 0)
            {
                progress?.Report(new LfsAnalysisProgress(
                    "Local objects", 12,
                    $"{objects.Count:N0} LFS object(s) indexed; {inspected:N0} cache files inspected…"));
            }
        }
        return objects.Values.ToArray();
    }

    private async Task<IReadOnlyList<LfsHistoryEntry>> ReadLfsHistoryAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        ProcessResult history = await RunGitAsync(
            repository,
            ["-c", "core.quotePath=false", "lfs", "ls-files", "--all", "--deleted", "--long", "--size"],
            cancellationToken);
        if (!history.Succeeded)
            return [];

        return history.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLfsHistoryLine)
            .Where(item => item is not null)
            .Select(item => item!)
            .DistinctBy(item => (item.Oid, item.Path), LfsHistoryEntryComparer.Instance)
            .ToArray();
    }

    private static LfsHistoryEntry? ParseLfsHistoryLine(string line)
    {
        Match match = LfsHistoryLineRegex.Match(line);
        if (!match.Success)
            return null;
        string path = match.Groups["path"].Value.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        return new LfsHistoryEntry(match.Groups["oid"].Value.ToLowerInvariant(), path.Replace('\\', '/'));
    }

    private static HashSet<string> BuildRecentVersionSet(
        IReadOnlyList<LfsHistoryEntry> history,
        IReadOnlyDictionary<string, LocalObject> localByOid,
        LfsManagementProfile profile)
    {
        HashSet<string> recent = new(StringComparer.OrdinalIgnoreCase);
        if (!profile.TrimRemoteBackedHistory)
            return recent;
        IReadOnlySet<string> extensions = profile.ParseManagedExtensions();
        foreach (IGrouping<string, LfsHistoryEntry> pathHistory in history
                     .Where(item => extensions.Contains(Path.GetExtension(item.Path)))
                     .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (LfsHistoryEntry item in pathHistory
                         .DistinctBy(entry => entry.Oid, StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(entry => localByOid.TryGetValue(entry.Oid, out LocalObject? local)
                             ? local.LastModifiedAt
                             : DateTimeOffset.MinValue)
                         .ThenBy(entry => entry.Oid, StringComparer.OrdinalIgnoreCase)
                         .Take(profile.RecentVersionsPerFile))
            {
                recent.Add(item.Oid);
            }
        }
        return recent;
    }

    private static DirectorySize MeasureDirectory(
        string directory,
        IReadOnlyCollection<string> excludedRoots,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        int files = 0;
        foreach (MeasuredFile file in EnumerateMeasuredFiles(directory, excludedRoots, cancellationToken))
        {
            bytes = checked(bytes + file.Size);
            files++;
        }
        return new DirectorySize(bytes, files);
    }

    private static IEnumerable<MeasuredFile> EnumerateMeasuredFiles(
        string directory,
        IReadOnlyCollection<string> excludedRoots,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            yield break;
        string[] exclusions = excludedRoots
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Path.GetFullPath)
            .ToArray();
        Stack<string> pending = new();
        pending.Push(Path.GetFullPath(directory));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();
            if (exclusions.Any(root => PathsEqual(current, root) || IsInside(current, root)))
                continue;
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(current).ToArray();
                directories = Directory.EnumerateDirectories(current).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string path in files)
            {
                FileInfo file;
                try { file = new FileInfo(path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                yield return new MeasuredFile(file.FullName, file.Length);
            }
            foreach (string child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
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
    private sealed record LfsHistoryEntry(string Oid, string Path);
    private sealed record DirectorySize(long Bytes, int Files);
    private sealed record MeasuredFile(string Path, long Size);
    private sealed record LfsBranchTipCache(
        int Version,
        DateTimeOffset UpdatedAt,
        string BranchFingerprint,
        IReadOnlyList<string> Oids)
    {
        public static LfsBranchTipCache Empty { get; } = new(2, DateTimeOffset.MinValue, string.Empty, []);
    }

    private sealed class LfsHistoryEntryComparer : IEqualityComparer<(string Oid, string Path)>
    {
        public static LfsHistoryEntryComparer Instance { get; } = new();

        public bool Equals((string Oid, string Path) x, (string Oid, string Path) y) =>
            string.Equals(x.Oid, y.Oid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Oid, string Path) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Oid),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path));
    }
}
