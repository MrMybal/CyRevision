using System.Text.Json;
namespace CyRevision.Desktop.Native;

// "id" belongs exclusively to the IPC envelope. Entity identifiers must use
// named target fields, otherwise Client.send replaces them with a request ID.
internal static class NativeRequestTargets
{
    internal static Guid Project(JsonElement request)
    {
        if(!request.TryGetProperty("targetProjectId",out var value) || value.ValueKind!=JsonValueKind.String ||
           !Guid.TryParse(value.GetString(),out Guid id) || id==Guid.Empty)
            throw new ArgumentException("Choose a valid target project.");
        return id;
    }
    internal static string Workspace(JsonElement request)
    {
        if(!request.TryGetProperty("workspace",out var value) || value.ValueKind!=JsonValueKind.String ||
           value.GetString() is not {} name || !NativeWorkspaceCatalog.All.Any(item=>item.Id==name || item.LoadTab==name))
            throw new ArgumentException("Choose a valid workspace.");
        return name;
    }
}
