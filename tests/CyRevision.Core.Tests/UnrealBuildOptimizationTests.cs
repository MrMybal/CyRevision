using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class UnrealBuildOptimizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-unreal-build-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiscoveryIsRestoredFromDisposableProjectCache()
    {
        string project = Path.Combine(_root, "Sample");
        Directory.CreateDirectory(project);
        string projectFile = Path.Combine(project, "Sample.uproject");
        await File.WriteAllTextAsync(projectFile, "{\"FileVersion\":3,\"EngineAssociation\":\"5.5\"}");
        UnrealBuildService service = new(Path.Combine(_root, "config"), Path.Combine(_root, "data"));

        UnrealBuildDiscovery first = await service.DiscoverAsync(projectFile, CancellationToken.None);
        UnrealBuildDiscovery second = await service.DiscoverAsync(projectFile, CancellationToken.None);

        Assert.False(first.IsCached);
        Assert.True(second.IsCached);
        Assert.True(File.Exists(Path.Combine(project, ".cyrevision", "cache", "unreal", "build-discovery.json")));
    }

    [Fact]
    public async Task BuildPresetsRoundTripByName()
    {
        Guid projectId = Guid.NewGuid();
        UnrealBuildService service = new(Path.Combine(_root, "config"), Path.Combine(_root, "data"));
        UnrealBuildProfile profile = new(projectId, "D:/UE_5.5", "GameEditor", UnrealBuildPlatform.Win64,
            UnrealBuildConfiguration.Development, string.Empty, string.Empty, string.Empty, string.Empty,
            Path.Combine(_root, "output"), false, true, 60, DateTimeOffset.UtcNow, "Windows editor", 2);

        await service.SavePresetAsync(profile, CancellationToken.None);
        UnrealBuildProfile loaded = Assert.Single(await service.LoadPresetsAsync(projectId, CancellationToken.None));
        await service.DeletePresetAsync(projectId, profile.PresetName, CancellationToken.None);

        Assert.Equal("Windows editor", loaded.PresetName);
        Assert.Equal(2, loaded.MaximumParallelBuilds);
        Assert.Empty(await service.LoadPresetsAsync(projectId, CancellationToken.None));
    }

    [Theory]
    [InlineData("Source/File.cpp(42,7): error C2143: syntax error", UnrealBuildDiagnosticSeverity.Error, "C2143", 42)]
    [InlineData("Source/File.cpp:18:3: warning: unused variable", UnrealBuildDiagnosticSeverity.Warning, "", 18)]
    [InlineData("AutomationTool exiting with ExitCode=6 (6)", UnrealBuildDiagnosticSeverity.Error, "UAT", null)]
    public void CompilerOutputProducesStructuredDiagnostics(
        string line,
        UnrealBuildDiagnosticSeverity severity,
        string code,
        int? lineNumber)
    {
        UnrealBuildDiagnostic diagnostic = Assert.IsType<UnrealBuildDiagnostic>(UnrealBuildService.ParseDiagnostic(line));

        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(lineNumber, diagnostic.Line);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
