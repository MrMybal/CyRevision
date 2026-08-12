using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.AI;

internal sealed class JsonAiMcpProfileStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonAiMcpProfileStore(string configurationDirectory)
    {
        _directory = Path.Combine(configurationDirectory, "ai", "mcp");
    }

    public async Task<AiMcpProjectProfile> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path)) return AiMcpProjectProfile.CreateDefault(projectId);
        await using FileStream stream = File.OpenRead(path);
        AiMcpProjectProfile? profile = await JsonSerializer.DeserializeAsync<AiMcpProjectProfile>(
            stream, _jsonOptions, cancellationToken);
        return profile is null || profile.ProjectId != projectId
            ? AiMcpProjectProfile.CreateDefault(projectId)
            : profile;
    }

    public async Task SaveAsync(AiMcpProjectProfile profile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        string path = GetPath(profile.ProjectId);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, profile, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, path, true);
    }

    private string GetPath(Guid projectId) => Path.Combine(_directory, projectId.ToString("N") + ".json");
}
