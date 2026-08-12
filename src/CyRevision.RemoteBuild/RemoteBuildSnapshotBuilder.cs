using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CyRevision.RemoteBuild;

public sealed class RemoteBuildSnapshotBuilder
{
    private static readonly string[] ExcludedSegments =
    [
        ".git", ".vs", ".idea", "Binaries", "DerivedDataCache", "Intermediate", "Saved",
        "node_modules", "bin", "obj"
    ];

    public async Task<RemoteBuildSnapshotResult> CreateAsync(
        string repositoryPath,
        string outputPath,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        string[] files = await ReadGitFilesAsync(root, cancellationToken);
        string revision = (await RunGitAsync(root, ["rev-parse", "HEAD"], cancellationToken)).Trim();
        bool changed = !(await RunGitAsync(root, ["status", "--porcelain"], cancellationToken)).Trim().Equals(string.Empty);
        string archive = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        string temporary = archive + ".partial-" + Guid.NewGuid().ToString("N");
        int count = 0;
        long total = 0;
        try
        {
            await using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            using ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (string relativeRaw in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = NormalizeRelativePath(relativeRaw);
                if (IsExcluded(relative))
                    continue;
                string source = Path.GetFullPath(Path.Combine(root, relative));
                if (!IsInside(source, root) || !File.Exists(source))
                    continue;
                FileInfo info = new(source);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Snapshot refused reparse point: {relative}");
                total = checked(total + info.Length);
                if (total > maximumBytes)
                    throw new InvalidOperationException("The working snapshot exceeds the configured upload limit.");
                ZipArchiveEntry entry = zip.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Fastest);
                await using Stream destination = entry.Open();
                await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
                await input.CopyToAsync(destination, cancellationToken);
                count++;
            }

            ZipArchiveEntry manifestEntry = zip.CreateEntry(".cyrevision-build-manifest.json", CompressionLevel.Optimal);
            await using (Stream manifestStream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(manifestStream, new
                {
                    CreatedAt = DateTimeOffset.UtcNow,
                    RepositoryName = Path.GetFileName(root),
                    Revision = revision,
                    HasLocalChanges = changed,
                    FileCount = count,
                    SourceBytes = total
                }, cancellationToken: cancellationToken);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
        File.Move(temporary, archive, true);
        return new RemoteBuildSnapshotResult(archive, count, total, revision, changed);
    }

    private static async Task<string[]> ReadGitFilesAsync(string root, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { "-c", "core.quotepath=false", "ls-files", "--cached", "--others", "--exclude-standard", "-z" })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Unable to enumerate snapshot files: " + error.Trim());
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<string> RunGitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());
        return output;
    }

    internal static string NormalizeRelativePath(string value)
    {
        string relative = value.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || Path.IsPathRooted(relative) ||
            relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            throw new InvalidDataException("Snapshot contains an unsafe path.");
        return relative;
    }

    private static bool IsExcluded(string path) => path.Split('/').Any(segment =>
        ExcludedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));

    internal static bool IsInside(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
                                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
