using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class UnrealMetadataSemanticDiffTests
{
    [Fact]
    public void ReportsMeshMetadataChanges()
    {
        UnrealMetadataSemanticDiffResult? result = UnrealMetadataSemanticDiff.Compare(
            """{"assetKind":"Static mesh","lodCount":2,"materialSlots":3,"thumbnailWritten":false}""",
            """{"assetKind":"Static mesh","lodCount":4,"materialSlots":3,"thumbnailWritten":true}""",
            "Content/SM_Demo.uasset");

        Assert.NotNull(result);
        Assert.Contains("lod count", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1", result.Metadata["Semantic changes"]);
    }

    [Fact]
    public void ReportsTextureMetadataChanges()
    {
        UnrealMetadataSemanticDiffResult? result = UnrealMetadataSemanticDiff.Compare(
            """{"assetKind":"Texture","width":1024,"height":1024}""",
            """{"assetKind":"Texture","width":2048,"height":1024}""",
            "Content/T_Demo.uasset");

        Assert.NotNull(result);
        Assert.Contains("width", result.Text);
    }

    [Fact]
    public void DoesNotClaimBlueprintOrGenericAssets()
    {
        Assert.Null(UnrealMetadataSemanticDiff.Compare(
            """{"assetKind":"Blueprint"}""",
            """{"assetKind":"Blueprint"}""",
            "Content/BP_Demo.uasset"));
        Assert.Null(UnrealMetadataSemanticDiff.Compare(
            """{"assetKind":"Unreal asset"}""",
            """{"assetKind":"Unreal asset"}""",
            "Content/A_Demo.uasset"));
    }
}
