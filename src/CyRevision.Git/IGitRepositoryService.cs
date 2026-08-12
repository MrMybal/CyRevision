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

    Task<IReadOnlyList<GitGraphCommit>> GetCommitGraphAsync(
        string repositoryPath,
        int maximumCount = 250,
        bool includeAllBranches = true,
        CancellationToken cancellationToken = default);

    Task<GitFileActivityGraph> GetFileActivityGraphAsync(
        string repositoryPath,
        int maximumCommitCount = 250,
        int maximumFileCount = 80,
        bool includeAllBranches = true,
        CancellationToken cancellationToken = default);

    Task<GitRepositoryInsights> GetRepositoryInsightsAsync(
        string repositoryPath,
        int maximumCommitCount = 500,
        bool includeAllBranches = true,
        CancellationToken cancellationToken = default);

    Task<GitCommitDetails> GetCommitDetailsAsync(
        string repositoryPath,
        string revision,
        CancellationToken cancellationToken = default);

    Task<GitCommitComparison> CompareCommitsAsync(
        string repositoryPath,
        string fromRevision,
        string toRevision,
        CancellationToken cancellationToken = default);

    Task<string> GetCommitDiffAsync(
        string repositoryPath,
        string revision,
        string? relativePath = null,
        CancellationToken cancellationToken = default);

    Task<string> GetComparisonDiffAsync(
        string repositoryPath,
        string fromRevision,
        string toRevision,
        string? relativePath = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitFileRevision>> GetFileHistoryAsync(
        string repositoryPath,
        string relativePath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task CreateBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task CreateBranchFromAsync(
        string repositoryPath,
        string branchName,
        string startPoint,
        CancellationToken cancellationToken = default);

    Task CheckoutBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task MergeBranchAsync(
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

    Task ExportFileFromRevisionAsync(
        string repositoryPath,
        string relativePath,
        string revision,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task AddOrUpdateRemoteAsync(
        string repositoryPath,
        string remoteName,
        string remoteUrl,
        CancellationToken cancellationToken = default);

    Task<string?> GetRemoteUrlAsync(
        string repositoryPath,
        string remoteName = "origin",
        CancellationToken cancellationToken = default);

    Task FetchAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task FetchReferenceAsync(
        string repositoryPath,
        string remoteName,
        string referenceSpec,
        CancellationToken cancellationToken = default);

    Task FastForwardAsync(
        string repositoryPath,
        string reference,
        CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<LfsTrackedFile>> GetLfsTrackedFilesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LfsFileVersion>> GetLfsFileVersionsAsync(
        string repositoryPath,
        string relativePath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default);

    Task ExportLfsFileVersionAsync(
        string repositoryPath,
        LfsFileVersion version,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task RestoreLfsFileVersionAsync(
        string repositoryPath,
        LfsFileVersion version,
        CancellationToken cancellationToken = default);
}
