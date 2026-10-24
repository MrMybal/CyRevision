using System.Text.Json;
using CyRevision.Desktop.Native;
namespace CyRevision.Core.Tests;

public sealed class NativeRequestTargetTests
{
    [Fact]
    public void ProjectTargetIsIndependentOfTransportAndCurrentProjectIds()
    {
        Guid target=Guid.NewGuid();
        using var document=JsonDocument.Parse(JsonSerializer.Serialize(new{id=451,projectId=Guid.NewGuid(),targetProjectId=target}));
        Assert.Equal(target,NativeRequestTargets.Project(document.RootElement));
    }
    [Theory]
    [InlineData("{\"id\":451}")]
    [InlineData("{\"id\":451,\"targetProjectId\":12}")]
    [InlineData("{\"id\":451,\"targetProjectId\":\"invalid\"}")]
    public void ProjectSelectionRejectsAnInvalidTargetWithoutUsingTheTransportId(string json)
    {
        using var document=JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(()=>NativeRequestTargets.Project(document.RootElement));
    }
    [Fact]
    public void WorkspaceTargetIsIndependentOfTheTransportId()
    {
        using var document=JsonDocument.Parse("{\"id\":451,\"workspace\":\"ci\"}");
        Assert.Equal("ci",NativeRequestTargets.Workspace(document.RootElement));
    }
    [Fact]
    public void WorkspaceSelectionDoesNotFallBackToAnUnknownTransportTarget()
    {
        using var document=JsonDocument.Parse("{\"id\":451}");
        Assert.Throws<ArgumentException>(()=>NativeRequestTargets.Workspace(document.RootElement));
    }
}
