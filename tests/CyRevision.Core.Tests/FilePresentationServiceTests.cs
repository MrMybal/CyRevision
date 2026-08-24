using CyRevision.Desktop.Plugins;
using CyRevision.Diff;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Unreal;
using SkiaSharp;

namespace CyRevision.Core.Tests;

public sealed class FilePresentationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-present-" + Guid.NewGuid().ToString("N"));

    public FilePresentationServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CreatesBuiltInPngPreview()
    {
        string path = Path.Combine(_root, "preview.png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        await using CyRevisionPluginManager manager = CreateManager();
        FilePresentationService service = new(manager, new AssetDiffService());

        FilePresentationResult? result = await service.CreatePreviewAsync(
            new FilePreviewRequest(_root, "preview.png", path, new FileInfo(path).Length));

        Assert.NotNull(result);
        Assert.Equal(FilePresentationKind.Image, result!.Kind);
        Assert.Equal(path, result.ImagePath);
        Assert.Equal("cyrevision.images", result.ProviderId);
    }

    [Fact]
    public async Task BuiltInImageDiffExposesBothSidesAndHeatmap()
    {
        string baseline = Path.Combine(_root, "baseline.png");
        string candidate = Path.Combine(_root, "candidate.png");
        WriteTexture(baseline, SKColors.Black);
        WriteTexture(candidate, SKColors.Red);
        await using CyRevisionPluginManager manager = CreateManager();
        FilePresentationService service = new(manager, new AssetDiffService());

        FilePresentationResult? result = await service.CreateDiffAsync(
            new FileDiffRequest(_root, "preview.png", baseline, candidate),
            Path.Combine(_root, "artifacts"));

        Assert.NotNull(result);
        Assert.Equal(FilePresentationKind.Image, result!.Kind);
        Assert.Equal(baseline, result.BaselineImagePath);
        Assert.Equal(candidate, result.CandidateImagePath);
        Assert.NotNull(result.DifferenceImagePath);
        Assert.True(File.Exists(result.DifferenceImagePath));
    }
    [Fact]
    public async Task UnrealPluginAddsOfflinePackagePreview()
    {
        string path = Path.Combine(_root, "Example.uasset");
        await File.WriteAllBytesAsync(path, "UE4Package ExampleMaterial Texture2D /Game/Test"u8.ToArray());
        UnrealIntegrationPlugin plugin = new();
        FilePreviewRequest request = new(_root, "Example.uasset", path, new FileInfo(path).Length);

        FilePresentationResult? result = await plugin.CreatePreviewAsync(request);

        Assert.True(plugin.CanPreview(request));
        Assert.NotNull(result);
        Assert.Equal(FilePresentationKind.Metadata, result!.Kind);
        Assert.Contains("Unreal package preview", result.TextContent);
        Assert.Contains("ExampleMaterial", result.TextContent);
        await plugin.DisposeAsync();
    }

    private static void WriteTexture(string path, SKColor color)
    {
        using SKBitmap bitmap = new(2, 2);
        bitmap.Erase(color);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        encoded.SaveTo(stream);
    }
    private CyRevisionPluginManager CreateManager() => new(
        _root,
        Path.Combine(_root, "config"),
        Path.Combine(_root, "data"),
        "test");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
