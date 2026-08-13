using System.Net;
using System.Text;
using CyRevision.PullRequests;

namespace CyRevision.Core.Tests;

public sealed class GitHubPullRequestServiceTests
{
    [Theory]
    [InlineData("https://github.com/MrMybal/CyRevision.git")]
    [InlineData("git@github.com:MrMybal/CyRevision.git")]
    [InlineData("ssh://git@github.com/MrMybal/CyRevision.git")]
    public void ParsesCommonGitHubRemoteFormats(string remote)
    {
        bool parsed = GitRemoteRepositoryParser.TryParseGitHub(remote, null, out PullRequestRepository? repository);

        Assert.True(parsed);
        Assert.NotNull(repository);
        Assert.Equal("MrMybal/CyRevision", repository.FullName);
        Assert.Equal(new Uri("https://api.github.com/"), repository.ApiBaseUri);
    }

    [Fact]
    public void RejectsGitLabRemoteUntilAProviderIsInstalled()
    {
        Assert.False(GitRemoteRepositoryParser.TryParseGitHub(
            "git@gitlab.com:team/project.git", null, out PullRequestRepository? repository));
        Assert.Null(repository);
    }

    [Fact]
    public async Task ListsAndParsesPullRequests()
    {
        RecordingHandler handler = new(_ => Json(HttpStatusCode.OK, """
            [{
              "number": 42,
              "title": "Add review workspace",
              "state": "open",
              "draft": false,
              "user": { "login": "CyberAlien" },
              "head": { "ref": "feature/reviews" },
              "base": { "ref": "main" },
              "created_at": "2026-08-10T10:00:00Z",
              "updated_at": "2026-08-11T10:00:00Z",
              "html_url": "https://github.com/MrMybal/CyRevision/pull/42"
            }]
            """));
        await using GitHubPullRequestService service = new(handler);
        PullRequestRepository repository = Repository();

        IReadOnlyList<PullRequestSummary> pulls = await service.ListAsync(
            repository, PullRequestStateFilter.Open, null);

        PullRequestSummary pull = Assert.Single(pulls);
        Assert.Equal(42, pull.Number);
        Assert.Equal("feature/reviews → main", pull.BranchText);
        Assert.Contains("state=open", handler.Requests.Single().RequestUri!.Query);
        Assert.Equal("2026-03-10", handler.Requests.Single().Headers.GetValues("X-GitHub-Api-Version").Single());
    }

    [Fact]
    public async Task CreateUsesAuthenticatedWriteEndpoint()
    {
        RecordingHandler handler = new(_ => Json(HttpStatusCode.Created, PullJson(7)));
        await using GitHubPullRequestService service = new(handler);

        PullRequestSummary created = await service.CreateAsync(
            Repository(),
            new CreatePullRequestRequest("Feature", "Body", "feature/test", "main", true),
            "session-secret");

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/repos/MrMybal/CyRevision/pulls", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("session-secret", request.Headers.Authorization.Parameter);
        Assert.Contains("\"head\":\"feature/test\"", handler.Bodies.Single());
        Assert.True(created.IsDraft);
    }

    [Fact]
    public async Task WriteOperationsRequireTokenBeforeNetworkCall()
    {
        RecordingHandler handler = new(_ => throw new InvalidOperationException("Network should not be called."));
        await using GitHubPullRequestService service = new(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddCommentAsync(
            Repository(), 1, "Comment", string.Empty));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProviderErrorsNeverIncludeTheToken()
    {
        RecordingHandler handler = new(_ => Json(HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}"));
        await using GitHubPullRequestService service = new(handler);

        PullRequestProviderException exception = await Assert.ThrowsAsync<PullRequestProviderException>(() =>
            service.CreateAsync(
                Repository(),
                new CreatePullRequestRequest("Feature", string.Empty, "feature", "main", false),
                "do-not-leak"));

        Assert.Contains("Bad credentials", exception.Message);
        Assert.DoesNotContain("do-not-leak", exception.Message);
    }

    [Fact]
    public async Task PrivateRepositoryNotFoundExplainsHowToAuthenticate()
    {
        RecordingHandler handler = new(_ => Json(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"));
        await using GitHubPullRequestService service = new(handler);

        PullRequestProviderException exception = await Assert.ThrowsAsync<PullRequestProviderException>(() =>
            service.ListAsync(Repository(), PullRequestStateFilter.All, null));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("private", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PullRequestRepository Repository() => new(
        "GitHub",
        "github.com",
        "MrMybal",
        "CyRevision",
        new Uri("https://github.com/"),
        new Uri("https://api.github.com/"),
        "https://github.com/MrMybal/CyRevision.git");

    private static string PullJson(int number) => $$"""
        {
          "number": {{number}},
          "title": "Feature",
          "state": "open",
          "draft": true,
          "user": { "login": "CyberAlien" },
          "head": { "ref": "feature/test" },
          "base": { "ref": "main" },
          "created_at": "2026-08-10T10:00:00Z",
          "updated_at": "2026-08-11T10:00:00Z",
          "html_url": "https://github.com/MrMybal/CyRevision/pull/{{number}}"
        }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }
}
