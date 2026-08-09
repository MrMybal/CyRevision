namespace CyRevision.Git;

public interface IGitRepositoryService
{
    Task<GitToolAvailability> GetToolAvailabilityAsync(CancellationToken cancellationToken = default);

    Task InitializeAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task<GitRepositoryStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task ConfigureIdentityAsync(
        string repositoryPath,
        string userName,
        string userEmail,
        CancellationToken cancellationToken = default);

    Task StageAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task UnstageAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task CreateRevisionAsync(
        string repositoryPath,
        string message,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitRevision>> GetHistoryAsync(
        string repositoryPath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task CreateBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task CheckoutBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<string> GetDiffAsync(
        string repositoryPath,
        string? relativePath = null,
        bool staged = false,
        CancellationToken cancellationToken = default);

    Task RestoreFileFromRevisionAsync(
        string repositoryPath,
        string relativePath,
        string revision,
        CancellationToken cancellationToken = default);

    Task AddOrUpdateRemoteAsync(
        string repositoryPath,
        string remoteName,
        string remoteUrl,
        CancellationToken cancellationToken = default);

    Task FetchAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task PullAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task PushAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task TrackLfsPatternAsync(
        string repositoryPath,
        string pattern,
        CancellationToken cancellationToken = default);

    Task UntrackLfsPatternAsync(
        string repositoryPath,
        string pattern,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LfsTrackedPattern>> GetLfsPatternsAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}

