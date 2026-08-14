using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncthingRuntimeResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionSyncthingRuntime", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DetectsBundledRuntimeWithoutAProjectOverride()
    {
        string bundledRoot = Path.Combine(_root, "bundled");
        string managedRoot = Path.Combine(_root, "managed");
        SyncthingRuntimeResolver resolver = new(bundledRoot, managedRoot, () => string.Empty);
        string runtimeDirectory = resolver.ResolveRuntimeDirectory(bundledRoot);
        Directory.CreateDirectory(runtimeDirectory);
        string executable = Path.Combine(runtimeDirectory, OperatingSystem.IsWindows() ? "syncthing.exe" : "syncthing");
        File.WriteAllText(executable, "test");

        SyncthingRuntimeInstallation detected = resolver.Detect();

        Assert.True(detected.IsAvailable);
        Assert.Equal(SyncthingRuntimeSource.Bundled, detected.Source);
        Assert.Equal(Path.GetFullPath(executable), detected.ExecutablePath);
    }

    [Fact]
    public void CustomRuntimeTakesPriority()
    {
        Directory.CreateDirectory(_root);
        string executable = Path.Combine(_root, OperatingSystem.IsWindows() ? "custom.exe" : "custom");
        File.WriteAllText(executable, "test");
        SyncthingRuntimeResolver resolver = new(
            Path.Combine(_root, "bundled"),
            Path.Combine(_root, "managed"),
            () => string.Empty);

        SyncthingRuntimeInstallation detected = resolver.Detect(executable);

        Assert.Equal(SyncthingRuntimeSource.Custom, detected.Source);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
