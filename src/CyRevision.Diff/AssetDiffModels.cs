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
}
