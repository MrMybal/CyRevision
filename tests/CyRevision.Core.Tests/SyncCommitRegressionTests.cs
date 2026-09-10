using System.IO.Compression;
using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncCommitRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionModeRegression", Guid.NewGuid().ToString("N"));
    private readonly Guid _project = Guid.NewGuid();
    private readonly SyncCommitService _service = new();
    private string Source => Path.Combine(_root, "source");
    private string Receiver => Path.Combine(_root, "receiver");
    private string Exchange => Path.Combine(_root, "exchange");
    private string State => Path.Combine(_root, "state");
    private string ReceiverState => Path.Combine(_root, "receiver-state");
    private string Recovery => Path.Combine(_root, "recovery");

    private async Task<SyncCommitCreateResult> BaseAsync()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Receiver);
        foreach (string file in new[] { "a.txt", "z.txt" })
        {
            await File.WriteAllTextAsync(Path.Combine(Source, file), "base");
            await File.WriteAllTextAsync(Path.Combine(Receiver, file), "base");
        }
        return await CommitAsync();
    }
    private Task<SyncCommitCreateResult> CommitAsync() =>
        _service.CreateCommitAsync(_project, Source, Exchange, State, "Revision", "Tester");
    private Task ApplyAsync(SyncCommitManifest manifest, IReadOnlyDictionary<string, SyncCommitConflictChoice>? choices = null) =>
        _service.ApplyAsync(Receiver, Exchange, ReceiverState, Recovery, manifest, choices);

    [Fact]
    public async Task IncomingChangePreservesUnrelatedLocalEditsAdditionsAndDeletions()
    {
        await BaseAsync();
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "incoming");
        SyncCommitManifest incoming = (await CommitAsync()).Manifest;
        await File.WriteAllTextAsync(Path.Combine(Receiver, "z.txt"), "local edit");
        await File.WriteAllTextAsync(Path.Combine(Receiver, "local-only.txt"), "keep me");
        await ApplyAsync(incoming);
        Assert.Equal("incoming", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
        Assert.Equal("local edit", await File.ReadAllTextAsync(Path.Combine(Receiver, "z.txt")));
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(Receiver, "local-only.txt")));
        using ZipArchive backup = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Recovery, "*.zip")));
        Assert.Equal("base", await new StreamReader(backup.GetEntry("a.txt")!.Open()).ReadToEndAsync());
        Assert.Null(backup.GetEntry("z.txt"));
        File.Delete(Path.Combine(Receiver, "z.txt"));
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "third");
        await ApplyAsync((await CommitAsync()).Manifest);
        Assert.False(File.Exists(Path.Combine(Receiver, "z.txt")));
    }

    [Theory]
    [InlineData(SyncCommitConflictChoice.KeepLocal)]
    [InlineData(SyncCommitConflictChoice.UseIncoming)]
    public async Task DeleteConflictHonorsChoiceAndBacksUpEveryAffectedFile(SyncCommitConflictChoice choice)
    {
        await BaseAsync();
        File.Delete(Path.Combine(Source, "a.txt"));
        SyncCommitManifest incoming = (await CommitAsync()).Manifest;
        await File.WriteAllTextAsync(Path.Combine(Receiver, "a.txt"), "local edit");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAsync(incoming));
        await ApplyAsync(incoming, new Dictionary<string, SyncCommitConflictChoice> { ["a.txt"] = choice });
        Assert.Equal(choice == SyncCommitConflictChoice.KeepLocal, File.Exists(Path.Combine(Receiver, "a.txt")));
        using ZipArchive backup = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Recovery, "*.zip")));
        if (choice == SyncCommitConflictChoice.UseIncoming)
            Assert.Equal("local edit", await new StreamReader(backup.GetEntry("a.txt")!.Open()).ReadToEndAsync());
        else Assert.Equal("local edit", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrMissingEntryIsRejectedBeforeAnyWorkingFileOrHeadChanges(bool missing)
    {
        SyncCommitCreateResult first = await BaseAsync();
        await ApplyAsync(first.Manifest);
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "next");
        await File.WriteAllTextAsync(Path.Combine(Source, "z.txt"), "next");
        SyncCommitCreateResult incoming = await CommitAsync();
        using (ZipArchive zip = ZipFile.Open(incoming.PackagePath, ZipArchiveMode.Update))
        {
            zip.GetEntry("files/z.txt")!.Delete();
            if (!missing)
            {
                using StreamWriter writer = new(zip.CreateEntry("files/z.txt").Open());
                writer.Write("evil"); // Same length, different hash.
            }
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => ApplyAsync(incoming.Manifest));
        Assert.Equal("base", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
        Assert.Equal("base", await File.ReadAllTextAsync(Path.Combine(Receiver, "z.txt")));
        Assert.Equal(first.Manifest.CommitId, (await File.ReadAllTextAsync(Path.Combine(ReceiverState, "HEAD"))).Trim());
        Assert.Empty(Directory.GetDirectories(ReceiverState, "apply-*"));
    }

    [Fact]
    public async Task MissingParentBlocksApplication()
    {
        SyncCommitCreateResult first = await BaseAsync();
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "incoming");
        SyncCommitManifest incoming = (await CommitAsync()).Manifest;
        File.Delete(first.PackagePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => ApplyAsync(incoming));
        Assert.Equal("base", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
    }

    [Fact]
    public async Task SkippedIntermediateCommitsUseLastAppliedCommonAncestor()
    {
        await ApplyAsync((await BaseAsync()).Manifest);
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "intermediate");
        await CommitAsync();
        await File.WriteAllTextAsync(Path.Combine(Source, "z.txt"), "latest");
        SyncCommitManifest latest = (await CommitAsync()).Manifest;
        await ApplyAsync(latest);
        Assert.Equal("intermediate", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
        Assert.Equal("latest", await File.ReadAllTextAsync(Path.Combine(Receiver, "z.txt")));
        await File.WriteAllTextAsync(Path.Combine(Receiver, "a.txt"), "new local edit");
        await ApplyAsync(latest);
        Assert.Equal("new local edit", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
    }

    [Fact]
    public async Task LocalDirectoryCollisionIsRejectedWithoutPartialApply()
    {
        await BaseAsync();
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "incoming");
        await File.WriteAllTextAsync(Path.Combine(Source, "new.txt"), "incoming");
        SyncCommitManifest incoming = (await CommitAsync()).Manifest;
        Directory.CreateDirectory(Path.Combine(Receiver, "new.txt"));
        await File.WriteAllTextAsync(Path.Combine(Receiver, "new.txt", "local.txt"), "keep");
        await Assert.ThrowsAsync<IOException>(() => ApplyAsync(incoming));
        Assert.Equal("base", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(Receiver, "new.txt", "local.txt")));
        Assert.False(File.Exists(Path.Combine(ReceiverState, "HEAD")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData("folder/../file.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("/absolute.txt")]
    public async Task UnsafeManifestCannotWriteOutsideProjectOrIntoGit(string path)
    {
        SyncCommitManifest manifest = (await BaseAsync()).Manifest;
        manifest = manifest with { Files = [manifest.Files[0] with { Path = path }] };
        await Assert.ThrowsAsync<InvalidDataException>(() => ApplyAsync(manifest));
        Assert.Equal("base", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
    }

    [Fact]
    public async Task CancelledApplyDoesNotChangeFiles()
    {
        await BaseAsync();
        await File.WriteAllTextAsync(Path.Combine(Source, "a.txt"), "incoming");
        SyncCommitManifest incoming = (await CommitAsync()).Manifest;
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ApplyAsync(Receiver, Exchange, ReceiverState, Recovery, incoming, cancellationToken: cancellation.Token));
        Assert.Equal("base", await File.ReadAllTextAsync(Path.Combine(Receiver, "a.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
