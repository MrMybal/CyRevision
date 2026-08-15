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
            AccentColor: "#4C9BE8");

        await catalog.UpsertAsync(project);
        ProjectDefinition? restored = await catalog.FindByIdAsync(project.Id);

        Assert.NotNull(restored);
        Assert.Equal(project.Name, restored.Name);
        Assert.True(restored.Features.GitEnabled);
        Assert.Equal(RetentionMode.Permanent, restored.Retention.Mode);
        Assert.Equal(3, restored.SidebarOrder);
        Assert.Equal("#4C9BE8", restored.AccentColor);
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

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
