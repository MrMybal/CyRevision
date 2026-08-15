using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.AI;

internal sealed class CodexAppServerSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly StringBuilder _standardError = new();
    private readonly StringBuilder _activeResponse = new();
    private readonly Task _readLoop;
    private readonly Task _errorLoop;
    private TaskCompletionSource<AiChatTurnResult>? _activeTurn;
    private IProgress<AiChatProgress>? _activeProgress;
    private Stopwatch? _activeStopwatch;
    private string _activeTurnId = string.Empty;
    private long _nextRequestId;
    private bool _disposed;

    private CodexAppServerSession(Process process)
    {
        _process = process;
        // Process.StandardInput is a StreamWriter using the console encoding and may emit a
        // BOM on Windows. App Server consumes JSONL and rejects any byte before the opening '{'.
        _input = process.StandardInput.BaseStream;
        _readLoop = ReadLoopAsync(_lifetime.Token);
        _errorLoop = ReadErrorLoopAsync(_lifetime.Token);
    }

    public string ThreadId { get; private set; } = string.Empty;

    public bool IsConnected => !_disposed && !_process.HasExited && ThreadId.Length > 0;

    public static async Task<CodexAppServerSession> ConnectAsync(
        AiChatConnectRequest request,
        CancellationToken cancellationToken)
    {
        string executable = string.IsNullOrWhiteSpace(request.ExecutablePath)
            ? "codex"
            : request.ExecutablePath.Trim();
        string repository = Path.GetFullPath(request.RepositoryPath);
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = repository,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The Codex App Server process could not start.");
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }

        CodexAppServerSession session = new(process);
        try
        {
            using CancellationTokenSource startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            await session.InitializeAsync(request, startupTimeout.Token).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AiChatTurnResult> SendAsync(
        string message,
        IProgress<AiChatProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsConnected) throw new InvalidOperationException("Codex is not connected to this project.");

        await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _activeResponse.Clear();
            _activeProgress = progress;
            _activeStopwatch = Stopwatch.StartNew();
            _activeTurnId = string.Empty;
            _activeTurn = new TaskCompletionSource<AiChatTurnResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            progress?.Report(new AiChatProgress("status", "Codex is working…"));

            JsonElement result = await SendRequestAsync(
                "turn/start",
                new
                {
                    threadId = ThreadId,
                    input = new[] { new { type = "text", text = message.Trim() } }
                },
                cancellationToken).ConfigureAwait(false);
            if (result.TryGetProperty("turn", out JsonElement turn) &&
                turn.TryGetProperty("id", out JsonElement turnId))
            {
                _activeTurnId = turnId.GetString() ?? string.Empty;
            }

            try
            {
                return await _activeTurn.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await TryInterruptTurnAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _activeTurn = null;
            _activeProgress = null;
            _activeStopwatch = null;
            _activeTurnId = string.Empty;
            _turnGate.Release();
        }
    }

    private async Task InitializeAsync(AiChatConnectRequest request, CancellationToken cancellationToken)
    {
        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "cyrevision",
                    title = "CyRevision",
                    version = typeof(CodexAppServerSession).Assembly.GetName().Version?.ToString(3)
                              ?? "0.1.14-alpha"
                }
            },
            cancellationToken).ConfigureAwait(false);
        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);

        Dictionary<string, object?> threadParameters = new(StringComparer.Ordinal)
        {
            ["cwd"] = Path.GetFullPath(request.RepositoryPath),
            ["approvalPolicy"] = "never",
            ["sandbox"] = request.Permissions.HasFlag(AiWorkspacePermission.ModifyFiles)
                ? "workspace-write"
                : "read-only",
            ["developerInstructions"] = BuildProjectInstructions(request),
            ["threadSource"] = "cyrevision"
        };
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            threadParameters["model"] = request.Model.Trim();
        }

        JsonElement threadResult = await SendRequestAsync(
            "thread/start",
            threadParameters,
            cancellationToken).ConfigureAwait(false);
        if (!threadResult.TryGetProperty("thread", out JsonElement thread) ||
            !thread.TryGetProperty("id", out JsonElement threadId) ||
            string.IsNullOrWhiteSpace(threadId.GetString()))
        {
            throw new InvalidOperationException("Codex did not return a conversation thread identifier.");
        }
        ThreadId = threadId.GetString()!;

        try
        {
            await SendRequestAsync(
                "thread/name/set",
                new { threadId = ThreadId, name = $"CyRevision · {request.ProjectName}" },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Older app-server builds may not expose thread naming. The chat remains usable.
        }
    }

    private static string BuildProjectInstructions(AiChatConnectRequest request)
    {
        StringBuilder instructions = new();
        instructions.AppendLine("You are connected to a project through CyRevision.");
        instructions.AppendLine($"Project name: {request.ProjectName}");
        instructions.AppendLine($"Project path: {Path.GetFullPath(request.RepositoryPath)}");
        instructions.AppendLine("Treat this conversation as project-scoped and preserve unrelated user changes.");
        instructions.AppendLine(request.Permissions.HasFlag(AiWorkspacePermission.ModifyFiles)
            ? "CyRevision authorizes modifications only inside this project."
            : "This connection is read-only. Do not modify, create, rename, or delete project files.");
        instructions.AppendLine(request.Permissions.HasFlag(AiWorkspacePermission.NetworkAccess)
            ? "Network access is authorized when required by the user's request."
            : "Do not use network access or contact external services.");
        instructions.AppendLine("Never push or rewrite Git history. CyRevision brokers explicit Git operations separately.");
        return instructions.ToString();
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        long id = Interlocked.Increment(ref _nextRequestId);
        TaskCompletionSource<JsonElement> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(id, completion))
            throw new InvalidOperationException("Could not allocate a Codex request identifier.");

        try
        {
            await WriteMessageAsync(new { method, id, @params = parameters }, cancellationToken).ConfigureAwait(false);
            JsonElement envelope = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (envelope.TryGetProperty("error", out JsonElement error))
            {
                string message = error.TryGetProperty("message", out JsonElement errorMessage)
                    ? errorMessage.GetString() ?? error.GetRawText()
                    : error.GetRawText();
                throw new InvalidOperationException($"Codex App Server: {message}");
            }
            return envelope.TryGetProperty("result", out JsonElement result)
                ? result.Clone()
                : default;
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        if (_process.HasExited) throw new InvalidOperationException(BuildExitDiagnostic());
        string json = JsonSerializer.Serialize(message);
        byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _input.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("method", out JsonElement methodElement))
                {
                    string method = methodElement.GetString() ?? string.Empty;
                    if (root.TryGetProperty("id", out JsonElement serverRequestId))
                    {
                        await RejectServerRequestAsync(serverRequestId.Clone(), method, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        HandleNotification(method, root);
                    }
                    continue;
                }

                if (root.TryGetProperty("id", out JsonElement idElement) &&
                    idElement.TryGetInt64(out long id) &&
                    _pendingRequests.TryGetValue(id, out TaskCompletionSource<JsonElement>? completion))
                {
                    completion.TrySetResult(root.Clone());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailPending(exception);
        }
        finally
        {
            if (!_disposed)
            {
                FailPending(new InvalidOperationException(BuildExitDiagnostic()));
            }
        }
    }

    private void HandleNotification(string method, JsonElement root)
    {
        if (!root.TryGetProperty("params", out JsonElement parameters)) return;
        switch (method)
        {
            case "item/agentMessage/delta":
                if (parameters.TryGetProperty("delta", out JsonElement deltaElement))
                {
                    string delta = deltaElement.GetString() ?? string.Empty;
                    if (delta.Length > 0)
                    {
                        _activeResponse.Append(delta);
                        _activeProgress?.Report(new AiChatProgress("delta", delta));
                    }
                }
                break;
            case "item/completed":
                if (_activeResponse.Length == 0 &&
                    parameters.TryGetProperty("item", out JsonElement item) &&
                    item.TryGetProperty("type", out JsonElement itemType) &&
                    itemType.GetString() == "agentMessage" &&
                    item.TryGetProperty("text", out JsonElement completedText))
                {
                    string text = completedText.GetString() ?? string.Empty;
                    _activeResponse.Append(text);
                    _activeProgress?.Report(new AiChatProgress("delta", text));
                }
                break;
            case "turn/started":
                _activeProgress?.Report(new AiChatProgress("status", "Codex turn started."));
                break;
            case "turn/completed":
                CompleteTurn(parameters);
                break;
            case "error":
                string error = parameters.TryGetProperty("message", out JsonElement message)
                    ? message.GetString() ?? parameters.GetRawText()
                    : parameters.GetRawText();
                _activeProgress?.Report(new AiChatProgress("error", error));
                break;
        }
    }

    private void CompleteTurn(JsonElement parameters)
    {
        if (_activeTurn is null || !parameters.TryGetProperty("turn", out JsonElement turn)) return;
        string turnId = turn.TryGetProperty("id", out JsonElement id)
            ? id.GetString() ?? _activeTurnId
            : _activeTurnId;
        string status = turn.TryGetProperty("status", out JsonElement statusElement)
            ? statusElement.GetString() ?? "failed"
            : "failed";
        string diagnostic = string.Empty;
        if (turn.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
        {
            diagnostic = error.TryGetProperty("message", out JsonElement errorMessage)
                ? errorMessage.GetString() ?? error.GetRawText()
                : error.GetRawText();
        }
        _activeStopwatch?.Stop();
        bool succeeded = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
        AiChatTurnResult result = new(
            succeeded,
            _activeResponse.ToString(),
            diagnostic.Length == 0 ? status : diagnostic,
            turnId,
            _activeStopwatch?.Elapsed ?? TimeSpan.Zero);
        _activeProgress?.Report(new AiChatProgress("completed", succeeded ? "Completed" : result.Diagnostic));
        _activeTurn.TrySetResult(result);
    }

    private async Task RejectServerRequestAsync(JsonElement id, string method, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new
        {
            id,
            error = new
            {
                code = -32010,
                message = $"CyRevision declined interactive App Server request '{method}'."
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadErrorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0) continue;
                lock (_standardError)
                {
                    if (_standardError.Length > 16_000) _standardError.Remove(0, 8_000);
                    _standardError.AppendLine(line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryInterruptTurnAsync()
    {
        if (_activeTurnId.Length == 0 || ThreadId.Length == 0 || _process.HasExited) return;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            await SendRequestAsync(
                "turn/interrupt",
                new { threadId = ThreadId, turnId = _activeTurnId },
                timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (TaskCompletionSource<JsonElement> request in _pendingRequests.Values)
        {
            request.TrySetException(exception);
        }
        _activeTurn?.TrySetException(exception);
    }

    private string BuildExitDiagnostic()
    {
        string error;
        lock (_standardError) error = _standardError.ToString().Trim();
        return error.Length == 0
            ? "The Codex App Server connection closed unexpectedly."
            : $"The Codex App Server connection closed: {error}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try { _input.Close(); } catch { }
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
        try { await Task.WhenAll(_readLoop, _errorLoop).ConfigureAwait(false); } catch { }
        FailPending(new ObjectDisposedException(nameof(CodexAppServerSession)));
        _process.Dispose();
        _writeGate.Dispose();
        _turnGate.Dispose();
        _lifetime.Dispose();
    }
}

internal static class CodexLocalDetector
{
    public static async Task<AiCodexDetectionResult> DetectAsync(
        string configuredExecutable,
        CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            (bool running, IReadOnlyList<string> runningPaths) = FindRunningProcesses();
            string executable = ResolveExecutable(configuredExecutable, runningPaths);
            if (executable.Length == 0)
            {
                return new AiCodexDetectionResult(
                    false,
                    running,
                    string.Empty,
                    string.Empty,
                    running
                        ? "Codex is running, but its command-line executable could not be accessed."
                        : "Codex was not detected. Install or open Codex, then scan again.");
            }

            string version = await ReadVersionAsync(executable, cancellationToken).ConfigureAwait(false);
            string state = running ? "running" : "installed";
            return new AiCodexDetectionResult(
                true,
                running,
                executable,
                version,
                $"Codex {state}{(version.Length == 0 ? string.Empty : $" · {version}")}");
        }, cancellationToken).ConfigureAwait(false);
    }

    private static (bool Running, IReadOnlyList<string> Paths) FindRunningProcesses()
    {
        List<string> paths = [];
        bool running = false;
        foreach (string processName in new[] { "codex", "ChatGPT" })
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(processName); }
            catch { continue; }
            running |= processes.Length > 0;
            foreach (Process process in processes)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) &&
                        string.Equals(Path.GetFileNameWithoutExtension(path), "codex", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return (running, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string ResolveExecutable(string configuredExecutable, IReadOnlyList<string> runningPaths)
    {
        if (!string.IsNullOrWhiteSpace(configuredExecutable) && Path.IsPathRooted(configuredExecutable))
        {
            string full = Path.GetFullPath(configuredExecutable.Trim());
            if (File.Exists(full)) return full;
        }

        string? running = runningPaths
            .Where(File.Exists)
            .OrderBy(path => path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();
        if (running is not null && !running.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return running;

        foreach (string knownPath in KnownInstallPaths())
        {
            if (File.Exists(knownPath)) return knownPath;
        }

        string executableName = string.IsNullOrWhiteSpace(configuredExecutable)
            ? (OperatingSystem.IsWindows() ? "codex.exe" : "codex")
            : configuredExecutable.Trim();
        string? fromPath = FindOnPath(executableName);
        if (fromPath is not null && !fromPath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return fromPath;
        return running ?? fromPath ?? string.Empty;
    }

    private static IEnumerable<string> KnownInstallPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string binRoot = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(binRoot))
            {
                IEnumerable<string> candidates;
                try { candidates = Directory.EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories); }
                catch { candidates = []; }
                foreach (string candidate in candidates.OrderByDescending(File.GetLastWriteTimeUtc)) yield return candidate;
            }
            yield break;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".local", "bin", "codex");
        yield return "/usr/local/bin/codex";
        yield return "/usr/bin/codex";
        if (OperatingSystem.IsMacOS())
            yield return "/Applications/ChatGPT.app/Contents/Resources/codex";
    }

    private static string? FindOnPath(string executable)
    {
        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;
        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string extension in extensions)
            {
                string name = Path.HasExtension(executable) || extension.Length == 0
                    ? executable
                    : executable + extension.ToLowerInvariant();
                string candidate;
                try { candidate = Path.Combine(directory.Trim(), name); }
                catch { continue; }
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static async Task<string> ReadVersionAsync(string executable, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--version");
        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return string.Empty;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return output.Trim();
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return string.Empty;
        }
    }
}
