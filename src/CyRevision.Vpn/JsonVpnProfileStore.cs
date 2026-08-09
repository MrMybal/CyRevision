using System.Text.Json;

namespace CyRevision.Vpn;

public interface IVpnProfileStore
{
    Task<VpnProjectProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task SaveAsync(VpnProjectProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class JsonVpnProfileStore : IVpnProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _profilesDirectory;

    public JsonVpnProfileStore(string rootDirectory)
    {
        _profilesDirectory = Path.Combine(Path.GetFullPath(rootDirectory), "profiles");
    }

    public async Task<VpnProjectProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        VpnProjectProfile profile = await JsonSerializer.DeserializeAsync<VpnProjectProfile>(stream, JsonOptions, cancellationToken)
                                    ?? throw new InvalidDataException("Le profil VPN est vide.");
        VpnProfileValidator.Validate(profile);
        return profile;
    }

    public async Task SaveAsync(VpnProjectProfile profile, CancellationToken cancellationToken = default)
    {
        VpnProfileValidator.Validate(profile);
        Directory.CreateDirectory(_profilesDirectory);
        string path = GetPath(profile.ProjectId);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions), cancellationToken);
            File.Move(temporary, path, true);
            Restrict(path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetPath(projectId));
        return Task.CompletedTask;
    }

    private string GetPath(Guid projectId) => Path.Combine(_profilesDirectory, projectId.ToString("N") + ".json");

    internal static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
