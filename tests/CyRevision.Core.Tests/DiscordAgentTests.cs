using System.Net;
using System.Text;
using System.Text.Json;
using CyRevision.Discord;

namespace CyRevision.Core.Tests;

public sealed class DiscordAgentTests : IDisposable
{
    private const string WebhookUrl =
        "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz_1234567890";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CyRevisionDiscordTests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("http://discord.com/api/webhooks/123/token-token-token-token-token", false)]
    [InlineData("https://example.com/api/webhooks/123/token-token-token-token-token", false)]
    [InlineData("https://discord.com/api/webhooks/not-a-number/token-token-token-token-token", false)]
    [InlineData(WebhookUrl, true)]
    public void WebhookAddress_AcceptsOnlyDiscordHttpsWebhooks(string value, bool expected)
    {
        Assert.Equal(expected, DiscordWebhookAddress.TryCreate(value, out _));
    }

    [Fact]
    public async Task WebhookClient_GroupsCommitsAndDisablesMentions()
    {
        RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        using DiscordWebhookClient client = new(httpClient);
        DiscordAgentProfile profile = CreateProfile() with
        {
            RepositoryWebUrl = "https://github.com/example/project"
        };
        DiscordCommit[] commits =
        [
            new("222222", "222222", "Alice", DateTimeOffset.UtcNow, "Add Discord notifications @everyone"),
            new("111111", "111111", "Bob", DateTimeOffset.UtcNow.AddMinutes(-1), "Update project")
        ];
        DiscordProjectSnapshot snapshot = new("main", "222222", commits);

        await client.SendProjectUpdateAsync(profile, "Project", snapshot, commits, "develop", false);

        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("wait=true", request.Uri.Query.TrimStart('?'));
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement root = document.RootElement;
        Assert.Equal("CyRevision", root.GetProperty("username").GetString());
        Assert.Empty(root.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
        JsonElement[] embeds = root.GetProperty("embeds").EnumerateArray().ToArray();
        Assert.Equal(2, embeds.Length);
        Assert.Contains("2 new commits", embeds[0].GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Contains("@everyone", embeds[0].GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Active branch changed", embeds[1].GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_BaselinesWithoutPublishingThenSendsEachCommitBatchOnce()
    {
        DiscordCommit baseline = new("111111", "111111", "Alice", DateTimeOffset.UtcNow.AddMinutes(-2), "Initial");
        DiscordCommit update = new("222222", "222222", "Bob", DateTimeOffset.UtcNow, "New work");
        SequenceSnapshotProvider snapshots = new(
            new DiscordProjectSnapshot("main", baseline.Hash, [baseline]),
            new DiscordProjectSnapshot("main", update.Hash, [update, baseline]),
            new DiscordProjectSnapshot("main", update.Hash, [update, baseline]));
        RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        JsonDiscordAgentStore store = new(_root);
        await using DiscordProjectAgent agent = new(
            snapshots,
            store,
            new DiscordWebhookClient(httpClient));
        DiscordAgentProfile profile = CreateProfile() with { PollIntervalSeconds = 3600 };

        await agent.StartAsync(profile, _root, "Project");
        Assert.Empty(handler.Requests);

        await agent.PollNowAsync();
        Assert.Single(handler.Requests);

        await agent.PollNowAsync();
        Assert.Single(handler.Requests);
        DiscordAgentCheckpoint? checkpoint = await store.GetCheckpointAsync(profile.ProjectId);
        Assert.NotNull(checkpoint);
        Assert.Equal(update.Hash, checkpoint.LastAnnouncedCommitHash);
        Assert.NotNull(checkpoint.LastNotificationAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static DiscordAgentProfile CreateProfile() => new(
        Guid.NewGuid(),
        WebhookUrl,
        "CyRevision",
        "Project",
        null,
        30,
        true,
        true,
        false);

    private sealed class SequenceSnapshotProvider(params DiscordProjectSnapshot[] snapshots)
        : IDiscordProjectSnapshotProvider
    {
        private int _index;

        public Task<DiscordProjectSnapshot> GetSnapshotAsync(
            string repositoryPath,
            int maximumCommitCount = 100,
            CancellationToken cancellationToken = default)
        {
            int index = Math.Min(_index++, snapshots.Length - 1);
            return Task.FromResult(snapshots[index]);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body);
}
