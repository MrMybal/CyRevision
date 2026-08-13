using System.Globalization;
using System.Text.Json;

namespace CyRevision.Git;

public sealed partial class GitCliRepositoryService
{
    private static readonly TimeSpan CompositionPlanLifetime = TimeSpan.FromMinutes(15);

    public async Task<GitBranchComparison> CompareBranchesAsync(
        string repositoryPath,
        string sourceBranch,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);

        string sourceTip = await ResolveCommitAsync(repositoryPath, sourceBranch, cancellationToken).ConfigureAwait(false);
        string targetTip = await ResolveCommitAsync(repositoryPath, targetBranch, cancellationToken).ConfigureAwait(false);
        ProcessResult mergeBaseResult = await RunGitResultAsync(
            repositoryPath,
            ["merge-base", targetTip, sourceTip],
            cancellationToken).ConfigureAwait(false);

        const string format = "%m%x1f%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s";
        ProcessResult logResult = await RunGitResultAsync(
            repositoryPath,
            ["log", "--left-right", "--cherry-mark", $"--format={format}", $"{targetTip}...{sourceTip}"],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(logResult, "Unable to compare branches.");

        List<GitBranchComparisonCommit> commits = [];
        foreach (string line in logResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\x1f');
            if (fields.Length < 7)
            {
                continue;
            }

            char marker = fields[0].Length > 0 ? fields[0][0] : ' ';
            GitBranchCommitPresence presence = marker switch
            {
                '>' => GitBranchCommitPresence.SourceOnly,
                '<' => GitBranchCommitPresence.TargetOnly,
                '=' => GitBranchCommitPresence.PatchEquivalent,
                _ => GitBranchCommitPresence.PatchEquivalent
            };
            string side = marker == '<' ? "Target" : marker == '>' ? "Source" : "Equivalent";
            DateTimeOffset.TryParse(fields[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt);
            commits.Add(new GitBranchComparisonCommit(
                new GitRevision(fields[1], fields[2], fields[3], fields[4], authoredAt, fields[6]),
                presence,
                side));
        }

        return new GitBranchComparison(
            sourceBranch,
            targetBranch,
            sourceTip,
            targetTip,
            mergeBaseResult.Succeeded ? mergeBaseResult.StandardOutput.Trim() : null,
            commits);
    }

    public async Task<GitCherryPickPlan> CreateCherryPickPlanAsync(
        string repositoryPath,
        string sourceBranch,
        string targetBranch,
        IReadOnlyList<string> orderedCommitHashes,
        GitCherryPickMode mode,
        string? combinedCommitMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);
        ArgumentNullException.ThrowIfNull(orderedCommitHashes);

        ProcessResult targetBranchResult = await RunGitResultAsync(
            repositoryPath,
            ["show-ref", "--verify", "--quiet", $"refs/heads/{targetBranch}"],
            cancellationToken).ConfigureAwait(false);
        if (!targetBranchResult.Succeeded)
        {
            throw new GitOperationException("The cherry-pick target must be an existing local branch.");
        }

        GitBranchComparison comparison = await CompareBranchesAsync(
            repositoryPath,
            sourceBranch,
            targetBranch,
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, GitRevision> available = comparison.Commits
            .Where(commit => commit.Presence == GitBranchCommitPresence.SourceOnly)
            .ToDictionary(commit => commit.Revision.Hash, commit => commit.Revision, StringComparer.OrdinalIgnoreCase);

        List<GitRevision> ordered = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string requestedHash in orderedCommitHashes)
        {
            GitRevision? revision = available.Values.FirstOrDefault(candidate =>
                candidate.Hash.Equals(requestedHash, StringComparison.OrdinalIgnoreCase) ||
                candidate.ShortHash.Equals(requestedHash, StringComparison.OrdinalIgnoreCase));
            if (revision is null)
            {
                throw new GitOperationException($"Commit {requestedHash} is not a source-only commit in this comparison.");
            }

            if (seen.Add(revision.Hash))
            {
                ordered.Add(revision);
            }
        }

        GitRepositoryStatus status = await GetStatusAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        bool usesTemporaryWorktree = !string.Equals(status.CurrentBranch, targetBranch, StringComparison.Ordinal);
        List<string> warnings = [];
        if (ordered.Count == 0)
        {
            warnings.Add("Select at least one source-only commit.");
        }

        if (mode == GitCherryPickMode.CombineIntoOne && string.IsNullOrWhiteSpace(combinedCommitMessage))
        {
            warnings.Add("A commit message is required when combining commits.");
        }

        foreach (GitRevision revision in ordered)
        {
            ProcessResult parents = await RunGitResultAsync(
                repositoryPath,
                ["rev-list", "--parents", "-n", "1", revision.Hash],
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(parents, $"Unable to inspect commit {revision.ShortHash}.");
            if (parents.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 2)
            {
                warnings.Add($"Merge commit {revision.ShortHash} requires an explicit mainline and cannot be composed here yet.");
            }
        }

        if (!usesTemporaryWorktree && status.Changes.Count > 0)
        {
            warnings.Add("The target branch is currently open and its working tree is not clean.");
        }

        if (usesTemporaryWorktree && await IsBranchCheckedOutElsewhereAsync(repositoryPath, targetBranch, cancellationToken).ConfigureAwait(false))
        {
            warnings.Add("The target branch is already checked out in another worktree.");
        }

        return new GitCherryPickPlan(
            Guid.NewGuid(),
            sourceBranch,
            targetBranch,
            comparison.TargetTip,
            DateTimeOffset.UtcNow,
            ordered,
            mode,
            combinedCommitMessage?.Trim(),
            usesTemporaryWorktree,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<GitCherryPickResult> ApplyCherryPickPlanAsync(
        string repositoryPath,
        GitCherryPickPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply)
        {
            throw new GitOperationException("The cherry-pick plan contains blocking warnings.");
        }
        EnsurePlanIsFresh(plan.CreatedAt);

        string targetTip = await ResolveCommitAsync(repositoryPath, plan.TargetBranch, cancellationToken).ConfigureAwait(false);
        if (!targetTip.Equals(plan.TargetTip, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitOperationException("The target branch changed after the preview. Build the plan again.");
        }

        string executionPath = Path.GetFullPath(repositoryPath);
        string? temporaryWorktree = null;
        try
        {
            if (plan.UsesTemporaryWorktree)
            {
                temporaryWorktree = Path.Combine(Path.GetTempPath(), "CyRevision", "cherry-pick", plan.Id.ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryWorktree)!);
                ProcessResult addWorktree = await RunGitResultAsync(
                    repositoryPath,
                    ["worktree", "add", "--quiet", temporaryWorktree, plan.TargetBranch],
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(addWorktree, "Unable to create the isolated cherry-pick worktree.");
                executionPath = temporaryWorktree;
            }
            else
            {
                GitRepositoryStatus status = await GetStatusAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(status.CurrentBranch, plan.TargetBranch, StringComparison.Ordinal) || status.Changes.Count > 0)
                {
                    throw new GitOperationException("The active target worktree must remain on the selected branch and be clean.");
                }
            }

            List<string> cherryPickArguments = ["cherry-pick"];
            if (plan.Mode == GitCherryPickMode.CombineIntoOne)
            {
                cherryPickArguments.Add("--no-commit");
            }
            cherryPickArguments.AddRange(plan.OrderedCommits.Select(commit => commit.Hash));

            ProcessResult cherryPick = await RunGitResultAsync(executionPath, cherryPickArguments, cancellationToken).ConfigureAwait(false);
            if (!cherryPick.Succeeded)
            {
                await RollBackCherryPickAsync(executionPath, plan.TargetTip, cancellationToken).ConfigureAwait(false);
                throw new GitOperationException("Cherry-pick stopped and was rolled back. " + ReadGitError(cherryPick));
            }

            if (plan.Mode == GitCherryPickMode.CombineIntoOne)
            {
                ProcessResult commit = await RunGitResultAsync(
                    executionPath,
                    ["commit", "-m", plan.CombinedCommitMessage!],
                    cancellationToken).ConfigureAwait(false);
                if (!commit.Succeeded)
                {
                    await RollBackCherryPickAsync(executionPath, plan.TargetTip, cancellationToken).ConfigureAwait(false);
                    throw new GitOperationException("The combined commit could not be created and was rolled back. " + ReadGitError(commit));
                }
            }

            string newTip = await ResolveCommitAsync(executionPath, "HEAD", cancellationToken).ConfigureAwait(false);
            return new GitCherryPickResult(
                plan.TargetBranch,
                plan.TargetTip,
                newTip,
                plan.OrderedCommits.Count,
                plan.Mode);
        }
        finally
        {
            if (temporaryWorktree is not null)
            {
                await RunGitResultAsync(
                    repositoryPath,
                    ["worktree", "remove", "--force", temporaryWorktree],
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task<GitMultiRestorePlan> CreateMultiRestorePlanAsync(
        string repositoryPath,
        string commitHash,
        IReadOnlyList<GitMultiRestoreSelection> selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);
        ArgumentNullException.ThrowIfNull(selections);

        GitCommitDetails details = await GetCommitDetailsAsync(repositoryPath, commitHash, cancellationToken).ConfigureAwait(false);
        string repositoryHead = await ResolveCommitAsync(repositoryPath, "HEAD", cancellationToken).ConfigureAwait(false);
        string? parentHash = details.ParentHashes.FirstOrDefault();
        LfsStoragePaths lfsStorage = await LfsStoragePathResolver.ResolveAsync(
            _processRunner,
            _gitExecutable,
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, GitMultiRestoreOperation> operations = new(StringComparer.OrdinalIgnoreCase);
        List<string> warnings = [];
        foreach (GitMultiRestoreSelection selection in selections)
        {
            GitCommitFileChange? change = details.Files.FirstOrDefault(file =>
                file.Path.Equals(selection.Path, StringComparison.OrdinalIgnoreCase));
            if (change is null)
            {
                throw new GitOperationException($"{selection.Path} is not part of commit {details.Revision.ShortHash}.");
            }

            foreach ((string path, GitMultiRestoreOperationKind kind, string? source) in BuildRestoreOperations(change, selection.RestorePoint, details.Revision.Hash, parentHash))
            {
                string safePath = NormalizeAndValidateGitPath(path);
                LfsPointerInfo? pointer = kind == GitMultiRestoreOperationKind.Restore && source is not null
                    ? await TryReadLfsPointerAtRevisionAsync(repositoryPath, source, safePath, cancellationToken).ConfigureAwait(false)
                    : null;
                string? lfsObjectPath = pointer is null ? null : lfsStorage.GetObjectPath(pointer.OidSha256);
                bool lfsAvailable = pointer is null || File.Exists(lfsObjectPath);
                bool hasLocalChanges = await HasLocalChangesAsync(repositoryPath, safePath, cancellationToken).ConfigureAwait(false);

                if (pointer is not null && !lfsAvailable)
                {
                    warnings.Add($"Missing local LFS object {pointer.OidSha256[..12]} for {safePath}.");
                }

                operations[safePath] = new GitMultiRestoreOperation(
                    change.Path,
                    safePath,
                    kind,
                    source,
                    selection.RestorePoint,
                    hasLocalChanges,
                    pointer,
                    lfsAvailable,
                    lfsObjectPath);
            }
        }

        if (operations.Values.Any(operation => operation.HasLocalChanges))
        {
            warnings.Add("Some selected paths contain local changes. Explicit overwrite confirmation is required.");
        }

        return new GitMultiRestorePlan(
            Guid.NewGuid(),
            details.Revision.Hash,
            parentHash,
            repositoryHead,
            DateTimeOffset.UtcNow,
            operations.Values.OrderBy(operation => operation.WorkingTreePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<GitMultiRestoreResult> ApplyMultiRestorePlanAsync(
        string repositoryPath,
        GitMultiRestorePlan plan,
        bool overwriteLocalChanges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply)
        {
            throw new GitOperationException("The restore plan is blocked by missing LFS objects or contains no operation.");
        }
        EnsurePlanIsFresh(plan.CreatedAt);

        string head = await ResolveCommitAsync(repositoryPath, "HEAD", cancellationToken).ConfigureAwait(false);
        if (!head.Equals(plan.RepositoryHead, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitOperationException("HEAD changed after the preview. Build the restore plan again.");
        }

        foreach (GitMultiRestoreOperation operation in plan.Operations)
        {
            bool changed = await HasLocalChangesAsync(repositoryPath, operation.WorkingTreePath, cancellationToken).ConfigureAwait(false);
            if (changed && !overwriteLocalChanges)
            {
                throw new GitOperationException($"{operation.WorkingTreePath} contains local changes. Confirm overwrite or remove it from the plan.");
            }
        }

        LfsStoragePaths storage = await LfsStoragePathResolver.ResolveAsync(
            _processRunner,
            _gitExecutable,
            repositoryPath,
            cancellationToken).ConfigureAwait(false);
        string backupDirectory = Path.Combine(
            storage.GitCommonDirectory,
            "cyrevision",
            "multi-restore",
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{plan.Id:N}");
        string filesBackupDirectory = Path.Combine(backupDirectory, "files");
        Directory.CreateDirectory(filesBackupDirectory);

        string repositoryRoot = (await RunGitAsync(repositoryPath, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false)).Trim();
        foreach (GitMultiRestoreOperation operation in plan.Operations)
        {
            string sourcePath = GetSafeWorkingTreePath(repositoryRoot, operation.WorkingTreePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            string backupPath = GetSafeWorkingTreePath(filesBackupDirectory, operation.WorkingTreePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(sourcePath, backupPath, overwrite: true);
        }

        string manifestPath = Path.Combine(backupDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        int restored = 0;
        int deleted = 0;
        try
        {
            foreach (GitMultiRestoreOperation operation in plan.Operations)
            {
                string destination = GetSafeWorkingTreePath(repositoryRoot, operation.WorkingTreePath);
                if (operation.Kind == GitMultiRestoreOperationKind.Delete)
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                    deleted++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                ProcessResult restore = await RunGitResultAsync(
                    repositoryRoot,
                    ["restore", $"--source={operation.SourceRevision}", "--worktree", "--", operation.WorkingTreePath],
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(restore, $"Unable to restore {operation.WorkingTreePath}. Backup: {backupDirectory}");
                restored++;
            }
        }
        catch
        {
            foreach (GitMultiRestoreOperation operation in plan.Operations)
            {
                string destination = GetSafeWorkingTreePath(repositoryRoot, operation.WorkingTreePath);
                string backupPath = GetSafeWorkingTreePath(filesBackupDirectory, operation.WorkingTreePath);
                if (File.Exists(backupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupPath, destination, overwrite: true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            throw;
        }

        return new GitMultiRestoreResult(
            restored,
            deleted,
            backupDirectory,
            plan.Operations.Select(operation => operation.WorkingTreePath).ToArray());
    }

    private static IEnumerable<(string Path, GitMultiRestoreOperationKind Kind, string? Source)> BuildRestoreOperations(
        GitCommitFileChange change,
        GitRestorePoint restorePoint,
        string commitHash,
        string? parentHash)
    {
        if (restorePoint == GitRestorePoint.AtCommit)
        {
            if (change.Kind == GitChangeKind.Renamed && !string.IsNullOrWhiteSpace(change.OriginalPath))
            {
                yield return (change.OriginalPath, GitMultiRestoreOperationKind.Delete, null);
            }

            yield return change.Kind == GitChangeKind.Deleted
                ? (change.Path, GitMultiRestoreOperationKind.Delete, null)
                : (change.Path, GitMultiRestoreOperationKind.Restore, commitHash);
            yield break;
        }

        if (change.Kind == GitChangeKind.Renamed && !string.IsNullOrWhiteSpace(change.OriginalPath))
        {
            yield return (change.Path, GitMultiRestoreOperationKind.Delete, null);
            if (parentHash is not null)
            {
                yield return (change.OriginalPath, GitMultiRestoreOperationKind.Restore, parentHash);
            }
            yield break;
        }

        yield return change.Kind == GitChangeKind.Added || parentHash is null
            ? (change.Path, GitMultiRestoreOperationKind.Delete, null)
            : (change.Path, GitMultiRestoreOperationKind.Restore, parentHash);
    }

    private async Task<string> ResolveCommitAsync(string repositoryPath, string revision, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(
            repositoryPath,
            ["rev-parse", "--verify", $"{revision}^{{commit}}"],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"Unable to resolve {revision}.");
        return result.StandardOutput.Trim();
    }

    private async Task<bool> HasLocalChangesAsync(string repositoryPath, string relativePath, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(
            repositoryPath,
            ["status", "--porcelain=v1", "--untracked-files=all", "--", relativePath],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"Unable to inspect {relativePath}.");
        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    private async Task<bool> IsBranchCheckedOutElsewhereAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(
            repositoryPath,
            ["worktree", "list", "--porcelain"],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "Unable to inspect Git worktrees.");
        return result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Equals($"branch refs/heads/{branchName}", StringComparison.Ordinal));
    }

    private async Task RollBackCherryPickAsync(string workingDirectory, string targetTip, CancellationToken cancellationToken)
    {
        await RunGitResultAsync(workingDirectory, ["cherry-pick", "--abort"], cancellationToken).ConfigureAwait(false);
        await RunGitResultAsync(workingDirectory, ["reset", "--hard", targetTip], cancellationToken).ConfigureAwait(false);
    }

    private static void EnsurePlanIsFresh(DateTimeOffset createdAt)
    {
        if (DateTimeOffset.UtcNow - createdAt > CompositionPlanLifetime)
        {
            throw new GitOperationException("This safety preview expired. Build the plan again before applying it.");
        }
    }

    private static string NormalizeAndValidateGitPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(path) || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new GitOperationException($"Unsafe repository path: {path}");
        }
        return normalized;
    }

    private static string GetSafeWorkingTreePath(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, NormalizeAndValidateGitPath(relativePath)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitOperationException($"Path escapes the repository: {relativePath}");
        }
        return candidate;
    }

    private static string ReadGitError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
}
