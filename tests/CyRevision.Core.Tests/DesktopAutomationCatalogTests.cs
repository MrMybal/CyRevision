using CyRevision.Automation;
using CyRevision.Desktop.Native;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Core.Tests;

public sealed class DesktopAutomationCatalogTests
{
    [Fact]
    public void EveryExistingPublicUiActionIsExplicitlyClassified()
    {
        var exposed=DesktopAutomationCatalog.Actions.Select(action=>action.Method).ToArray();
        Assert.Equal(exposed.Length,exposed.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(exposed.Except(NativeModelAdapter.AutomationMethods.Keys));
        Assert.Empty(NativeModelAdapter.AutomationMethods.Keys.Except(exposed).Except(DesktopAutomationCatalog.Excluded.Keys));
        Assert.Empty(exposed.Intersect(DesktopAutomationCatalog.Excluded.Keys));
        Assert.All(DesktopAutomationCatalog.Excluded.Values,reason=>Assert.True(reason.Length>30));
    }
    [Theory]
    [InlineData("pullrequests","MergeSelectedPullRequestAsync",AutomationCapability.Publication)]
    [InlineData("sync","StartSyncAsync",AutomationCapability.Services|AutomationCapability.Publication)]
    [InlineData("vpn","ApplyVpnFirewallAsync",AutomationCapability.Services|AutomationCapability.Administration)]
    [InlineData("plugins","EnableSelectedPluginAsync",AutomationCapability.Administration)]
    [InlineData("branches","RemoveLocalBranchAsync",AutomationCapability.Destructive)]
    [InlineData("builds","StartRemoteBuildAsync",AutomationCapability.Builds|AutomationCapability.Publication)]
    public void HighImpactActionsRequireSeparateCapabilities(string module,string action,AutomationCapability required)
        =>Assert.Equal(required,DesktopAutomationCatalog.Capabilities(module,action)&required);
    [Theory]
    [InlineData("AiSdkAllowWrite")][InlineData("AiMcpEnabled")][InlineData("AiMcpEmergencyBlocked")]
    [InlineData("PullRequestToken")][InlineData("VpnExchangeText")][InlineData("PeerExchangeText")][InlineData("RepositoryConsoleCommand")]
    [InlineData("SwarmAgentPath")][InlineData("SwarmCoordinatorPath")][InlineData("LoreCliPath")][InlineData("PerforceCliPath")]
    public void FieldsCannotGrantTheAssistantItsOwnAuthorityOrExposeSecrets(string name)
    {
        var property=typeof(MainWindowViewModel).GetProperty(name);
        Assert.NotNull(property);Assert.False(DesktopAutomationCatalog.IsAllowedField(property));
    }
    [Fact]
    public void AllGrantsAreOffUntilTheHostEnablesThem()
    {
        var permissions=new AutomationPermissions();Assert.Equal(AutomationCapability.None,permissions.Capabilities);
        Assert.False(permissions.Write);Assert.False(permissions.Network);Assert.False(permissions.Stage);Assert.False(permissions.Commit);
    }
    [Theory]
    [InlineData("SelectedPeerRole")][InlineData("SelectedVpnCapability")]
    public void SafeScalarSelectionsRemainEditableForTheExistingWorkflow(string name)
    {
        var property=typeof(MainWindowViewModel).GetProperty(name)!;
        Assert.True(DesktopAutomationCatalog.IsAllowedField(property));
        Assert.True(property.SetMethod?.IsPublic);
    }
    [Theory]
    [InlineData("NewPullRequestTitle","pullrequests")]
    [InlineData("NewPullRequestBody","pullrequests")]
    [InlineData("SelectedPeerRole","members")]
    [InlineData("SelectedVpnCapability","vpn")]
    public void EditableDraftsBelongToTheirActualWorkspace(string field,string workspace)
        =>Assert.Equal(workspace,NativeWorkspaceCatalog.GroupFor(field));
    [Theory]
    [InlineData("StartSyncAsync")][InlineData("DispatchSelectedCiWorkflowAsync")][InlineData("MergeSelectedPullRequestAsync")]
    [InlineData("SaveSyncthingSettingsAsync")][InlineData("SendTeamChatMessageAsync")][InlineData("ApplyPerforceSyncAsync")]
    [InlineData("CheckoutSelectedBranchAsync")][InlineData("CheckoutSelectedPullRequestAsync")][InlineData("ScanSyncthingFolderAsync")][InlineData("ScanSelectedSharedSyncFolderAsync")]
    public void RealMutationsAreNeverAdvertisedAsReadOnly(string method)=>Assert.False(DesktopAutomationCatalog.ReadOnly(method));
}
