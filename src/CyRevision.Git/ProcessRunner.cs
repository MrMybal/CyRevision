using System.Diagnostics;

namespace CyRevision.Git;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

internal sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
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

