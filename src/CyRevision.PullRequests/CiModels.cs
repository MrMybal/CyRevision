namespace CyRevision.PullRequests;

public sealed record CiWorkflow(
    long Id,
    string Name,
    string Path,
    string State)
{
    public string DisplayName => $"{Name}  ·  {Path}";
}

public sealed record CiWorkflowRun(
    long Id,
    string Name,
    string Event,
    string Status,
    string Conclusion,
    string Branch,
    string CommitSha,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Uri WebUrl)
{
    public string ShortSha => CommitSha[..Math.Min(8, CommitSha.Length)];
    public string StateText => string.IsNullOrWhiteSpace(Conclusion) ? Status : Conclusion;
    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("g");
}

public sealed record CiWorkflowStep(
    int Number,
    string Name,
    string Status,
    string Conclusion,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public string StateText => string.IsNullOrWhiteSpace(Conclusion) ? Status : Conclusion;
}

public sealed record CiWorkflowJob(
    long Id,
    string Name,
    string Status,
    string Conclusion,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Uri? WebUrl,
    IReadOnlyList<CiWorkflowStep> Steps)
{
    public string StateText => string.IsNullOrWhiteSpace(Conclusion) ? Status : Conclusion;
    public string FailedStepSummary => string.Join(", ", Steps
        .Where(step => string.Equals(step.Conclusion, "failure", StringComparison.OrdinalIgnoreCase))
        .Select(step => step.Name));
}

public sealed record CiWorkflowRunDetails(
    CiWorkflowRun Run,
    IReadOnlyList<CiWorkflowJob> Jobs)
{
    public string ErrorReport
    {
        get
        {
            CiWorkflowJob[] failedJobs = Jobs
                .Where(job => string.Equals(job.Conclusion, "failure", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(job.Conclusion, "cancelled", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (failedJobs.Length == 0)
                return $"{Run.Name}: {Run.StateText}. No failed job was reported.";

            return string.Join(Environment.NewLine, failedJobs.Select(job =>
                $"[{job.StateText.ToUpperInvariant()}] {job.Name}" +
                (string.IsNullOrWhiteSpace(job.FailedStepSummary) ? string.Empty : $" · {job.FailedStepSummary}")));
        }
    }
}

public interface ICiWorkflowService
{
    bool TryResolveRepository(string remoteUrl, string? apiBaseUrl, out PullRequestRepository? repository);

    Task<IReadOnlyList<CiWorkflow>> ListWorkflowsAsync(
        PullRequestRepository repository,
        string? token,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CiWorkflowRun>> ListRunsAsync(
        PullRequestRepository repository,
        string? token,
        CancellationToken cancellationToken = default);

    Task<CiWorkflowRunDetails> GetRunDetailsAsync(
        PullRequestRepository repository,
        CiWorkflowRun run,
        string? token,
        CancellationToken cancellationToken = default);

    Task DispatchAsync(
        PullRequestRepository repository,
        CiWorkflow workflow,
        string gitRef,
        IReadOnlyDictionary<string, string> inputs,
        string token,
        CancellationToken cancellationToken = default);

    Task RerunFailedJobsAsync(
        PullRequestRepository repository,
        long runId,
        string token,
        CancellationToken cancellationToken = default);

    Task CancelRunAsync(
        PullRequestRepository repository,
        long runId,
        string token,
        CancellationToken cancellationToken = default);
}
