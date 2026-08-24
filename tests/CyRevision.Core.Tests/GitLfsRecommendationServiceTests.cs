using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitLfsRecommendationServiceTests
{
    [Fact]
    public void Build_UnrealProject_RecommendsPackagesAndSourceAssets()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Sample.uproject"), "{}");
            GitLfsRecommendation result = GitLfsRecommendationService.Build(root);
            Assert.Contains("Unreal Engine", result.DetectedProjectTypes);
            Assert.Contains(result.Patterns, item => item.Pattern == "*.uasset" && item.IsRecommended);
            Assert.Contains(result.Patterns, item => item.Pattern == "*.umap" && item.IsRecommended);
            Assert.Contains(result.Patterns, item => item.Pattern == "*.fbx" && item.IsRecommended);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_UnityProject_LeavesTextAssetsOutsideLfs()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            GitLfsRecommendation result = GitLfsRecommendationService.Build(root);
            Assert.Contains("Unity", result.DetectedProjectTypes);
            Assert.DoesNotContain(result.Patterns, item => item.Pattern is "*.unity" or "*.prefab" or "*.meta");
            Assert.Contains(result.Patterns, item => item.Pattern == "*.unitypackage" && item.IsRecommended);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildAttributesContent_DeduplicatesPatterns()
    {
        string content = GitLfsRecommendationService.BuildAttributesContent(["*.uasset", "*.UASSET", "*.umap"]);
        Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("*.uasset filter=lfs diff=lfs merge=lfs -text", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "CyRevision-LfsRecommendationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
