using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Vpn;

public sealed class JsonVpnFileExchangeProfileStore : IVpnFileExchangeProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _directory;

    public JsonVpnFileExchangeProfileStore(string vpnDirectory)
    {
        _directory = Path.Combine(Path.GetFullPath(vpnDirectory), "file-exchange");
    }

    public async Task<VpnFileExchangeCredentials?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string profilePath = GetProfilePath(projectId);
        string tokenPath = GetTokenPath(projectId);
        if (!File.Exists(profilePath) && !File.Exists(tokenPath))
        {
            return null;
        }
        if (!File.Exists(profilePath) || !File.Exists(tokenPath))
        {
            throw new InvalidDataException("The VPN file-exchange profile is incomplete; profile and access token are both required.");
        }

        await using FileStream stream = File.OpenRead(profilePath);
        VpnFileExchangeProfile profile = await JsonSerializer.DeserializeAsync<VpnFileExchangeProfile>(
            stream, JsonOptions, cancellationToken) ?? throw new InvalidDataException("The VPN file-exchange profile is invalid.");
        string token = (await File.ReadAllTextAsync(tokenPath, cancellationToken)).Trim();
        VpnFileExchangeService.ValidateProfile(profile, token);
        return new VpnFileExchangeCredentials(profile, token);
    }

    public async Task<VpnFileExchangeCredentials> GetOrCreateAsync(
        VpnProjectProfile vpnProfile,
        string defaultDataDirectory,
        CancellationToken cancellationToken = default)
    {
        VpnFileExchangeCredentials? existing = await GetAsync(vpnProfile.ProjectId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }
        VpnFileExchangeCredentials created = new(
            VpnFileExchangeDefaults.Create(vpnProfile, defaultDataDirectory),
            CreateToken());
        await SaveAsync(created, cancellationToken);
        return created;
    }

    public async Task SaveAsync(VpnFileExchangeCredentials credentials, CancellationToken cancellationToken = default)
    {
        VpnFileExchangeService.ValidateProfile(credentials.Profile, credentials.AccessToken);
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(credentials.Profile.InboxPath);
        if (!string.IsNullOrWhiteSpace(credentials.Profile.SharedFolderPath))
        {
            Directory.CreateDirectory(credentials.Profile.SharedFolderPath);
        }
        await WriteAtomicallyAsync(
            GetProfilePath(credentials.Profile.ProjectId),
            JsonSerializer.SerializeToUtf8Bytes(credentials.Profile, JsonOptions),
            cancellationToken);
        await WriteAtomicallyAsync(
            GetTokenPath(credentials.Profile.ProjectId),
            System.Text.Encoding.UTF8.GetBytes(credentials.AccessToken),
            cancellationToken);
        RestrictPermissions(GetProfilePath(credentials.Profile.ProjectId));
        RestrictPermissions(GetTokenPath(credentials.Profile.ProjectId));
    }

    public async Task<VpnFileExchangeCredentials> RotateTokenAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        VpnFileExchangeCredentials current = await GetAsync(projectId, cancellationToken)
                                             ?? throw new InvalidOperationException("Configure VPN file exchange first.");
        VpnFileExchangeCredentials updated = current with { AccessToken = CreateToken() };
        await SaveAsync(updated, cancellationToken);
        return updated;
    }

    private string GetProfilePath(Guid projectId) => Path.Combine(_directory, projectId.ToString("N") + ".json");

    private string GetTokenPath(Guid projectId) => Path.Combine(_directory, projectId.ToString("N") + ".token");

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task WriteAtomicallyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
