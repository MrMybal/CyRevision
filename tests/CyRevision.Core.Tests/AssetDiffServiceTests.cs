using CyRevision.Diff;
using SkiaSharp;

namespace CyRevision.Core.Tests;

public sealed class AssetDiffServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionDiffTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TextureComparisonProducesHeatmap()
    {
        Directory.CreateDirectory(_root);
        string baseline = Path.Combine(_root, "baseline.png");
        string candidate = Path.Combine(_root, "candidate.png");
        WriteTexture(baseline, SKColors.Black);
        WriteTexture(candidate, SKColors.Red);

        AssetDiffResult result = await new AssetDiffService().CompareAsync(
            baseline,
            candidate,
            Path.Combine(_root, "artifacts"));

        Assert.Equal(AssetDiffKind.Texture, result.Kind);
        Assert.False(result.Equivalent);
        Assert.NotNull(result.PreviewImagePath);
        Assert.True(File.Exists(result.PreviewImagePath));
    }

    [Fact]
    public async Task ObjComparisonReportsTopologyDelta()
    {
        Directory.CreateDirectory(_root);
        string baseline = Path.Combine(_root, "baseline.obj");
        string candidate = Path.Combine(_root, "candidate.obj");
        await File.WriteAllTextAsync(baseline, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        await File.WriteAllTextAsync(candidate, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 2 3\nf 1 3 4\n");

        AssetDiffResult result = await new AssetDiffService().CompareAsync(baseline, candidate, _root);

        Assert.Equal(AssetDiffKind.ObjMesh, result.Kind);
        Assert.Contains(result.Details, detail => detail.Contains("sommets", StringComparison.Ordinal));
        Assert.Contains(result.Details, detail => detail.Contains("faces", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnrealPackageComparisonExtractsOfflineTypeHints()
    {
        Directory.CreateDirectory(_root);
        string baseline = Path.Combine(_root, "baseline.uasset");
        string candidate = Path.Combine(_root, "candidate.uasset");
        byte[] magic = [0xC1, 0x83, 0x2A, 0x9E];
        await File.WriteAllBytesAsync(baseline, magic.Concat("BlueprintGeneratedClass OldVariable"u8.ToArray()).ToArray());
        await File.WriteAllBytesAsync(candidate, magic.Concat("BlueprintGeneratedClass NewVariable Texture2D"u8.ToArray()).ToArray());

        AssetDiffResult result = await new AssetDiffService().CompareAsync(baseline, candidate, _root);

        Assert.Equal(AssetDiffKind.UnrealPackage, result.Kind);
        Assert.False(result.Equivalent);
        Assert.Contains("Blueprint", result.Metrics["Types probables"], StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, true);
    }

    private static void WriteTexture(string path, SKColor changedColor)
    {
        using SKBitmap bitmap = new(2, 2);
        bitmap.Erase(SKColors.Black);
        bitmap.SetPixel(1, 1, changedColor);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }
}
