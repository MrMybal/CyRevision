using CyRevision.Desktop.Native;
using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class NativeConflictActionsTests
{
    [Fact]
    public void EditingRequiresTheExactSelectedConflictSnapshot()
    {
        GitConflictFile selected = Conflict();
        NativeConflictActions.ValidateSelection(selected, NativeTable.Key(selected), selected.Path);
        Assert.Throws<InvalidOperationException>(() => NativeConflictActions.ValidateSelection(selected, NativeTable.Key(Conflict()), selected.Path));
        Assert.Throws<InvalidOperationException>(() => NativeConflictActions.ValidateSelection(selected, NativeTable.Key(selected), "Another.cpp"));
        Assert.Throws<InvalidOperationException>(() => NativeConflictActions.ValidateSelection(null, "", ""));
    }

    [Fact]
    public void EditorLimitCountsUtf8BytesNotCharacters()
    {
        GitConflictFile selected = Conflict();
        NativeConflictActions.ValidateManualResult(selected, "before", new string('a', 750_000));
        Assert.Throws<ArgumentException>(() => NativeConflictActions.ValidateManualResult(selected, "before", new string('a', 750_001)));
        Assert.Throws<ArgumentException>(() => NativeConflictActions.ValidateManualResult(selected, "before", new string('界', 250_001)));
    }

    [Fact]
    public void LargeOrBinarySourceCannotBeReplacedByTruncatedManualText()
    {
        Assert.Throws<InvalidOperationException>(() => NativeConflictActions.ValidateManualResult(Conflict(), new string('a', 750_001), "truncated"));
        Assert.Throws<InvalidOperationException>(() => NativeConflictActions.ValidateManualResult(Conflict(binary: true), "binary", "text"));
    }

    private static GitConflictFile Conflict(bool binary = false)
    {
        GitConflictVersion version = new(true, "1234567890", 10, binary ? null : "contents", binary, false);
        return new("Source/Example.cpp", version, version, version, binary ? null : "contents", true);
    }
}
