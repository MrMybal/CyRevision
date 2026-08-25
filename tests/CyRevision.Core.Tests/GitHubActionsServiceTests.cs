using System.IO.Compression;
using System.Net;
using System.Text;
using CyRevision.PullRequests;

namespace CyRevision.Core.Tests;

public sealed class GitHubActionsServiceTests
{
    [Fact]
    public async Task ListsWorkflowsRunsAndFailureDetails()
    {
        QueueHandler handler = new(
            """{"workflows":[{"id":42,"name":"Build release","path":".github/workflows/release.yml","state":"active"}]}""",
            """{"workflow_runs":[{"id":75,"name":"Build release","event":"workflow_dispatch","status":"completed","conclusion":"failure","head_branch":"main","head_sha":"0123456789abcdef","created_at":"2026-08-14T10:00:00Z","updated_at":"2026-08-14T10:05:00Z","html_url":"https://github.com/acme/game/actions/runs/75"}]}""",
            """{"jobs":[{"id":90,"name":"macOS","status":"completed","conclusion":"failure","started_at":"2026-08-14T10:01:00Z","completed_at":"2026-08-14T10:04:00Z","html_url":"https://github.com/acme/game/actions/runs/75/job/90","steps":[{"number":3,"name":"Build DMG","status":"completed","conclusion":"failure","started_at":"2026-08-14T10:02:00Z","completed_at":"2026-08-14T10:03:00Z"}]}]}""");
        using GitHubActionsService service = new(handler);
        PullRequestRepository repository = Repository();

        CiWorkflow workflow = Assert.Single(await service.ListWorkflowsAsync(repository, null));
        CiWorkflowRun run = Assert.Single(await service.ListRunsAsync(repository, null));
        CiWorkflowRunDetails details = await service.GetRunDetailsAsync(repository, run, null);

        Assert.Equal(42, workflow.Id);
        Assert.Equal("failure", run.StateText);
        Assert.Equal("#FF6B79", run.StateColor);
        Assert.Contains("Build DMG", details.ErrorReport);
        Assert.Contains("Run ID     #75", details.FullReport);
        Assert.Contains("Commit     0123456789abcdef", details.FullReport);
        Assert.Contains("3. Build DMG — failure", details.FullReport);
    }

    [Fact]
    public async Task ReadsZippedRunLogsAndClassifiesOnlyRealErrors()
    {
        using MemoryStream buffer = new();
        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("Build/compile.txt");
            await using Stream stream = entry.Open();
            await using StreamWriter writer = new(stream, Encoding.UTF8);
            await writer.WriteLineAsync("Compiling CyRevision");
            await writer.WriteLineAsync("error CS1002: ; expected");
            await writer.WriteLineAsync("warning CS0168: variable is never used");
        }

        using GitHubActionsService service = new(new BinaryHandler(buffer.ToArray()));
        CiWorkflowRun run = new(
            75,
            "Build release",
            "pull_request",
            "completed",
            "failure",
            "feature/pr-ci",
            "0123456789abcdef",
            DateTimeOffset.Parse("2026-08-14T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-14T10:05:00Z"),
            new Uri("https://github.com/acme/game/actions/runs/75"));

        IReadOnlyList<CiLogLine> lines = await service.GetRunLogLinesAsync(Repository(), run, null);

        Assert.Equal(3, lines.Count);
        Assert.Single(lines.Where(line => line.IsError));
        Assert.Single(lines.Where(line => line.IsWarning));
        Assert.Equal(3, lines.Count(line => line.Matches(CiLogFilterMode.All)));
        Assert.Single(lines.Where(line => line.Matches(CiLogFilterMode.Errors)));
        Assert.Single(lines.Where(line => line.Matches(CiLogFilterMode.Warnings)));
        Assert.All(lines, line => Assert.Equal("Build/compile.txt", line.Source));
    }
    [Fact]
    public void ActiveJobsOverrideAStaleSuccessfulPullRequestBadge()
    {
        CiWorkflowRun success = new(
            80, "PullRequest", "pull_request", "completed", "success", "feature/ci",
            "0123456789abcdef", DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1),
            new Uri("https://github.com/acme/game/actions/runs/80"));
        CiWorkflowRun staleRun = success with
        {
            Id = 81,
            Status = "queued",
            Conclusion = string.Empty,
            WebUrl = new Uri("https://github.com/acme/game/actions/runs/81")
        };
        CiWorkflowJob[] jobs =
        [
            new(1, "Beyond Android", "in_progress", string.Empty, DateTimeOffset.UtcNow, null, null, []),
            new(2, "Reality", "queued", string.Empty, null, null, null, [])
        ];

        CiWorkflowRun effective = CiStatePresentation.ApplyJobState(staleRun, jobs);
        (string status, string conclusion) = CiStatePresentation.AggregateRuns([success, effective]);

        Assert.Equal("in_progress", effective.Status);
        Assert.Equal("in_progress", status);
        Assert.Empty(conclusion);
        Assert.Equal("#55C7E8", CiStatePresentation.Color(status, conclusion));
    }
    [Fact]
    public async Task DispatchSendsRefAndReleaseInput()
    {
        QueueHandler handler = new(string.Empty) { StatusCode = HttpStatusCode.NoContent };
        using GitHubActionsService service = new(handler);
        CiWorkflow workflow = new(42, "Build release", ".github/workflows/release.yml", "active");

        await service.DispatchAsync(Repository(), workflow, "main", new Dictionary<string, string>
        {
            ["version"] = "0.1.13"
        }, "secret");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Contains("\"ref\":\"main\"", handler.LastBody);
        Assert.Contains("\"version\":\"0.1.13\"", handler.LastBody);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
    }

    private static PullRequestRepository Repository() => new(
        "GitHub",
        "github.com",
        "acme",
        "game",
        new Uri("https://github.com/"),
        new Uri("https://api.github.com/"),
        "https://github.com/acme/game.git");

    private sealed class BinaryHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
    }
    private sealed class QueueHandler(params string[] payloads) : HttpMessageHandler
    {
        private readonly Queue<string> _payloads = new(payloads);

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public HttpMethod? LastMethod { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        public string? LastAuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string payload = _payloads.Count == 0 ? string.Empty : _payloads.Dequeue();
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
