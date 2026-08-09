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

public sealed record GitChange(string Path, GitChangeKind Kind, bool IsStaged, bool IsLfsObject);

public sealed record GitRepositoryStatus(
    string RootPath,
    string CurrentBranch,
    bool HasRemote,
    IReadOnlyList<GitChange> Changes);

public interface IGitRepositoryService
{
    Task<GitRepositoryStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task InitializeAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task CreateRevisionAsync(
        string repositoryPath,
        string message,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);
}

