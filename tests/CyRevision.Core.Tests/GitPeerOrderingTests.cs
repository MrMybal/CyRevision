using CyRevision.Git;
using CyRevision.Security;
using System.Diagnostics;

namespace CyRevision.Core.Tests;

public sealed class GitPeerOrderingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionPeerOrderingTests", Guid.NewGuid().ToString("N"));
    private string Owner => Path.Combine(_root, "owner");
    private string Receiver => Path.Combine(_root, "receiver");
    private string Exchange => Path.Combine(_root, "exchange");
    private string State => Path.Combine(_root, "state");

    private async Task<string> Git(string root, params string[] args)
    {
        GitCommandResult result = await RunGit(root, args);
        Assert.True(result.Succeeded, result.StandardError);
        return result.StandardOutput.Trim();
    }

    private static async Task<GitCommandResult> RunGit(string root, params string[] args)
    {
        ProcessStartInfo info = new("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode == 0, await output, await error);
    }
    private sealed record GitCommandResult(bool Succeeded, string StandardOutput, string StandardError);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateOrReverseOrderedPublicationsNeverRegressPeerRefs(bool late)
    {
        Directory.CreateDirectory(Owner);
        Directory.CreateDirectory(Receiver);
        await Git(Owner, "init", "-b", "main");
        await Git(Receiver, "init", "-b", "main");
        await Git(Owner, "config", "user.name", "Test");
        await Git(Owner, "config", "user.email", "test@example.invalid");
        using FileDeviceIdentityStore identity = await FileDeviceIdentityStore.OpenOrCreateAsync(Path.Combine(_root, "identity"), "Owner", "OWNER");
        GitPeerExchangeService service = new();
        Guid project = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(Owner, "file.txt"), "one");
        await Git(Owner, "add", "file.txt");
        await Git(Owner, "commit", "-m", "One");
        Guid first = (await service.ExportAsync(project, Owner, Exchange, identity))!.Value;
        await File.WriteAllTextAsync(Path.Combine(Owner, "file.txt"), "two");
        await Git(Owner, "commit", "-am", "Two");
        string newest = await Git(Owner, "rev-parse", "HEAD");
        Guid second = (await service.ExportAsync(project, Owner, Exchange, identity))!.Value;
        Assert.Null(await service.ExportAsync(project, Owner, Exchange, identity));
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(Exchange, "transactions")).Length);

        string old = Path.Combine(Exchange, "transactions", first.ToString("N"));
        string latest = Path.Combine(Exchange, "transactions", second.ToString("N"));
        Directory.Move(latest, Path.Combine(Exchange, "transactions", "aaa-latest"));
        string held = late ? Path.Combine(_root, "held-old") : Path.Combine(Exchange, "transactions", "zzz-old");
        Directory.Move(old, held);
        await service.ImportAsync(project, Receiver, Exchange, State, [identity.Identity]);
        if (late)
        {
            Directory.Move(held, Path.Combine(Exchange, "transactions", "zzz-old"));
            await service.ImportAsync(project, Receiver, Exchange, State, [identity.Identity]);
        }
        string peerRef = $"refs/remotes/cyrevision/{identity.Identity.DeviceId:N}";
        peerRef = peerRef[..("refs/remotes/cyrevision/".Length + 12)] + "/main";
        Assert.Equal(newest, await Git(Receiver, "rev-parse", peerRef));
        Assert.False(File.Exists(Path.Combine(Receiver, "file.txt"))); // Working tree untouched.
        Assert.Equal(0, (await service.ImportAsync(project, Receiver, Exchange, State, [identity.Identity])).ImportedTransactions);

        // A newer explicit rewind is legitimate; stale-message protection must not block it.
        await Git(Owner, "update-ref", "refs/heads/test-branch", newest);
        await service.ExportAsync(project, Owner, Exchange, identity);
        await service.ImportAsync(project, Receiver, Exchange, State, [identity.Identity]);
        await Git(Owner, "update-ref", "-d", "refs/heads/test-branch");
        await Git(Owner, "update-ref", "refs/heads/main", "HEAD~1");
        await service.ExportAsync(project, Owner, Exchange, identity);
        await service.ImportAsync(project, Receiver, Exchange, State, [identity.Identity]);
        Assert.Equal(await Git(Owner, "rev-parse", "HEAD"), await Git(Receiver, "rev-parse", peerRef));
        GitCommandResult removed = await RunGit(Receiver, "show-ref", "--verify", peerRef.Replace("/main", "/test-branch"));
        Assert.False(removed.Succeeded);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
