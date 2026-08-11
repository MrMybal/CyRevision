using System.Text.Json;

namespace CyRevision.Discord.Control;

public enum DiscordAgentExecutionMode
{
    Integrated,
    Autonomous
}

public sealed record DiscordControlConnection(
    Guid ProjectId,
    DiscordAgentExecutionMode Mode,
    string Endpoint,
    string ApiToken,
    bool AllowPrivateHttp,
    string? AgentRepositoryPath);

public sealed class DiscordControlConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public DiscordControlConnectionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async Task<DiscordControlConnection?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DiscordControlConnection>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        DiscordControlConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection.ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("A project ID is required for the Discord control connection.");
        }

        _ = DiscordAgentEndpoint.Create(connection.Endpoint, connection.AllowPrivateHttp);
        if (connection.Mode == DiscordAgentExecutionMode.Autonomous && connection.ApiToken.Trim().Length < 32)
        {
            throw new InvalidDataException("The autonomous agent token must contain at least 32 characters.");
        }

        Directory.CreateDirectory(_directory);
        string path = GetPath(connection.ProjectId);
        string temporaryPath = path + ".new";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, connection, JsonOptions, cancellationToken);
        }

        Restrict(temporaryPath);
        File.Move(temporaryPath, path, overwrite: true);
        Restrict(path);
    }

    public Task RemoveAsync(Guid projectId)
    {
        File.Delete(GetPath(projectId));
        return Task.CompletedTask;
    }

    private static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string GetPath(Guid projectId) => Path.Combine(_directory, $"{projectId:N}.json");
}
