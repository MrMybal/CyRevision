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

    public Task<DiscordAgentRegistration?> GetRegistrationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        ReadAsync<DiscordAgentRegistration>(GetRegistrationPath(projectId), cancellationToken);

    public async Task<IReadOnlyList<DiscordAgentRegistration>> GetRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            List<DiscordAgentRegistration> registrations = [];
            foreach (string path in Directory.EnumerateFiles(_directory, "*.registration.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using FileStream stream = File.OpenRead(path);
                    DiscordAgentRegistration? registration = await JsonSerializer.DeserializeAsync<DiscordAgentRegistration>(
                        stream,
                        JsonOptions,
                        cancellationToken);
                    if (registration is not null)
                    {
                        registration.Validate();
                        registrations.Add(registration);
                    }
                }
                catch (JsonException)
                {
                    // A damaged registration is skipped without stopping the other projects.
                }
                catch (InvalidDataException)
                {
                    // A registration with invalid values is ignored.
                }
                catch (InvalidOperationException)
                {
                    // A registration with inconsistent project identifiers is ignored.
                }
            }

            return registrations;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveRegistrationAsync(
        DiscordAgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();
        await WriteAsync(GetRegistrationPath(registration.ProjectId), registration, cancellationToken);
        await SaveProfileAsync(registration.Profile, cancellationToken);
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
            File.Delete(GetRegistrationPath(projectId));
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

    private string GetRegistrationPath(Guid projectId) =>
        Path.Combine(_directory, $"{projectId:N}.registration.json");
}
