using CyRevision.Security;

namespace CyRevision.Git;

public enum PeerLfsTransferMode
{
    CurrentRevisionOnly,
    CurrentAndRecent,
    AllAvailable
}

public enum PeerLfsObjectPriority
{
    Current,
    Requested,
    RecentHistory,
    Archive
}

public sealed record GitPeerExchangeOptions(
    PeerLfsTransferMode LfsTransferMode = PeerLfsTransferMode.CurrentRevisionOnly,
    int RecentHistoricalObjectCount = 3,
    long MaximumTransferBytes = 10L * 1024 * 1024 * 1024)
{
    public static GitPeerExchangeOptions Default { get; } = new();

    public void Validate()
    {
        if (RecentHistoricalObjectCount is < 1 or > 1000)
        {
            throw new InvalidOperationException("The recent LFS object count must be between 1 and 1000.");
        }

        if (MaximumTransferBytes <= 0)
        {
            throw new InvalidOperationException("The maximum transfer size must be greater than zero.");
        }
    }
}

public sealed record PeerLfsInventoryObject(
    string OidSha256,
    long Size,
    PeerLfsObjectPriority Priority,
    IReadOnlyList<string> Paths,
    bool PublishedToExchange);

public sealed record GitPeerContentInventory(
    Guid InventoryId,
    Guid ProjectId,
    DeviceIdentity Device,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PeerLfsInventoryObject> LfsObjects);

public sealed record SignedGitPeerContentInventory(
    GitPeerContentInventory Inventory,
    string Signature);

public sealed record PeerLfsObjectRequest(
    Guid RequestId,
    Guid ProjectId,
    DeviceIdentity Requester,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string OidSha256,
    string Reason);

public sealed record SignedPeerLfsObjectRequest(
    PeerLfsObjectRequest Request,
    string Signature);

public sealed record GitPeerExportResult(
    Guid? TransactionId,
    int PublishedLfsObjects,
    int ResumedLfsObjects,
    int DeferredLfsObjects,
    long PublishedLfsBytes,
    int FulfilledRequests,
    int InventoryObjectCount);

public sealed record PeerLfsAvailabilityLocation(
    Guid DeviceId,
    string DisplayName,
    DateTimeOffset LastSeenAt,
    long Size,
    PeerLfsObjectPriority Priority,
    bool PublishedToExchange);

public sealed record PeerLfsObjectAvailability(
    string OidSha256,
    IReadOnlyList<PeerLfsAvailabilityLocation> Peers);

public sealed record PeerLfsAvailabilityCache(
    Guid ProjectId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PeerLfsObjectAvailability> Objects)
{
    public static PeerLfsAvailabilityCache Empty(Guid projectId) => new(projectId, DateTimeOffset.MinValue, []);
}

public enum LfsStorageKind
{
    Peer,
    Archive
}

public sealed record LfsObjectLocation(
    LfsStorageKind Kind,
    string DisplayName,
    DateTimeOffset LastSeenAt,
    bool IsImmediatelyAvailable);
