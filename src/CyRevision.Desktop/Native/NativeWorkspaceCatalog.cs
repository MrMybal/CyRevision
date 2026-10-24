using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

public sealed record NativeWorkspace(string Id,string Label,string Category,string LoadTab,string[] Tables,string[] Prefixes);

public static class NativeWorkspaceCatalog
{
    public static IReadOnlyList<NativeWorkspace> All { get; } =
    [
        new("overview","Project","Overview","ProjectWorkspaceTab",["Presets"],["Project","SelectedPreset","Runtime"]),
        new("gitoverview","Git repository","Overview","GitOverviewWorkspaceTab",["GitRemotes"],["GitOverview","GitRemote","Remote","GitConnection"]),
        new("members","Members","Overview","MembersWorkspaceTab",["GitProjectMembers","SyncProjectMembers","VpnProjectMembers","PeerMembers"],["Member","Peer","ProjectMember"]),
        new("notifications","Notifications","Overview","NotificationsWorkspaceTab",["ProjectNotifications"],["ProjectNotification"]),
        new("visible","Visible tabs","Overview","VisibleTabsWorkspaceTab",[],[]),
        new("license","License","Overview","LicenseWorkspaceTab",[],["ProjectLicense","License"]),
        new("changes","Changes","Git","ChangesWorkspaceTab",["NativeGitChanges","NativeGitFolders","PreparedChanges","LocalOnlyChanges"],["Commit","Change","Diff","GitAhead","GitBehind"]),
        new("history","History","Git","HistoryWorkspaceTab",["History","ExplorerFiles","ExplorerFileHistory","MultiRestoreFiles"],["Explorer","MultiRestore"]),
        new("composition","Compose","Git","CompositionWorkspaceTab",["MultiRestoreFiles","MultiRestoreOperations","BranchComparisonCommits","CherryPickOperations"],["MultiRestore","BranchCompare","BranchComparison","CherryPick"]),
        new("branches","Branches","Git","BranchesWorkspaceTab",["FilteredBranches","SelectedBranchHistory","HistoricalWorktrees","NativeBranchFiles","NativeBranchHistory"],["Branch","SelectedBranch","Historical"]),
        new("annotations","Annotations","Git","GitAnnotationsWorkspaceTab",["GitAnnotations"],["GitAnnotation"]),
        new("pullrequests","Pull Requests","Git","PullRequestsWorkspaceTab",["FilteredPullRequests","PullRequestFiles","PullRequestReviews","PullRequestComments","PullRequestCiWorkflows","PullRequestCiRuns","PullRequestCiJobs","FilteredPullRequestCiLogLines","PullRequestConflictFiles","PullRequestLinkedWorkItems","PullRequestCommitRevisions"],["PullRequest"]),
        new("ci","CI / Actions","Git","CiWorkspaceTab",["CiRuns","CiJobs","FilteredCiLogLines","CiWorkflows"],["Ci","ReleaseVersion"]),
        new("graphs","Git graphs","Git","GitGraphsWorkspaceTab",["GitGraphCommits","GitGraphFileRelations","GitGraphTeamActivity"],["GitGraph"]),
        new("locks","File locks","Git","LfsLocksWorkspaceTab",["LfsLocks","WorkInProgressReservations"],["LfsLock","WorkInProgress","Lock"]),
        new("ignore","Ignore rules","Git","GitIgnoreWorkspaceTab",["GitIgnoreRules","IgnoredFiles"],["GitIgnore","Ignore"]),
        new("lfs","Git LFS","Git","GitLfsWorkspaceTab",["LfsFiles","LfsVersions","LfsCleanupItems","LfsStorageAreas","LfsLargestFiles","LfsPatterns"],["Lfs"]),
        new("code","Solution Explorer","Code & Assets","SolutionExplorerWorkspaceTab",["NativeCodeFiles","NativeFileHistory","CodeFileSearchResults","CodeFileList"],["Code"]),
        new("search","Code search","Code & Assets","CodeWorkspaceTab",["CodeSearchResults","CodeHistory","CodeSymbols"],["Code"]),
        new("console","Console & Logs","Code & Assets","ConsoleWorkspaceTab",["FilteredApplicationLogEntries","RecentOperations"],["RepositoryConsole","RepositoryCommand","ApplicationLog","Console"]),
        new("assets","Asset preview & diff","Code & Assets","AssetDiffWorkspaceTab",["AssetExplorerFiles"],["Asset","UnrealAsset"]),
        new("ai","AI Assistant","Code & Assets","AiWorkspaceTab",["AiChatMessages","AiProviders","AiConversations"],["Ai","Codex"]),
        new("mcp","MCP","Code & Assets","McpWorkspaceTab",["AiMcpServers","AiMcpTools"],["AiMcp","Mcp"]),
        new("backups","Backups","Backup","BackupsWorkspaceTab",["Backups","GitArchiveCandidates","ArchivedGitBranches"],["Backup","ColdArchive","GitArchive"]),
        new("sync","Syncthing","Sync","SynchronizationWorkspaceTab",["SyncthingDifferences","SyncthingLogs","SyncHistory","SyncCommits"],["Sync","Syncthing","SmartSync"]),
        new("syncconflicts","Sync conflicts","Sync","SyncConflictsWorkspaceTab",["SyncConflicts","SyncCommitConflicts"],["SyncConflict","SelectedSyncConflict","SyncCommit"]),
        new("sharedsync","Shared Sync folders","Team & Network","SharedSyncWorkspaceTab",["SharedSyncFolders"],["SharedSync"]),
        new("vpn","WireGuard VPN","Team & Network","VpnWorkspaceTab",["VpnPeers","VpnNetworks"],["Vpn"]),
        new("swarm","Swarm over VPN","Team & Network","SwarmWorkspaceTab",["SwarmPeers","SwarmNetworks"],["Swarm"]),
        new("vpnfiles","VPN files","Team & Network","VpnFilesWorkspaceTab",["VpnFileShares","VpnFileTransfers"],["VpnFile"]),
        new("teamchat","Team chat","Team & Network","TeamChatWorkspaceTab",["TeamChatMessages","TeamChatChannels","TeamChatPeers"],["TeamChat"]),
        new("builds","Remote builds","Team & Network","RemoteBuildsWorkspaceTab",[],["RemoteBuild"]),
        new("discord","Discord agent","Team & Network","DiscordWorkspaceTab",["DiscordConnections","DiscordActivity"],["Discord"]),
        new("wip","Work in progress","Team & Network","WorkInProgressWorkspaceTab",["WorkInProgressReservations"],["WorkInProgress"]),
        new("plugins","Plugins","Extensions & Help","PluginsWorkspaceTab",["Plugins"],["Plugin","ActivePlugin"]),
        new("cystore","CyStore Alpha","Extensions & Help","CyStoreWorkspaceTab",["CyStoreVersions","CyStoreFiles"],["CyStore"]),
        new("unreal","Unreal","Extensions & Help","UnrealWorkspaceTab",[],["Unreal"]),
        new("unity","Unity","Extensions & Help","UnityWorkspaceTab",[],["Unity"]),
        new("godot","Godot","Extensions & Help","GodotWorkspaceTab",[],["Godot"]),
        new("lore","Lore","Extensions & Help","LoreWorkspaceTab",[],["Lore"]),
        new("perforce","Perforce","Extensions & Help","PerforceWorkspaceTab",["PerforceOpenedFiles","PerforcePendingChanges","PerforceFileHistory"],["Perforce"]),
        new("unrealbuilds","Unreal builds","Extensions & Help","UnrealBuildWorkspaceTab",["UnrealBuildEngines","UnrealBuildTargets","UnrealBuildPresets","UnrealBuildResults","UnrealBuildDiagnostics"],["UnrealBuild"]),
        new("diagnostics","Diagnostics","Extensions & Help","DiagnosticsWorkspaceTab",["PerformanceMetrics","FilteredApplicationLogEntries","RecentOperations"],["Performance","ApplicationLog","RepositoryCommand"]),
        new("help","Help","Extensions & Help","HelpWorkspaceTab",["DocumentationPages"],["Documentation","Help"]),
    ];

    public static NativeWorkspace Find(string id) => All.FirstOrDefault(workspace=>workspace.Id==id || workspace.LoadTab==id)
        ?? throw new ArgumentException("Unknown workspace.");

    public static bool IsAvailable(NativeWorkspace workspace,MainWindowViewModel model)
    {
        var features=model.SelectedProject?.Definition.Features;
        if(features is null) return workspace.Id is "overview" or "visible";
        return workspace.Id switch
        {
            "gitoverview"=>features.GitEnabled,
            "sync"=>features.PeerSyncEnabled,
            "syncconflicts"=>features.PeerSyncEnabled || model.SharedSyncFolders.Count>0,
            "unreal" or "unrealbuilds"=>model.IsUnrealIntegrationEnabled && model.IsUnrealProjectDetected,
            "unity"=>model.IsUnityIntegrationEnabled && model.IsUnityProjectDetected,
            "godot"=>model.IsGodotIntegrationEnabled && model.IsGodotProjectDetected,
            "lore"=>model.IsLoreIntegrationEnabled && model.IsLoreProjectDetected,
            "perforce"=>model.IsPerforceIntegrationEnabled,
            "cystore"=>model.IsCyStorePluginEnabled,
            "ai"=>model.IsAiIntegrationEnabled,
            _=>workspace.Category!="Git" || features.GitEnabled
        };
    }

    public static string CategoryFor(NativeWorkspace workspace,MainWindowViewModel model) =>
        model.ActivePluginOperatingModeWorkspaceTabs.Contains(workspace.LoadTab,StringComparer.OrdinalIgnoreCase)
            ? model.ActivePluginOperatingModeName : workspace.Category;

    public static bool IsSecret(string name)=>new[]{"Password","ApiKey","ApiToken","SessionToken","PrivateKey","Certificate","WebhookUrl","Secret"}
        .Any(part=>name.Contains(part,StringComparison.OrdinalIgnoreCase)) ||
        name.EndsWith("Token",StringComparison.OrdinalIgnoreCase) && !name.EndsWith("EnvironmentVariable",StringComparison.OrdinalIgnoreCase);
    public static bool IsScalar(Type type)=>(Nullable.GetUnderlyingType(type)??type) is Type value &&
        (value.IsPrimitive || value.IsEnum || value==typeof(string) || value==typeof(decimal) || value==typeof(Guid) || value==typeof(DateTime) || value==typeof(DateTimeOffset) || value==typeof(TimeSpan));
    public static string Label(string value)=>System.Text.RegularExpressions.Regex.Replace(value.Replace("Async","",StringComparison.Ordinal),"(?<=[a-z0-9])(?=[A-Z])"," ");
    public static string GroupFor(string name)
    {
        string stripped=name;
        foreach(string verb in new[]{"Refresh","Selected","Filtered","Configure","Initialize","Analyze","Execute","Create","Save","Load","Start","Stop","Pause","Resume","Apply","Set","Get","Cancel","Remove","Delete","Restore","Archive","Install","Detect","Enable","Disable","Open","Clear","Test","Compare","Read","List"})
            if(stripped.StartsWith(verb,StringComparison.Ordinal)){stripped=stripped[verb.Length..];break;}
        return All.OrderByDescending(workspace=>workspace.Prefixes.DefaultIfEmpty("").Max(prefix=>stripped.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?prefix.Length:0))
            .FirstOrDefault(workspace=>workspace.Prefixes.Any(prefix=>stripped.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))?.Id
            ??(name.Contains("Commit",StringComparison.Ordinal)||name.Contains("Stage",StringComparison.Ordinal)||name.Contains("Change",StringComparison.Ordinal)||name is "FetchAsync" or "PullAsync" or "PushAsync" or "RefreshAsync"?"changes":"overview");
    }
}
