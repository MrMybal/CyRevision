namespace CyRevision.Git;

public enum GitChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Untracked,
    Conflicted
}

public sealed record GitChange(
    string Path,
    GitChangeKind Kind,
    bool IsStaged,
    bool IsLfsObject,
    string? OriginalPath = null);

public sealed record GitRepositoryStatus(
    string RootPath,
    string CurrentBranch,
    bool IsDetachedHead,
    bool HasRemote,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<GitChange> Changes);

public sealed record GitRevision(
    string Hash,
    string ShortHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject);

public sealed record GitBranch(
    string Name,
    string ShortCommitHash,
    bool IsCurrent,
    bool IsRemote = false,
    string? RemoteName = null,
    int AheadBy = 0,
    int BehindBy = 0,
    bool IsTrackingRemote = false,
    string TipAuthorName = "Unknown",
    DateTimeOffset? TipAuthoredAt = null,
    string TipSubject = "")
{
    public string Scope => IsRemote ? "Remote" : IsCurrent ? "Current" : "Local";
    public string DisplayColor => IsCurrent ? "#78D7B7" : IsRemote ? "#61AFEF" : "#DFE1E5";
    public bool IsPublished => IsRemote || !string.IsNullOrWhiteSpace(RemoteName);
    public string PublicationStatus => IsRemote ? "Remote ref" : IsPublished ? "Published" : "Local only";
    public string SyncStatus => IsRemote
        ? "Read-only remote reference"
        : !IsPublished
            ? "Not published"
            : !IsTrackingRemote
                ? "Remote available · not tracking"
            : AheadBy == 0 && BehindBy == 0
                ? "Up to date"
                : AheadBy > 0 && BehindBy > 0
                    ? $"Diverged · ↑{AheadBy} ↓{BehindBy}"
                    : AheadBy > 0
                        ? $"{AheadBy} commit(s) to push · ↑{AheadBy}"
                        : $"{BehindBy} commit(s) to pull · ↓{BehindBy}";
    public string TrackingText => IsRemote
        ? Name
        : string.IsNullOrWhiteSpace(RemoteName)
            ? "No remote branch"
            : !IsTrackingRemote
                ? $"{RemoteName} · tracking not configured"
                : $"{RemoteName} · ↑{AheadBy} ↓{BehindBy}";
    public string PublicationColor => IsRemote
        ? "#61AFEF"
        : !IsPublished
            ? "#E5C07B"
            : !IsTrackingRemote
                ? "#F2C66D"
            : AheadBy == 0 && BehindBy == 0 ? "#78D7B7" : "#F2C66D";
    public string TipUpdateText => TipAuthoredAt is null
        ? $"Last commit by {TipAuthorName}"
        : $"Updated {TipAuthoredAt.Value.ToLocalTime():g} by {TipAuthorName}";
}

public sealed record GitBranchDetails(
    string BranchName,
    string? ComparisonBase,
    int UniqueCommitCount,
    string? InferredCreatorName,
    DateTimeOffset? InferredCreatedAt,
    string LastAuthorName,
    DateTimeOffset? LastUpdatedAt,
    string LastSubject)
{
    public string ComparisonBaseText => ComparisonBase ?? "No suitable base detected";
    public string UniqueCommitText => ComparisonBase is null
        ? "Unknown"
        : $"{UniqueCommitCount:N0} commit(s) ahead";
    public string CreatorText => string.IsNullOrWhiteSpace(InferredCreatorName)
        ? "Unknown · Git stores no branch creator"
        : $"{InferredCreatorName} · inferred";
    public string CreatedText => InferredCreatedAt?.ToLocalTime().ToString("g") ?? "Unknown";
    public string UpdatedText => LastUpdatedAt?.ToLocalTime().ToString("g") ?? "Unknown";
}

public sealed record GitToolAvailability(
    bool GitAvailable,
    string? GitVersion,
    bool LfsAvailable,
    string? LfsVersion);

public sealed record LfsTrackedPattern(string Pattern, string SourceFile);

public sealed record LfsFileLock(
    string Id,
    string Path,
    string OwnerName,
    DateTimeOffset? LockedAt,
    bool IsOurs,
    bool IsCached = false)
{
    public string Ownership => IsOurs ? "Mine" : "Other user";
    public string Source => IsCached ? "Cached" : "Live";
    public string LockedAtText => LockedAt?.ToLocalTime().ToString("g") ?? "Unknown";
    public string ShortId => Id.Length > 10 ? Id[..10] + "…" : Id;
    public string UnlockAction => IsOurs ? "Unlock" : "Force unlock…";
    public string OwnershipColor => IsOurs ? "#78D7B7" : "#E5C07B";
    public string OwnerColor => IsOurs ? "#78D7B7" : "#61AFEF";
    public string FileColor => System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
    {
        ".uasset" or ".umap" => "#C678DD",
        ".png" or ".jpg" or ".jpeg" or ".tga" or ".exr" => "#E06C75",
        ".fbx" or ".obj" or ".gltf" or ".glb" => "#E5C07B",
        ".wav" or ".mp3" or ".ogg" => "#56B6C2",
        _ => "#DFE1E5"
    };
}

public sealed record GitGraphCommit(
    string Hash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    DateTimeOffset AuthoredAt,
    string Subject,
    string Decorations)
{
    public bool IsMerge => ParentHashes.Count > 1;
}

public enum GitFileKind
{
    Code,
    UnrealAsset,
    Texture,
    Model,
    Audio,
    Document,
    Configuration,
    Other
}

public sealed record GitFileActivity(
    string Path,
    GitFileKind Kind,
    int ChangeCount,
    long AddedLines,
    long DeletedLines,
    int BinaryChangeCount,
    DateTimeOffset LastChangedAt);

public sealed record GitFileRelation(
    string SourcePath,
    string TargetPath,
    int CoChangeCount);

public sealed record GitFileActivityGraph(
    IReadOnlyList<GitFileActivity> Files,
    IReadOnlyList<GitFileRelation> Relations,
    int AnalyzedCommitCount,
    int TotalFileCount);

public sealed record LfsPointerInfo(string OidSha256, long Size);

public sealed record GitCommitFileChange(
    string Path,
    GitChangeKind Kind,
    long? AddedLines,
    long? DeletedLines,
    bool IsBinary,
    string? OriginalPath = null,
    LfsPointerInfo? LfsPointer = null)
{
    public string ChangeSummary => IsBinary
        ? "Binary"
        : $"+{AddedLines ?? 0} / -{DeletedLines ?? 0}";

    public bool IsLfsObject => LfsPointer is not null;
}

public sealed record GitCommitDetails(
    GitRevision Revision,
    IReadOnlyList<string> ParentHashes,
    string Body,
    IReadOnlyList<GitCommitFileChange> Files,
    long AddedLines,
    long DeletedLines,
    int BinaryFileCount);

public sealed record GitCommitComparison(
    string FromRevision,
    string ToRevision,
    IReadOnlyList<GitCommitFileChange> Files,
    long AddedLines,
    long DeletedLines,
    int BinaryFileCount);

public sealed record GitFileRevision(
    GitRevision Revision,
    string Path,
    GitChangeKind Kind,
    long? AddedLines,
    long? DeletedLines,
    bool IsBinary,
    LfsPointerInfo? LfsPointer = null);

public sealed record LfsTrackedFile(
    string Path,
    GitFileKind Kind,
    LfsPointerInfo Pointer,
    bool IsAvailableLocally,
    string? LocalObjectPath)
{
    public string Availability => IsAvailableLocally ? "Local" : "Missing";
    public string SizeText => FormatSize(Pointer.Size);

    private static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

public sealed record LfsFileVersion(
    string Path,
    GitRevision Revision,
    LfsPointerInfo Pointer,
    bool IsAvailableLocally,
    string? LocalObjectPath,
    bool IsCurrent,
    IReadOnlyList<LfsObjectLocation>? KnownLocations = null)
{
    public string Availability
    {
        get
        {
            List<string> locations = [];
            if (IsAvailableLocally)
            {
                locations.Add("Local");
            }

            if (KnownLocations is not null)
            {
                locations.AddRange(KnownLocations.Select(location => location.Kind switch
                {
                    LfsStorageKind.Archive => "Archive",
                    _ => $"Peer: {location.DisplayName}"
                }));
            }

            return locations.Count == 0 ? "Missing" : string.Join(" · ", locations.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    public bool HasPeerCopy => KnownLocations?.Any(location => location.Kind == LfsStorageKind.Peer) == true;
    public bool HasArchiveCopy => KnownLocations?.Any(location => location.Kind == LfsStorageKind.Archive) == true;
    public bool CanRequestFromPeer => !IsAvailableLocally && HasPeerCopy;
    public string SizeText => LfsTrackedFileSizeFormatter.Format(Pointer.Size);

    private static class LfsTrackedFileSizeFormatter
    {
        public static string Format(long size)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = size;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.#} {units[unit]}";
        }
    }
}

public sealed record GitContributorActivity(
    string AuthorName,
    string AuthorEmail,
    int CommitCount,
    int FilesTouched,
    long AddedLines,
    long DeletedLines,
    int BinaryChanges,
    DateTimeOffset LastActiveAt)
{
    public string LineSummary => $"+{AddedLines} / -{DeletedLines}";
}

public sealed record GitDailyActivity(
    DateOnly Day,
    int CommitCount,
    int FilesTouched,
    long AddedLines,
    long DeletedLines);

public sealed record GitRepositoryInsights(
    int CommitCount,
    int MergeCount,
    int ContributorCount,
    int FileCount,
    long AddedLines,
    long DeletedLines,
    int BinaryChanges,
    IReadOnlyList<GitContributorActivity> Contributors,
    IReadOnlyList<GitDailyActivity> DailyActivity,
    IReadOnlyList<GitFileActivity> HotFiles);
