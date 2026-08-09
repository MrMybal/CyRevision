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
    bool IsCurrent,
    bool IsRemote = false);

public sealed record GitToolAvailability(
    bool GitAvailable,
    string? GitVersion,
    bool LfsAvailable,
    string? LfsVersion);

public sealed record LfsTrackedPattern(string Pattern, string SourceFile);

public sealed record GitGraphCommit(
    string Hash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    DateTimeOffset AuthoredAt,
    string Subject,
    string Decorations)
{
    public bool IsMerge => ParentHashes.Count > 1;
}

public enum GitFileKind
{
    Code,
    UnrealAsset,
    Texture,
    Model,
    Audio,
    Document,
    Configuration,
    Other
}

public sealed record GitFileActivity(
    string Path,
    GitFileKind Kind,
    int ChangeCount,
    long AddedLines,
    long DeletedLines,
    int BinaryChangeCount,
    DateTimeOffset LastChangedAt);

public sealed record GitFileRelation(
    string SourcePath,
    string TargetPath,
    int CoChangeCount);

public sealed record GitFileActivityGraph(
    IReadOnlyList<GitFileActivity> Files,
    IReadOnlyList<GitFileRelation> Relations,
    int AnalyzedCommitCount,
    int TotalFileCount);
