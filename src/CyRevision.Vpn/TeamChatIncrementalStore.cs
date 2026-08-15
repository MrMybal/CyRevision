using System.Collections.Concurrent;
using System.Text.Json;

namespace CyRevision.Vpn;

/// <summary>
/// Keeps the immutable synchronized chat log cheap to reopen. The shared folder
/// remains the source of truth; this index is local, disposable and never synced.
/// </summary>
internal sealed class TeamChatIncrementalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _fallbackRoot;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<Guid, TeamChatIndexDocument> _memory = new();

    public TeamChatIncrementalStore(string fallbackRoot)
    {
        _fallbackRoot = Path.GetFullPath(fallbackRoot);
    }

    public async Task<TeamChatSnapshot> ReadAsync(
        TeamChatProfile profile,
        string syncRoot,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _gates.GetOrAdd(profile.ProjectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TeamChatIndexDocument index = await GetIndexAsync(profile, cancellationToken).ConfigureAwait(false);
            string messagesRoot = Path.Combine(syncRoot, "messages");
            if (!Directory.Exists(messagesRoot))
                return new TeamChatSnapshot([], await ReadPresenceAsync(syncRoot, cancellationToken).ConfigureAwait(false), 0, 0, DateTimeOffset.UtcNow);

            DateTimeOffset oldest = profile.RetentionDays > 0
                ? DateTimeOffset.UtcNow.AddDays(-profile.RetentionDays)
                : DateTimeOffset.MinValue;
            Dictionary<string, TeamChatIndexEntry> retained = new(StringComparer.OrdinalIgnoreCase);
            int scanned = 0;
            int parsed = 0;
            foreach (string path in EnumerateMessageFiles(messagesRoot, oldest))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                FileInfo info;
                try { info = new FileInfo(path); }
                catch (IOException) { continue; }
                string key = Path.GetRelativePath(messagesRoot, path).Replace('\\', '/');
                if (index.Entries.TryGetValue(key, out TeamChatIndexEntry? cached) &&
                    cached.Length == info.Length && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
                {
                    if (cached.Message.ProjectId == profile.ProjectId && cached.Message.SentAt >= oldest)
                        retained[key] = cached;
                    continue;
                }

                TeamChatMessage? message = await ReadMessageAsync(path, profile.AccessToken, cancellationToken).ConfigureAwait(false);
                parsed++;
                if (message?.ProjectId != profile.ProjectId || message.SentAt < oldest) continue;
                retained[key] = new TeamChatIndexEntry(info.LastWriteTimeUtc.Ticks, info.Length, message);
            }

            TeamChatIndexDocument updated = new(2, DateTimeOffset.UtcNow, retained);
            _memory[profile.ProjectId] = updated;
            if (parsed > 0 || retained.Count != index.Entries.Count)
                await SaveIndexAsync(profile, updated, cancellationToken).ConfigureAwait(false);

            TeamChatMessage[] messages = retained.Values
                .Select(entry => MaterializePath(entry.Message, syncRoot))
                .OrderBy(message => message.SentAt)
                .TakeLast(2000)
                .ToArray();
            IReadOnlyList<TeamChatParticipant> participants = await ReadPresenceAsync(syncRoot, cancellationToken).ConfigureAwait(false);
            return new TeamChatSnapshot(messages, participants, scanned, parsed, DateTimeOffset.UtcNow);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WritePresenceAsync(TeamChatProfile profile, string syncRoot, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(syncRoot, "presence");
        Directory.CreateDirectory(directory);
        string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(profile.DisplayName.Trim())))[..16].ToLowerInvariant();
        string path = Path.Combine(directory, identity + ".json");
        TeamChatParticipant participant = new(profile.DisplayName.Trim(), DateTimeOffset.UtcNow, true, "Sync");
        await WriteAtomicAsync(path, participant, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TeamChatIndexDocument> GetIndexAsync(TeamChatProfile profile, CancellationToken cancellationToken)
    {
        if (_memory.TryGetValue(profile.ProjectId, out TeamChatIndexDocument? cached)) return cached;
        string path = IndexPath(profile);
        if (File.Exists(path))
        {
            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
                TeamChatIndexDocument? loaded = await JsonSerializer.DeserializeAsync<TeamChatIndexDocument>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (loaded is { Version: 2 })
                {
                    _memory[profile.ProjectId] = loaded;
                    return loaded;
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        TeamChatIndexDocument empty = new(2, DateTimeOffset.MinValue,
            new Dictionary<string, TeamChatIndexEntry>(StringComparer.OrdinalIgnoreCase));
        _memory[profile.ProjectId] = empty;
        return empty;
    }

    private async Task SaveIndexAsync(TeamChatProfile profile, TeamChatIndexDocument document, CancellationToken cancellationToken)
    {
        string path = IndexPath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteAtomicAsync(path, document, cancellationToken).ConfigureAwait(false);
    }

    private string IndexPath(TeamChatProfile profile)
    {
        string root = !string.IsNullOrWhiteSpace(profile.ProjectRoot)
            ? Path.Combine(Path.GetFullPath(profile.ProjectRoot), ".cyrevision", "cache", "chat")
            : Path.Combine(_fallbackRoot, profile.ProjectId.ToString("N"), "cache", "chat");
        return Path.Combine(root, "sync-index.json");
    }

    private static IEnumerable<string> EnumerateMessageFiles(string messagesRoot, DateTimeOffset oldest)
    {
        IEnumerable<string> directories = Directory.EnumerateDirectories(messagesRoot, "*", SearchOption.TopDirectoryOnly);
        foreach (string directory in directories)
        {
            string name = Path.GetFileName(directory);
            if (DateTimeOffset.TryParseExact(name + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset month) &&
                month.AddMonths(1) < oldest)
                continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)) yield return path;
        }
    }

    private static async Task<TeamChatMessage?> ReadMessageAsync(
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            return await TeamChatArchiveCipher.ReadJsonAsync<TeamChatMessage>(path, token, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static async Task<IReadOnlyList<TeamChatParticipant>> ReadPresenceAsync(
        string syncRoot,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(syncRoot, "presence");
        if (!Directory.Exists(directory)) return [];
        List<TeamChatParticipant> participants = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 8 * 1024, true);
                TeamChatParticipant? item = await JsonSerializer.DeserializeAsync<TeamChatParticipant>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (item is null) continue;
                bool online = DateTimeOffset.UtcNow - item.LastSeen < TimeSpan.FromMinutes(3);
                participants.Add(item with { IsOnline = online });
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return participants.OrderByDescending(item => item.IsOnline).ThenBy(item => item.DisplayName).ToArray();
    }

    private static TeamChatMessage MaterializePath(TeamChatMessage message, string syncRoot) => message with
    {
        AttachmentLocalPath = string.IsNullOrWhiteSpace(message.AttachmentRelativePath) ||
                              message.AttachmentRelativePath.EndsWith(".cyenc", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Path.Combine(syncRoot, message.AttachmentRelativePath.Replace('/', Path.DirectorySeparatorChar))
    };

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string temporary = path + "." + Environment.ProcessId + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, true))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed record TeamChatIndexDocument(
        int Version,
        DateTimeOffset UpdatedAt,
        Dictionary<string, TeamChatIndexEntry> Entries);

    private sealed record TeamChatIndexEntry(long LastWriteUtcTicks, long Length, TeamChatMessage Message);
}

public sealed class TeamChatSyncWatcher : IAsyncDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private int _pending;

    internal TeamChatSyncWatcher(string syncRoot)
    {
        string messages = Path.Combine(syncRoot, "messages");
        Directory.CreateDirectory(messages);
        _debounce = new Timer(_ => Publish(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(messages, "*.json")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Created += Changed;
        _watcher.Changed += Changed;
        _watcher.Renamed += Changed;
        _watcher.Deleted += Changed;
    }

    public event EventHandler? ChangedAvailable;

    private void Changed(object sender, FileSystemEventArgs args)
    {
        Interlocked.Exchange(ref _pending, 1);
        _debounce.Change(250, Timeout.Infinite);
    }

    private void Publish()
    {
        if (Interlocked.Exchange(ref _pending, 0) == 1) ChangedAvailable?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
        return ValueTask.CompletedTask;
    }
}
