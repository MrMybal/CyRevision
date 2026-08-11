using System.Net;
using System.Text;
using System.Text.Json;
using CyRevision.Discord;
using CyRevision.Discord.Agent;
using CyRevision.Discord.Control;

namespace CyRevision.Core.Tests;

public sealed class DiscordAutonomousAgentTests : IDisposable
{
    private const string WebhookUrl =
        "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz_1234567890";
    private const string ControlToken = "control-token-with-more-than-thirty-two-characters";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CyRevisionAutonomousAgentTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Endpoint_RequiresHttpsOrExplicitTrustedVpnMode()
    {
        Assert.Equal("http://127.0.0.1:47831/", DiscordAgentEndpoint.Create(
            "http://127.0.0.1:47831", false).AbsoluteUri);
        Assert.Equal("https://agent.example.test:47831/", DiscordAgentEndpoint.Create(
            "https://agent.example.test:47831", false).AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => DiscordAgentEndpoint.Create(
            "http://10.70.0.2:47831", false));
        Assert.Equal("http://10.70.0.2:47831/", DiscordAgentEndpoint.Create(
            "http://10.70.0.2:47831", true).AbsoluteUri);
    }

    [Fact]
    public async Task ControlClient_SendsBearerTokenAndDoesNotRequireWebhookSecretsInResponses()
    {
        DiscordAgentHostStatus expected = new(
            "CyRevision.Discord.Agent", "0.2.0", DateTimeOffset.UtcNow, 2, 1);
        RecordingJsonHandler handler = new(expected);
        using HttpClient httpClient = new(handler);
        using DiscordAgentControlClient client = new(
            "http://127.0.0.1:47831",
            ControlToken,
            false,
            httpClient);

        DiscordAgentHostStatus actual = await client.GetHostStatusAsync();

        Assert.Equal(expected, actual);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.AuthenticationScheme);
        Assert.Equal(ControlToken, request.AuthenticationParameter);
        Assert.Equal("/api/v1/health", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Supervisor_RunsMultipleRegisteredProjectsIndependently()
    {
        string firstRepository = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        string secondRepository = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        JsonDiscordAgentStore store = new(Path.Combine(_root, "store"));
        StaticSnapshotProvider snapshots = new();
        await using DiscordAgentSupervisor supervisor = new(
            store,
            () => new DiscordProjectAgent(
                snapshots,
                store,
                new DiscordWebhookClient(new HttpClient(new SuccessHandler()))));
        DiscordAgentRegistration first = CreateRegistration(Guid.NewGuid(), "First", firstRepository);
        DiscordAgentRegistration second = CreateRegistration(Guid.NewGuid(), "Second", secondRepository);
        await supervisor.ConfigureAsync(first);
        await supervisor.ConfigureAsync(second);

        await supervisor.StartAsync(first.ProjectId);
        await supervisor.StartAsync(second.ProjectId);

        DiscordAgentPublicStatus firstStatus = Assert.IsType<DiscordAgentPublicStatus>(
            await supervisor.GetStatusAsync(first.ProjectId));
        DiscordAgentPublicStatus secondStatus = Assert.IsType<DiscordAgentPublicStatus>(
            await supervisor.GetStatusAsync(second.ProjectId));
        Assert.True(firstStatus.IsRunning);
        Assert.True(secondStatus.IsRunning);
        Assert.Equal(DiscordAgentRuntimeState.Watching, firstStatus.State);
        Assert.DoesNotContain(WebhookUrl, JsonSerializer.Serialize(firstStatus), StringComparison.Ordinal);

        await supervisor.StopAsync(first.ProjectId);
        firstStatus = Assert.IsType<DiscordAgentPublicStatus>(await supervisor.GetStatusAsync(first.ProjectId));
        secondStatus = Assert.IsType<DiscordAgentPublicStatus>(await supervisor.GetStatusAsync(second.ProjectId));
        Assert.False(firstStatus.IsRunning);
        Assert.True(secondStatus.IsRunning);
    }

    [Fact]
    public async Task ControlToken_IsGeneratedOnceAndBearerValidationIsExact()
    {
        string path = Path.Combine(_root, "token", "control-token.txt");
        (string first, bool created) = await ControlTokenProvider.LoadOrCreateAsync(
            path,
            useEnvironmentOverride: false);
        (string second, bool createdAgain) = await ControlTokenProvider.LoadOrCreateAsync(
            path,
            useEnvironmentOverride: false);

        Assert.True(created);
        Assert.False(createdAgain);
        Assert.Equal(first, second);
        Assert.True(ControlTokenProvider.IsValid(first, "Bearer " + first));
        Assert.False(ControlTokenProvider.IsValid(first, "Bearer " + first + "x"));
        Assert.False(ControlTokenProvider.IsValid(first, null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static DiscordAgentRegistration CreateRegistration(Guid projectId, string name, string path) => new(
        projectId,
        name,
        path,
        new DiscordAgentProfile(
            projectId,
            WebhookUrl,
            "CyRevision",
            name,
            null,
            3600,
            true,
            true,
            false));

    private sealed class StaticSnapshotProvider : IDiscordProjectSnapshotProvider
    {
        public Task<DiscordProjectSnapshot> GetSnapshotAsync(
            string repositoryPath,
            int maximumCommitCount = 100,
            CancellationToken cancellationToken = default)
        {
            DiscordCommit commit = new(
                repositoryPath.GetHashCode().ToString("x8"),
                "baseline",
                "Test",
                DateTimeOffset.UtcNow,
                "Baseline");
            return Task.FromResult(new DiscordProjectSnapshot("main", commit.Hash, [commit]));
        }
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
    }

    private class RecordingJsonHandler<T>(T response) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingJsonHandler(DiscordAgentHostStatus response)
        : RecordingJsonHandler<DiscordAgentHostStatus>(response);

    private sealed record CapturedRequest(Uri Uri, string? AuthenticationScheme, string? AuthenticationParameter);
}
