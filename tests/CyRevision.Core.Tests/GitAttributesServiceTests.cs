using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitAttributesServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "CyRevision-GitAttributesTests", Guid.NewGuid().ToString("N"));

    public GitAttributesServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MergeLfsPatterns_PreservesExistingRulesAndAddsOnlyMissingPatterns()
    {
        string path = Path.Combine(_root, ".gitattributes");
        await File.WriteAllTextAsync(path,
            "*.cs text eol=lf\n*.uasset filter=lfs diff=lfs merge=lfs -text\n");
        GitAttributesService service = new();

        GitAttributesMergeResult result = await service.MergeLfsPatternsAsync(
            _root, ["*.uasset", "*.umap"]);

        string content = await File.ReadAllTextAsync(path);
        Assert.Equal(["*.umap"], result.AddedPatterns);
        Assert.Contains("*.cs text eol=lf", content);
        Assert.Equal(1, Count(content, "*.uasset filter=lfs"));
        Assert.Contains("*.umap filter=lfs diff=lfs merge=lfs -text", content);
    }

    [Fact]
    public async Task MergeLfsPatterns_NoMissingPattern_DoesNotRewriteFile()
    {
        string path = Path.Combine(_root, ".gitattributes");
        const string original = "# custom\n*.bin filter=lfs diff=lfs merge=lfs -text\n";
        await File.WriteAllTextAsync(path, original);
        DateTime before = File.GetLastWriteTimeUtc(path);
        GitAttributesService service = new();

        GitAttributesMergeResult result = await service.MergeLfsPatternsAsync(_root, ["*.bin"]);

        Assert.False(result.Changed);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void NormalizePattern_RejectsWhitespaceAndComments()
    {
        Assert.Throws<ArgumentException>(() => GitAttributesService.NormalizePattern("folder/my file.bin"));
        Assert.Throws<ArgumentException>(() => GitAttributesService.NormalizePattern("# invalid"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static int Count(string value, string needle)
    {
        int count = 0;
        for (int index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }
}
