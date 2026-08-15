using CyRevision.Code;

namespace CyRevision.Core.Tests;

public sealed class CodeWorkspaceServiceTests
{
    [Theory]
    [InlineData("Tools/Build/Compile.bat", "*.bat", true)]
    [InlineData("Source/Reality/Actor.cpp", ".cpp", true)]
    [InlineData("Plugins/Reality/Private/Actor.cpp.generated", "Reality\\*.cpp*", true)]
    [InlineData("Plugins/Other/Private/Actor.cpp", "Reality\\*.cpp*", false)]
    [InlineData("Source/Reality/Actor.h", "*.cpp;*.h", true)]
    public void FilePatternMatcherSupportsSubstringsExtensionsAndGlobs(
        string path,
        string expression,
        bool expected)
    {
        Assert.Equal(expected, CodeFilePatternMatcher.IsMatch(path, expression));
    }

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

        CodeWorkspaceService service = new();
        CodeWorkspaceSnapshot snapshot = await service.BuildTreeAsync(workspace.Path);

        Assert.Equal(0, snapshot.FileCount);
        Assert.Equal("src", Assert.Single(snapshot.Roots).Name);
        Assert.True(snapshot.Roots[0].HasUnloadedChildren);

        IReadOnlyList<CodeTreeNode> children = await service.LoadDirectoryAsync(workspace.Path, "src");

        Assert.Equal("Feature.cs", Assert.Single(children).Name);
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
    public async Task FileIndexIncludesNestedFilesWithoutLoadingTheirFolders()
    {
        using TemporaryDirectory workspace = new();
        string nested = Path.Combine(workspace.Path, "Source", "UI", "Widgets");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "UI_InventoryPanel.cpp"), "void Draw() {}\n");

        CodeFileIndex index = await new CodeWorkspaceService().BuildFileIndexAsync(workspace.Path);

        CodeFileEntry file = Assert.Single(index.Files);
        Assert.Equal("Source/UI/Widgets/UI_InventoryPanel.cpp", file.RelativePath);
        Assert.Equal("C/C++", file.Language);
    }

    [Fact]
    public async Task LargeTextPreviewIsBoundedAndMarkedAsTruncated()
    {
        using TemporaryDirectory workspace = new();
        string path = Path.Combine(workspace.Path, "Generated.cpp");
        await File.WriteAllTextAsync(path, new string('x', 2 * 1024 * 1024));

        CodeFilePreview preview = await new CodeWorkspaceService()
            .ReadPreviewAsync(workspace.Path, "Generated.cpp");

        Assert.False(preview.IsBinary);
        Assert.True(preview.WasTruncated);
        Assert.Equal(256 * 1024, preview.Text.Length);
        Assert.Equal(2 * 1024 * 1024, preview.Size);
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
