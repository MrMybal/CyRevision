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

    [Fact]
    public async Task LfsInventorySupportsPriorityOnDemandRequestAndResumableVerifiedImport()
    {
        GitCliRepositoryService git = new();
        GitToolAvailability tools = await git.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        string ownerRepository = Path.Combine(_root, "lfs-owner");
        string peerRepository = Path.Combine(_root, "lfs-peer");
        string exchange = Path.Combine(_root, "lfs-exchange");
        string peerState = Path.Combine(_root, "lfs-peer-state");
        await git.InitializeAsync(ownerRepository);
        await git.InitializeAsync(peerRepository);
        await git.ConfigureIdentityAsync(ownerRepository, "Owner", "owner@cyrevision.local");
        await git.TrackLfsPatternAsync(ownerRepository, "*.uasset");
        string assetPath = Path.Combine(ownerRepository, "Content", "Hero.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        byte[] firstVersion = Enumerable.Range(0, 2 * 1024 * 1024)
            .Select(index => (byte)((index * 17) % 251)).ToArray();
        byte[] secondVersion = Enumerable.Range(0, 3 * 1024 * 1024)
            .Select(index => (byte)((index * 29) % 253)).ToArray();
        await File.WriteAllBytesAsync(assetPath, firstVersion);
        await git.CreateRevisionAsync(ownerRepository, "Add hero", [".gitattributes", "Content/Hero.uasset"]);
        await File.WriteAllBytesAsync(assetPath, secondVersion);
        await git.CreateRevisionAsync(ownerRepository, "Update hero", ["Content/Hero.uasset"]);
        IReadOnlyList<LfsFileVersion> versions = await git.GetLfsFileVersionsAsync(ownerRepository, "Content/Hero.uasset");
        Assert.Equal(2, versions.Count);
        LfsFileVersion current = versions[0];
        LfsFileVersion old = versions[1];

        using FileDeviceIdentityStore ownerIdentity = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "lfs-owner-identity"), "Owner device", "OWNER-LFS-SYNCTHING-ID");
        using FileDeviceIdentityStore peerIdentity = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "lfs-peer-identity"), "Peer device", "PEER-LFS-SYNCTHING-ID");
        DeviceIdentity[] authorized = [ownerIdentity.Identity, peerIdentity.Identity];
        Guid projectId = Guid.NewGuid();
        GitPeerExchangeService exchangeService = new();
        GitPeerExchangeOptions onDemand = new(PeerLfsTransferMode.CurrentRevisionOnly, 1, 1024L * 1024 * 1024);

        GitPeerExportResult firstExport = await exchangeService.ExportDetailedAsync(
            projectId, ownerRepository, exchange, ownerIdentity, authorized, onDemand);
        Assert.NotNull(firstExport.TransactionId);
        Assert.Equal(1, firstExport.PublishedLfsObjects);
        Assert.Equal(2, firstExport.InventoryObjectCount);
        Assert.True(File.Exists(GetExchangeObjectPath(exchange, current.Pointer.OidSha256)));
        Assert.False(File.Exists(GetExchangeObjectPath(exchange, old.Pointer.OidSha256)));

        GitPeerExchangeResult firstImport = await exchangeService.ImportDetailedAsync(
            projectId,
            peerRepository,
            exchange,
            peerState,
            authorized,
            peerIdentity.Identity.DeviceId,
            onDemand);
        Assert.Equal(1, firstImport.ImportedTransactions);
        Assert.Equal(1, firstImport.ImportedLfsObjects);
        Assert.True(File.Exists(GetLocalObjectPath(peerRepository, current.Pointer.OidSha256)));
        Assert.False(File.Exists(GetLocalObjectPath(peerRepository, old.Pointer.OidSha256)));
        PeerLfsAvailabilityCache cache = await exchangeService.GetCachedLfsAvailabilityAsync(peerState, projectId);
        Assert.Equal(2, cache.Objects.Count);
        Assert.Contains(cache.Objects, item =>
            item.OidSha256 == old.Pointer.OidSha256 &&
            item.Peers.Any(peer => peer.DeviceId == ownerIdentity.Identity.DeviceId && !peer.PublishedToExchange));

        await exchangeService.RequestLfsObjectAsync(
            projectId,
            exchange,
            peerIdentity,
            old.Pointer.OidSha256,
            "Restore old hero asset");
        GitPeerExportResult requestedExport = await exchangeService.ExportDetailedAsync(
            projectId, ownerRepository, exchange, ownerIdentity, authorized, onDemand);
        Assert.Equal(1, requestedExport.FulfilledRequests);
        string sharedOldObject = GetExchangeObjectPath(exchange, old.Pointer.OidSha256);
        Assert.True(File.Exists(sharedOldObject));

        string peerOldObject = GetLocalObjectPath(peerRepository, old.Pointer.OidSha256);
        string partial = peerOldObject + ".cyrevision-partial";
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await using (FileStream source = new(sharedOldObject, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (FileStream destination = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] prefix = new byte[source.Length / 2];
            int read = await source.ReadAsync(prefix);
            await destination.WriteAsync(prefix.AsMemory(0, read));
        }

        GitPeerExchangeResult resumedImport = await exchangeService.ImportDetailedAsync(
            projectId,
            peerRepository,
            exchange,
            peerState,
            authorized,
            peerIdentity.Identity.DeviceId,
            onDemand);
        Assert.Equal(1, resumedImport.ImportedLfsObjects);
        Assert.Equal(1, resumedImport.ResumedLfsObjects);
        Assert.Equal(firstVersion, await File.ReadAllBytesAsync(peerOldObject));
        Assert.False(File.Exists(partial));

        string inventoryPath = Path.Combine(
            exchange,
            "inventories",
            ownerIdentity.Identity.DeviceId.ToString("N") + ".json");
        string signedInventory = await File.ReadAllTextAsync(inventoryPath);
        await File.WriteAllTextAsync(inventoryPath, signedInventory.Replace("Owner device", "Tampered device"));
        byte[] rogueContent = "unannounced LFS content"u8.ToArray();
        string rogueOid = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rogueContent)).ToLowerInvariant();
        string rogueExchangeObject = GetExchangeObjectPath(exchange, rogueOid);
        Directory.CreateDirectory(Path.GetDirectoryName(rogueExchangeObject)!);
        await File.WriteAllBytesAsync(rogueExchangeObject, rogueContent);
        string tamperedState = Path.Combine(_root, "tampered-inventory-state");
        GitPeerExchangeResult tamperedImport = await exchangeService.ImportDetailedAsync(
            projectId,
            peerRepository,
            exchange,
            tamperedState,
            authorized,
            peerIdentity.Identity.DeviceId,
            GitPeerExchangeOptions.Default with { LfsTransferMode = PeerLfsTransferMode.AllAvailable });
        Assert.Equal(0, tamperedImport.AvailablePeerLfsObjects);
        Assert.Empty((await exchangeService.GetCachedLfsAvailabilityAsync(tamperedState, projectId)).Objects);
        Assert.False(File.Exists(GetLocalObjectPath(peerRepository, rogueOid)));
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

    private static string GetExchangeObjectPath(string exchangeRoot, string oid) =>
        Path.Combine(exchangeRoot, "lfs", "objects", oid[..2], oid.Substring(2, 2), oid);

    private static string GetLocalObjectPath(string repositoryRoot, string oid) =>
        Path.Combine(repositoryRoot, ".git", "lfs", "objects", oid[..2], oid.Substring(2, 2), oid);
}
