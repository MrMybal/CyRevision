using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using CyRevision.Vpn;

namespace CyRevision.Core.Tests;

public sealed class TeamChatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-team-chat-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProfileStoreCreatesAndRotatesProjectToken()
    {
        JsonTeamChatProfileStore store = new(Path.Combine(_root, "profiles"));
        Guid projectId = Guid.NewGuid();

        TeamChatProfile created = await store.GetOrCreateAsync(projectId, "Alice");
        TeamChatProfile rotated = await store.RotateTokenAsync(projectId);

        Assert.Equal(projectId, created.ProjectId);
        Assert.Equal("Alice", created.DisplayName);
        Assert.NotEmpty(created.AccessToken);
        Assert.NotEqual(created.AccessToken, rotated.AccessToken);
    }

    [Fact]
    public async Task SyncTransportUsesOneMessageFileAndPreservesAttachment()
    {
        Guid projectId = Guid.NewGuid();
        string sync = Path.Combine(_root, "sync");
        string attachment = Path.Combine(_root, "preview.png");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(attachment, [1, 2, 3, 4, 5]);
        TeamChatProfile profile = CreateProfile(projectId) with
        {
            Transport = TeamChatTransport.SyncFolder,
            SyncFolderPath = sync,
            ProjectRoot = Path.Combine(_root, "project")
        };
        TeamChatService service = new(Path.Combine(_root, "data"));

        TeamChatMessage sent = await service.SendSyncAsync(profile, "Build ready", attachment);
        IReadOnlyList<TeamChatMessage> loaded = await service.ReadSyncAsync(profile);

        TeamChatMessage message = Assert.Single(loaded);
        Assert.Equal(sent.Id, message.Id);
        Assert.Equal("Build ready", message.Text);
        Assert.Equal("preview.png", message.AttachmentName);
        Assert.True(File.Exists(message.AttachmentLocalPath));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(sync, ".cyrevision-chat", "messages"), "*.json", SearchOption.AllDirectories));
        TeamChatSnapshot cached = await service.ReadSyncSnapshotAsync(profile);
        Assert.Equal(0, cached.FilesParsed);
        Assert.True(File.Exists(Path.Combine(profile.ProjectRoot, ".cyrevision", "cache", "chat", "sync-index.json")));
    }

    [Fact]
    public async Task SyncTransportCanEncryptMessagesAndAttachmentsAtRest()
    {
        Guid projectId = Guid.NewGuid();
        string sync = Path.Combine(_root, "encrypted-sync");
        string attachment = Path.Combine(_root, "secret.png");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(attachment, [9, 8, 7, 6, 5]);
        TeamChatProfile profile = CreateProfile(projectId) with
        {
            Transport = TeamChatTransport.SyncFolder,
            SyncFolderPath = sync,
            ProjectRoot = Path.Combine(_root, "encrypted-project"),
            EncryptStoredConversations = true
        };
        TeamChatService service = new(Path.Combine(_root, "data"));

        await service.SendSyncAsync(profile, "Confidential build", attachment);
        string messageFile = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(sync, ".cyrevision-chat", "messages"), "*.json", SearchOption.AllDirectories));
        Assert.DoesNotContain("Confidential build", await File.ReadAllTextAsync(messageFile));
        TeamChatMessage loaded = Assert.Single(await service.ReadSyncAsync(profile));
        Assert.Empty(loaded.AttachmentLocalPath);
        TeamChatMessage materialized = await service.PrepareSyncAttachmentAsync(profile, loaded);
        Assert.Equal(new byte[] { 9, 8, 7, 6, 5 }, await File.ReadAllBytesAsync(materialized.AttachmentLocalPath));
    }

    [Fact]
    public async Task VpnTransportAuthenticatesAndExchangesMessage()
    {
        Guid projectId = Guid.NewGuid();
        int port = ReservePort();
        TeamChatProfile profile = CreateProfile(projectId) with
        {
            ListenAddress = "127.0.0.1",
            Port = port,
            PeerEndpoint = $"127.0.0.1:{port}",
            SaveConversations = false
        };
        TeamChatService service = new(Path.Combine(_root, "data"));
        await using TeamChatHost host = await service.StartVpnHostAsync(profile);

        TeamChatMessage sent = await service.SendVpnAsync(profile, "Hello over WireGuard", null);
        IReadOnlyList<TeamChatMessage> received = await service.ReadVpnAsync(profile);

        TeamChatMessage message = Assert.Single(received);
        Assert.Equal(sent.Id, message.Id);
        Assert.Equal("Hello over WireGuard", message.Text);
        TeamChatSnapshot snapshot = await service.ReadVpnSnapshotAsync(profile);
        Assert.Contains(snapshot.Participants, item => item.DisplayName == "Alice" && item.IsOnline);
    }

    [Fact]
    public async Task VpnAttachmentsAreDownloadedOnlyWhenRequested()
    {
        Guid projectId = Guid.NewGuid();
        int port = ReservePort();
        string attachment = Path.Combine(_root, "large-preview.jpg");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(attachment, Enumerable.Repeat((byte)42, 64 * 1024).ToArray());
        TeamChatProfile profile = CreateProfile(projectId) with
        {
            Port = port,
            PeerEndpoint = $"127.0.0.1:{port}",
            SaveConversations = false
        };
        TeamChatService service = new(Path.Combine(_root, "data"));
        await using TeamChatHost host = await service.StartVpnHostAsync(profile);

        await service.SendVpnAsync(profile, "Preview", attachment);
        TeamChatMessage listed = Assert.Single(await service.ReadVpnAsync(profile));
        Assert.Empty(listed.AttachmentLocalPath);
        TeamChatMessage downloaded = await service.DownloadVpnAttachmentAsync(profile, listed);
        Assert.True(File.Exists(downloaded.AttachmentLocalPath));
        Assert.Equal(64 * 1024, new FileInfo(downloaded.AttachmentLocalPath).Length);
    }

    [Fact]
    public async Task IncrementalIndexKeepsTenThousandMessageConversationResponsive()
    {
        const int messageCount = 10_000;
        Guid projectId = Guid.NewGuid();
        string sync = Path.Combine(_root, "large-sync");
        string month = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        string messages = Path.Combine(sync, ".cyrevision-chat", "messages", month);
        Directory.CreateDirectory(messages);
        TeamChatProfile profile = CreateProfile(projectId) with
        {
            Transport = TeamChatTransport.SyncFolder,
            SyncFolderPath = sync,
            ProjectRoot = Path.Combine(_root, "large-project")
        };
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-messageCount);
        await Parallel.ForEachAsync(Enumerable.Range(0, messageCount), new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (index, cancellationToken) =>
            {
                TeamChatMessage message = new(Guid.NewGuid(), projectId, "Load test", $"Message {index}",
                    start.AddMinutes(index), string.Empty, 0, string.Empty, string.Empty, string.Empty);
                string path = Path.Combine(messages, $"{index:D5}.json");
                await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(message, options), cancellationToken);
            });
        TeamChatService service = new(Path.Combine(_root, "large-data"));

        TeamChatSnapshot initial = await service.ReadSyncSnapshotAsync(profile);
        Stopwatch stopwatch = Stopwatch.StartNew();
        TeamChatSnapshot cached = await service.ReadSyncSnapshotAsync(profile);
        stopwatch.Stop();

        Assert.Equal(messageCount, initial.FilesScanned);
        Assert.Equal(messageCount, initial.FilesParsed);
        Assert.Equal(2_000, cached.Messages.Count);
        Assert.Equal(0, cached.FilesParsed);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Cached read took {stopwatch.Elapsed}.");
    }

    private static TeamChatProfile CreateProfile(Guid projectId) => new(
        projectId,
        "Alice",
        TeamChatTransport.Vpn,
        "127.0.0.1",
        TeamChatDefaults.Port,
        $"127.0.0.1:{TeamChatDefaults.Port}",
        "A-token-long-enough-for-a-project-chat",
        string.Empty,
        true,
        365,
        TeamChatDefaults.MaxAttachmentBytes,
        DateTimeOffset.UtcNow);

    private static int ReservePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
