using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class UnrealAssetInspectionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "cyrevision-unreal-inspection-" + Guid.NewGuid().ToString("N"));

    public UnrealAssetInspectionServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task AdvancedInspectionIsDisabledByDefaultAndPersistsPerProject()
    {
        string project = CreateProject("Demo");
        using UnrealAssetInspectionService service = CreateService();

        UnrealAssetInspectionOptions defaults = await service.LoadOptionsAsync(project, CancellationToken.None);
        Assert.False(defaults.Enabled);
        Assert.Equal(512, defaults.PreviewResolution);
        Assert.True(defaults.RenderMeshThumbnails);

        UnrealAssetInspectionOptions configured = new(true, 768, 3L * 1024 * 1024 * 1024, true, 240);
        await service.SaveOptionsAsync(project, configured, CancellationToken.None);

        Assert.Equal(configured, await service.LoadOptionsAsync(project, CancellationToken.None));
    }

    [Fact]
    public async Task CacheIsProjectLocalAndClearNeverTouchesSourceAssets()
    {
        string project = CreateProject("CacheDemo");
        string asset = Path.Combine(project, "Content", "Keep.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllTextAsync(asset, "source asset");
        string cacheEntry = Path.Combine(project, ".cyrevision", "cache", "unreal-assets", "aa", "entry");
        Directory.CreateDirectory(cacheEntry);
        await File.WriteAllTextAsync(Path.Combine(cacheEntry, "inspection.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(cacheEntry, "preview.png"), "preview");
        using UnrealAssetInspectionService service = CreateService();

        UnrealAssetInspectionCacheStatus before = await service.GetCacheStatusAsync(project, CancellationToken.None);
        Assert.Equal(1, before.EntryCount);
        Assert.StartsWith(Path.Combine(project, ".cyrevision"), before.CacheDirectory, StringComparison.OrdinalIgnoreCase);

        UnrealAssetInspectionCacheStatus after = await service.ClearCacheAsync(project, CancellationToken.None);
        Assert.Equal(0, after.EntryCount);
        Assert.True(File.Exists(asset));
        Assert.Equal("source asset", await File.ReadAllTextAsync(asset));
    }

    private UnrealAssetInspectionService CreateService()
    {
        string configuration = Path.Combine(_root, "config");
        UnrealBuildService builds = new(configuration, Path.Combine(_root, "data"));
        return new UnrealAssetInspectionService(configuration, builds, path => new UnrealProjectInspection(
            true,
            path,
            Directory.EnumerateFiles(path, "*.uproject").FirstOrDefault() ?? string.Empty,
            Path.GetFileName(path),
            "5.5",
            "5.5",
            UnrealProjectKind.Cpp,
            UnrealPluginInstallMode.Source,
            true,
            "Compatible",
            UnrealPluginCompatibility.SupportedEngineVersions,
            "Win64",
            [],
            true,
            "0.8.0",
            "0.8.0",
            false,
            "Ready"));
    }

    private string CreateProject(string name)
    {
        string project = Path.Combine(_root, name);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, name + ".uproject"), "{}");
        return project;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
