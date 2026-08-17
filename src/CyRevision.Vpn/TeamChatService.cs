using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Vpn;

public sealed class TeamChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string _dataDirectory;
    private readonly TeamChatIncrementalStore _syncIndex;
    private readonly HttpClient _httpClient;

    public TeamChatService(string dataDirectory, HttpClient? httpClient = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _syncIndex = new TeamChatIncrementalStore(_dataDirectory);
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<TeamChatHost> StartVpnHostAsync(
        TeamChatProfile profile,
        CancellationToken cancellationToken = default)
    {
        TeamChatHost host = new(profile, ProjectDirectory(profile.ProjectId), JsonOptions);
        await host.StartAsync(cancellationToken);
        return host;
    }

    public async Task<TeamChatMessage> SendVpnAsync(
        TeamChatProfile profile,
        string text,
        string? attachmentPath,
        CancellationToken cancellationToken = default)
    {
        TeamChatWireItem item = await CreateWireItemAsync(profile, text, attachmentPath, cancellationToken);
        try
        {
            TeamChatWireResponse response = await ExchangeAsync(
                profile,
                new TeamChatWireRequest("send", profile.AccessToken, null, item, profile.DisplayName, null, profile.SelectedChannelId),
                cancellationToken);
            if (!response.Succeeded) throw new InvalidOperationException(response.Error);
            await FlushOutboxAsync(profile, cancellationToken).ConfigureAwait(false);
            return (await MaterializeAsync(profile.ProjectId, response.Messages.Single(), cancellationToken).ConfigureAwait(false)) with
            {
                DeliveryState = TeamChatDeliveryState.Delivered,
                DeliveryDetail = "Acknowledged by host"
            };
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            await QueueOutboxAsync(profile, item, cancellationToken).ConfigureAwait(false);
            return item.Message with
            {
                DeliveryState = TeamChatDeliveryState.Pending,
                DeliveryDetail = "Queued locally; retry occurs on the next refresh"
            };
        }
    }

    public async Task<IReadOnlyList<TeamChatMessage>> ReadVpnAsync(
        TeamChatProfile profile,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await FlushOutboxAsync(profile, cancellationToken).ConfigureAwait(false);
        TeamChatWireResponse response = await ExchangeAsync(
            profile,
            new TeamChatWireRequest("list", profile.AccessToken, since, null, profile.DisplayName, null, profile.SelectedChannelId),
            cancellationToken);
        if (!response.Succeeded) throw new InvalidOperationException(response.Error);
        List<TeamChatMessage> messages = [];
        foreach (TeamChatWireItem item in response.Messages.OrderBy(item => item.Message.SentAt))
            messages.Add(item.Message with { DeliveryState = TeamChatDeliveryState.Delivered });
        return messages;
    }

    public async Task<TeamChatSnapshot> ReadVpnSnapshotAsync(
        TeamChatProfile profile,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await FlushOutboxAsync(profile, cancellationToken).ConfigureAwait(false);
        TeamChatWireResponse response = await ExchangeAsync(profile,
            new TeamChatWireRequest("list", profile.AccessToken, since, null, profile.DisplayName, null, profile.SelectedChannelId), cancellationToken)
            .ConfigureAwait(false);
        if (!response.Succeeded) throw new InvalidOperationException(response.Error);
        TeamChatMessage[] messages = response.Messages.Select(item => item.Message with
            { DeliveryState = TeamChatDeliveryState.Delivered }).OrderBy(item => item.SentAt).ToArray();
        return new TeamChatSnapshot(messages, response.Participants, messages.Length, messages.Length, DateTimeOffset.UtcNow);
    }

    public async Task<TeamChatMessage> DownloadVpnAttachmentAsync(
        TeamChatProfile profile,
        TeamChatMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentName)) return message;
        if (!string.IsNullOrWhiteSpace(message.AttachmentLocalPath) && File.Exists(message.AttachmentLocalPath)) return message;
        TeamChatWireResponse response = await ExchangeAsync(profile,
            new TeamChatWireRequest("attachment", profile.AccessToken, null, null, profile.DisplayName, message.Id, message.ChannelId),
            cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded) throw new InvalidOperationException(response.Error);
        TeamChatWireItem item = response.Messages.Single();
        return await MaterializeAsync(profile.ProjectId, item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TeamChatMessage> SendSyncAsync(
        TeamChatProfile profile,
        string text,
        string? attachmentPath,
        CancellationToken cancellationToken = default)
    {
        string root = ResolveSyncRoot(profile);
        TeamChatWireItem item = await CreateWireItemAsync(profile, text, attachmentPath, cancellationToken);
        TeamChatMessage message = item.Message;
        if (item.AttachmentBytes is { Length: > 0 } bytes)
        {
            string relative = Path.Combine("attachments", message.Id.ToString("N"), SafeFileName(message.AttachmentName));
            if (profile.EncryptStoredConversations) relative += ".cyenc";
            string destination = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await TeamChatArchiveCipher.WriteBytesAsync(destination, bytes, profile.AccessToken,
                profile.EncryptStoredConversations, cancellationToken).ConfigureAwait(false);
            message = message with { AttachmentRelativePath = relative.Replace('\\', '/') };
        }
        string messagesDirectory = Path.Combine(root, "messages", message.SentAt.ToString("yyyy-MM"));
        Directory.CreateDirectory(messagesDirectory);
        string path = Path.Combine(messagesDirectory, message.Id.ToString("N") + ".json");
        await TeamChatArchiveCipher.WriteJsonAsync(path, message, profile.AccessToken,
            profile.EncryptStoredConversations, cancellationToken).ConfigureAwait(false);
        await _syncIndex.WritePresenceAsync(profile, root, cancellationToken).ConfigureAwait(false);
        return message with
        {
            AttachmentLocalPath = string.IsNullOrWhiteSpace(message.AttachmentRelativePath)
                ? string.Empty
                : Path.Combine(root, message.AttachmentRelativePath.Replace('/', Path.DirectorySeparatorChar))
        };
    }

    public async Task<IReadOnlyList<TeamChatMessage>> ReadSyncAsync(
        TeamChatProfile profile,
        CancellationToken cancellationToken = default)
    {
        return (await ReadSyncSnapshotAsync(profile, cancellationToken).ConfigureAwait(false)).Messages;
    }

    public async Task<TeamChatSnapshot> ReadSyncSnapshotAsync(
        TeamChatProfile profile,
        CancellationToken cancellationToken = default)
    {
        string root = ResolveSyncRoot(profile);
        await _syncIndex.WritePresenceAsync(profile, root, cancellationToken).ConfigureAwait(false);
        return await _syncIndex.ReadAsync(profile, root, cancellationToken).ConfigureAwait(false);
    }

    public TeamChatSyncWatcher WatchSync(TeamChatProfile profile) => new(ResolveSyncRoot(profile));

    public async Task<TeamChatMessage> PrepareSyncAttachmentAsync(
        TeamChatProfile profile,
        TeamChatMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentRelativePath)) return message;
        string source = Path.Combine(ResolveSyncRoot(profile),
            message.AttachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(source)) throw new FileNotFoundException("The synchronized chat attachment is not available yet.", source);
        bool encrypted = source.EndsWith(".cyenc", StringComparison.OrdinalIgnoreCase);
        if (!encrypted) return message with { AttachmentLocalPath = source };
        byte[] bytes = await TeamChatArchiveCipher.ReadBytesAsync(source, profile.AccessToken, true, cancellationToken)
            .ConfigureAwait(false);
        string directory = Path.Combine(ProjectDirectory(profile.ProjectId), "received", message.Id.ToString("N"));
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, SafeFileName(message.AttachmentName));
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, message.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Decrypted chat attachment failed SHA-256 verification.");
        return message with { AttachmentLocalPath = destination };
    }

    public async Task<TeamChatMessage> SendServerAsync(
        TeamChatProfile profile,
        string text,
        string? attachmentPath,
        CancellationToken cancellationToken = default)
    {
        TeamChatWireItem item = await CreateWireItemAsync(profile, text, attachmentPath, cancellationToken)
            .ConfigureAwait(false);
        TeamChatServerSendRequest payload = new(
            profile.DisplayName,
            item.Message.ChannelId,
            item.Message.Text,
            item.Message.AttachmentName,
            item.AttachmentBytes,
            item.Message.AttachmentSha256);
        using HttpRequestMessage request = await CreateServerRequestAsync(
            profile,
            HttpMethod.Post,
            $"api/v1/projects/{profile.ProjectId:D}/chat/messages",
            cancellationToken).ConfigureAwait(false);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        TeamChatMessage message = await response.Content.ReadFromJsonAsync<TeamChatMessage>(JsonOptions, cancellationToken)
                                  ?? throw new InvalidDataException("The private chat server returned an empty message.");
        return message with { DeliveryState = TeamChatDeliveryState.Delivered, DeliveryDetail = "Stored by private server" };
    }

    public async Task<TeamChatSnapshot> ReadServerSnapshotAsync(
        TeamChatProfile profile,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        string relative = $"api/v1/projects/{profile.ProjectId:D}/chat/snapshot?user={Uri.EscapeDataString(profile.DisplayName)}";
        if (since is not null) relative += $"&since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        using HttpRequestMessage request = await CreateServerRequestAsync(profile, HttpMethod.Get, relative, cancellationToken)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<TeamChatSnapshot>(JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("The private chat server returned an empty conversation.");
    }

    public async Task<TeamChatChannel> CreateServerChannelAsync(
        TeamChatProfile profile,
        string name,
        string topic,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = await CreateServerRequestAsync(
            profile,
            HttpMethod.Post,
            $"api/v1/projects/{profile.ProjectId:D}/chat/channels",
            cancellationToken).ConfigureAwait(false);
        request.Content = JsonContent.Create(new TeamChatServerCreateChannelRequest(name, topic), options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<TeamChatChannel>(JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("The private chat server returned an empty channel.");
    }

    public async Task<TeamChatMessage> DownloadServerAttachmentAsync(
        TeamChatProfile profile,
        TeamChatMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentName)) return message;
        if (!string.IsNullOrWhiteSpace(message.AttachmentLocalPath) && File.Exists(message.AttachmentLocalPath)) return message;
        using HttpRequestMessage request = await CreateServerRequestAsync(
            profile,
            HttpMethod.Get,
            $"api/v1/projects/{profile.ProjectId:D}/chat/messages/{message.Id:D}/attachment",
            cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        TeamChatServerAttachment attachment = await response.Content.ReadFromJsonAsync<TeamChatServerAttachment>(JsonOptions, cancellationToken)
                                              ?? throw new InvalidDataException("The private chat server returned an empty attachment.");
        if (attachment.Bytes.LongLength > profile.MaxAttachmentBytes)
            throw new InvalidDataException("Downloaded attachment exceeds the configured size limit.");
        string actual = Convert.ToHexString(SHA256.HashData(attachment.Bytes)).ToLowerInvariant();
        if (!string.Equals(actual, attachment.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actual, message.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded attachment failed SHA-256 verification.");
        string directory = Path.Combine(ProjectDirectory(profile.ProjectId), "received", message.Id.ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, SafeFileName(attachment.Name));
        await File.WriteAllBytesAsync(path, attachment.Bytes, cancellationToken).ConfigureAwait(false);
        return message with { AttachmentLocalPath = path };
    }

    private async Task<TeamChatWireItem> CreateWireItemAsync(
        TeamChatProfile profile,
        string text,
        string? attachmentPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(attachmentPath))
            throw new InvalidDataException("Write a message or select an attachment.");
        byte[]? bytes = null;
        string name = string.Empty;
        string hash = string.Empty;
        if (!string.IsNullOrWhiteSpace(attachmentPath))
        {
            FileInfo info = new(attachmentPath);
            if (!info.Exists) throw new FileNotFoundException("Chat attachment not found.", attachmentPath);
            if (info.Length > profile.MaxAttachmentBytes)
                throw new InvalidDataException($"Attachment exceeds the {profile.MaxAttachmentBytes / (1024 * 1024)} MB limit.");
            bytes = await File.ReadAllBytesAsync(info.FullName, cancellationToken);
            name = SafeFileName(info.Name);
            hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        TeamChatMessage message = new(
            Guid.NewGuid(), profile.ProjectId, profile.DisplayName.Trim(), text.Trim(), DateTimeOffset.UtcNow,
            name, bytes?.LongLength ?? 0, hash, string.Empty, string.Empty,
            ChannelId: NormalizeChannelId(profile.SelectedChannelId));
        return new TeamChatWireItem(message, bytes);
    }

    private async Task<HttpRequestMessage> CreateServerRequestAsync(
        TeamChatProfile profile,
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.ServerApiToken))
            throw new InvalidOperationException("Enter the private CyRevision server access token.");
        Uri baseUri = await ValidateServerUriAsync(profile, cancellationToken).ConfigureAwait(false);
        HttpRequestMessage request = new(method, new Uri(baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ServerApiToken.Trim());
        request.Headers.Add("X-CyRevision-Chat-User", profile.DisplayName.Trim());
        return request;
    }

    private static async Task<Uri> ValidateServerUriAsync(TeamChatProfile profile, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(profile.ServerBaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("https" or "http"))
            throw new InvalidDataException("Private server URL must be an absolute HTTP or HTTPS URL.");
        if (uri.Scheme == "https") return uri;
        if (!profile.AllowPrivateServerHttp)
            throw new InvalidOperationException("HTTPS is required. Enable private HTTP only for a trusted LAN or VPN address.");
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.All(address => !IsPrivateAddress(address)))
            throw new InvalidOperationException("Plain HTTP is allowed only for loopback, LAN or VPN server addresses.");
        return uri;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > 600) detail = detail[..600];
        throw new HttpRequestException(
            $"Private chat server returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail}",
            null,
            response.StatusCode);
    }

    private static string NormalizeChannelId(string value)
    {
        string normalized = new((value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());
        normalized = normalized.Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "general" : normalized;
    }

    private async Task<TeamChatMessage> MaterializeAsync(
        Guid projectId,
        TeamChatWireItem item,
        CancellationToken cancellationToken)
    {
        TeamChatMessage message = item.Message;
        if (item.AttachmentBytes is not { Length: > 0 } bytes) return message;
        string directory = Path.Combine(ProjectDirectory(projectId), "received", message.Id.ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, SafeFileName(message.AttachmentName));
        if (!File.Exists(path)) await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return message with { AttachmentLocalPath = path };
    }

    private async Task<TeamChatWireResponse> ExchangeAsync(
        TeamChatProfile profile,
        TeamChatWireRequest request,
        CancellationToken cancellationToken)
    {
        (string host, int port) = ParseEndpoint(profile.PeerEndpoint, profile.Port);
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        IPAddress? privateAddress = addresses.FirstOrDefault(IsPrivateAddress);
        if (privateAddress is null)
            throw new InvalidOperationException("Team chat peer must resolve to a loopback, private or VPN address.");
        using TcpClient client = new();
        await client.ConnectAsync(privateAddress, port, cancellationToken);
        await using NetworkStream stream = client.GetStream();
        await TeamChatWire.WriteAsync(stream, request, JsonOptions, cancellationToken);
        return await TeamChatWire.ReadAsync<TeamChatWireResponse>(
            stream, checked((int)Math.Min(int.MaxValue, profile.MaxAttachmentBytes + 4 * 1024 * 1024)), JsonOptions, cancellationToken);
    }

    private async Task QueueOutboxAsync(
        TeamChatProfile profile,
        TeamChatWireItem item,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(ProjectDirectory(profile.ProjectId), "outbox");
        Directory.CreateDirectory(directory);
        await WriteJsonAtomicAsync(Path.Combine(directory, item.Message.Id.ToString("N") + ".json"), item, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task FlushOutboxAsync(TeamChatProfile profile, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(ProjectDirectory(profile.ProjectId), "outbox");
        if (!Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(File.GetCreationTimeUtc).Take(25))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TeamChatWireItem? item;
            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
                item = await JsonSerializer.DeserializeAsync<TeamChatWireItem>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException) { File.Delete(path); continue; }
            catch (IOException) { continue; }
            if (item is null) { File.Delete(path); continue; }
            TeamChatWireResponse response;
            try
            {
                response = await ExchangeAsync(profile,
                    new TeamChatWireRequest("send", profile.AccessToken, null, item, profile.DisplayName, null, item.Message.ChannelId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                break;
            }
            if (response.Succeeded) File.Delete(path);
        }
    }

    private string ProjectDirectory(Guid projectId) => Path.Combine(_dataDirectory, projectId.ToString("N"));

    private static string ResolveSyncRoot(TeamChatProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SyncFolderPath))
            throw new InvalidOperationException("Select a synchronized folder for team chat first.");
        string root = Path.Combine(Path.GetFullPath(profile.SyncFolderPath), ".cyrevision-chat");
        Directory.CreateDirectory(root);
        return root;
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint, int fallbackPort)
    {
        string value = endpoint.Trim();
        int separator = value.LastIndexOf(':');
        if (separator > 0 && int.TryParse(value[(separator + 1)..], out int port)) return (value[..separator], port);
        return (value, fallbackPort);
    }

    private static string SafeFileName(string value) => string.Concat(Path.GetFileName(value).Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        return bytes[0] == 10 || bytes[0] == 127 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, true))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
    }
}

public sealed class TeamChatHost : IAsyncDisposable
{
    private readonly TeamChatProfile _profile;
    private readonly string _projectDirectory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<Guid, TeamChatWireItem> _messages = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _presence = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private TcpListener? _listener;
    private Task? _loop;

    internal TeamChatHost(TeamChatProfile profile, string projectDirectory, JsonSerializerOptions jsonOptions)
    {
        _profile = profile;
        _projectDirectory = projectDirectory;
        _jsonOptions = jsonOptions;
    }

    public string Endpoint => $"{_profile.ListenAddress}:{_profile.Port}";

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        IPAddress address = IPAddress.Parse(_profile.ListenAddress);
        if (!address.Equals(IPAddress.Loopback) && !IsPrivate(address))
            throw new InvalidOperationException("Team chat must listen on loopback or a private/VPN address.");
        if (_profile.SaveConversations) await LoadArchiveAsync(cancellationToken);
        _listener = new TcpListener(address, _profile.Port);
        _listener.Start();
        _loop = AcceptLoopAsync(_lifetime.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            IPEndPoint? remote = client.Client.RemoteEndPoint as IPEndPoint;
            if (remote is null || !IsPrivate(remote.Address)) return;
            await using NetworkStream stream = client.GetStream();
            TeamChatWireResponse response;
            try
            {
                int maximum = checked((int)Math.Min(int.MaxValue, _profile.MaxAttachmentBytes + 4 * 1024 * 1024));
                TeamChatWireRequest request = await TeamChatWire.ReadAsync<TeamChatWireRequest>(
                    stream, maximum, _jsonOptions, cancellationToken);
                if (!TokenEquals(request.Token, _profile.AccessToken))
                    response = CreateResponse(false, "Unauthorized team chat request.", []);
                else if (string.IsNullOrWhiteSpace(request.ClientName))
                    response = CreateResponse(false, "A display name is required.", []);
                else if (request.Action == "send" && request.Item is not null)
                {
                    TouchPresence(request.ClientName);
                    response = await ReceiveAsync(request.Item, cancellationToken);
                }
                else if (request.Action == "list")
                {
                    TouchPresence(request.ClientName);
                    response = CreateResponse(true, string.Empty, _messages.Values
                        .Where(item => request.Since is null || item.Message.SentAt > request.Since)
                        .Where(item => string.IsNullOrWhiteSpace(request.ChannelId) ||
                                       string.Equals(item.Message.ChannelId, request.ChannelId, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.Message.SentAt).TakeLast(500)
                        .Select(item => item with { AttachmentBytes = null }).ToArray());
                }
                else if (request.Action == "attachment" && request.MessageId is Guid messageId &&
                         _messages.TryGetValue(messageId, out TeamChatWireItem? attachment))
                {
                    TouchPresence(request.ClientName);
                    response = CreateResponse(true, string.Empty, [attachment]);
                }
                else
                    response = CreateResponse(false, "Unknown team chat action.", []);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                response = CreateResponse(false, exception.Message, []);
            }
            await TeamChatWire.WriteAsync(stream, response, _jsonOptions, cancellationToken);
        }
    }

    private async Task<TeamChatWireResponse> ReceiveAsync(TeamChatWireItem item, CancellationToken cancellationToken)
    {
        TeamChatMessage message = item.Message;
        if (message.ProjectId != _profile.ProjectId) return CreateResponse(false, "Project mismatch.", []);
        if (item.AttachmentBytes?.LongLength > _profile.MaxAttachmentBytes)
            return CreateResponse(false, "Attachment exceeds the configured limit.", []);
        if (item.AttachmentBytes is { Length: > 0 } bytes)
        {
            string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, message.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
                return CreateResponse(false, "Attachment SHA-256 verification failed.", []);
        }
        _messages[message.Id] = item;
        if (_profile.SaveConversations) await PersistAsync(item, cancellationToken);
        return CreateResponse(true, string.Empty, [item]);
    }

    private void TouchPresence(string displayName) => _presence[displayName.Trim()] = DateTimeOffset.UtcNow;

    private TeamChatWireResponse CreateResponse(bool succeeded, string error, IReadOnlyList<TeamChatWireItem> messages)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TeamChatParticipant[] participants = _presence
            .Select(pair => new TeamChatParticipant(pair.Key, pair.Value, now - pair.Value < TimeSpan.FromMinutes(2), "VPN"))
            .OrderByDescending(item => item.IsOnline)
            .ThenBy(item => item.DisplayName)
            .ToArray();
        return new TeamChatWireResponse(succeeded, error, messages, participants);
    }

    private async Task PersistAsync(TeamChatWireItem item, CancellationToken cancellationToken)
    {
        string messages = Path.Combine(_projectDirectory, "archive", "messages");
        Directory.CreateDirectory(messages);
        TeamChatMessage message = item.Message;
        if (item.AttachmentBytes is { Length: > 0 } bytes)
        {
            string path = Path.Combine(_projectDirectory, "archive", "attachments", message.Id.ToString("N"),
                string.Concat(Path.GetFileName(message.AttachmentName).Select(character =>
                    Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)));
            if (_profile.EncryptStoredConversations) path += ".cyenc";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await TeamChatArchiveCipher.WriteBytesAsync(path, bytes, _profile.AccessToken,
                _profile.EncryptStoredConversations, cancellationToken).ConfigureAwait(false);
        }
        string messagePath = Path.Combine(messages, message.Id.ToString("N") + ".json");
        await TeamChatArchiveCipher.WriteJsonAsync(messagePath, message, _profile.AccessToken,
            _profile.EncryptStoredConversations, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadArchiveAsync(CancellationToken cancellationToken)
    {
        string messages = Path.Combine(_projectDirectory, "archive", "messages");
        if (!Directory.Exists(messages)) return;
        foreach (string path in Directory.EnumerateFiles(messages, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                TeamChatMessage? message = await TeamChatArchiveCipher.ReadJsonAsync<TeamChatMessage>(
                    path, _profile.AccessToken, cancellationToken).ConfigureAwait(false);
                if (message is null) continue;
                if (_profile.RetentionDays > 0 &&
                    message.SentAt < DateTimeOffset.UtcNow.AddDays(-_profile.RetentionDays)) continue;
                string attachment = Path.Combine(_projectDirectory, "archive", "attachments", message.Id.ToString("N"),
                    Path.GetFileName(message.AttachmentName));
                string encryptedAttachment = attachment + ".cyenc";
                byte[]? bytes = File.Exists(encryptedAttachment)
                    ? await TeamChatArchiveCipher.ReadBytesAsync(encryptedAttachment, _profile.AccessToken, true, cancellationToken)
                        .ConfigureAwait(false)
                    : File.Exists(attachment)
                        ? await File.ReadAllBytesAsync(attachment, cancellationToken).ConfigureAwait(false)
                        : null;
                _messages[message.Id] = new TeamChatWireItem(message, bytes);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _listener?.Stop();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
    }

    private static bool TokenEquals(string left, string right)
    {
        byte[] a = System.Text.Encoding.UTF8.GetBytes(left);
        byte[] b = System.Text.Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        return bytes[0] == 10 || bytes[0] == 127 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }
}

internal sealed record TeamChatWireRequest(
    string Action,
    string Token,
    DateTimeOffset? Since,
    TeamChatWireItem? Item,
    string ClientName,
    Guid? MessageId,
    string ChannelId = "general");

internal sealed record TeamChatWireResponse(
    bool Succeeded,
    string Error,
    IReadOnlyList<TeamChatWireItem> Messages,
    IReadOnlyList<TeamChatParticipant> Participants);

internal sealed record TeamChatWireItem(TeamChatMessage Message, byte[]? AttachmentBytes);

internal static class TeamChatWire
{
    public static async Task WriteAsync<T>(
        Stream stream,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, options);
        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        int maximumBytes,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken);
        int count = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(length));
        if (count is <= 0 || count > maximumBytes) throw new InvalidDataException("Team chat message size is invalid.");
        byte[] payload = new byte[count];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, options)
               ?? throw new InvalidDataException("Team chat message is empty.");
    }
}
