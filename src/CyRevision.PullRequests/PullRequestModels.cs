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
    bool? IsMergeable,
    string MergeableState = "",
    string CurrentUser = "",
    string CiStatus = "",
    string CiConclusion = "")
{
    public string NumberText => $"#{Number}";
    public string StateText => IsMerged ? "MERGED" : IsDraft ? "DRAFT" : State.ToUpperInvariant();
    public string BranchText => $"{HeadBranch} → {BaseBranch}";
    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("g");
    public string StateColor => IsMerged
        ? "#C792EA"
        : IsDraft
            ? "#E6B85C"
            : State.Equals("open", StringComparison.OrdinalIgnoreCase)
                ? "#66D9A9"
                : "#FF7B86";
    public string StateBackgroundColor => IsMerged
        ? "#392A48"
        : IsDraft
            ? "#443A25"
            : State.Equals("open", StringComparison.OrdinalIgnoreCase)
                ? "#203E35"
                : "#472930";
    public bool IsOwnedByCurrentUser =>
        !string.IsNullOrWhiteSpace(CurrentUser) &&
        Author.Equals(CurrentUser, StringComparison.OrdinalIgnoreCase);
    public string OwnershipText => IsOwnedByCurrentUser ? "MINE" : string.Empty;
    public string OwnershipColor => IsOwnedByCurrentUser ? "#55C7E8" : "#7F8796";
    public string CiStateText => string.IsNullOrWhiteSpace(CiConclusion)
        ? string.IsNullOrWhiteSpace(CiStatus) ? "NO CI" : CiStatus.ToUpperInvariant()
        : CiConclusion.ToUpperInvariant();
    public string CiStateColor => CiStatePresentation.Color(CiStatus, CiConclusion);
    public bool HasCiFailure => CiConclusion.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
                                CiConclusion.Equals("timed_out", StringComparison.OrdinalIgnoreCase) ||
                                CiConclusion.Equals("startup_failure", StringComparison.OrdinalIgnoreCase);
    public bool HasMergeConflicts =>
        MergeableState.Equals("dirty", StringComparison.OrdinalIgnoreCase) ||
        IsMergeable == false;
    public string MergeabilityText => HasMergeConflicts
        ? "CONFLICTS"
        : IsMergeable == true
            ? "MERGEABLE"
            : string.IsNullOrWhiteSpace(MergeableState)
                ? "UNKNOWN"
                : MergeableState.ToUpperInvariant();
    public string MergeabilityColor => HasMergeConflicts ? "#FF7B86" : IsMergeable == true ? "#66D9A9" : "#A7ACB7";
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

public sealed record PullRequestConflictFile(
    string Path,
    string Detail)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? string.Empty;
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

public sealed record PullRequestCommit(
    string Hash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject);

public sealed record PullRequestDetails(
    PullRequestSummary Summary,
    string Body,
    int Additions,
    int Deletions,
    int ChangedFiles,
    int CommitCount,
    int CommentCount,
    IReadOnlyList<PullRequestCommit> Commits,
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