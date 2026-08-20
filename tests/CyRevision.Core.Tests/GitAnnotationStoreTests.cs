using CyRevision.Desktop.Workspace;

namespace CyRevision.Core.Tests;

public sealed class GitAnnotationStoreTests
{
    [Fact]
    public async Task StoresAnnotationsOutsideRepositoryAndScopesThemByProject()
    {
        string configuration = CreateTemporaryDirectory();
        try
        {
            GitAnnotationStore store = new(configuration);
            Guid firstProject = Guid.NewGuid();
            Guid secondProject = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            GitAnnotation first = new(
                Guid.NewGuid(), firstProject, GitAnnotationTargetKind.Branch, "feature/render",
                "Rendering test", "Keep until QA validates it.", "qa,render", now, now);
            GitAnnotation second = new(
                Guid.NewGuid(), secondProject, GitAnnotationTargetKind.Commit, "1234567",
                "Other project", "Must not leak across projects.", "private", now, now);

            await store.SaveAsync(first);
            await store.SaveAsync(second);

            GitAnnotation loaded = Assert.Single(await store.LoadAsync(firstProject));
            Assert.Equal(first.Id, loaded.Id);
            Assert.Equal("feature/render", loaded.Target);
            Assert.True(File.Exists(Path.Combine(configuration, "git-annotations.json")));

            await store.DeleteAsync(first.Id);
            Assert.Empty(await store.LoadAsync(firstProject));
            Assert.Single(await store.LoadAsync(secondProject));
        }
        finally
        {
            Directory.Delete(configuration, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "cyrevision-annotations-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
