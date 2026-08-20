using System.Globalization;

namespace CyRevision.Git;

public sealed partial class GitCliRepositoryService
{
    public async Task<GitLocalBranchRemovalAnalysis> AnalyzeLocalBranchRemovalAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(branchName);
        IReadOnlyList<GitBranch> branches = await GetBranchesAsync(repositoryPath, cancellationToken)
            .ConfigureAwait(false);
        GitBranch selected = branches.FirstOrDefault(branch =>
                                 branch.Name.Equals(branchName, StringComparison.Ordinal) && !branch.IsRemote)
                             ?? throw new GitOperationException($"Local branch '{branchName}' no longer exists.");

        IReadOnlyList<GitHistoricalWorktree> worktrees = await GetHistoricalWorktreesAsync(
                repositoryPath,
                cancellationToken)
            .ConfigureAwait(false);
        bool checkedOutInWorktree = worktrees.Any(worktree =>
            worktree.Branch.Equals(branchName, StringComparison.Ordinal));

        string mergedOutput = await RunGitAsync(
                repositoryPath,
                ["branch", "--merged", "HEAD", "--format=%(refname:short)"],
                cancellationToken)
            .ConfigureAwait(false);
        bool mergedIntoCurrent = mergedOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(branchName, StringComparer.Ordinal);

        GitBranch? remote = string.IsNullOrWhiteSpace(selected.RemoteName)
            ? null
            : branches.FirstOrDefault(branch =>
                branch.IsRemote && branch.Name.Equals(selected.RemoteName, StringComparison.Ordinal));
        int missingFromRemote = 0;
        bool fullyPublished = false;
        if (remote is not null)
        {
            string countOutput = await RunGitAsync(
                    repositoryPath,
                    ["rev-list", "--count", $"{remote.Name}..{branchName}"],
                    cancellationToken)
                .ConfigureAwait(false);
            missingFromRemote = int.TryParse(
                countOutput.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int count)
                ? count
                : int.MaxValue;
            fullyPublished = missingFromRemote == 0;
        }

        GitBranchDetails details = await GetBranchDetailsAsync(repositoryPath, branchName, cancellationToken)
            .ConfigureAwait(false);
        string safetyMessage = selected.IsCurrent
            ? "The current branch cannot be removed. Switch to another branch first."
            : checkedOutInWorktree
                ? "This branch is checked out in a worktree. Close or remove that worktree first."
                : fullyPublished
                    ? $"Every commit is retained by {remote!.Name}. Only the local branch reference will be removed."
                    : mergedIntoCurrent
                        ? "Every commit is already retained by the current branch history."
                        : remote is null
                            ? "This branch has no verified remote copy and is not merged into the current branch. Archive or publish it before removal."
                            : $"{missingFromRemote:N0} commit(s) are not present on {remote.Name}. Publish, merge, or archive them before removal.";

        return new GitLocalBranchRemovalAnalysis(
            branchName,
            remote?.Name,
            selected.IsCurrent,
            selected.IsRemote,
            checkedOutInWorktree,
            mergedIntoCurrent,
            fullyPublished,
            missingFromRemote,
            details.UniqueCommitCount,
            safetyMessage);
    }

    public async Task RemoveLocalBranchAsync(
        string repositoryPath,
        string branchName,
        bool forceUnretained = false,
        CancellationToken cancellationToken = default)
    {
        GitLocalBranchRemovalAnalysis analysis = await AnalyzeLocalBranchRemovalAsync(
                repositoryPath,
                branchName,
                cancellationToken)
            .ConfigureAwait(false);
        if (!analysis.CanRemoveSafely && !(forceUnretained && analysis.CanForceRemove))
            throw new GitOperationException(analysis.SafetyMessage);

        string deleteMode = analysis.IsMergedIntoCurrent && !forceUnretained ? "-d" : "-D";
        await RunGitWithoutOutputAsync(
                repositoryPath,
                ["branch", deleteMode, "--", branchName],
                cancellationToken)
            .ConfigureAwait(false);
    }
}
