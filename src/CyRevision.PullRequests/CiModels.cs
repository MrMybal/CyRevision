using System.Text;

namespace CyRevision.PullRequests;

public enum CiLogFilterMode
{
    All,
    Errors,
    Warnings
}

public static class CiStatePresentation
{
    public static string Color(string? status, string? conclusion)
    {
        string state = string.IsNullOrWhiteSpace(conclusion) ? status ?? string.Empty : conclusion;
        return state.ToLowerInvariant() switch
        {
            "success" => "#66D9A9",
            "failure" or "timed_out" or "startup_failure" => "#FF6B79",
            "cancelled" or "skipped" or "stale" => "#A7ACB7",
            "in_progress" or "waiting" or "requested" or "pending" => "#55C7E8",
            "queued" => "#E6B85C",
            "action_required" => "#F08A5D",
            _ => "#C9CDD6"
        };
    }

    public static string Duration(DateTimeOffset? startedAt, DateTimeOffset? completedAt)
    {
        if (startedAt is null) return "Not started";
        TimeSpan duration = (completedAt ?? DateTimeOffset.Now) - startedAt.Value;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{Math.Max(0, duration.Seconds)}s";
    }
    public static (string Status, string Conclusion) AggregateRuns(IEnumerable<CiWorkflowRun> runs)
    {
        CiWorkflowRun[] all = runs.ToArray();
        if (all.Length == 0) return (string.Empty, string.Empty);

        if (all.Any(run => run.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase)))
            return ("in_progress", string.Empty);
        if (all.Any(run => run.Status is "queued" or "waiting" or "requested" or "pending"))
            return ("queued", string.Empty);

        CiWorkflowRun[] latestByWorkflow = all
            .GroupBy(run => run.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(run => run.Id).First())
            .ToArray();
        CiWorkflowRun? failure = latestByWorkflow.FirstOrDefault(run => run.HasFailed);
        if (failure is not null) return ("completed", failure.Conclusion);
        CiWorkflowRun? actionRequired = latestByWorkflow.FirstOrDefault(run =>
            run.Conclusion.Equals("action_required", StringComparison.OrdinalIgnoreCase));
        if (actionRequired is not null) return ("completed", "action_required");
        CiWorkflowRun? cancelled = latestByWorkflow.FirstOrDefault(run =>
            run.Conclusion.Equals("cancelled", StringComparison.OrdinalIgnoreCase));
        if (cancelled is not null) return ("completed", "cancelled");
        if (latestByWorkflow.All(run => run.Conclusion.Equals("success", StringComparison.OrdinalIgnoreCase)))
            return ("completed", "success");

        CiWorkflowRun latest = latestByWorkflow.OrderByDescending(run => run.Id).First();
        return (latest.Status, latest.Conclusion);
    }

    public static CiWorkflowRun ApplyJobState(
        CiWorkflowRun run,
        IReadOnlyList<CiWorkflowJob> jobs)
    {
        if (jobs.Count == 0) return run;
        if (jobs.Any(job => job.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase)))
            return run with { Status = "in_progress", Conclusion = string.Empty };
        if (jobs.Any(job => job.Status is "queued" or "waiting" or "requested" or "pending"))
            return run with { Status = "queued", Conclusion = string.Empty };

        CiWorkflowJob? failure = jobs.FirstOrDefault(job => job.HasFailed);
        if (failure is not null)
            return run with { Status = "completed", Conclusion = failure.Conclusion };
        if (jobs.Any(job => job.Conclusion.Equals("action_required", StringComparison.OrdinalIgnoreCase)))
            return run with { Status = "completed", Conclusion = "action_required" };
        if (jobs.Any(job => job.Conclusion.Equals("cancelled", StringComparison.OrdinalIgnoreCase)))
            return run with { Status = "completed", Conclusion = "cancelled" };
        if (jobs.All(job => job.Conclusion.Equals("success", StringComparison.OrdinalIgnoreCase)))
            return run with { Status = "completed", Conclusion = "success" };
        return run;
    }
}

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
    public string StateColor => CiStatePresentation.Color(Status, Conclusion);
    public string CreatedText => CreatedAt.LocalDateTime.ToString("g");
    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("g");
    public string DurationText => CiStatePresentation.Duration(CreatedAt, UpdatedAt);
    public bool HasFailed => Conclusion.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
                             Conclusion.Equals("timed_out", StringComparison.OrdinalIgnoreCase) ||
                             Conclusion.Equals("startup_failure", StringComparison.OrdinalIgnoreCase);
    public bool IsRunning => Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase) ||
                             Status.Equals("queued", StringComparison.OrdinalIgnoreCase) ||
                             Status.Equals("waiting", StringComparison.OrdinalIgnoreCase);
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
    public string StateColor => CiStatePresentation.Color(Status, Conclusion);
    public string DurationText => CiStatePresentation.Duration(StartedAt, CompletedAt);
    public bool HasFailed => Conclusion.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
                             Conclusion.Equals("timed_out", StringComparison.OrdinalIgnoreCase) ||
                             Conclusion.Equals("startup_failure", StringComparison.OrdinalIgnoreCase);
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
    public string StateColor => CiStatePresentation.Color(Status, Conclusion);
    public string DurationText => CiStatePresentation.Duration(StartedAt, CompletedAt);
    public bool HasFailed => Conclusion.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
                             Conclusion.Equals("timed_out", StringComparison.OrdinalIgnoreCase) ||
                             Conclusion.Equals("startup_failure", StringComparison.OrdinalIgnoreCase);
    public string FailedStepSummary => string.Join(", ", Steps
        .Where(step => step.HasFailed)
        .Select(step => step.Name));
}

public sealed record CiLogLine(
    int Number,
    string Source,
    string Text,
    bool IsError,
    bool IsWarning)
{
    public string NumberText => Number.ToString();
    public string StateColor => IsError ? "#FF7B86" : IsWarning ? "#E6B85C" : "#C9CDD6";

    public bool Matches(CiLogFilterMode filterMode) => filterMode switch
    {
        CiLogFilterMode.Errors => IsError,
        CiLogFilterMode.Warnings => IsWarning,
        _ => true
    };

    public static CiLogLine Create(int number, string source, string text)
    {
        string normalized = text.TrimEnd();
        bool error = normalized.Contains("##[error]", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains(" error ", StringComparison.OrdinalIgnoreCase) ||
                     normalized.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains("fatal:", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains("exception:", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains("failed", StringComparison.OrdinalIgnoreCase);
        bool warning = !error && (normalized.Contains("##[warning]", StringComparison.OrdinalIgnoreCase) ||
                                  normalized.Contains(" warning ", StringComparison.OrdinalIgnoreCase) ||
                                  normalized.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ||
                                  normalized.Contains(": warning", StringComparison.OrdinalIgnoreCase));
        return new CiLogLine(number, source, normalized, error, warning);
    }
}

public sealed record CiWorkflowRunDetails(
    CiWorkflowRun Run,
    IReadOnlyList<CiWorkflowJob> Jobs)
{
    public string FullReport
    {
        get
        {
            StringBuilder report = new();
            report.AppendLine($"Name       {Run.Name}");
            report.AppendLine($"Run ID     #{Run.Id}");
            report.AppendLine($"Event      {Run.Event}");
            report.AppendLine($"State      {Run.StateText}");
            report.AppendLine($"Branch     {Run.Branch}");
            report.AppendLine($"Commit     {Run.CommitSha}");
            report.AppendLine($"Created    {Run.CreatedText}");
            report.AppendLine($"Updated    {Run.UpdatedText}");
            report.AppendLine($"Duration   {Run.DurationText}");
            report.AppendLine($"URL        {Run.WebUrl}");
            report.AppendLine();
            report.AppendLine($"Jobs ({Jobs.Count})");

            if (Jobs.Count == 0)
            {
                report.Append("No job has been reported yet.");
                return report.ToString();
            }

            foreach (CiWorkflowJob job in Jobs)
            {
                report.AppendLine($"• {job.Name} — {job.StateText} — {job.DurationText}");
                foreach (CiWorkflowStep step in job.Steps)
                    report.AppendLine($"    {step.Number}. {step.Name} — {step.StateText} — {step.DurationText}");
            }

            return report.ToString().TrimEnd();
        }
    }

    public string ErrorReport
    {
        get
        {
            CiWorkflowJob[] failedJobs = Jobs
                .Where(job => job.HasFailed ||
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

    Task<IReadOnlyList<CiLogLine>> GetRunLogLinesAsync(
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