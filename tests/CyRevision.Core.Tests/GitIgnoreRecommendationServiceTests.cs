using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitIgnoreRecommendationServiceTests
{
    [Fact]
    public void Build_DetectsUnrealAndKeepsProjectSourcesTrackable()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Game.uproject"), "{}");

            GitIgnoreRecommendation result = GitIgnoreRecommendationService.Build(root);

            Assert.Contains("Unreal Engine", result.DetectedProjectTypes);
            Assert.Contains("Binaries/", result.Content);
            Assert.Contains("DerivedDataCache/", result.Content);
            Assert.Contains(".cyrevision/", result.Content);
            Assert.DoesNotContain("\nContent/\n", result.Content);
            Assert.DoesNotContain("\nConfig/\n", result.Content);
            Assert.DoesNotContain("\nSource/\n", result.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_CombinesDetectedStacksWithoutDuplicateRules()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);
            File.WriteAllText(Path.Combine(root, "package.json"), "{}");

            GitIgnoreRecommendation result = GitIgnoreRecommendationService.Build(root);

            Assert.Contains(".NET", result.DetectedProjectTypes);
            Assert.Contains("Node.js", result.DetectedProjectTypes);
            Assert.Contains("node_modules/", result.Content);
            Assert.Equal(1, result.Content.Split('\n').Count(line => line == "*.suo"));
            Assert.EndsWith("\n", result.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_ProvidesSafeGenericRulesWhenNoFrameworkIsDetected()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            GitIgnoreRecommendation result = GitIgnoreRecommendationService.Build(root);

            Assert.Empty(result.DetectedProjectTypes);
            Assert.Equal("Generic project", result.DetectionSummary);
            Assert.Contains(".cyrevision/", result.Content);
            Assert.Contains(".idea/", result.Content);
            Assert.Contains("Thumbs.db", result.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "CyRevisionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
