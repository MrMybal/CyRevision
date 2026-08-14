using CyRevision.Diff;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.Plugins;

public sealed class FilePresentationService
{
    private static readonly HashSet<string> BuiltInImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico"
    };

    private readonly CyRevisionPluginManager _pluginManager;
    private readonly IAssetDiffService _assetDiffService;

    public FilePresentationService(CyRevisionPluginManager pluginManager, IAssetDiffService assetDiffService)
    {
        _pluginManager = pluginManager;
        _assetDiffService = assetDiffService;
    }

    public bool IsBuiltInImage(string path) => BuiltInImageExtensions.Contains(Path.GetExtension(path));

    public bool HasProviderFor(string path)
    {
        if (IsBuiltInImage(path)) return true;
        FileInfo info = new(path);
        FilePreviewRequest request = new(
            Path.GetDirectoryName(path) ?? string.Empty,
            Path.GetFileName(path),
            path,
            info.Exists ? info.Length : 0);
        return Providers().Any(provider => SafeCanPreview(provider, request));
    }

    public async Task<FilePresentationResult?> CreatePreviewAsync(
        FilePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        foreach (IFilePresentationProvider provider in Providers())
        {
            if (!SafeCanPreview(provider, request)) continue;
            FilePresentationResult? result = await provider.CreatePreviewAsync(request, cancellationToken);
            if (result is not null) return result;
        }

        if (!IsBuiltInImage(request.FilePath)) return null;
        return new FilePresentationResult(
            "cyrevision.images",
            FilePresentationKind.Image,
            $"Image · {request.FileSize:N0} bytes · built-in preview",
            ImagePath: request.FilePath);
    }

    public async Task<FilePresentationResult?> CreateDiffAsync(
        FileDiffRequest request,
        string artifactDirectory,
        CancellationToken cancellationToken = default)
    {
        foreach (IFilePresentationProvider provider in Providers())
        {
            if (!SafeCanCompare(provider, request)) continue;
            FilePresentationResult? providerResult = await provider.CreateDiffAsync(request, cancellationToken);
            if (providerResult is not null) return providerResult;
        }

        if (!File.Exists(request.BaselinePath) || !File.Exists(request.CandidatePath)) return null;
        AssetDiffResult result = await _assetDiffService.CompareAsync(
            request.BaselinePath,
            request.CandidatePath,
            artifactDirectory,
            cancellationToken);
        string report = string.Join(Environment.NewLine,
            result.Summary,
            string.Join(Environment.NewLine, result.Metrics.Select(metric => $"{metric.Key}: {metric.Value}")),
            string.Join(Environment.NewLine, result.Details));
        return new FilePresentationResult(
            "cyrevision.diff",
            result.PreviewImagePath is null ? FilePresentationKind.Metadata : FilePresentationKind.Image,
            result.Summary,
            report,
            result.PreviewImagePath,
            result.Metrics);
    }

    private IEnumerable<IFilePresentationProvider> Providers() =>
        _pluginManager.GetExtensions<IFilePresentationProvider>()
            .OrderByDescending(provider => provider.Priority);

    private static bool SafeCanPreview(IFilePresentationProvider provider, FilePreviewRequest request)
    {
        try { return provider.CanPreview(request); }
        catch { return false; }
    }

    private static bool SafeCanCompare(IFilePresentationProvider provider, FileDiffRequest request)
    {
        try { return provider.CanCompare(request); }
        catch { return false; }
    }
}
