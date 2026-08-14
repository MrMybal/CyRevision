using System.Diagnostics;
using System.Collections.Concurrent;

namespace CyRevision.Git;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

internal sealed class ProcessRunner
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        string? gitDirectory = FindGitDirectory(workingDirectory);
        SemaphoreSlim? gate = gitDirectory is null
            ? null
            : RepositoryGates.GetOrAdd(gitDirectory, _ => new SemaphoreSlim(1, 1));
        if (gate is not null) await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (gitDirectory is not null && IsMutatingCommand(arguments))
                await WaitForExternalIndexLockAsync(gitDirectory, cancellationToken).ConfigureAwait(false);
            return await RunCoreAsync(executable, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate?.Release();
        }
    }

    private static async Task<ProcessResult> RunCoreAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory,
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
        catch (Exception exception) when (exception is not GitOperationException)
        {
            throw new GitOperationException($"Unable to start {executable}.", exception);
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
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
        return command is "add" or "apply" or "checkout" or "cherry-pick" or "clean" or "commit" or
            "fetch" or "init" or "lfs" or "merge" or "mv" or "pull" or "push" or "rebase" or
            "reset" or "restore" or "revert" or "rm" or "stash" or "switch" or "worktree";
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
