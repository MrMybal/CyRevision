using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyRevision.Sync;

public sealed record SyncthingRuntimeStatus(
    string DeviceId,
    int ConnectedPeers,
    long PendingBytes,
    string? Version = null);

public sealed record SyncthingPeerConnectionStatus(
    string DeviceId,
    bool Connected,
    string? Address,
    DateTimeOffset? LastSeenAt,
    long IncomingBytes,
    long OutgoingBytes,
    string? ConnectionType);

public sealed record SyncthingDeviceConfiguration(
    string DeviceId,
    string Name,
    IReadOnlyList<string>? Addresses = null,
    bool Paused = false);

public sealed record SyncthingFolderConfiguration(
    string FolderId,
    string Label,
    string Path,
    IReadOnlyList<string> DeviceIds,
    string FolderType = "sendreceive",
    string VersioningType = "",
    int? KeepVersions = null,
    int? CleanoutDays = null,
    bool Paused = false);

public sealed class SyncthingApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public SyncthingApiClient(Uri endpoint, string apiKey, HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = endpoint;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync("rest/noauth/health", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            response.Dispose();
            using HttpResponseMessage authenticated = await _httpClient.GetAsync("rest/system/ping", cancellationToken);
            return authenticated.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<SyncthingRuntimeStatus> GetRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        JsonObject status = await GetObjectAsync("rest/system/status", cancellationToken);
        JsonObject connections = await GetObjectAsync("rest/system/connections", cancellationToken);
        int connected = connections["connections"] is JsonObject peers
            ? peers.Count(peer => peer.Value?["connected"]?.GetValue<bool>() == true)
            : 0;

        long pendingBytes = 0;
        JsonNode? folderList = await GetNodeAsync("rest/config/folders", cancellationToken);
        if (folderList is JsonArray folders)
        {
            foreach (JsonNode? folder in folders)
            {
                string? id = folder?["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                try
                {
                    JsonObject database = await GetObjectAsync(
                        "rest/db/status?folder=" + Uri.EscapeDataString(id),
                        cancellationToken);
                    pendingBytes += database["needBytes"]?.GetValue<long>() ?? 0;
                }
                catch (HttpRequestException)
                {
                    // A folder can disappear between the configuration and status calls.
                }
            }
        }

        return new SyncthingRuntimeStatus(
            status["myID"]?.GetValue<string>() ?? string.Empty,
            connected,
            pendingBytes,
            status["version"]?.GetValue<string>());
    }

    public async Task<IReadOnlyList<SyncthingPeerConnectionStatus>> GetPeerConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        JsonObject root = await GetObjectAsync("rest/system/connections", cancellationToken);
        if (root["connections"] is not JsonObject connections)
        {
            return [];
        }

        List<SyncthingPeerConnectionStatus> result = [];
        foreach ((string deviceId, JsonNode? node) in connections)
        {
            if (node is not JsonObject connection)
            {
                continue;
            }

            DateTimeOffset? lastSeen = DateTimeOffset.TryParse(
                connection["at"]?.GetValue<string>(),
                out DateTimeOffset parsed)
                ? parsed
                : null;
            result.Add(new SyncthingPeerConnectionStatus(
                deviceId,
                connection["connected"]?.GetValue<bool>() == true,
                connection["address"]?.GetValue<string>(),
                lastSeen,
                connection["inBytesTotal"]?.GetValue<long>() ?? 0,
                connection["outBytesTotal"]?.GetValue<long>() ?? 0,
                connection["type"]?.GetValue<string>()));
        }

        return result;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        PostEmptyAsync("rest/system/pause", cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        PostEmptyAsync("rest/system/resume", cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        PostEmptyAsync("rest/system/shutdown", cancellationToken);

    public async Task PutDeviceAsync(SyncthingDeviceConfiguration device, CancellationToken cancellationToken = default)
    {
        object payload = new
        {
            deviceID = device.DeviceId,
            name = device.Name,
            addresses = device.Addresses ?? ["dynamic"],
            compression = "metadata",
            introducer = false,
            autoAcceptFolders = false,
            paused = device.Paused
        };
        await PutJsonAsync("rest/config/devices/" + Uri.EscapeDataString(device.DeviceId), payload, cancellationToken);
    }

    public async Task DeleteDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(
            "rest/config/devices/" + Uri.EscapeDataString(deviceId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutFolderAsync(SyncthingFolderConfiguration folder, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> versioningParameters = [];
        if (folder.KeepVersions is { } keep)
        {
            versioningParameters["keep"] = keep.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (folder.CleanoutDays is { } days)
        {
            versioningParameters["cleanoutDays"] = days.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        object payload = new
        {
            id = folder.FolderId,
            label = folder.Label,
            path = Path.GetFullPath(folder.Path),
            type = folder.FolderType,
            devices = folder.DeviceIds.Select(id => new { deviceID = id }).ToArray(),
            rescanIntervalS = 60,
            fsWatcherEnabled = true,
            paused = folder.Paused,
            versioning = new
            {
                type = folder.VersioningType,
                @params = versioningParameters
            }
        };
        await PutJsonAsync("rest/config/folders/" + Uri.EscapeDataString(folder.FolderId), payload, cancellationToken);
    }

    public async Task DeleteFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(
            "rest/config/folders/" + Uri.EscapeDataString(folderId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<JsonObject> GetObjectAsync(string path, CancellationToken cancellationToken)
    {
        JsonNode? node = await GetNodeAsync(path, cancellationToken);
        return node as JsonObject ?? throw new JsonException($"Syncthing returned an unexpected response for {path}.");
    }

    private async Task<JsonNode?> GetNodeAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task PostEmptyAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(path, null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PutJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(path, value, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
