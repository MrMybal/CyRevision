using System.Reflection;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

internal static class NativeActivity
{
    private static readonly PropertyInfo[] LoadingFlags=typeof(MainWindowViewModel).GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property=>property.PropertyType==typeof(bool) && IsLoadingFlag(property.Name)).ToArray();
    // Only transient work belongs here, not long-lived Sync/VPN/Discord services.
    internal static bool IsLoadingFlag(string name) => name != "IsBusy" &&
        (name.StartsWith("Is", StringComparison.Ordinal) && (name.EndsWith("Loading", StringComparison.Ordinal) || name.EndsWith("Busy", StringComparison.Ordinal)) ||
         name is "IsLfsAnalysisRunning" or "IsCodeSearchRunning" or "IsCodeFileSearchRunning" or "IsRepositoryConsoleRunning" or "IsUnrealBuildRunning" or "IsRemoteBuildRunning");

    internal static string WorkspaceFor(string name) => name switch
    {
        "IsProjectLoading" => "project",
        "IsWorkingTreeDiffLoading" => "changes",
        "IsIgnoreSuggestionLoading" => "ignore",
        "IsProjectLicenseLoading" => "license",
        "IsCodeSearchRunning" => "search",
        "IsExplorerLoading" or "IsExplorerDiffLoading" or "IsExplorerAnyLoading" => "history",
        _ => NativeWorkspaceCatalog.GroupFor(name.StartsWith("Is", StringComparison.Ordinal) ? name[2..] : name)
    };

    internal static bool IsInteractive(string command) => command is "select" or "navigation" or "tools.tree.select";

    internal static bool CanRunDuringProjectScan(string action) =>
        action.StartsWith("Refresh", StringComparison.Ordinal) || action.StartsWith("Load", StringComparison.Ordinal) ||
        action.StartsWith("Detect", StringComparison.Ordinal) || action is "AddExistingRepositoryAsync" or "AddFolderProjectAsync" or "CloneGitRepositoryAsync";

    internal static object[] Snapshot(MainWindowViewModel model) => LoadingFlags
        .Where(property => NativeTable.Read(property, model) is true)
        .Select(property => (object)new { key=property.Name,workspace=WorkspaceFor(property.Name),label=NativeWorkspaceCatalog.Label(property.Name[2..]) }).ToArray();
}
