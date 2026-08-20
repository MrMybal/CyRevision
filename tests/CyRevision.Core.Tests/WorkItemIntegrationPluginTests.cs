using System.Net;
using System.Text;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.ClickUp;
using CyRevision.Plugin.Jira;

namespace CyRevision.Core.Tests;

public sealed class WorkItemIntegrationPluginTests
{
    [Fact]
    public async Task JiraSearchUsesCloudApiAndCreatesStableIssueLink()
    {
        using TemporaryDirectory temporary = new();
        RecordingHandler handler = new(_ => Json("""
            {"sections":[{"label":"Current Search","issues":[
              {"id":"10042","key":"GAME-42","summaryText":"Fix editor crash"}
            ]}]}
            """));
        await using JiraIntegrationPlugin plugin = new(new HttpClient(handler));
        await InitializeAsync(plugin, temporary.Path);
        Guid projectId = Guid.NewGuid();
        WorkItemConnectionSettings settings = new(
            projectId, "https://studio.atlassian.net", "GAME", "developer@example.com", "JIRA_API_TOKEN");

        IReadOnlyList<WorkItemReference> results = await plugin.SearchAsync(settings, "jira-secret", "crash");

        WorkItemReference issue = Assert.Single(results);
        Assert.Equal("GAME-42", issue.DisplayKey);
        Assert.Equal("https://studio.atlassian.net/browse/GAME-42", issue.Url);
        Assert.Contains("/rest/api/3/issue/picker", handler.LastRequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("Basic", handler.LastAuthorizationScheme);
        Assert.Equal(
            "developer@example.com:jira-secret",
            Encoding.UTF8.GetString(Convert.FromBase64String(handler.LastAuthorizationValue!)));
    }

    [Fact]
    public async Task ClickUpSearchUsesWorkspaceAndFiltersTaskMetadata()
    {
        using TemporaryDirectory temporary = new();
        RecordingHandler handler = new(_ => Json("""
            {"tasks":[
              {"id":"abc","custom_id":"TEAM-9","name":"Improve project sync",
               "url":"https://app.clickup.com/t/abc","status":{"status":"in progress"},
               "list":{"name":"Sprint"},"folder":{"name":"Desktop"},"space":{"name":"CyRevision"}},
              {"id":"def","name":"Unrelated task","status":{"status":"open"}}
            ]}
            """));
        await using ClickUpIntegrationPlugin plugin = new(new HttpClient(handler));
        await InitializeAsync(plugin, temporary.Path);
        WorkItemConnectionSettings settings = new(
            Guid.NewGuid(), "https://api.clickup.com/api/v2", "7654321", string.Empty, "CLICKUP_API_TOKEN");

        IReadOnlyList<WorkItemReference> results = await plugin.SearchAsync(settings, "pk_session", "sync");

        WorkItemReference task = Assert.Single(results);
        Assert.Equal("TEAM-9", task.DisplayKey);
        Assert.Equal("https://app.clickup.com/t/abc", task.Url);
        Assert.Contains("/api/v2/team/7654321/task", handler.LastRequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("pk_session", handler.RawAuthorization);
    }

    [Fact]
    public async Task ProjectSettingsRoundTripWithoutPersistingSessionTokens()
    {
        using TemporaryDirectory temporary = new();
        await using JiraIntegrationPlugin plugin = new(new HttpClient(new RecordingHandler(_ => Json("{}"))));
        await InitializeAsync(plugin, temporary.Path);
        Guid projectId = Guid.NewGuid();
        WorkItemConnectionSettings expected = new(
            projectId, "https://studio.atlassian.net/", "GAME", "developer@example.com", string.Empty);

        await plugin.SaveConnectionAsync(expected);
        WorkItemConnectionSettings actual = await plugin.LoadConnectionAsync(projectId);

        Assert.Equal("https://studio.atlassian.net", actual.BaseUrl);
        Assert.Equal("JIRA_API_TOKEN", actual.TokenEnvironmentVariable);
        string configuration = await File.ReadAllTextAsync(Path.Combine(
            temporary.Path, "config", "work-items", "jira", projectId.ToString("N") + ".json"));
        Assert.DoesNotContain("jira-secret", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionToken", configuration, StringComparison.OrdinalIgnoreCase);
    }

    private static Task InitializeAsync(ICyRevisionPlugin plugin, string root) => plugin.InitializeAsync(
        new CyRevisionPluginContext(
            root,
            root,
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            "test"));

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationValue { get; private set; }
        public string? RawAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationValue = request.Headers.Authorization?.Parameter;
            RawAuthorization = request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
                ? values.Single()
                : null;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "cyrevision-work-item-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
