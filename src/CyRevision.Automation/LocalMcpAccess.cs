using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyRevision.Automation;

public enum LocalMcpAccessProfile { Free, LocalControlled, TokenOnly, Protected }

public static class LocalMcpAccess
{
    public static bool RequiresToken(this LocalMcpAccessProfile profile) => profile is LocalMcpAccessProfile.TokenOnly or LocalMcpAccessProfile.Protected;
    public static bool ControlsOperations(this LocalMcpAccessProfile profile) => profile is LocalMcpAccessProfile.LocalControlled or LocalMcpAccessProfile.Protected;
    public static string Id(this LocalMcpAccessProfile profile) => profile switch
    {
        LocalMcpAccessProfile.Free => "free",
        LocalMcpAccessProfile.LocalControlled => "local_controlled",
        LocalMcpAccessProfile.TokenOnly => "token_only",
        LocalMcpAccessProfile.Protected => "protected_access",
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
    public static LocalMcpAccessProfile Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return LocalMcpAccessProfile.Free;
        return value.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant() switch
        {
            "free" => LocalMcpAccessProfile.Free,
            "localcontrolled" => LocalMcpAccessProfile.LocalControlled,
            "tokenonly" => LocalMcpAccessProfile.TokenOnly,
            "protected" or "protectedaccess" => LocalMcpAccessProfile.Protected,
            _ => throw new ArgumentException("Unknown local MCP access profile.")
        };
    }
}

/// <summary>Host-owned project settings. Missing choices migrate to Free; explicit invalid choices never become Free.</summary>
public static class LocalMcpAccessStore
{
    public static LocalMcpAccessProfile LoadOrMigrate(string path)
    {
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException("Invalid local MCP settings.") : new JsonObject();
        var choice = document["accessProfile"] ?? document["localAccessProfile"];
        var profile = LocalMcpAccess.Parse(choice?.GetValue<string>());
        if (choice is null || string.IsNullOrWhiteSpace(choice.GetValue<string>())) Save(path, profile, document);
        return profile;
    }

    public static void Save(string path, LocalMcpAccessProfile profile) => Save(path, profile,
        File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException("Invalid local MCP settings.") : new JsonObject());

    private static void Save(string path, LocalMcpAccessProfile profile, JsonObject document)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        document["accessProfile"] = profile.Id();
        string temporary = Path.Combine(directory, ".local-access-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) JsonSerializer.Serialize(stream, document);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
