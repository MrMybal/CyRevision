using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;

namespace CyRevision.Core.Tests;

public sealed class JsonProjectCatalogTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"cyrevision-catalog-{Guid.NewGuid():N}");

    [Fact]
    public async Task CatalogRoundTripsAProject()
    {
        string catalogPath = Path.Combine(_temporaryDirectory, "projects.json");
        using JsonProjectCatalog catalog = new(catalogPath);
        ProjectDefinition project = new(
            Guid.NewGuid(),
            "Example",
            Path.Combine(_temporaryDirectory, "Example"),
            new ProjectFeatures(true, true, false, true, false),
            RetentionPolicy.KeepForever,
            CreatedAt: DateTimeOffset.UtcNow,
            SidebarOrder: 3,
            AccentColor: "#4C9BE8",
            EnabledPluginIds: ["cyrevision.ai", "cyrevision.unreal"],
            OperatingMode: ProjectPresetKind.GitWithPeerSync,
            BackupArchiveProfile: "safe",
            RemoveArchivedGitBranches: false,
            RemoveArchivedHotBackups: false,
            GitArchiveProfile: "balanced");

        await catalog.UpsertAsync(project);
        ProjectDefinition? restored = await catalog.FindByIdAsync(project.Id);

        Assert.NotNull(restored);
        Assert.Equal(project.Name, restored.Name);
        Assert.True(restored.Features.GitEnabled);
        Assert.Equal(RetentionMode.Permanent, restored.Retention.Mode);
        Assert.Equal(3, restored.SidebarOrder);
        Assert.Equal("#4C9BE8", restored.AccentColor);
        Assert.Equal(["cyrevision.ai", "cyrevision.unreal"], restored.EnabledPluginIds);
        Assert.Equal(ProjectPresetKind.GitWithPeerSync, restored.OperatingMode);
        Assert.Equal("safe", restored.BackupArchiveProfile);
        Assert.False(restored.RemoveArchivedGitBranches);
        Assert.False(restored.RemoveArchivedHotBackups);
        Assert.Equal("balanced", restored.GitArchiveProfile);
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#12345Z")]
    public void ProjectRejectsInvalidAccentColors(string color)
    {
        ProjectDefinition project = new(
            Guid.NewGuid(),
            "Example",
            Path.Combine(_temporaryDirectory, "Example"),
            new ProjectFeatures(true, false, false, false, false),
            RetentionPolicy.CurrentStateOnly,
            AccentColor: color);

        Assert.Throws<InvalidOperationException>(project.Validate);
    }

    [Fact]
    public async Task CatalogRoundTripsAPluginOwnedOperatingMode()
    {
        string catalogPath = Path.Combine(_temporaryDirectory, "plugin-mode-projects.json");
        using JsonProjectCatalog catalog = new(catalogPath);
        ProjectDefinition project = new(
            Guid.NewGuid(),
            "Lore Example",
            Path.Combine(_temporaryDirectory, "LoreExample"),
            new ProjectFeatures(false, false, false, true, false),
            new RetentionPolicy(RetentionMode.Timeline, 60, TimeSpan.FromDays(180)),
            EnabledPluginIds: ["cyrevision.lore"],
            OperatingMode: ProjectPresetKind.Custom,
            PluginOperatingModeId: "lore",
            PluginOperatingModeProviderId: "cyrevision.lore");

        await catalog.UpsertAsync(project);
        ProjectDefinition? restored = await catalog.FindByIdAsync(project.Id);

        Assert.NotNull(restored);
        Assert.Equal("lore", restored.PluginOperatingModeId);
        Assert.Equal("cyrevision.lore", restored.PluginOperatingModeProviderId);
        Assert.Contains("cyrevision.lore", restored.EnabledPluginIds!);
    }

    [Fact]
    public void ProjectRejectsAPluginModeWhoseProviderIsNotEnabled()
    {
        ProjectDefinition project = new(
            Guid.NewGuid(),
            "Invalid plugin mode",
            Path.Combine(_temporaryDirectory, "InvalidPluginMode"),
            new ProjectFeatures(false, false, false, true, false),
            RetentionPolicy.KeepForever,
            EnabledPluginIds: ["cyrevision.ai"],
            OperatingMode: ProjectPresetKind.Custom,
            PluginOperatingModeId: "lore",
            PluginOperatingModeProviderId: "cyrevision.lore");

        Assert.Throws<InvalidOperationException>(project.Validate);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
