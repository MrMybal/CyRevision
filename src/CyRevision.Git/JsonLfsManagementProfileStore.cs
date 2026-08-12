using System.Text.Json;

namespace CyRevision.Git;

public sealed class JsonLfsManagementProfileStore(string rootDirectory) : ILfsManagementProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _rootDirectory = Path.GetFullPath(rootDirectory);

    public async Task<LfsManagementProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path))
            return null;

        try
        {
            LfsManagementProfile? profile = JsonSerializer.Deserialize<LfsManagementProfile>(
                await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
            return profile?.ProjectId == projectId ? profile : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(LfsManagementProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Directory.CreateDirectory(_rootDirectory);
        string path = GetPath(profile.ProjectId);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions), cancellationToken);
        File.Move(temporary, path, true);
    }

    private string GetPath(Guid projectId) => Path.Combine(_rootDirectory, projectId.ToString("N") + ".json");
}
