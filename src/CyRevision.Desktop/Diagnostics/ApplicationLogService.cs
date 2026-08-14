using System.Text.Json;

namespace CyRevision.Desktop.Diagnostics;

public enum ApplicationLogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public sealed record ApplicationLogEntry(
    DateTimeOffset Timestamp,
    ApplicationLogLevel Level,
    string Area,
    string Message,
    string? ProjectPath = null)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string LevelText => Level.ToString();
    public string LinuxLine =>
        $"{Timestamp.ToLocalTime():MMM dd HH:mm:ss.fff} cyrevision[{Environment.ProcessId}] " +
        $"{Level.ToString().ToUpperInvariant(),-11} {Area,-14} {Message.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}";
    public string LevelColor => Level switch
    {
        ApplicationLogLevel.Debug => "#9B9DA3",
        ApplicationLogLevel.Information => "#78D7B7",
        ApplicationLogLevel.Warning => "#F2C66D",
        ApplicationLogLevel.Error => "#FF8B9D",
        _ => "#DFE1E5"
    };
}

public sealed class ApplicationLogService : IDisposable
{
    private const int RetainedDays = 30;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public ApplicationLogService(string dataDirectory)
    {
        LogDirectory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(LogDirectory);
        CurrentLogPath = Path.Combine(LogDirectory, $"cyrevision-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        _writer = new StreamWriter(new FileStream(
            CurrentLogPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete))
        {
            AutoFlush = true
        };
        RemoveExpiredLogs();
    }

    public string LogDirectory { get; }
    public string CurrentLogPath { get; }

    public event EventHandler<ApplicationLogEntry>? EntryWritten;

    public void Debug(string area, string message, string? projectPath = null) =>
        Write(ApplicationLogLevel.Debug, area, message, projectPath);
    public void Information(string area, string message, string? projectPath = null) =>
        Write(ApplicationLogLevel.Information, area, message, projectPath);
    public void Warning(string area, string message, string? projectPath = null) =>
        Write(ApplicationLogLevel.Warning, area, message, projectPath);

    public void Error(string area, string message, Exception? exception = null, string? projectPath = null) =>
        Write(ApplicationLogLevel.Error, area, exception is null ? message : $"{message}{Environment.NewLine}{exception}", projectPath);

    public void Write(ApplicationLogLevel level, string area, string message, string? projectPath = null)
    {
        ApplicationLogEntry entry = new(DateTimeOffset.UtcNow, level, area, message, projectPath);
        lock (_gate)
        {
            if (_disposed) return;
            _writer.WriteLine(JsonSerializer.Serialize(entry));
        }
        EntryWritten?.Invoke(this, entry);
    }

    public IReadOnlyList<ApplicationLogEntry> LoadRecent(int maximumCount = 1_000)
    {
        List<ApplicationLogEntry> entries = [];
        foreach (string file in Directory.EnumerateFiles(LogDirectory, "cyrevision-*.jsonl")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            IEnumerable<string> lines;
            try { lines = ReadSharedLines(file).Reverse().Take(maximumCount - entries.Count).ToArray(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (string line in lines)
            {
                try
                {
                    ApplicationLogEntry? entry = ParseEntry(line);
                    if (entry is not null) entries.Add(entry);
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
                {
                    // A partially written final line is ignored and will not block the log viewer.
                }
            }
            if (entries.Count >= maximumCount) break;
        }
        return entries.OrderByDescending(entry => entry.Timestamp).Take(maximumCount).ToArray();
    }

    private static IReadOnlyList<string> ReadSharedLines(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        List<string> lines = [];
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    private static ApplicationLogEntry? ParseEntry(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        DateTimeOffset timestamp = root.GetProperty(nameof(ApplicationLogEntry.Timestamp)).GetDateTimeOffset();
        JsonElement levelElement = root.GetProperty(nameof(ApplicationLogEntry.Level));
        ApplicationLogLevel level = levelElement.ValueKind == JsonValueKind.Number
            ? (ApplicationLogLevel)levelElement.GetInt32()
            : Enum.Parse<ApplicationLogLevel>(levelElement.GetString() ?? string.Empty, true);
        string area = root.GetProperty(nameof(ApplicationLogEntry.Area)).GetString() ?? string.Empty;
        string message = root.GetProperty(nameof(ApplicationLogEntry.Message)).GetString() ?? string.Empty;
        string? projectPath = root.TryGetProperty(nameof(ApplicationLogEntry.ProjectPath), out JsonElement projectElement) &&
                              projectElement.ValueKind == JsonValueKind.String
            ? projectElement.GetString()
            : null;
        return new ApplicationLogEntry(timestamp, level, area, message, projectPath);
    }

    private void RemoveExpiredLogs()
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-RetainedDays);
        foreach (string file in Directory.EnumerateFiles(LogDirectory, "cyrevision-*.jsonl"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
