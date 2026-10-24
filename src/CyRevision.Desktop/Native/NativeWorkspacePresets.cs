namespace CyRevision.Desktop.Native;
internal static class NativeWorkspacePresets
{
    internal static readonly Dictionary<string,string[]> Presets=new(StringComparer.Ordinal){
        ["Full workspace"]=NativeWorkspaceCatalog.All.Select(item=>item.Id).ToArray(),
        ["Git essentials"]="overview gitoverview visible members changes history branches pullrequests locks lfs backups help".Split(' '),
        ["Developer"]="overview gitoverview visible members changes history composition branches pullrequests ci graphs locks ignore lfs backups code search console assets ai mcp plugins unreal unity godot lore perforce unrealbuilds diagnostics help".Split(' '),
        ["Unreal production"]="overview gitoverview visible members changes history composition branches pullrequests ci graphs locks ignore lfs backups code search console assets plugins unreal lore perforce unrealbuilds diagnostics help".Split(' '),
        ["Game engines"]="overview gitoverview visible members changes history branches pullrequests ci locks ignore lfs backups code search console assets plugins unreal unity godot lore perforce unrealbuilds diagnostics help".Split(' '),
        ["Team & network"]="overview gitoverview visible members changes history sync syncconflicts sharedsync vpn swarm vpnfiles teamchat builds discord wip console diagnostics help".Split(' '),
        ["Minimal"]="overview gitoverview visible changes history code console help".Split(' ')
    };
    internal static HashSet<string> Hidden(string preset)=>Presets.TryGetValue(preset,out var shown)?NativeWorkspaceCatalog.All.Where(item=>!shown.Contains(item.Id)).Select(item=>item.LoadTab).ToHashSet(StringComparer.Ordinal):[];
}
