using System.Text.Json;
using CyRevision.Desktop.Native;

namespace CyRevision.Core.Tests;

public sealed class NativeViewRequestTests
{
    [Fact]
    public void DetachedViewsDoNotReplaceTheMainWorkspace()
    {
        using var request=JsonDocument.Parse("{\"workspaces\":[\"history\",\"ci\",\"history\",\"overview\"]}");
        Assert.Equal(new[]{"overview","history","ci"},NativeViewRequests.Workspaces(request.RootElement,NativeWorkspaceCatalog.Find("overview")).Select(view=>view.Id));
    }
    [Fact]
    public void SimultaneousTablesKeepDifferentViewKeysForTheSameSource()
    {
        using var one=JsonDocument.Parse("{\"source\":\"History\",\"key\":\"main:History\"}");
        using var two=JsonDocument.Parse("{\"source\":\"History\",\"key\":\"view-1:History\"}");
        Assert.Equal("main:History",NativeViewRequests.TableKey(one.RootElement,"History"));
        Assert.Equal("view-1:History",NativeViewRequests.TableKey(two.RootElement,"History"));
    }
    [Fact]
    public void OlderRequestsRetainTheirSourceKey()
    {
        using var request=JsonDocument.Parse("{}");
        Assert.Equal("History",NativeViewRequests.TableKey(request.RootElement,"History"));
    }
    [Theory]
    [InlineData("{\"key\":12}")]
    [InlineData("{\"key\":\"\"}")]
    [InlineData("{\"key\":\"bad\\nkey\"}")]
    public void MalformedViewKeysAreRejected(string json)
    {
        using var request=JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(()=>NativeViewRequests.TableKey(request.RootElement,"History"));
    }
    [Fact]
    public void UnknownWorkspacesAreNotLoadedFromSavedLayouts()
    {
        using var request=JsonDocument.Parse("{\"workspaces\":[\"unknown\"]}");
        Assert.Throws<ArgumentException>(()=>NativeViewRequests.Workspaces(request.RootElement,NativeWorkspaceCatalog.Find("overview")));
    }
}
