using CyRevision.Code;

namespace CyRevision.Core.Tests;

public sealed class CodeWorkspaceServiceTests
{
    [Fact]
    public async Task BuildTreeExcludesGeneratedAndGitDirectories()
    {
        using TemporaryDirectory workspace = new();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "Intermediate"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "src", "Feature.cs"), "class Feature {}\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "Intermediate", "Generated.cpp"), "void Generated() {}\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, ".git", "config"), "secret\n");

        CodeWorkspaceSnapshot snapshot = await new CodeWorkspaceService().BuildTreeAsync(workspace.Path);

        Assert.Equal(1, snapshot.FileCount);
        Assert.Equal("src", Assert.Single(snapshot.Roots).Name);
        Assert.Equal("Feature.cs", Assert.Single(snapshot.Roots[0].Children).Name);
    }

    [Fact]
    public async Task SearchReturnsLocationsAndHonorsFilePattern()
    {
        using TemporaryDirectory workspace = new();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "Feature.cs"), "first\nNeedle value\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "Feature.txt"), "Needle ignored\n");

        CodeSearchReport report = await new CodeWorkspaceService().SearchAsync(
            workspace.Path,
            "needle",
            new CodeSearchOptions(FilePatterns: "*.cs"));

        CodeSearchResult result = Assert.Single(report.Results);
        Assert.Equal("Feature.cs", result.RelativePath);
        Assert.Equal(2, result.LineNumber);
        Assert.Equal(1, result.ColumnNumber);
    }

    [Fact]
    public void SelectionOffsetsAreConvertedToInclusiveLines()
    {
        string text = "one\ntwo\nthree\nfour";

        CodeSelection selection = CodeWorkspaceService.SelectionFromOffsets(text, 4, 12);

        Assert.Equal(2, selection.StartLine);
        Assert.Equal(3, selection.EndLine);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrevision-code-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
