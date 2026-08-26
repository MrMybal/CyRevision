using System.Net;
using System.Text;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.CyTask;

namespace CyRevision.Core.Tests;

public sealed class CyTaskWorkItemIntegrationPluginTests
{
    [Fact]
    public async Task SearchUsesCyTaskApiAndCreatesStableTicketLinks()
    {
        using TemporaryDirectory temporary = new();
        Guid taskId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        RecordingHandler handler = new(_ => Json($$$"""
            [{"id":"{{{taskId}}}","projectId":"{{{projectId}}}","key":"NEB-42",
              "title":"Repair hangar lighting","description":"Lumen regression",
              "status":"in_progress","priority":"high","revision":3}]
            """));
        await using CyTaskIntegrationPlugin plugin = new(new HttpClient(handler));
        await InitializeAsync(plugin, temporary.Path);
        WorkItemConnectionSettings settings = new(
            Guid.NewGuid(), "https://tasks.example.test", projectId.ToString(), "", "CYTASK_API_TOKEN");

        WorkItemReference ticket = Assert.Single(
            await plugin.SearchAsync(settings, "cytask-secret", "lighting"));

        Assert.Equal("NEB-42", ticket.DisplayKey);
        Assert.Equal($"https://tasks.example.test/#/tasks/{taskId}", ticket.Url);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
        Assert.Equal("cytask-secret", handler.LastAuthorizationValue);
        Assert.EndsWith($"/api/v1/projects/{projectId}/tasks", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CompletionTransitionUpdatesTaskWithOptimisticRevision()
    {
        using TemporaryDirectory temporary = new();
        Guid taskId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        RecordingHandler handler = new(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/statuses", StringComparison.Ordinal))
                return Json("""[{"key":"in_progress","name":"In progress"},{"key":"done","name":"Done"}]""");
            if (request.Method == HttpMethod.Get)
                return Json($$$"""
                    {"task":{"id":"{{{taskId}}}","projectId":"{{{projectId}}}","key":"NEB-42",
                    "title":"Repair lighting","description":"","status":"in_progress","priority":"high",
                    "dueAt":null,"revision":7,"assignees":[]}}
                    """);
            return Json($$$"""
                {"id":"{{{taskId}}}","projectId":"{{{projectId}}}","key":"NEB-42",
                "title":"Repair lighting","description":"","status":"done","priority":"high","revision":8}
                """);
        });
        await using CyTaskIntegrationPlugin plugin = new(new HttpClient(handler));
        await InitializeAsync(plugin, temporary.Path);
        WorkItemConnectionSettings settings = new(
            Guid.NewGuid(), "https://tasks.example.test", projectId.ToString(), "", "CYTASK_API_TOKEN");
        WorkItemReference ticket = new(
            "cytask", "CyTask", taskId.ToString(), "NEB-42", "Repair lighting", "In progress",
            $"https://tasks.example.test/#/tasks/{taskId}");

        WorkItemTransitionOption done = Assert.Single(
            (await plugin.GetTransitionsAsync(settings, "token", ticket)).Where(option => option.IsCompletion));
        WorkItemStatusUpdateResult result = await plugin.ApplyTransitionAsync(
            settings, "token", ticket, done);

        Assert.Equal("Done", result.NewStatus);
        Assert.Equal(HttpMethod.Patch, handler.LastMethod);
        Assert.Contains(@"""expectedRevision"":7", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains(@"""status"":""done""", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectSettingsNeverPersistSessionTokens()
    {
        using TemporaryDirectory temporary = new();
        await using CyTaskIntegrationPlugin plugin = new(new HttpClient(new RecordingHandler(_ => Json("[]"))));
        await InitializeAsync(plugin, temporary.Path);
        Guid projectId = Guid.NewGuid();
        WorkItemConnectionSettings expected = new(
            projectId, "https://tasks.example.test/", Guid.NewGuid().ToString(), "", "");

        await plugin.SaveConnectionAsync(expected);
        WorkItemConnectionSettings actual = await plugin.LoadConnectionAsync(projectId);

        Assert.Equal("https://tasks.example.test", actual.BaseUrl);
        Assert.Equal("CYTASK_API_TOKEN", actual.TokenEnvironmentVariable);
        string configuration = await File.ReadAllTextAsync(Path.Combine(
            temporary.Path, "config", "work-items", "cytask", projectId.ToString("N") + ".json"));
        Assert.DoesNotContain("sessionToken", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cytask-secret", configuration, StringComparison.OrdinalIgnoreCase);
    }

    private static Task InitializeAsync(ICyRevisionPlugin plugin, string root) => plugin.InitializeAsync(
        new CyRevisionPluginContext(
            root, root, Path.Combine(root, "config"), Path.Combine(root, "data"), "test"));

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationValue { get; private set; }
        public string LastRequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationValue = request.Headers.Authorization?.Parameter;
            LastRequestBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "cyrevision-cytask-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
