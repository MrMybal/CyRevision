using System.Text.Json;

namespace CyRevision.Discord;

public sealed class JsonDiscordAgentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonDiscordAgentStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public Task<DiscordAgentProfile?> GetProfileAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        ReadAsync<DiscordAgentProfile>(GetProfilePath(projectId), cancellationToken);

    public async Task SaveProfileAsync(
        DiscordAgentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        await WriteAsync(GetProfilePath(profile.ProjectId), profile, cancellationToken);
    }

    public Task<DiscordAgentCheckpoint?> GetCheckpointAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        ReadAsync<DiscordAgentCheckpoint>(GetCheckpointPath(projectId), cancellationToken);

    public Task SaveCheckpointAsync(
        DiscordAgentCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("A project ID is required.");
        }

        return WriteAsync(GetCheckpointPath(checkpoint.ProjectId), checkpoint, cancellationToken);
    }

    public async Task RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            File.Delete(GetProfilePath(projectId));
            File.Delete(GetCheckpointPath(projectId));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            string temporaryPath = path + ".new";
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            RestrictToCurrentUser(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            RestrictToCurrentUser(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RestrictToCurrentUser(string path)
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
            // The host file system does not expose Unix permissions.
        }
        catch (UnauthorizedAccessException)
        {
            // Saving the profile remains preferable to losing the configuration.
        }
    }

    private string GetProfilePath(Guid projectId) =>
        Path.Combine(_directory, $"{projectId:N}.profile.json");

    private string GetCheckpointPath(Guid projectId) =>
        Path.Combine(_directory, $"{projectId:N}.checkpoint.json");
}
