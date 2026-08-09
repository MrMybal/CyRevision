namespace CyRevision.Diff;

public enum AssetDiffKind
{
    Text,
    Texture,
    ObjMesh,
    UnrealPackage,
    Binary
}

public sealed record AssetDiffResult(
    AssetDiffKind Kind,
    bool Equivalent,
    string Summary,
    IReadOnlyDictionary<string, string> Metrics,
    IReadOnlyList<string> Details,
    string? PreviewImagePath = null);

public interface IAssetDiffService
{
    Task<AssetDiffResult> CompareAsync(
        string baselinePath,
        string candidatePath,
        string artifactDirectory,
        CancellationToken cancellationToken = default);

    Task<UnrealDependencyGraph> ScanUnrealDependenciesAsync(
        string projectRoot,
        int maximumAssetCount = 500,
        CancellationToken cancellationToken = default);
}

public sealed record UnrealAssetNode(
    string Path,
    string PackageName,
    string AssetType,
    long Size,
    int DependencyCount,
    int ReferencedByCount)
{
    public string RelationSummary => $"{DependencyCount} out · {ReferencedByCount} in";
}

public sealed record UnrealAssetDependency(
    string SourcePath,
    string TargetPath,
    string PackageReference);

public sealed record UnrealDependencyGraph(
    IReadOnlyList<UnrealAssetNode> Assets,
    IReadOnlyList<UnrealAssetDependency> Dependencies,
    int TotalAssetCount,
    int InspectedAssetCount,
    int UnresolvedReferenceCount);
