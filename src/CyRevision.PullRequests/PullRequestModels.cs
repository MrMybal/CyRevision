namespace CyRevision.PullRequests;

public enum PullRequestStateFilter
{
    Open,
    Closed,
    All
}

public enum PullRequestMergeMethod
{
    Merge,
    Squash,
    Rebase
}

public enum PullRequestReviewAction
{
    Comment,
    Approve,
    RequestChanges
}

public sealed record PullRequestRepository(
    string Provider,
    string Host,
    string Owner,
    string Name,
    Uri WebBaseUri,
    Uri ApiBaseUri,
    string RemoteUrl)
{
    public string FullName => $"{Owner}/{Name}";
}

public sealed record PullRequestSummary(
    int Number,
    string Title,
    string State,
    bool IsDraft,
    string Author,
    string HeadBranch,
    string BaseBranch,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Uri WebUrl,
    bool IsMerged,
    bool? IsMergeable)
{
    public string NumberText => $"#{Number}";
    public string StateText => IsMerged ? "MERGED" : IsDraft ? "DRAFT" : State.ToUpperInvariant();
    public string BranchText => $"{HeadBranch} → {BaseBranch}";
    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("g");
}

public sealed record PullRequestFile(
    string Path,
    string Status,
    int Additions,
    int Deletions,
    int Changes,
    string Patch,
    Uri? WebUrl)
{
    public string ChangeText => $"+{Additions} / -{Deletions}";
}

public sealed record PullRequestReview(
    long Id,
    string Author,
    string State,
    string Body,
    DateTimeOffset? SubmittedAt)
{
    public string SubmittedText => SubmittedAt?.LocalDateTime.ToString("g") ?? "Pending";
}

public sealed record PullRequestComment(
    long Id,
    string Author,
    string Body,
    DateTimeOffset CreatedAt,
    Uri? WebUrl)
{
    public string CreatedText => CreatedAt.LocalDateTime.ToString("g");
}

public sealed record PullRequestDetails(
    PullRequestSummary Summary,
    string Body,
    int Additions,
    int Deletions,
    int ChangedFiles,
    int CommitCount,
    int CommentCount,
    IReadOnlyList<PullRequestFile> Files,
    IReadOnlyList<PullRequestReview> Reviews,
    IReadOnlyList<PullRequestComment> Comments)
{
    public string ChangeSummary => $"{ChangedFiles} file(s) · {CommitCount} commit(s) · +{Additions} / -{Deletions}";
}

public sealed record CreatePullRequestRequest(
    string Title,
    string Body,
    string HeadBranch,
    string BaseBranch,
    bool IsDraft,
    bool MaintainerCanModify = true);

public sealed record MergePullRequestResult(bool Merged, string Message, string? CommitSha);
