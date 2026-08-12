using System.Diagnostics;
using System.IO.Compression;
using CyRevision.RemoteBuild;

namespace CyRevision.Core.Tests;

public sealed class RemoteBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionRemoteBuildTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SnapshotContainsWorkingFilesButNotGitOrGeneratedCaches()
    {
        string repository = Path.Combine(_root, "repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.name", "CyRevision Test");
        await GitAsync(repository, "config", "user.email", "test@cyrevision.local");
        Directory.CreateDirectory(Path.Combine(repository, "Source"));
        Directory.CreateDirectory(Path.Combine(repository, "Binaries"));
        await File.WriteAllTextAsync(Path.Combine(repository, "Source", "Game.cpp"), "int main() { return 0; }");
        await File.WriteAllTextAsync(Path.Combine(repository, "Binaries", "generated.bin"), "generated");
        await GitAsync(repository, "add", ".");
        await GitAsync(repository, "commit", "-m", "Initial");
        await File.WriteAllTextAsync(Path.Combine(repository, "notes.txt"), "untracked but not ignored");
        string archive = Path.Combine(_root, "snapshot.zip");

        RemoteBuildSnapshotResult result = await new RemoteBuildSnapshotBuilder().CreateAsync(
            repository, archive, 100 * 1024 * 1024);
        Assert.True(result.HasLocalChanges);
        using ZipArchive zip = ZipFile.OpenRead(archive);
        string[] entries = zip.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("Source/Game.cpp", entries);
        Assert.Contains("notes.txt", entries);
        Assert.Contains(".cyrevision-build-manifest.json", entries);
        Assert.DoesNotContain(entries, entry => entry.StartsWith(".git/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("Binaries/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunnerRejectsSnapshotTraversalBeforeStartingRecipe()
    {
        string snapshot = Path.Combine(_root, "malicious.zip");
        Directory.CreateDirectory(_root);
        using (ZipArchive zip = ZipFile.Open(snapshot, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("../escape.txt");
            await using StreamWriter writer = new(entry.Open());
            await writer.WriteAsync("escape");
        }

        RemoteBuildRecipe recipe = new("dotnet-info", "Dotnet info", "dotnet", ["--info"], ".", ["**/*.txt"], 2);
        RemoteBuildAgentProject project = new(Guid.NewGuid(), "Test", Path.Combine(_root, "workspace"), true, 1024 * 1024, [recipe]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new RemoteBuildJobRunner().RunAsync(
            Guid.NewGuid(), project, recipe, RemoteBuildSourceMode.UploadedSnapshot, snapshot,
            Path.Combine(_root, "jobs"), (_, _) => { }, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public void PublicPlainHttpEndpointIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => RemoteBuildEndpoint.Create("http://8.8.8.8:47841", true));
        Assert.Equal("10.70.0.10", RemoteBuildEndpoint.Create("http://10.70.0.10:47841", true).Host);
        Assert.Equal("127.0.0.1", RemoteBuildEndpoint.Create("http://127.0.0.1:47841", false).Host);
    }

    private static async Task GitAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
