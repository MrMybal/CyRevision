using System.Text.Json;

namespace CyRevision.RemoteBuild;

public sealed class JsonRemoteBuildConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;

    public JsonRemoteBuildConnectionStore(string root) => _root = Path.GetFullPath(root);

    public async Task<RemoteBuildCredentials?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        string profilePath = GetProfilePath(projectId);
        if (!File.Exists(profilePath))
            return null;
        RemoteBuildConnectionProfile? profile = JsonSerializer.Deserialize<RemoteBuildConnectionProfile>(
            await File.ReadAllBytesAsync(profilePath, cancellationToken), JsonOptions);
        if (profile?.ProjectId != projectId)
            return null;
        string tokenPath = GetTokenPath(projectId);
        string token = File.Exists(tokenPath)
            ? (await File.ReadAllTextAsync(tokenPath, cancellationToken)).Trim()
            : string.Empty;
        return new RemoteBuildCredentials(profile, token);
    }

    public async Task SaveAsync(RemoteBuildCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ValidateProfile(credentials.Profile);
        Directory.CreateDirectory(_root);
        await WriteAtomicallyAsync(GetProfilePath(credentials.Profile.ProjectId),
            JsonSerializer.SerializeToUtf8Bytes(credentials.Profile, JsonOptions), cancellationToken);
        string tokenPath = GetTokenPath(credentials.Profile.ProjectId);
        await WriteAtomicallyAsync(tokenPath, System.Text.Encoding.UTF8.GetBytes(credentials.AccessToken.Trim()), cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void ValidateProfile(RemoteBuildConnectionProfile profile)
    {
        if (profile.ProjectId == Guid.Empty)
            throw new InvalidOperationException("A project is required for remote builds.");
        Uri endpoint = RemoteBuildEndpoint.Create(profile.Endpoint, profile.AllowPrivateHttp);
        _ = endpoint;
        if (string.IsNullOrWhiteSpace(profile.RecipeId))
            throw new InvalidOperationException("Choose an allowlisted build recipe.");
        if (string.IsNullOrWhiteSpace(profile.ArtifactDestination))
            throw new InvalidOperationException("Choose an artifact destination.");
        if (profile.MaximumUploadBytes <= 0)
            throw new InvalidOperationException("Maximum upload size must be greater than zero.");
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] data, CancellationToken cancellationToken)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temporary, data, cancellationToken);
        File.Move(temporary, path, true);
    }

    private string GetProfilePath(Guid projectId) => Path.Combine(_root, projectId.ToString("N") + ".json");
    private string GetTokenPath(Guid projectId) => Path.Combine(_root, projectId.ToString("N") + ".token");
}

public static class RemoteBuildEndpoint
{
    public static Uri Create(string value, bool allowPrivateHttp)
    {
        if (!Uri.TryCreate(value?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The remote build endpoint must be an HTTP or HTTPS URL.");
        bool loopback = endpoint.IsLoopback;
        bool privateHost = System.Net.IPAddress.TryParse(endpoint.Host, out System.Net.IPAddress? address) &&
                           IsPrivate(address);
        if (endpoint.Scheme == "http" && !loopback && !(allowPrivateHttp && privateHost))
            throw new InvalidOperationException("Plain HTTP is allowed only on loopback or an explicitly trusted private/VPN address.");
        return endpoint;
    }

    private static bool IsPrivate(System.Net.IPAddress address)
    {
        byte[] bytes = address.MapToIPv4().GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 127 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}
