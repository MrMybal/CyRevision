using CyRevision.Diff;

namespace CyRevision.Core.Tests;

public sealed class CodeDiffParserTests
{
    private readonly CodeDiffParser _parser = new();

    [Fact]
    public void Parse_AssignsKindsAndLineNumbersInsideHunk()
    {
        const string diff = """
            diff --git a/Test.cs b/Test.cs
            index 1111111..2222222 100644
            --- a/Test.cs
            +++ b/Test.cs
            @@ -10,3 +10,4 @@ public class Test
             unchanged
            -old value
            +new value
            +another value
             ending
            """;

        ParsedCodeDiff parsed = _parser.Parse(diff);

        Assert.Equal(2, parsed.AddedLineCount);
        Assert.Equal(1, parsed.RemovedLineCount);
        Assert.Equal(1, parsed.HunkCount);
        Assert.Equal(CodeDiffLineKind.FileHeader, parsed.Lines[2].Kind);
        Assert.Equal(CodeDiffLineKind.FileHeader, parsed.Lines[3].Kind);

        CodeDiffLine context = Assert.Single(parsed.Lines.Where(line => line.Content == "unchanged"));
        Assert.Equal(10, context.OldLineNumber);
        Assert.Equal(10, context.NewLineNumber);

        CodeDiffLine removed = Assert.Single(parsed.Lines.Where(line => line.Content == "old value"));
        Assert.Equal(11, removed.OldLineNumber);
        Assert.Null(removed.NewLineNumber);

        CodeDiffLine secondAddition = Assert.Single(parsed.Lines.Where(line => line.Content == "another value"));
        Assert.Null(secondAddition.OldLineNumber);
        Assert.Equal(12, secondAddition.NewLineNumber);
    }

    [Fact]
    public void Parse_DoesNotTreatFileMarkersAsChanges()
    {
        const string diff = "--- a/readme.md\n+++ b/readme.md\n@@ -1 +1 @@\n-old\n+new\n";

        ParsedCodeDiff parsed = _parser.Parse(diff);

        Assert.Equal(CodeDiffLineKind.FileHeader, parsed.Lines[0].Kind);
        Assert.Equal(CodeDiffLineKind.FileHeader, parsed.Lines[1].Kind);
        Assert.Equal(1, parsed.RemovedLineCount);
        Assert.Equal(1, parsed.AddedLineCount);
    }

    [Fact]
    public void Parse_BuildsPairedRowsForSideBySideView()
    {
        const string diff = "@@ -3,3 +3,3 @@\n context\n-old one\n-old two\n+new one\n context after";

        ParsedCodeDiff parsed = _parser.Parse(diff);
        CodeDiffSplitRow[] contentRows = parsed.SplitRows.Where(row => row.FullWidth is null).ToArray();

        Assert.Same(contentRows[0].Left, contentRows[0].Right);
        Assert.Equal("old one", contentRows[1].Left?.Content);
        Assert.Equal("new one", contentRows[1].Right?.Content);
        Assert.Equal("old two", contentRows[2].Left?.Content);
        Assert.Null(contentRows[2].Right);
        Assert.Same(contentRows[3].Left, contentRows[3].Right);
    }

    [Fact]
    public void Parse_HandlesAdditionOnlyBlocksAndEmptyInput()
    {
        ParsedCodeDiff addition = _parser.Parse("@@ -0,0 +1,2 @@\n+first\n+second");
        ParsedCodeDiff empty = _parser.Parse(string.Empty);

        Assert.Equal(2, addition.AddedLineCount);
        Assert.Equal(2, addition.SplitRows.Count(row => row.Right?.Kind == CodeDiffLineKind.Added));
        Assert.Empty(empty.Lines);
        Assert.Empty(empty.SplitRows);
    }
}
