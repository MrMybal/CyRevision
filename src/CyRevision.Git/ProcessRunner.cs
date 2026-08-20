using System.Diagnostics;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace CyRevision.Git;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

internal sealed class ProcessRunner
{
    private static readonly TimeSpan ReadCommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly ConcurrentDictionary<string, RepositoryCommandGate> RepositoryGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken) =>
        await RunAsync(executable, arguments, workingDirectory, standardInput: null, cancellationToken).ConfigureAwait(false);

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        string? gitDirectory = FindGitDirectory(workingDirectory);
        RepositoryCommandGate? gate = gitDirectory is null
            ? null
            : RepositoryGates.GetOrAdd(gitDirectory, _ => new RepositoryCommandGate());
        bool mutating = gitDirectory is not null && IsMutatingCommand(arguments);
        using CancellationTokenSource? timeoutCancellation = gitDirectory is not null && !mutating
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCancellation?.CancelAfter(ReadCommandTimeout);
        CancellationToken effectiveCancellation = timeoutCancellation?.Token ?? cancellationToken;
        IDisposable? lease = null;
        try
        {
            lease = gate is null
                ? null
                : mutating
                    ? await gate.EnterWriteAsync(effectiveCancellation).ConfigureAwait(false)
                    : await gate.EnterReadAsync(effectiveCancellation).ConfigureAwait(false);
            if (mutating)
                await WaitForExternalIndexLockAsync(gitDirectory!, effectiveCancellation).ConfigureAwait(false);
            return await RunCoreAsync(executable, arguments, workingDirectory, standardInput, effectiveCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutCancellation?.IsCancellationRequested == true)
        {
            string command = FindCommand(arguments);
            throw new GitOperationException(
                $"Git read command '{command}' timed out after {ReadCommandTimeout.TotalSeconds:0} seconds. " +
                "The operation was stopped so the repository remains responsive.",
                exception);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private static async Task<ProcessResult> RunCoreAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null
        };

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        if (!IsMutatingCommand(arguments)) startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new GitOperationException($"Unable to start {executable}.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 206)
        {
            throw new GitOperationException(
                $"Unable to start {executable}: the generated command line is too long for Windows. " +
                "Use a streamed path list or split the operation into smaller batches.",
                exception);
        }
        catch (Exception exception) when (exception is not GitOperationException)
        {
            throw new GitOperationException($"Unable to start {executable}.", exception);
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task inputTask = WriteStandardInputAsync(process, standardInput, cancellationToken);

        try
        {
            await inputTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }

    private static async Task WriteStandardInputAsync(
        Process process,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        if (standardInput is null) return;
        await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private static string? FindGitDirectory(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return null;
        DirectoryInfo? current;
        try { current = new DirectoryInfo(Path.GetFullPath(workingDirectory)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            if (File.Exists(candidate))
            {
                try
                {
                    string marker = File.ReadAllText(candidate).Trim();
                    if (marker.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = marker[7..].Trim();
                        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(current.FullName, path));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            current = current.Parent;
        }
        return null;
    }

    private static bool IsMutatingCommand(IReadOnlyCollection<string> arguments)
    {
        string command = FindCommand(arguments);
        string[] values = arguments.ToArray();
        if (command == "branch")
            return !values.Any(value => value is "--show-current" or "--list" or "-l");
        if (command == "config")
            return !values.Any(value => value is "--get" or "--get-all" or "--get-regexp" or "--list" or "-l");
        if (command == "remote")
            return values.Any(value => value is "add" or "remove" or "rename" or "set-url" or "prune" or "update");
        if (command == "lfs")
        {
            string subcommand = FindSubcommand(values, "lfs");
            return subcommand is not "env" and not "locks" and not "ls-files" and not "pointer" and
                not "status" and not "version";
        }

        return command is "add" or "apply" or "checkout" or "checkout-index" or "cherry-pick" or "clean" or "commit" or
            "fetch" or "init" or "merge" or "mv" or "pull" or "push" or "rebase" or
            "reset" or "restore" or "revert" or "rm" or "stash" or "switch" or "tag" or
            "update-index" or "worktree";
    }

    private static string FindCommand(IReadOnlyCollection<string> arguments)
    {
        string[] values = arguments.ToArray();
        for (int index = 0; index < values.Length; index++)
        {
            string value = values[index];
            if (value == "-c" && index + 1 < values.Length)
            {
                index++;
                continue;
            }
            if (value.StartsWith("-", StringComparison.Ordinal)) continue;
            return value.ToLowerInvariant();
        }
        return string.Empty;
    }

    private static string FindSubcommand(IReadOnlyList<string> arguments, string command)
    {
        int commandIndex = -1;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], command, StringComparison.OrdinalIgnoreCase))
            {
                commandIndex = index;
                break;
            }
        }

        for (int index = commandIndex + 1; index > 0 && index < arguments.Count; index++)
        {
            string value = arguments[index];
            if (!value.StartsWith("-", StringComparison.Ordinal)) return value.ToLowerInvariant();
        }
        return string.Empty;
    }

    private static async Task WaitForExternalIndexLockAsync(string gitDirectory, CancellationToken cancellationToken)
    {
        string lockPath = Path.Combine(gitDirectory, "index.lock");
        if (!File.Exists(lockPath)) return;

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(12);
        while (File.Exists(lockPath) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        if (File.Exists(lockPath))
        {
            throw new GitOperationException(
                $"Git is busy: {lockPath} is still present after 12 seconds. " +
                "Wait for Rider, another Git client, or a build process to finish, then retry. CyRevision did not delete the lock.");
        }
    }

    private sealed class RepositoryCommandGate
    {
        private readonly SemaphoreSlim _turnstile = new(1, 1);
        private readonly SemaphoreSlim _readerMutex = new(1, 1);
        private readonly SemaphoreSlim _roomEmpty = new(1, 1);
        private int _readerCount;

        public async Task<IDisposable> EnterReadAsync(CancellationToken cancellationToken)
        {
            await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _readerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (_readerCount == 0)
                        await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
                    _readerCount++;
                }
                finally
                {
                    _readerMutex.Release();
                }
            }
            finally
            {
                _turnstile.Release();
            }
            return new GateLease(this, isWriter: false);
        }

        public async Task<IDisposable> EnterWriteAsync(CancellationToken cancellationToken)
        {
            await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _turnstile.Release();
                throw;
            }
            return new GateLease(this, isWriter: true);
        }

        private void ExitRead()
        {
            _readerMutex.Wait();
            try
            {
                _readerCount--;
                if (_readerCount == 0) _roomEmpty.Release();
            }
            finally
            {
                _readerMutex.Release();
            }
        }

        private void ExitWrite()
        {
            _roomEmpty.Release();
            _turnstile.Release();
        }

        private sealed class GateLease(RepositoryCommandGate owner, bool isWriter) : IDisposable
        {
            private RepositoryCommandGate? _owner = owner;

            public void Dispose()
            {
                RepositoryCommandGate? current = Interlocked.Exchange(ref _owner, null);
                if (current is null) return;
                if (isWriter) current.ExitWrite();
                else current.ExitRead();
            }
        }
    }

    public async Task<ProcessResult> RunToFileAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitOperationException($"Unable to start {executable}.");
        }

        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await using (FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 128, true))
            {
                await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            File.Delete(destinationPath);
            throw;
        }

        return new ProcessResult(process.ExitCode, string.Empty, await errorTask);
    }
}

public sealed class GitOperationException : Exception
{
    public GitOperationException(string message)
        : base(message)
    {
    }

    public GitOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
