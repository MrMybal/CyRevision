using System.Text.Json;

namespace CyRevision.Desktop.Native;

internal static class NativeViewRequests
{
    internal static IReadOnlyList<NativeWorkspace> Workspaces(JsonElement request,NativeWorkspace primary)
    {
        var result=new List<NativeWorkspace>{primary};
        if(request.TryGetProperty("workspaces",out var views) && views.ValueKind==JsonValueKind.Array)
            foreach(var item in views.EnumerateArray().Take(16))
            {
                if(item.ValueKind!=JsonValueKind.String)throw new ArgumentException("Invalid workspace view.");
                var workspace=NativeWorkspaceCatalog.Find(item.GetString()!);
                if(result.All(existing=>existing.Id!=workspace.Id))result.Add(workspace);
            }
        return result;
    }

    internal static string TableKey(JsonElement request,string source)
    {
        if(!request.TryGetProperty("key",out var value))return source;
        if(value.ValueKind!=JsonValueKind.String || value.GetString() is not { Length: >0 and <=160 } key || key.Any(char.IsControl))
            throw new ArgumentException("Invalid table view key.");
        return key;
    }
}
