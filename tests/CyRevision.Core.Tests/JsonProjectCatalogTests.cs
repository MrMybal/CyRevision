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
            CreatedAt: DateTimeOffset.UtcNow);

        await catalog.UpsertAsync(project);
        ProjectDefinition? restored = await catalog.FindByIdAsync(project.Id);

        Assert.NotNull(restored);
        Assert.Equal(project.Name, restored.Name);
        Assert.True(restored.Features.GitEnabled);
        Assert.Equal(RetentionMode.Permanent, restored.Retention.Mode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}

