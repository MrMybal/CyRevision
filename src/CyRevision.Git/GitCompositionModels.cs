namespace CyRevision.Git;

public enum GitRestorePoint
{
    BeforeCommit,
    AtCommit
}

public enum GitMultiRestoreOperationKind
{
    Restore,
    Delete
}

public sealed record GitMultiRestoreSelection(string Path, GitRestorePoint RestorePoint);

public sealed record GitMultiRestoreOperation(
    string DisplayPath,
    string WorkingTreePath,
    GitMultiRestoreOperationKind Kind,
    string? SourceRevision,
    GitRestorePoint RestorePoint,
    bool HasLocalChanges,
    LfsPointerInfo? LfsPointer,
    bool IsLfsObjectAvailable,
    string? LocalLfsObjectPath)
{
    public string ActionSummary => Kind == GitMultiRestoreOperationKind.Delete
        ? $"Delete {WorkingTreePath}"
        : $"Restore {WorkingTreePath} from {SourceRevision}";

    public bool IsBlocked => LfsPointer is not null && !IsLfsObjectAvailable;
}

public sealed record GitMultiRestorePlan(
    Guid Id,
    string CommitHash,
    string? ParentHash,
    string RepositoryHead,
    DateTimeOffset CreatedAt,
    IReadOnlyList<GitMultiRestoreOperation> Operations,
    IReadOnlyList<string> Warnings)
{
    public bool HasLocalChanges => Operations.Any(operation => operation.HasLocalChanges);
    public bool HasMissingLfsObjects => Operations.Any(operation => operation.IsBlocked);
    public bool CanApply => Operations.Count > 0 && !HasMissingLfsObjects;
}

public sealed record GitMultiRestoreResult(
    int RestoredFileCount,
    int DeletedFileCount,
    string BackupDirectory,
    IReadOnlyList<string> ChangedPaths);

public enum GitBranchCommitPresence
{
    SourceOnly,
    TargetOnly,
    PatchEquivalent
}

public sealed record GitBranchComparisonCommit(
    GitRevision Revision,
    GitBranchCommitPresence Presence,
    string Side)
{
    public bool CanCherryPick => Presence == GitBranchCommitPresence.SourceOnly;
}

public sealed record GitBranchComparison(
    string SourceBranch,
    string TargetBranch,
    string SourceTip,
    string TargetTip,
    string? MergeBase,
    IReadOnlyList<GitBranchComparisonCommit> Commits)
{
    public int SourceOnlyCount => Commits.Count(commit => commit.Presence == GitBranchCommitPresence.SourceOnly);
    public int TargetOnlyCount => Commits.Count(commit => commit.Presence == GitBranchCommitPresence.TargetOnly);
    public int EquivalentCount => Commits.Count(commit => commit.Presence == GitBranchCommitPresence.PatchEquivalent) / 2;
}

public enum GitCherryPickMode
{
    KeepCommits,
    CombineIntoOne
}

public sealed record GitCherryPickPlan(
    Guid Id,
    string SourceBranch,
    string TargetBranch,
    string TargetTip,
    DateTimeOffset CreatedAt,
    IReadOnlyList<GitRevision> OrderedCommits,
    GitCherryPickMode Mode,
    string? CombinedCommitMessage,
    bool UsesTemporaryWorktree,
    IReadOnlyList<string> Warnings)
{
    public bool CanApply => OrderedCommits.Count > 0 && Warnings.Count == 0;
}

public sealed record GitCherryPickResult(
    string TargetBranch,
    string PreviousTip,
    string NewTip,
    int AppliedCommitCount,
    GitCherryPickMode Mode);
