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
    int RequiredCopies,
    IReadOnlyList<string>? RepositoryPaths = null,
    string FileType = "Unknown",
    bool IsKeptAsRecentVersion = false)
{
    public bool CanDelete => !IsReferencedLocally && !IsInsideGracePeriod && Evidence.Count >= RequiredCopies;

    public string DisplayPath => RepositoryPaths?.FirstOrDefault() ?? "Unmapped LFS object";
    public string ShortOid => OidSha256.Length > 12 ? OidSha256[..12] : OidSha256;
    public string SizeText => LfsStorageFormatting.FormatBytes(Size);
    public string CachedAtText => LastModifiedAt.ToLocalTime().ToString("g");

    public string Decision => IsReferencedLocally
        ? IsKeptAsRecentVersion
            ? "Keep hot: recent cached version"
            : "Keep hot: current checkout, worktree, protected ref, or unmanaged type"
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
    string RemoteVerificationOutput,
    LfsManagementProfile? Policy = null)
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

public sealed record LfsAnalysisProgress(
    string Stage,
    int Percent,
    string Detail);

public sealed record LfsPruneResult(
    bool DryRun,
    bool VerifiedRemote,
    TimeSpan Duration,
    string Output)
{
    public string Summary => DryRun
        ? $"Git LFS prune preview completed in {Duration.TotalSeconds:0.#} s."
        : $"Git LFS prune completed in {Duration.TotalSeconds:0.#} s.";
}

public sealed record LfsManagementProfile(
    Guid ProjectId,
    string ExternalStoragePath,
    string ArchivePath,
    string RemoteName,
    int RequiredVerifiedCopies,
    int GracePeriodDays,
    int PeerProofMaximumAgeHours,
    bool VerifyRemote,
    bool TrimRemoteBackedHistory = false,
    int RecentVersionsPerFile = 3,
    string RecentVersionExtensions = ".uasset;.umap",
    int RemoteVerificationTimeoutSeconds = 45)
{
    public static LfsManagementProfile CreateDefault(Guid projectId) => new(
        projectId,
        string.Empty,
        string.Empty,
        "origin",
        1,
        7,
        24,
        true,
        false,
        3,
        ".uasset;.umap",
        45);

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
        if (RecentVersionsPerFile is < 1 or > 100)
            throw new InvalidOperationException("Recent LFS versions per file must be between 1 and 100.");
        if (TrimRemoteBackedHistory && ParseManagedExtensions().Count == 0)
            throw new InvalidOperationException("Choose at least one file extension for remote-backed history trimming.");
        if (RemoteVerificationTimeoutSeconds is < 10 or > 3600)
            throw new InvalidOperationException("Remote LFS verification timeout must be between 10 and 3600 seconds.");
    }

    public IReadOnlySet<string> ParseManagedExtensions() => RecentVersionExtensions
        .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => value.StartsWith('.') ? value : "." + value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed record RepositoryStorageArea(string Name, string Path, long Size, int FileCount)
{
    public string SizeText => LfsStorageFormatting.FormatBytes(Size);
}

public sealed record RepositoryLargeFile(string RelativePath, string Area, string Extension, long Size)
{
    public string SizeText => LfsStorageFormatting.FormatBytes(Size);
}

public sealed record RepositoryStorageReport(
    string RepositoryPath,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RepositoryStorageArea> Areas,
    IReadOnlyList<RepositoryLargeFile> LargestFiles)
{
    public long TotalBytes => Areas.Sum(area => area.Size);
    public string TotalSizeText => LfsStorageFormatting.FormatBytes(TotalBytes);
}

internal static class LfsStorageFormatting
{
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, bytes);
        int suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }
        return $"{value:0.##} {suffixes[suffix]}";
    }
}

public interface ILfsManagementProfileStore
{
    Task<LfsManagementProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task SaveAsync(LfsManagementProfile profile, CancellationToken cancellationToken = default);
}
