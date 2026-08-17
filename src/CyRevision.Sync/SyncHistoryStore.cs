using System.Text.Json;

namespace CyRevision.Sync;

public sealed record SyncHistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid ProjectId,
    string Scope,
    string Path,
    string Action,
    string Direction,
    string Detail);

public sealed class JsonLineSyncHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLineSyncHistoryStore(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task AppendAsync(SyncHistoryEntry entry, CancellationToken cancellationToken = default)
        => await AppendManyAsync([entry], cancellationToken).ConfigureAwait(false);

    public async Task AppendManyAsync(
        IReadOnlyCollection<SyncHistoryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return;
        Guid projectId = entries.First().ProjectId;
        if (projectId == Guid.Empty || entries.Any(entry => entry.ProjectId != projectId))
            throw new ArgumentException("Every history entry must target the same non-empty project ID.", nameof(entries));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = GetPath(projectId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string payload = string.Join(Environment.NewLine, entries.Select(entry => JsonSerializer.Serialize(entry, JsonOptions))) + Environment.NewLine;
            await File.AppendAllTextAsync(path, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SyncHistoryEntry>> SearchAsync(
        Guid projectId,
        string? search = null,
        string? pathFilter = null,
        int limit = 2000,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = GetPath(projectId);
            if (!File.Exists(path)) return [];
            string[] lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            IEnumerable<SyncHistoryEntry> entries = lines
                .Reverse()
                .Select(TryDeserialize)
                .Where(entry => entry is not null)
                .Cast<SyncHistoryEntry>();
            if (!string.IsNullOrWhiteSpace(pathFilter))
                entries = entries.Where(entry => entry.Path.Contains(pathFilter, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(search))
            {
                entries = entries.Where(entry =>
                    entry.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Action.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Direction.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Scope.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Detail.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return entries.Take(Math.Clamp(limit, 1, 20_000)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(Guid projectId) => Path.Combine(_rootPath, projectId.ToString("N") + ".jsonl");

    private static SyncHistoryEntry? TryDeserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            return JsonSerializer.Deserialize<SyncHistoryEntry>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
