using System.Text.Json;

namespace CyRevision.Vpn;

public sealed class JsonSwarmProfileStore : ISwarmProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _directory;

    public JsonSwarmProfileStore(string vpnDirectory)
    {
        _directory = Path.Combine(Path.GetFullPath(vpnDirectory), "swarm");
    }

    public async Task<SwarmProjectProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SwarmProjectProfile>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(SwarmProjectProfile profile, CancellationToken cancellationToken = default)
    {
        SwarmSetupService.ValidateProfile(profile);
        Directory.CreateDirectory(_directory);
        string path = GetPath(profile.ProjectId);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
            }
            File.Move(temporary, path, true);
            RestrictPermissions(path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string GetPath(Guid projectId) => Path.Combine(_directory, projectId.ToString("N") + ".json");

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
