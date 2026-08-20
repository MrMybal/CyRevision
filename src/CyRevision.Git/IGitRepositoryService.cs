namespace CyRevision.Git;

public interface IGitRepositoryService
{
    Task<GitToolAvailability> GetToolAvailabilityAsync(CancellationToken cancellationToken = default);

    Task InitializeAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task CloneAsync(
        string remoteUrl,
        string destinationPath,
        bool recurseSubmodules = false,
        CancellationToken cancellationToken = default);

    Task<GitRepositoryStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task<GitRepositoryStatus> GetQuickStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task<GitRepositoryStatus> GetDetailedStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

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

    Task DiscardChangesAsync(
        string repositoryPath,
        IReadOnlyCollection<GitChange> changes,
        CancellationToken cancellationToken = default);

    Task DeleteWorkingTreePathsAsync(
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

    Task<IReadOnlyList<GitRevision>> GetHistoryAcrossRefsAsync(
        string repositoryPath,
        int maximumCount = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitRevision>> GetHistoryForReferenceAsync(
        string repositoryPath,
        string reference,
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

    Task<GitBranchComparison> CompareBranchesAsync(
        string repositoryPath,
        string sourceBranch,
        string targetBranch,
        CancellationToken cancellationToken = default);

    Task<GitCherryPickPlan> CreateCherryPickPlanAsync(
        string repositoryPath,
        string sourceBranch,
        string targetBranch,
        IReadOnlyList<string> orderedCommitHashes,
        GitCherryPickMode mode,
        string? combinedCommitMessage = null,
        CancellationToken cancellationToken = default);

    Task<GitCherryPickResult> ApplyCherryPickPlanAsync(
        string repositoryPath,
        GitCherryPickPlan plan,
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

    Task<GitBranchDetails> GetBranchDetailsAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<GitLocalBranchRemovalAnalysis> AnalyzeLocalBranchRemovalAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task RemoveLocalBranchAsync(
        string repositoryPath,
        string branchName,
        bool forceUnretained = false,
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

    Task<GitHistoricalWorktreeResult> CreateHistoricalWorktreeAsync(
        string repositoryPath,
        string commitHash,
        string? branchName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitHistoricalWorktree>> GetHistoricalWorktreesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task RemoveHistoricalWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task CheckoutBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task MergeBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<GitConflictState> GetConflictStateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task ResolveConflictAsync(
        string repositoryPath,
        string relativePath,
        GitConflictResolutionChoice choice,
        string? manualResult = null,
        CancellationToken cancellationToken = default);

    Task ContinueConflictOperationAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task AbortConflictOperationAsync(
        string repositoryPath,
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

    Task<GitMultiRestorePlan> CreateMultiRestorePlanAsync(
        string repositoryPath,
        string commitHash,
        IReadOnlyList<GitMultiRestoreSelection> selections,
        CancellationToken cancellationToken = default);

    Task<GitMultiRestoreResult> ApplyMultiRestorePlanAsync(
        string repositoryPath,
        GitMultiRestorePlan plan,
        bool overwriteLocalChanges,
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

    Task<IReadOnlyList<LfsFileLock>> GetLfsLocksAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task UnlockLfsFileAsync(
        string repositoryPath,
        string lockId,
        bool force = false,
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
