using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Vpn;

/// <summary>
/// Durable project-scoped chat storage used by CyRevision.Server. Messages are immutable files so a
/// server restart never loses the conversation and concurrent sends never rewrite a shared log.
/// </summary>
public sealed class TeamChatServerRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectLocks = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, string User), DateTimeOffset> _presence = new();

    public TeamChatServerRepository(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public async Task<TeamChatMessage> SendAsync(
        Guid projectId,
        TeamChatServerSendRequest request,
        long maximumAttachmentBytes = TeamChatDefaults.MaxAttachmentBytes,
        CancellationToken cancellationToken = default)
    {
        string author = NormalizeRequired(request.Author, "A chat author is required.", 80);
        string channelId = NormalizeChannelId(request.ChannelId);
        string text = (request.Text ?? string.Empty).Trim();
        byte[]? attachment = request.AttachmentBytes;
        if (string.IsNullOrWhiteSpace(text) && attachment is not { Length: > 0 })
            throw new InvalidDataException("Write a message or attach a file.");
        if (attachment is { LongLength: var length } && length > maximumAttachmentBytes)
            throw new InvalidDataException($"Attachment exceeds the {maximumAttachmentBytes / (1024 * 1024)} MB server limit.");

        string attachmentName = attachment is { Length: > 0 }
            ? SafeFileName(NormalizeRequired(request.AttachmentName, "An attachment name is required.", 240))
            : string.Empty;
        string hash = attachment is { Length: > 0 }
            ? Convert.ToHexString(SHA256.HashData(attachment)).ToLowerInvariant()
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(request.AttachmentSha256) &&
            !string.Equals(hash, request.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Attachment SHA-256 verification failed.");

        Guid id = Guid.NewGuid();
        DateTimeOffset sentAt = DateTimeOffset.UtcNow;
        string attachmentRelativePath = attachment is { Length: > 0 }
            ? $"attachments/{id:N}/{attachmentName}"
            : string.Empty;
        TeamChatMessage message = new(
            id,
            projectId,
            author,
            text,
            sentAt,
            attachmentName,
            attachment?.LongLength ?? 0,
            hash,
            string.Empty,
            attachmentRelativePath,
            ChannelId: channelId);

        SemaphoreSlim gate = _projectLocks.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string projectRoot = ProjectRoot(projectId);
            if (attachment is { Length: > 0 })
            {
                string attachmentPath = Path.Combine(projectRoot, attachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);
                await File.WriteAllBytesAsync(attachmentPath, attachment, cancellationToken).ConfigureAwait(false);
            }

            string messages = Path.Combine(projectRoot, "messages", sentAt.ToString("yyyy-MM"));
            Directory.CreateDirectory(messages);
            await WriteJsonAtomicAsync(Path.Combine(messages, $"{sentAt:yyyyMMddHHmmssfffffff}-{id:N}.json"), message, cancellationToken)
                .ConfigureAwait(false);
            Touch(projectId, author);
            return message;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TeamChatSnapshot> ReadSnapshotAsync(
        Guid projectId,
        string user,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(user)) Touch(projectId, user);
        string messagesRoot = Path.Combine(ProjectRoot(projectId), "messages");
        List<TeamChatMessage> messages = [];
        int scanned = 0;
        if (Directory.Exists(messagesRoot))
        {
            foreach (string path in Directory.EnumerateFiles(messagesRoot, "*.json", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(4000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                try
                {
                    await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                        32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    TeamChatMessage? message = await JsonSerializer.DeserializeAsync<TeamChatMessage>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    if (message is not null && (since is null || message.SentAt > since)) messages.Add(message);
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TeamChatParticipant[] participants = _presence
            .Where(pair => pair.Key.ProjectId == projectId)
            .Select(pair => new TeamChatParticipant(
                pair.Key.User,
                pair.Value,
                now - pair.Value < TimeSpan.FromMinutes(2),
                "Server"))
            .OrderByDescending(item => item.IsOnline)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IReadOnlyList<TeamChatChannel> channels = await ReadChannelsAsync(projectId, cancellationToken).ConfigureAwait(false);
        return new TeamChatSnapshot(
            messages.OrderBy(item => item.SentAt).TakeLast(2000).ToArray(),
            participants,
            scanned,
            messages.Count,
            now,
            channels);
    }

    public async Task<TeamChatChannel> CreateChannelAsync(
        Guid projectId,
        TeamChatServerCreateChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        string name = NormalizeRequired(request.Name, "A channel name is required.", 48).TrimStart('#');
        string id = NormalizeChannelId(name);
        string topic = (request.Topic ?? string.Empty).Trim();
        if (topic.Length > 240) topic = topic[..240];
        SemaphoreSlim gate = _projectLocks.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TeamChatChannel> channels = (await ReadChannelsAsync(projectId, cancellationToken).ConfigureAwait(false)).ToList();
            TeamChatChannel? existing = channels.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            TeamChatChannel channel = new(id, name, topic, channels.Count == 0 ? 0 : channels.Max(item => item.Position) + 10);
            channels.Add(channel);
            string path = ChannelsPath(projectId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await WriteJsonAtomicAsync(path, channels.OrderBy(item => item.Position).ToArray(), cancellationToken).ConfigureAwait(false);
            return channel;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TeamChatServerAttachment> ReadAttachmentAsync(
        Guid projectId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        TeamChatMessage? message = await FindMessageAsync(projectId, messageId, cancellationToken).ConfigureAwait(false);
        if (message is null || string.IsNullOrWhiteSpace(message.AttachmentRelativePath))
            throw new FileNotFoundException("Chat attachment was not found.");
        string path = Path.Combine(ProjectRoot(projectId), message.AttachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new FileNotFoundException("Chat attachment was not found.", path);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, message.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stored chat attachment failed SHA-256 verification.");
        return new TeamChatServerAttachment(message.AttachmentName, actual, bytes);
    }

    private async Task<TeamChatMessage?> FindMessageAsync(Guid projectId, Guid messageId, CancellationToken cancellationToken)
    {
        string root = Path.Combine(ProjectRoot(projectId), "messages");
        if (!Directory.Exists(root)) return null;
        foreach (string path in Directory.EnumerateFiles(root, $"*-{messageId:N}.json", SearchOption.AllDirectories))
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024, true);
            return await JsonSerializer.DeserializeAsync<TeamChatMessage>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<IReadOnlyList<TeamChatChannel>> ReadChannelsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        string path = ChannelsPath(projectId);
        if (!File.Exists(path)) return TeamChatDefaults.Channels;
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 16 * 1024, true);
            TeamChatChannel[]? channels = await JsonSerializer.DeserializeAsync<TeamChatChannel[]>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return channels is { Length: > 0 } ? channels.OrderBy(item => item.Position).ToArray() : TeamChatDefaults.Channels;
        }
        catch (JsonException)
        {
            return TeamChatDefaults.Channels;
        }
    }

    private void Touch(Guid projectId, string user) =>
        _presence[(projectId, NormalizeRequired(user, "A chat user is required.", 80))] = DateTimeOffset.UtcNow;

    private string ProjectRoot(Guid projectId) => Path.Combine(_root, projectId.ToString("N"));
    private string ChannelsPath(Guid projectId) => Path.Combine(ProjectRoot(projectId), "channels.json");

    private static string NormalizeChannelId(string value)
    {
        string normalized = new((value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());
        normalized = normalized.Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "general" : normalized[..Math.Min(normalized.Length, 48)];
    }

    private static string NormalizeRequired(string value, string error, int maximumLength)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new InvalidDataException(error);
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string SafeFileName(string value) => string.Concat(Path.GetFileName(value).Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }
}
