namespace CyRevision.Git;

public sealed record GitHistoricalWorktree(
    string Path,
    string CommitHash,
    string Branch,
    bool IsDetached,
    bool IsLocked,
    bool IsPrunable,
    bool IsManagedByCyRevision,
    DateTimeOffset? CreatedAt)
{
    public string DisplayName => System.IO.Path.GetFileName(Path);

    public string ShortCommitHash => CommitHash.Length > 9 ? CommitHash[..9] : CommitHash;

    public string State => IsLocked ? "Locked" : IsPrunable ? "Prunable" : IsDetached ? "Detached" : "Branch";
}

public sealed record GitHistoricalWorktreeResult(
    bool Succeeded,
    string RepositoryPath,
    string WorktreePath,
    string CommitHash,
    string BranchName,
    string Summary);
