namespace CyRevision.Git;

public enum LfsRetentionEvidenceKind
{
    Remote,
    Peer,
    Archive
}

public sealed record LfsRetentionEvidence(
    LfsRetentionEvidenceKind Kind,
    string LocationId,
    string DisplayName,
    DateTimeOffset VerifiedAt);

public sealed record LfsCleanupItem(
    string OidSha256,
    long Size,
    DateTimeOffset LastModifiedAt,
    string LocalPath,
    bool IsReferencedLocally,
    bool IsInsideGracePeriod,
    IReadOnlyList<LfsRetentionEvidence> Evidence,
    int RequiredCopies)
{
    public bool CanDelete => !IsReferencedLocally && !IsInsideGracePeriod && Evidence.Count >= RequiredCopies;

    public string Decision => IsReferencedLocally
        ? "Keep: referenced by a local ref, stash, index, or worktree"
        : IsInsideGracePeriod
            ? "Keep: inside the safety grace period"
            : CanDelete
                ? $"Eligible: {Evidence.Count} verified copy/copies"
                : $"Blocked: {Math.Max(0, RequiredCopies - Evidence.Count)} verified copy/copies missing";
}

public sealed record LfsCleanupPlan(
    Guid PlanId,
    string RepositoryPath,
    string StoragePath,
    DateTimeOffset CreatedAt,
    int RequiredCopies,
    IReadOnlyList<LfsCleanupItem> Objects,
    string RemoteVerificationOutput)
{
    public long TotalBytes => Objects.Sum(item => item.Size);
    public long ReclaimableBytes => Objects.Where(item => item.CanDelete).Sum(item => item.Size);
    public int ReclaimableCount => Objects.Count(item => item.CanDelete);
}

public sealed record LfsCleanupResult(
    Guid PlanId,
    int DeletedObjects,
    long ReclaimedBytes,
    int SkippedObjects,
    string AuditPath);

public sealed record LfsRelocationResult(
    string PreviousStoragePath,
    string ActiveStoragePath,
    int CopiedObjects,
    long CopiedBytes,
    bool OriginalObjectsRemoved);

public sealed record LfsArchiveResult(
    string ArchivePath,
    int ArchivedObjects,
    long ArchivedBytes);

public sealed record LfsManagementProfile(
    Guid ProjectId,
    string ExternalStoragePath,
    string ArchivePath,
    string RemoteName,
    int RequiredVerifiedCopies,
    int GracePeriodDays,
    int PeerProofMaximumAgeHours,
    bool VerifyRemote)
{
    public static LfsManagementProfile CreateDefault(Guid projectId) => new(
        projectId,
        string.Empty,
        string.Empty,
        "origin",
        1,
        7,
        24,
        true);

    public void Validate()
    {
        if (ProjectId == Guid.Empty)
            throw new InvalidOperationException("A project is required for LFS storage management.");
        if (RequiredVerifiedCopies is < 1 or > 5)
            throw new InvalidOperationException("Required verified copies must be between 1 and 5.");
        if (GracePeriodDays is < 0 or > 3650)
            throw new InvalidOperationException("The LFS grace period must be between 0 and 3650 days.");
        if (PeerProofMaximumAgeHours is < 1 or > 8760)
            throw new InvalidOperationException("Peer proof age must be between 1 and 8760 hours.");
        if (VerifyRemote && string.IsNullOrWhiteSpace(RemoteName))
            throw new InvalidOperationException("Choose the remote used for LFS verification.");
    }
}

public interface ILfsManagementProfileStore
{
    Task<LfsManagementProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task SaveAsync(LfsManagementProfile profile, CancellationToken cancellationToken = default);
}
