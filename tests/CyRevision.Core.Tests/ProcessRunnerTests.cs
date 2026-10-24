using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class ProcessRunnerTests
{
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrevision-process-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }

    [Fact]
    public async Task CommandWithoutInputReceivesEndOfInput()
    {
        using TemporaryDirectory folder = new();
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
        var result = await new ProcessRunner().RunAsync("git", ["hash-object", "--stdin"],
            folder.Path, cancellation.Token);
        Assert.True(result.Succeeded);
        Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task ExportWithoutInputReceivesEndOfInput()
    {
        using TemporaryDirectory folder = new();
        string destination = Path.Combine(folder.Path, "hash.txt");
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
        var result = await new ProcessRunner().RunToFileAsync("git", ["hash-object", "--stdin"],
            folder.Path, destination, cancellation.Token);
        Assert.True(result.Succeeded);
        Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", (await File.ReadAllTextAsync(destination)).Trim());
    }

    [Fact]
    public async Task FailedExportPreservesExistingDestination()
    {
        using TemporaryDirectory folder = new();
        string destination = Path.Combine(folder.Path, "export.bin");
        await File.WriteAllTextAsync(destination, "existing version");
        var result = await new ProcessRunner().RunToFileAsync("git", ["--invalid-cyrevision-option"],
            folder.Path, destination, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("existing version", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.partial"));
    }

    [Fact]
    public async Task SuccessfulExportReplacesDestinationOnlyAfterCompletion()
    {
        using TemporaryDirectory folder = new();
        string destination = Path.Combine(folder.Path, "export.txt");
        await File.WriteAllTextAsync(destination, "old");
        var result = await new ProcessRunner().RunToFileAsync("git", ["--version"],
            folder.Path, destination, CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.StartsWith("git version", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.partial"));
    }

    [Fact]
    public async Task CancelledExportStopsChildAndPreservesDestination()
    {
        using TemporaryDirectory folder = new();
        string destination = Path.Combine(folder.Path, "export.txt");
        await File.WriteAllTextAsync(destination, "old");
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(300));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string executable = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        string[] args = OperatingSystem.IsWindows()
            ? ["/d", "/c", "ping -n 30 127.0.0.1 >nul"] : ["-c", "sleep 30"];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunToFileAsync(
            executable, args, folder.Path, destination, cancellation.Token));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(8));
        Assert.Equal("old", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.partial"));
    }
}
