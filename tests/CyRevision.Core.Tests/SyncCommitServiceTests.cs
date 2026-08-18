using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncCommitServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionSyncCommitTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExchangeChangesOnlyWhenAnExplicitCommitIsCreated()
    {
        string source = Path.Combine(_root, "source");
        string exchange = Path.Combine(_root, "exchange");
        string state = Path.Combine(_root, "state");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(exchange);
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "one");
        SyncCommitService service = new();

        Assert.Empty(Directory.EnumerateFileSystemEntries(exchange));
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "two");
        Assert.Empty(Directory.EnumerateFileSystemEntries(exchange));

        SyncCommitCreateResult created = await service.CreateCommitAsync(
            Guid.NewGuid(), source, exchange, state, "Publish version", "Tester");

        Assert.True(File.Exists(created.PackagePath));
        SyncCommitManifest listed = Assert.Single(await service.ListCommitsAsync(exchange));
        Assert.Equal("Publish version", listed.Message);
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(source, "data.txt")));
    }

    [Fact]
    public async Task ConflictingCommitRequiresAChoiceAndCreatesRecoveryBackup()
    {
        string source = Path.Combine(_root, "conflict-source");
        string exchange = Path.Combine(_root, "conflict-exchange");
        string state = Path.Combine(_root, "conflict-state");
        string receiver = Path.Combine(_root, "receiver");
        string receiverState = Path.Combine(_root, "receiver-state");
        string backups = Path.Combine(_root, "recovery");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(receiver);
        await File.WriteAllTextAsync(Path.Combine(source, "asset.txt"), "base");
        await File.WriteAllTextAsync(Path.Combine(receiver, "asset.txt"), "base");
        Guid project = Guid.NewGuid();
        SyncCommitService service = new();
        await service.CreateCommitAsync(project, source, exchange, state, "Base", "Tester");
        await File.WriteAllTextAsync(Path.Combine(source, "asset.txt"), "incoming");
        SyncCommitManifest incoming = (await service.CreateCommitAsync(
            project, source, exchange, state, "Incoming", "Tester")).Manifest;
        await File.WriteAllTextAsync(Path.Combine(receiver, "asset.txt"), "local");

        SyncCommitAnalysis analysis = await service.AnalyzeAsync(receiver, exchange, incoming);
        SyncCommitConflict conflict = Assert.Single(analysis.Conflicts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            receiver, exchange, receiverState, backups, incoming));

        await service.ApplyAsync(
            receiver,
            exchange,
            receiverState,
            backups,
            incoming,
            new Dictionary<string, SyncCommitConflictChoice>(StringComparer.OrdinalIgnoreCase)
            {
                [conflict.Path] = SyncCommitConflictChoice.UseIncoming
            });

        Assert.Equal("incoming", await File.ReadAllTextAsync(Path.Combine(receiver, "asset.txt")));
        Assert.Single(Directory.EnumerateFiles(backups, "*.zip"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
