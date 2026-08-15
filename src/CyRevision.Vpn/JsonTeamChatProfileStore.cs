using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Vpn;

public sealed class JsonTeamChatProfileStore : ITeamChatProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _directory;

    public JsonTeamChatProfileStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public async Task<TeamChatProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path)) return null;
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, true);
        return await JsonSerializer.DeserializeAsync<TeamChatProfile>(stream, JsonOptions, cancellationToken);
    }

    public async Task<TeamChatProfile> GetOrCreateAsync(
        Guid projectId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        TeamChatProfile? existing = await GetAsync(projectId, cancellationToken);
        if (existing is not null) return existing;
        TeamChatProfile profile = new(
            projectId,
            string.IsNullOrWhiteSpace(displayName) ? Environment.UserName : displayName.Trim(),
            TeamChatTransport.Vpn,
            "127.0.0.1",
            TeamChatDefaults.Port,
            $"127.0.0.1:{TeamChatDefaults.Port}",
            CreateToken(),
            string.Empty,
            true,
            365,
            TeamChatDefaults.MaxAttachmentBytes,
            DateTimeOffset.UtcNow);
        await SaveAsync(profile, cancellationToken);
        return profile;
    }

    public async Task SaveAsync(TeamChatProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile.ProjectId == Guid.Empty) throw new InvalidDataException("A team chat profile needs a project ID.");
        if (profile.Port is < 1 or > 65535) throw new InvalidDataException("Team chat port is invalid.");
        if (profile.MaxAttachmentBytes is < 1 or > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Team chat attachment limit must be between 1 byte and 2 GB.");
        Directory.CreateDirectory(_directory);
        string path = GetPath(profile.ProjectId);
        string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, true))
            await JsonSerializer.SerializeAsync(stream, profile with { UpdatedAt = DateTimeOffset.UtcNow }, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
    }

    public async Task<TeamChatProfile> RotateTokenAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        TeamChatProfile profile = await GetAsync(projectId, cancellationToken)
                                  ?? throw new InvalidOperationException("Team chat is not configured for this project.");
        profile = profile with { AccessToken = CreateToken(), UpdatedAt = DateTimeOffset.UtcNow };
        await SaveAsync(profile, cancellationToken);
        return profile;
    }

    private string GetPath(Guid projectId) => Path.Combine(_directory, projectId.ToString("N") + ".json");

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
