using System.Text;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Core.Tests;

public sealed class WorkingTreeTextPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionPreviewFixture", Guid.NewGuid().ToString("N"));
    public WorkingTreeTextPreviewTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task LargeJsonOpensWithAUsefulBoundedPreviewRatherThanASizeRefusal()
    {
        string path = Path.Combine(_root, "large.json");
        await File.WriteAllTextAsync(path, "{\"visible\":true,\"data\":\"" + new string('x', 3 * 1024 * 1024) + "\"}");
        string diff = await WorkingTreeTextPreview.BuildDiffAsync(_root, "large.json", default);
        Assert.Contains("+{\"visible\":true", diff);
        Assert.Contains("[Preview truncated:", diff);
        Assert.True(diff.Length < WorkingTreeTextPreview.MaximumCharacters + 1000);
        Assert.True(new FileInfo(path).Length > 2 * 1024 * 1024);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public async Task IniTextIsDecodedIncludingUnicodeByteOrderMarks(string encodingName)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "DefaultEngine.ini"), "[Engine]\r\nName=Prévisualisation\r\nEnabled=True\r\n", Encoding.GetEncoding(encodingName));
        string diff = await WorkingTreeTextPreview.BuildDiffAsync(_root, "DefaultEngine.ini", default);
        Assert.Contains("@@ -0,0 +1,3 @@", diff);
        Assert.Contains("+Name=Prévisualisation", diff);
        Assert.DoesNotContain("binary", diff);
        Assert.DoesNotContain('\uFEFF', diff);
    }

    [Fact]
    public async Task BinaryFilesAreNotMisrepresentedAsText()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "asset.bin"), [1, 0, 2, 0, 3]);
        Assert.Contains("binary", await WorkingTreeTextPreview.BuildDiffAsync(_root, "asset.bin", default));
    }

    [Fact]
    public async Task ManyLinesRemainBoundedAndTruncationIsNotFakeAddedCode()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "build.log"), string.Concat(Enumerable.Repeat("real line\n", WorkingTreeTextPreview.MaximumLines + 30)));
        string diff = await WorkingTreeTextPreview.BuildDiffAsync(_root, "build.log", default);
        Assert.Equal(WorkingTreeTextPreview.MaximumLines, diff.Replace("\r\n", "\n").Split('\n').Count(line => line == "+real line"));
        Assert.Contains("[Preview truncated:", diff);
        Assert.DoesNotContain("+[Preview truncated", diff);
    }

    [Fact]
    public async Task MissingFilesEscapesAndCancellationAreHandled()
    {
        Assert.Contains("no longer available", await WorkingTreeTextPreview.BuildDiffAsync(_root, "../outside.ini", default));
        Assert.Contains("no longer available", await WorkingTreeTextPreview.BuildDiffAsync(_root, "missing.ini", default));
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkingTreeTextPreview.BuildDiffAsync(_root, "missing.ini", cancellation.Token));
    }
}
