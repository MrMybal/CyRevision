using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace CyRevision.RemoteBuild;

public sealed record RemoteBuildExecutionResult(
    bool Succeeded,
    int ExitCode,
    string Message,
    string LogPath,
    string ArtifactPath);

public sealed class RemoteBuildJobRunner
{
    public async Task<RemoteBuildExecutionResult> RunAsync(
        Guid jobId,
        RemoteBuildAgentProject project,
        RemoteBuildRecipe recipe,
        RemoteBuildSourceMode sourceMode,
        string? snapshotPath,
        string jobsRoot,
        Action<RemoteBuildJobState, string> progress,
        CancellationToken cancellationToken)
    {
        project.Validate();
        recipe.Validate();
        string jobRoot = Path.Combine(Path.GetFullPath(jobsRoot), jobId.ToString("N"));
        Directory.CreateDirectory(jobRoot);
        string logPath = Path.Combine(jobRoot, "build.log");
        string artifactPath = Path.Combine(jobRoot, "artifacts.zip");
        string workspace;
        progress(RemoteBuildJobState.Preparing, "Preparing isolated build workspace.");
        if (sourceMode == RemoteBuildSourceMode.UploadedSnapshot)
        {
            if (!project.AllowUploadedSnapshots)
                throw new UnauthorizedAccessException("This build project does not accept uploaded snapshots.");
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
                throw new InvalidDataException("The uploaded source snapshot is missing.");
            if (new FileInfo(snapshotPath).Length > project.MaximumSnapshotBytes)
                throw new InvalidDataException("The uploaded source snapshot exceeds the project limit.");
            workspace = Path.Combine(jobRoot, "source");
            Directory.CreateDirectory(workspace);
            await ExtractSafelyAsync(snapshotPath, workspace, project.MaximumSnapshotBytes, cancellationToken);
        }
        else
        {
            workspace = Path.GetFullPath(project.WorkspaceRoot);
            if (!Directory.Exists(workspace))
                throw new DirectoryNotFoundException($"Configured agent workspace does not exist: {workspace}");
        }

        string workingDirectory = ResolveInside(workspace, recipe.WorkingDirectory);
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Recipe working directory does not exist: {workingDirectory}");
        progress(RemoteBuildJobState.Running, $"Running allowlisted recipe '{recipe.DisplayName}'.");
        int exitCode = await RunProcessAsync(recipe, workingDirectory, logPath, cancellationToken);
        if (exitCode != 0)
            return new RemoteBuildExecutionResult(false, exitCode,
                $"Build recipe exited with code {exitCode}.", logPath, string.Empty);

        progress(RemoteBuildJobState.Packaging, "Packaging declared artifacts.");
        int artifacts = await PackageArtifactsAsync(workspace, recipe.ArtifactPatterns, logPath, artifactPath, cancellationToken);
        return new RemoteBuildExecutionResult(true, exitCode,
            $"Build completed; {artifacts} artifact file(s) packaged.", logPath, artifactPath);
    }

    private static async Task<int> RunProcessAsync(
        RemoteBuildRecipe recipe,
        string workingDirectory,
        string logPath,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(recipe.Executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in recipe.Arguments)
            start.ArgumentList.Add(argument);
        start.Environment["CYREVISION_REMOTE_BUILD"] = "1";
        start.Environment["CI"] = "true";
        using Process process = new() { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException($"Unable to start allowlisted executable '{recipe.Executable}'.");
        await using FileStream log = new(logPath, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, true);
        using StreamWriter writer = new(log) { AutoFlush = true };
        await writer.WriteLineAsync($"CyRevision remote build started {DateTimeOffset.UtcNow:O}");
        await writer.WriteLineAsync($"Executable: {recipe.Executable}");
        Task stdout = CopyLinesAsync(process.StandardOutput, writer, "", cancellationToken);
        Task stderr = CopyLinesAsync(process.StandardError, writer, "[stderr] ", cancellationToken);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(recipe.TimeoutMinutes));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            if (!cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"Build exceeded its {recipe.TimeoutMinutes}-minute recipe timeout.");
            throw;
        }
        await writer.WriteLineAsync($"Exit code: {process.ExitCode}");
        return process.ExitCode;
    }

    private static async Task CopyLinesAsync(
        StreamReader reader,
        StreamWriter writer,
        string prefix,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            await writer.WriteLineAsync(prefix + line);
    }

    private static async Task<int> PackageArtifactsAsync(
        string workspace,
        IReadOnlyList<string> patterns,
        string logPath,
        string artifactPath,
        CancellationToken cancellationToken)
    {
        Regex[] matchers = patterns.Select(BuildGlobRegex).ToArray();
        string[] files = Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .Where(path => RemoteBuildSnapshotBuilder.IsInside(path, workspace))
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Where(path => matchers.Any(regex => regex.IsMatch(Path.GetRelativePath(workspace, path).Replace('\\', '/'))))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        await using FileStream output = new(artifactPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true);
        using ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(workspace, file).Replace('\\', '/');
            ZipArchiveEntry entry = zip.CreateEntry("artifacts/" + relative, CompressionLevel.Fastest);
            await using Stream destination = entry.Open();
            await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            await input.CopyToAsync(destination, cancellationToken);
        }
        ZipArchiveEntry logEntry = zip.CreateEntry("build.log", CompressionLevel.Optimal);
        await using (Stream destination = logEntry.Open())
        await using (FileStream input = new(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true))
            await input.CopyToAsync(destination, cancellationToken);
        return files.Length;
    }

    internal static async Task ExtractSafelyAsync(
        string archivePath,
        string destinationRoot,
        long maximumExpandedBytes,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(destinationRoot);
        long expanded = 0;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            string relative = RemoteBuildSnapshotBuilder.NormalizeRelativePath(entry.FullName);
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!RemoteBuildSnapshotBuilder.IsInside(destination, root))
                throw new InvalidDataException("Build snapshot attempted to escape the isolated workspace.");
            expanded = checked(expanded + entry.Length);
            if (expanded > maximumExpandedBytes)
                throw new InvalidDataException("Expanded build snapshot exceeds the project limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using Stream source = entry.Open();
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await source.CopyToAsync(output, cancellationToken);
        }
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        string normalized = pattern.Replace('\\', '/').TrimStart('/');
        RemoteBuildRecipe.ValidateRelativePath(normalized, "artifact pattern", allowWildcards: true);
        string expression = Regex.Escape(normalized)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        if (!normalized.Contains('*') && !normalized.Contains('?'))
            expression += "(?:/.*)?";
        return new Regex("^" + expression + "$", RegexOptions.Compiled |
            (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None));
    }

    private static string ResolveInside(string root, string relative)
    {
        RemoteBuildRecipe.ValidateRelativePath(relative, "recipe working directory");
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!RemoteBuildSnapshotBuilder.IsInside(result, root))
            throw new InvalidDataException("Recipe path escaped the configured workspace.");
        return result;
    }
}
