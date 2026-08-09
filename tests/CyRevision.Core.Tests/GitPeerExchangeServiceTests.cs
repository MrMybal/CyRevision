using CyRevision.Git;
using CyRevision.Security;

namespace CyRevision.Core.Tests;

public sealed class GitPeerExchangeServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionGitExchangeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SignedBundleCreatesRemotePeerBranchWithoutSharingGitDirectory()
    {
        GitCliRepositoryService git = new();
        GitToolAvailability tools = await git.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        string ownerRepository = Path.Combine(_root, "owner-repository");
        string peerRepository = Path.Combine(_root, "peer-repository");
        string exchange = Path.Combine(_root, "exchange");
        await git.InitializeAsync(ownerRepository);
        await git.InitializeAsync(peerRepository);
        await git.ConfigureIdentityAsync(ownerRepository, "Owner", "owner@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(ownerRepository, "asset.txt"), "signed transaction");
        await git.CreateRevisionAsync(ownerRepository, "Shared revision", ["asset.txt"]);

        using FileDeviceIdentityStore ownerIdentity = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "identity"),
            "Owner device",
            "OWNER-SYNCTHING-ID");
        Guid projectId = Guid.NewGuid();
        GitPeerExchangeService exchangeService = new();
        Guid? transactionId = await exchangeService.ExportAsync(
            projectId,
            ownerRepository,
            exchange,
            ownerIdentity);

        GitPeerExchangeResult imported = await exchangeService.ImportAsync(
            projectId,
            peerRepository,
            exchange,
            Path.Combine(_root, "peer-state"),
            [ownerIdentity.Identity],
            localDeviceId: Guid.NewGuid());

        Assert.NotNull(transactionId);
        Assert.Equal(1, imported.ImportedTransactions);
        IReadOnlyList<GitBranch> branches = await git.GetBranchesAsync(peerRepository);
        GitBranch peerBranch = Assert.Single(branches.Where(branch => branch.IsRemote));
        Assert.StartsWith("cyrevision/", peerBranch.Name, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, true);
    }
}
