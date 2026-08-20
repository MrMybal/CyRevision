using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CyRevision.Desktop.Workspace;

public sealed record RepositoryCommandHistoryEntry(
    string RepositoryPath,
    string Command,
    string Shell,
    DateTimeOffset ExecutedAt)
{
    public string TimeText => ExecutedAt.ToLocalTime().ToString("g");
    public string RepositoryName => Path.GetFileName(RepositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

public sealed record RepositoryCommandResult(int ExitCode, TimeSpan Duration, bool WasCancelled);

public sealed class RepositoryConsoleService
{
    private const int MaximumHistoryEntries = 1_000;
    private readonly string _historyPath;
    private readonly object _historyGate = new();
    private List<RepositoryCommandHistoryEntry> _history;

    public RepositoryConsoleService(string configurationDirectory)
    {
        Directory.CreateDirectory(configurationDirectory);
        _historyPath = Path.Combine(configurationDirectory, "repository-console-history.json");
        _history = LoadHistory();
    }

    public IReadOnlyList<RepositoryCommandHistoryEntry> GetHistory(string repositoryPath, int maximumCount = 250)
    {
        string normalized = Normalize(repositoryPath);
        lock (_historyGate)
        {
            return _history
                .Where(entry => string.Equals(Normalize(entry.RepositoryPath), normalized, PathComparison))
                .OrderByDescending(entry => entry.ExecutedAt)
                .Take(maximumCount)
                .ToArray();
        }
    }

    public void ClearHistory(string repositoryPath)
    {
        string normalized = Normalize(repositoryPath);
        lock (_historyGate)
        {
            _history.RemoveAll(entry => string.Equals(Normalize(entry.RepositoryPath), normalized, PathComparison));
            SaveHistory();
        }
    }

    public async Task<RepositoryCommandResult> ExecuteAsync(
        string repositoryPath,
        string command,
        string shell,
        Action<string, bool> output,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        string root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        AddHistory(new RepositoryCommandHistoryEntry(root, command.Trim(), shell, DateTimeOffset.UtcNow));
        ProcessStartInfo startInfo = CreateStartInfo(root, command, shell);
        using Process process = new() { StartInfo = startInfo };
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException("The repository console process could not be started.");

        Task standardOutput = PumpAsync(process.StandardOutput, line => output(line, false), cancellationToken);
        Task standardError = PumpAsync(process.StandardError, line => output(line, true), cancellationToken);
        bool cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
        }
        stopwatch.Stop();
        return new RepositoryCommandResult(cancelled ? -1 : process.ExitCode, stopwatch.Elapsed, cancelled);
    }

    private static ProcessStartInfo CreateStartInfo(string root, string command, string shell)
    {
        ProcessStartInfo info = new()
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows() && string.Equals(shell, "Command Prompt", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/s");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(command);
        }
        else if (OperatingSystem.IsWindows())
        {
            info.FileName = "powershell.exe";
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            // Windows PowerShell can reinterpret quoting when a complete command is passed
            // through ProcessStartInfo.ArgumentList. EncodedCommand preserves the exact text,
            // including quotes and paths with spaces, and avoids intermittent exit-code 1
            // failures in both the embedded console and its test runner.
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        }
        else
        {
            info.FileName = string.Equals(shell, "Zsh", StringComparison.OrdinalIgnoreCase) ? "/bin/zsh" : "/bin/bash";
            info.ArgumentList.Add("-lc");
            info.ArgumentList.Add(command);
        }
        return info;
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> output, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line) output(line);
    }

    private void AddHistory(RepositoryCommandHistoryEntry entry)
    {
        lock (_historyGate)
        {
            _history.RemoveAll(item =>
                string.Equals(Normalize(item.RepositoryPath), Normalize(entry.RepositoryPath), PathComparison) &&
                string.Equals(item.Command, entry.Command, StringComparison.Ordinal) &&
                string.Equals(item.Shell, entry.Shell, StringComparison.Ordinal));
            _history.Insert(0, entry);
            if (_history.Count > MaximumHistoryEntries)
                _history.RemoveRange(MaximumHistoryEntries, _history.Count - MaximumHistoryEntries);
            SaveHistory();
        }
    }

    private List<RepositoryCommandHistoryEntry> LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyPath)) return [];
            return JsonSerializer.Deserialize<List<RepositoryCommandHistoryEntry>>(File.ReadAllText(_historyPath)) ?? [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    private void SaveHistory()
    {
        string temporary = _historyPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _historyPath, true);
    }

    private static string Normalize(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
