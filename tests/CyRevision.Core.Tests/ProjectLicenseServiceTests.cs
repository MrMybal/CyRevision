using CyRevision.Core.Projects;

namespace CyRevision.Core.Tests;

public sealed class ProjectLicenseServiceTests
{
    [Fact]
    public async Task InspectDetectsExistingMitLicense()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            ProjectLicenseService service = new();
            string content = service.RenderTemplate("MIT", "CyRevision Team", 2026, "Sample");
            await File.WriteAllTextAsync(Path.Combine(root, "LICENSE.md"), content);

            ProjectLicenseSnapshot snapshot = await service.InspectAsync(root);

            Assert.True(snapshot.Exists);
            Assert.Equal("LICENSE.md", snapshot.FileName);
            Assert.Equal("MIT", snapshot.DetectedTemplateId);
            Assert.Contains("CyRevision Team", snapshot.Content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveRequiresExplicitOverwriteAndKeepsFileInProjectRoot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            ProjectLicenseService service = new();
            await service.SaveAsync(root, "LICENSE", "first", overwrite: false);

            await Assert.ThrowsAsync<IOException>(() =>
                service.SaveAsync(root, "LICENSE", "second", overwrite: false));
            await service.SaveAsync(root, "LICENSE", "second", overwrite: true);

            Assert.Equal("second" + Environment.NewLine, await File.ReadAllTextAsync(Path.Combine(root, "LICENSE")));
            Assert.Throws<InvalidOperationException>(() => ProjectLicenseService.ValidateFileName("../LICENSE"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "cyrevision-license-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
