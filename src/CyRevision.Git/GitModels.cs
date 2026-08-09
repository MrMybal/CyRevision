namespace CyRevision.Git;

public enum GitChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Untracked,
    Conflicted
}

public sealed record GitChange(
    string Path,
    GitChangeKind Kind,
    bool IsStaged,
    bool IsLfsObject,
    string? OriginalPath = null);

public sealed record GitRepositoryStatus(
    string RootPath,
    string CurrentBranch,
    bool IsDetachedHead,
    bool HasRemote,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<GitChange> Changes);

public sealed record GitRevision(
    string Hash,
    string ShortHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject);

public sealed record GitBranch(
    string Name,
    string ShortCommitHash,
    bool IsCurrent);

public sealed record GitToolAvailability(
    bool GitAvailable,
    string? GitVersion,
    bool LfsAvailable,
    string? LfsVersion);

public sealed record LfsTrackedPattern(string Pattern, string SourceFile);

