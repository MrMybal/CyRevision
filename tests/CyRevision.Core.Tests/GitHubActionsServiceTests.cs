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
        Assert.Contains("Build DMG", details.ErrorReport);
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
