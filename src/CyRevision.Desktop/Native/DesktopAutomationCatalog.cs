using System.Reflection;
using System.Text.RegularExpressions;
using CyRevision.Automation;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

/// <summary>Reviewed application actions. New UI methods never acquire AI authority implicitly.</summary>
public static class DesktopAutomationCatalog
{
    public static readonly IReadOnlyDictionary<string,string[]> Modules=new Dictionary<string,string[]>(StringComparer.Ordinal)
    {
        ["overview"]=Names("AddExistingRepositoryAsync AddFolderProjectAsync CloneGitRepositoryAsync ApplySelectedPresetAsync SetProjectServiceAutoStartAsync SetSelectedProjectAccentColorAsync SetSelectedProjectDisplayNameAsync SetSelectedProjectSidebarGroupAsync MoveSelectedProjectAsync RemoveSelectedProjectAsync SetProjectNotificationsEnabledAsync MarkProjectNotificationsRead"),
        ["license"]=Names("RefreshProjectLicenseAsync ImportProjectLicenseDraftAsync ApplySelectedProjectLicenseTemplate SaveProjectLicenseAsync"),
        ["gitoverview"]=Names("RefreshGitOverviewRemotesAsync BeginNewGitRemote SaveGitRemoteAsync RemoveSelectedGitRemoteAsync SaveRemoteAsync"),
        ["changes"]=Names("RefreshAsync ScanAllUntrackedFilesAsync IncludeAllPreparedChanges KeepAllPreparedChanges DeselectVersionedChanges DeselectUnversionedChanges SetSelectedChangeLocalOnlyAsync CompareSelectedChangeToHeadAsync StageAllAsync UnstageAllAsync CommitAsync RollbackIncludedChangesAsync RollbackSelectedChangeAsync FetchAsync PullAsync PushAsync ExchangeGitAsync"),
        ["history"]=Names("SelectExplorerCommitAsync CompareExplorerCommitsAsync ExportSelectedExplorerFileAsync LoadSelectedDiffForExternalAsync"),
        ["composition"]=Names("CompareBranchesForCompositionAsync PreviewCherryPickAsync ApplyCherryPickAsync MoveSelectedCherryPickCommit PreviewMultiRestoreAsync ApplyMultiRestoreAsync SelectAllMultiRestoreFiles SelectAllSourceOnlyCommits"),
        ["branches"]=Names("CreateBranchAsync CreateBranchFromSelectedCommitAsync CheckoutSelectedBranchAsync MergeSelectedBranchAsync MergeCurrentBranchIntoSelectedAsync RemoveLocalBranchAsync AnalyzeSelectedLocalBranchRemovalAsync AnalyzeSelectedLocalBranchRemovalsAsync RefreshHistoricalWorktreesAsync RemoveSelectedHistoricalWorktreeAsync"),
        ["annotations"]=Names("NewGitAnnotation UseSelectedBranchForAnnotation UseSelectedCommitForAnnotation SaveGitAnnotationAsync DeleteSelectedGitAnnotationAsync"),
        ["pullrequests"]=Names("ResolvePullRequestRepositoryAsync RefreshPullRequestsAsync CreatePullRequestAsync MergeSelectedPullRequestAsync SubmitPullRequestReviewAsync AddPullRequestCommentAsync ToggleSelectedPullRequestStateAsync CheckoutSelectedPullRequestAsync CompleteLinkedPullRequestTasksAsync AnalyzeSelectedPullRequestLocalBranchRemovalAsync RemoveSelectedPullRequestLocalBranchAsync RefreshSelectedPullRequestCiAsync DispatchSelectedPullRequestCiWorkflowAsync CancelSelectedPullRequestCiRunAsync RerunSelectedPullRequestCiAsync"),
        ["ci"]=Names("RefreshCiAsync DispatchSelectedCiWorkflowAsync DispatchReleaseCiWorkflowAsync CancelSelectedCiRunAsync RerunFailedCiJobsAsync"),
        ["graphs"]=Names("AnalyzeGitGraphsAsync"),
        ["locks"]=Names("RefreshLfsLocksAsync RefreshAdvisoryReservationsAsync RemoveExpiredAdvisoryReservationsAsync UnlockAllLfsLocksAsync UnlockSelectedLfsLocksAsync"),
        ["ignore"]=Names("LoadGitIgnoreAsync LoadIgnoreSuggestionsAsync RefreshIgnoredFilesAsync TestGitIgnorePathAsync AppendSelectedSuggestionsToGitIgnore ApplyGitIgnoreTemplate SaveGitIgnoreAsync"),
        ["lfs"]=Names("LoadLfsInventoryAsync TrackLfsPatternAsync ExportSelectedLfsVersionAsync RestoreSelectedLfsVersionAsync RequestSelectedLfsVersionFromPeerAsync AnalyzeLfsStorageAsync AnalyzeRepositoryStorageAsync SaveLfsManagementAsync SetLfsExternalStoragePath SetLfsManagementArchivePath RelocateLfsStorageAsync ArchiveLfsCandidatesAsync ExecuteLfsCleanupAsync"),
        ["code"]=Names("EnsureCodeWorkspaceLoadedAsync RefreshCodeWorkspaceAsync LoadCodeSelectionHistoryAsync"),
        ["search"]=Names("SearchCodeAsync"),
        ["assets"]=Names("CompareAssetsAsync SetAssetBaseline SetAssetCandidate UseSelectedAssetAsBaseline UseSelectedAssetAsCandidate SaveUnrealAssetInspectionOptionsAsync RefreshUnrealAssetInspectionCacheAsync ClearUnrealAssetInspectionCacheAsync"),
        ["backups"]=Names("CreateBackupAsync SaveBackupSettingsAsync SetBackupStoreAsync RestoreSelectedBackupAsync ArchiveOldBackupsAsync SetColdArchivePath AnalyzeGitArchiveCandidatesAsync ArchiveSelectedGitBranchAsync RestoreSelectedGitArchiveAsync"),
        ["sync"]=Names("ConfigureSyncthingAutomaticallyAsync SaveSyncthingSettingsAsync StartSyncAsync StopSyncAsync PauseSyncAsync ResumeSyncAsync RefreshSyncthingWorkspaceAsync ScanSyncthingFolderAsync LoadSyncthingIgnoreRulesAsync SaveSyncthingIgnoreRulesAsync UseSyncthingUnrealIgnoreTemplate AppendSelectedSuggestionsToSyncthingIgnore RefreshSyncHistoryAsync FilterSyncHistoryForFileAsync ClearSyncHistoryFileFilter RefreshSyncCommitsAsync CreateSyncCommitAsync AnalyzeSelectedSyncCommitAsync ApplySelectedSyncCommitAsync ResolveSelectedSyncCommitConflict CreateCompressedSyncBackupAsync SetSyncCompressedBackupPath SetSyncSourceFolderPath SetSyncVersionStorePath"),
        ["syncconflicts"]=Names("RefreshSyncConflictsAsync ResolveSelectedSyncConflictAsync RestoreSelectedSyncConflictBackupAsync CleanExpiredSyncConflictBackupsAsync"),
        ["sharedsync"]=Names("SetSharedSyncFolderPath AddOrUpdateSharedSyncFolderAsync RemoveSelectedSharedSyncFolderAsync ScanSelectedSharedSyncFolderAsync"),
        ["members"]=Names("RefreshProjectMembersAsync RevokeSelectedPeerAsync UpdateSelectedPeerRoleAsync"),
        ["vpn"]=Names("ConfigureVpnAsync SaveVpnSettingsAsync StartVpnAsync StopVpnAsync RefreshVpnAsync InspectVpnSetupAsync ApplyVpnFirewallAsync RemoveVpnFirewallAsync TestVpnConnectivityAsync RemoveSelectedVpnPeerAsync RefreshVpnSyncMessagesAsync"),
        ["swarm"]=Names("SaveSwarmAsync DiagnoseSwarmAsync ApplySwarmOptionsAsync ApplySwarmDnsAsync RemoveSwarmDnsAsync LaunchSwarmAgentAsync LaunchSwarmCoordinatorAsync SetSwarmCacheFolder SetSwarmOptionsPath"),
        ["vpnfiles"]=Names("SaveVpnFileExchangeAsync StartVpnFileExchangeAsync StopVpnFileExchangeAsync TestVpnFilePeerAsync RefreshVpnSharedFilesAsync SendVpnFileAsync DownloadVpnSharedFileAsync SetVpnFileInboxPath SetVpnFileSharedFolderPath"),
        ["teamchat"]=Names("SaveTeamChatAsync StartTeamChatHostAsync StopTeamChatHostAsync LoadTeamChatAsync RefreshTeamChatAsync SendTeamChatMessageAsync CreateTeamChatChannelAsync SetTeamChatAttachment PrepareSelectedTeamChatAttachmentAsync SetTeamChatSyncFolder"),
        ["builds"]=Names("SaveRemoteBuildAsync TestRemoteBuildAsync StartRemoteBuildAsync CancelRemoteBuildAsync SetRemoteBuildArtifactDestination"),
        ["discord"]=Names("ConnectDiscordAgentAsync CheckDiscordAgentNowAsync SaveDiscordAgentAsync StartDiscordAgentAsync StopDiscordAgentAsync TestDiscordWebhookAsync RemoveDiscordWebhookAsync LaunchLocalDiscordAgentAsync"),
        ["plugins"]=Names("EnableSelectedPluginAsync DisableSelectedPluginAsync"),
        ["cystore"]=Names("InitializeCyStoreAsync CaptureCyStoreLfsAsync RefreshCyStoreAsync ReconstructSelectedCyStoreVersionAsync VerifySelectedCyStoreVersionAsync"),
        ["unreal"]=Names("SetUnrealProjectPath ConfigureUnrealBridgeAsync"),
        ["unity"]=Names("SetUnityProjectPath ConfigureUnityBridgeAsync"),
        ["godot"]=Names("SetGodotProjectPath ConfigureGodotBridgeAsync"),
        ["lore"]=Names("SetLoreProjectPath DetectLoreCliAsync ReadLoreStatusAsync ScanLoreStatusAsync StageLorePathAsync CommitLoreAsync SyncLoreAsync PushLoreAsync ListLoreBranchesAsync CreateLoreBranchAsync SwitchLoreBranchAsync"),
        ["perforce"]=Names("DetectPerforceCliAsync SavePerforceConfigurationAsync RefreshPerforceAsync OpenPerforcePathForEditAsync PreviewPerforceSyncAsync ApplyPerforceSyncAsync PreviewPerforceReconcileAsync ApplyPerforceReconcileAsync SubmitPerforceAsync RevertSelectedPerforceFileAsync LoadPerforceFileHistoryAsync"),
        ["unrealbuilds"]=Names("DiscoverUnrealBuildEnvironmentAsync ApplySelectedUnrealBuildPreset SaveUnrealBuildPresetAsync DeleteSelectedUnrealBuildPresetAsync RunSelectedUnrealBuildAsync RunUnrealBuildRangeAsync"),
        ["diagnostics"]=Names("RefreshPerformanceDiagnostics ClearPerformanceDiagnostics ClearRecentOperations"),
        ["help"]=Names("CheckForUpdatesAsync")
    };
    public static readonly IReadOnlyDictionary<string,string> Excluded=BuildExcluded();
    public static IEnumerable<(string Workspace,string Method)> Actions=>Modules.SelectMany(module=>module.Value.Select(method=>(module.Key,method)));
    private static string[] Names(string value)=>value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
    private static IReadOnlyDictionary<string,string> BuildExcluded()
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        void Add(string names,string why){foreach(string name in Names(names))result[name]=why;}
        Add("AddAiMcpServer RemoveSelectedAiMcpServer SaveAiMcpProfileAsync UnblockAiMcpAsync EmergencyBlockAiMcpAsync ApproveAiSdkOperation RejectAiSdkOperation ConnectAiSdkAsync ConnectAiSdkRemoteToolsAsync DisconnectAiSdkAsync DiscoverAiSdkProvidersAsync ListAiSdkModelsAsync ListAiSdkOperationsAsync ListAiSdkProvidersAsync RestartAiSdkAsync SaveAiSdkSessionAsync SendAiSdkMessageAsync ShowAiSdkMetricsAsync StartAiSdkMcpServerAsync StopAiSdkMcpServerAsync CancelAiSdkTurn","Host-only assistant permissions, approvals, credentials or transport lifecycle; an assistant cannot approve or grant itself access.");
        Add("CreateAiConversationAsync DeleteSelectedAiConversationAsync SaveAiConversationSettingsAsync SendCodexChatMessageAsync ToggleCodexChatConnectionAsync RunAiAgentAsync GenerateAiCodeSummaryAsync GenerateAiCommitDescriptionAsync GenerateAiPullRequestDraftAsync DetectCodexAsync ClearAiCodeSummary CancelAiChat","Separate assistant execution/session control stays in the host; prevents recursive agents, credential/configuration bypass and nested approvals.");
        Add("CreatePeerInvitationAsync PreparePeerJoinRequestAsync ApprovePeerJoinRequestAsync ImportMembershipGrantAsync CreateVpnInvitationAsync JoinVpnInvitationAsync AcceptVpnResponseAsync PublishVpnExchangeViaSyncAsync LoadSelectedVpnSyncMessageAsync RotateTeamChatTokenAsync RotateVpnFileTokenAsync","Identity enrollment, private invitations, membership grants and credential rotation require the human-owned security workflow; no keys/certificates are exported through AI tools.");
        Add("SetSyncthingExecutableAsync SetSwarmAgentPath SetSwarmCoordinatorPath InstallGodotEditorPluginAsync InstallLoreUnrealCompanionAsync InstallUnityEditorPluginAsync InstallUnrealEditorPluginAsync DownloadAvailableUpdateAsync","Executable installation, executable-path trust and application replacement remain host-owned workflows.");
        Add("RunRepositoryCommandAsync UseSelectedRepositoryCommand StopRepositoryCommand","Arbitrary terminal execution is not a business-operation API.");
        Add("RefreshApplicationLogs ClearApplicationLogView ClearRepositoryConsoleHistory ClearRepositoryConsoleOutput PublishApplicationError ReportFileHistoryIssue","Personal diagnostic logs and internal reporting helpers are not exported to the model.");
        Add("OpenSelectedPullRequestInBrowser OpenVpnRouterAsync DismissActivityCenter","Operating-system browser/window presentation remains in the UI; data/navigation equivalents are exposed separately.");
        Add("CancelCodeSearch CancelCyStoreOperation CancelLfsStorageAnalysis CancelUnrealBuild","Cancellation is forwarded from the owning SDK request to the actual operation, not an unrelated action.");
        Add("EnsureWorkspaceDataLoadedAsync GetGitAttributeLfsPatternsAsync GetGitToolAvailabilityAsync","Internal loaders or redundant helpers; use workspace navigation or the typed Git operations.");
        return result;
    }
    public static bool IsPresentation(string name)=>Regex.IsMatch(name,"Search|Filter|Sort|Layout|Show|Render|DisplayMode|DiffMode",RegexOptions.CultureInvariant);
    public static bool IsSensitive(string name)=>NativeWorkspaceCatalog.IsSecret(name)||Regex.IsMatch(name,"Token|Credential|Certificate|Identity|Invitation|JoinRequest|MembershipGrant|PeerExchangeText|VpnExchangeText|EnvironmentVariables|HttpHeaders|ForwardEnvironment|OAuth|Webhook|ConfigurationPreview|ConfigPreview|RawConfig",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static bool IsAllowedField(PropertyInfo property)=>property.GetMethod is not null&&NativeWorkspaceCatalog.IsScalar(property.PropertyType)&&
        !IsSensitive(property.Name)&&!Regex.IsMatch(property.Name,"^(Ai|Codex|SelectedAi|SelectedCodex)|Executable|Command|Arguments|Private|Secret|RawConfig|ConfigurationText|(?:Cli|Agent|Coordinator)(?:Repository)?Path|ToolchainPath|AndroidSdkPath|AndroidNdkPath|JavaHomePath",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)&&
        !property.Name.EndsWith("Color",StringComparison.Ordinal)&&!property.Name.EndsWith("Brush",StringComparison.Ordinal);
    public static AutomationCapability Capabilities(string module,string method)
    {
        AutomationCapability result=0;
        if(Regex.IsMatch(method,"Push|ExchangeGit|CreatePullRequest|MergeSelectedPullRequest|Submit|Comment|ToggleSelectedPullRequest|Dispatch|Rerun|CancelSelected.*Ci|CompleteLinked|CreateSyncCommit|Send.*(File|Message)|CreateTeamChatChannel|TestDiscordWebhook|SyncLore|StartSync|ResumeSync|ScanSyncthing|ScanSelectedShared|StartRemoteBuild|StartTeamChatHost|StartVpnFileExchange|StartDiscordAgent|ConfigureSyncthing|SetProjectServiceAutoStart"))result|=AutomationCapability.Publication;
        if(Regex.IsMatch(method,"Remove|Delete|Rollback|Restore|Cleanup|CleanExpired|Unlock|ApplyMultiRestore|ApplyCherryPick|ApplyPerforce|ResolveSelected|Revert|Revoke|Relocate|ArchiveOld|Reconstruct|Export"))result|=AutomationCapability.Destructive;
        if(module is "sync" or "sharedsync" or "syncconflicts" or "vpn" or "swarm" or "vpnfiles" or "teamchat" or "discord"||method=="SetProjectServiceAutoStartAsync")result|=AutomationCapability.Services;
        if(module is "overview" or "license" or "gitoverview" or "members" or "vpn" or "swarm" or "plugins"||Regex.IsMatch(method,"^Save|^Configure|^Set.*(Path|Folder|Store)|^Relocate|^Export"))result|=AutomationCapability.Administration;
        if(module is "builds" or "unrealbuilds"||method.StartsWith("Launch",StringComparison.Ordinal))result|=AutomationCapability.Builds;
        return result;
    }
    public static bool ReadOnly(string method)=>method is not ("ScanSyncthingFolderAsync" or "ScanSelectedSharedSyncFolderAsync")&&
        (Regex.IsMatch(method,"^(Refresh|Analyze|Inspect|Diagnose|Preview|Compare|Read|Scan|Search|Load|List|Detect|CheckForUpdates|Discover|Verify|TestGitIgnore)")||method=="EnsureCodeWorkspaceLoadedAsync");
    public static bool Network(string module,string method)=>module is "pullrequests" or "ci" or "members" or "locks" or "syncconflicts" or "sync" or "sharedsync" or "vpn" or "swarm" or "vpnfiles" or "teamchat" or "discord" or "builds" or "cystore" or "perforce"||Regex.IsMatch(method,"^(Push|Pull|Fetch|ExchangeGit)|Request.*Peer|^SyncLore|CheckForUpdates|ConfigureSyncthing");
}
