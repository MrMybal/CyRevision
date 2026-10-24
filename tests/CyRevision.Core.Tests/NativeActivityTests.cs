using CyRevision.Desktop.Native;

namespace CyRevision.Core.Tests;

public sealed class NativeActivityTests
{
    [Theory]
    [InlineData("IsExplorerLoading", "history")]
    [InlineData("IsExplorerDiffLoading", "history")]
    [InlineData("IsCiLoading", "ci")]
    [InlineData("IsPullRequestLoading", "pullrequests")]
    [InlineData("IsCodeWorkspaceLoading", "code")]
    [InlineData("IsProjectLoading", "project")]
    [InlineData("IsIgnoreSuggestionLoading", "ignore")]
    [InlineData("IsWorkingTreeDiffLoading", "changes")]
    public void TransientLoadersAreVisibleInTheirOwnWorkspace(string name,string workspace)
    {
        Assert.True(NativeActivity.IsLoadingFlag(name));
        Assert.Equal(workspace,NativeActivity.WorkspaceFor(name));
    }
    [Theory]
    [InlineData("IsSyncRunning")]
    [InlineData("IsVpnRunning")]
    [InlineData("IsTeamChatHostRunning")]
    [InlineData("DiscordIsRunning")]
    [InlineData("IsBusy")]
    public void ServicesAreNotMistakenForTransientLoads(string name)=>Assert.False(NativeActivity.IsLoadingFlag(name));

    [Theory]
    [InlineData("CommitAsync")]
    [InlineData("DeleteSelectedChangesAsync")]
    [InlineData("PushAsync")]
    [InlineData("RollbackSelectedAsync")]
    public void MutationsWaitForTheProjectScan(string action)=>Assert.False(NativeActivity.CanRunDuringProjectScan(action));
    [Fact]
    public void SelectionDoesNotTakeOrReleaseTheExclusiveOperationGate()
    {
        Assert.True(NativeActivity.IsInteractive("select"));
        Assert.True(NativeActivity.IsInteractive("navigation"));
        Assert.False(NativeActivity.IsInteractive("invoke"));
        Assert.False(NativeActivity.IsInteractive("set"));
        Assert.True(NativeActivity.CanRunDuringProjectScan("RefreshLfsLocksAsync"));
    }
}
