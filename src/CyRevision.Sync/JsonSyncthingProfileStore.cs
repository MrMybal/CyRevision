using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Sync;

public sealed class JsonSyncthingProfileStore : ISyncthingProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _rootPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSyncthingProfileStore(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<SyncthingProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = GetProfilePath(projectId);
            if (!File.Exists(path))
            {
                return null;
            }

            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<SyncthingProfile>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncthingProfile> CreateOrUpdateAsync(
        Guid projectId,
        string executablePath,
        string exchangeDirectory,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string projectDirectory = Path.Combine(_rootPath, "projects", projectId.ToString("N"));
            string profilePath = GetProfilePath(projectId);
            SyncthingProfile? existing = null;
            if (File.Exists(profilePath))
            {
                await using FileStream existingStream = new(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                existing = await JsonSerializer.DeserializeAsync<SyncthingProfile>(existingStream, JsonOptions, cancellationToken);
            }

            Uri apiEndpoint = existing?.ApiEndpoint ?? new Uri($"http://127.0.0.1:{GetFreeLoopbackPort()}");
            int listenPort = existing?.ListenPort is > 0
                ? existing.ListenPort
                : GetDistinctFreePort(apiEndpoint.Port);
            SyncthingProfile profile = new(
                projectId,
                Path.GetFullPath(executablePath),
                Path.Combine(projectDirectory, "config"),
                Path.Combine(projectDirectory, "data"),
                Path.GetFullPath(exchangeDirectory),
                apiEndpoint,
                existing?.ApiKey ?? CreateApiKey(),
                listenPort,
                existing?.FolderId ?? "cyrevision-" + projectId.ToString("N"))
            {
                FolderMode = existing?.FolderMode ?? SyncthingFolderMode.SendReceive,
                RescanIntervalSeconds = existing?.RescanIntervalSeconds is > 0
                    ? existing.RescanIntervalSeconds
                    : 60,
                FileWatcherEnabled = existing?.FileWatcherEnabled ?? true
            };
            profile.ToIsolationOptions().Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            await WriteAtomicallyAsync(profilePath, profile, cancellationToken);
            RestrictProfilePermissions(profilePath);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncthingProfile> SaveAsync(
        SyncthingProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(profile));
        }

        if (profile.RescanIntervalSeconds is < 0 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "The rescan interval must be between 0 and 86400 seconds.");
        }

        profile.ToIsolationOptions().Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string profilePath = GetProfilePath(profile.ProjectId);
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            await WriteAtomicallyAsync(profilePath, profile, cancellationToken);
            RestrictProfilePermissions(profilePath);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            File.Delete(GetProfilePath(projectId));
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetProfilePath(Guid projectId) =>
        Path.Combine(_rootPath, "profiles", projectId.ToString("N") + ".json");

    private static string CreateApiKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int GetFreeLoopbackPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetDistinctFreePort(int excludedPort)
    {
        int port;
        do
        {
            port = GetFreeLoopbackPort();
        }
        while (port == excludedPort);
        return port;
    }

    private static async Task WriteAtomicallyAsync(string path, SyncthingProfile profile, CancellationToken cancellationToken)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void RestrictProfilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
