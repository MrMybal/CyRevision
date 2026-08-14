using CyRevision.Desktop.Plugins;
using CyRevision.Diff;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Unreal;

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
