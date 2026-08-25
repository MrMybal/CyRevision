namespace CyRevision.PullRequests;

public interface IPullRequestService : IAsyncDisposable
{
    bool TryResolveRepository(string remoteUrl, string? apiBaseUrl, out PullRequestRepository? repository);

    Task<string?> GetCurrentUserAsync(
        PullRequestRepository repository,
        string? token,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PullRequestSummary>> ListAsync(
        PullRequestRepository repository,
        PullRequestStateFilter state,
        string? token,
        CancellationToken cancellationToken = default);

    Task<PullRequestDetails> GetDetailsAsync(
        PullRequestRepository repository,
        int number,
        string? token,
        CancellationToken cancellationToken = default);

    Task<PullRequestSummary> CreateAsync(
        PullRequestRepository repository,
        CreatePullRequestRequest request,
        string token,
        CancellationToken cancellationToken = default);

    Task AddCommentAsync(
        PullRequestRepository repository,
        int number,
        string body,
        string token,
        CancellationToken cancellationToken = default);

    Task SubmitReviewAsync(
        PullRequestRepository repository,
        int number,
        string body,
        PullRequestReviewAction action,
        string token,
        CancellationToken cancellationToken = default);

    Task<MergePullRequestResult> MergeAsync(
        PullRequestRepository repository,
        int number,
        PullRequestMergeMethod method,
        string token,
        CancellationToken cancellationToken = default);

    Task SetStateAsync(
        PullRequestRepository repository,
        int number,
        bool open,
        string token,
        CancellationToken cancellationToken = default);
}
