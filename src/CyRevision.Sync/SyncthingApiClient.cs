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
    bool Paused = false,
    int RescanIntervalSeconds = 60,
    bool FileWatcherEnabled = true);

public sealed record SyncthingFolderStatus(
    string FolderId,
    string State,
    DateTimeOffset? StateChangedAt,
    long GlobalFiles,
    long LocalFiles,
    long InSyncFiles,
    long NeededFiles,
    long NeededBytes,
    long ReceiveOnlyChangedFiles,
    long ReceiveOnlyChangedBytes,
    long ErrorCount)
{
    public bool IsInSync => NeededFiles == 0 && NeededBytes == 0 && ReceiveOnlyChangedFiles == 0;
}

public sealed record SyncthingDifferenceItem(
    string Name,
    string Direction,
    long Size,
    DateTimeOffset? ModifiedAt,
    bool Deleted,
    string Type);

public sealed record SyncthingLogEntry(
    DateTimeOffset? Timestamp,
    string Message,
    string Level = "Info",
    string Facility = "Syncthing");

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
        JsonObject version = await GetObjectAsync("rest/system/version", cancellationToken);
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
            version["version"]?.GetValue<string>() ?? status["version"]?.GetValue<string>());
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

    public async Task<SyncthingFolderConfiguration?> GetFolderAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JsonObject folder = await GetObjectAsync(
                "rest/config/folders/" + Uri.EscapeDataString(folderId),
                cancellationToken);
            List<string> deviceIds = [];
            if (folder["devices"] is JsonArray devices)
            {
                deviceIds.AddRange(devices
                    .Select(device => device?["deviceID"]?.GetValue<string>())
                    .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
                    .Select(deviceId => deviceId!));
            }

            return new SyncthingFolderConfiguration(
                folder["id"]?.GetValue<string>() ?? folderId,
                folder["label"]?.GetValue<string>() ?? string.Empty,
                folder["path"]?.GetValue<string>() ?? string.Empty,
                deviceIds,
                folder["type"]?.GetValue<string>() ?? "sendreceive",
                folder["versioning"]?["type"]?.GetValue<string>() ?? string.Empty,
                Paused: folder["paused"]?.GetValue<bool>() == true,
                RescanIntervalSeconds: folder["rescanIntervalS"]?.GetValue<int>() ?? 60,
                FileWatcherEnabled: folder["fsWatcherEnabled"]?.GetValue<bool>() != false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<SyncthingDeviceConfiguration>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        JsonNode? node = await GetNodeAsync("rest/config/devices", cancellationToken);
        if (node is not JsonArray devices)
        {
            return [];
        }

        return devices.OfType<JsonObject>().Select(device => new SyncthingDeviceConfiguration(
            device["deviceID"]?.GetValue<string>() ?? string.Empty,
            device["name"]?.GetValue<string>() ?? string.Empty,
            device["addresses"] is JsonArray addresses
                ? addresses.Select(address => address?.GetValue<string>())
                    .Where(address => !string.IsNullOrWhiteSpace(address))
                    .Select(address => address!)
                    .ToArray()
                : null,
            device["paused"]?.GetValue<bool>() == true)).ToArray();
    }

    public async Task<SyncthingFolderStatus> GetFolderStatusAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        JsonObject database = await GetObjectAsync(
            "rest/db/status?folder=" + Uri.EscapeDataString(folderId),
            cancellationToken);
        DateTimeOffset? stateChangedAt = DateTimeOffset.TryParse(
            database["stateChanged"]?.GetValue<string>(),
            out DateTimeOffset parsed)
            ? parsed
            : null;
        return new SyncthingFolderStatus(
            folderId,
            database["state"]?.GetValue<string>() ?? "unknown",
            stateChangedAt,
            database["globalFiles"]?.GetValue<long>() ?? 0,
            database["localFiles"]?.GetValue<long>() ?? 0,
            database["inSyncFiles"]?.GetValue<long>() ?? 0,
            database["needFiles"]?.GetValue<long>() ?? 0,
            database["needBytes"]?.GetValue<long>() ?? 0,
            database["receiveOnlyChangedFiles"]?.GetValue<long>() ?? 0,
            database["receiveOnlyChangedBytes"]?.GetValue<long>() ?? 0,
            (database["pullErrors"]?.GetValue<long>() ?? 0) +
            (string.IsNullOrWhiteSpace(database["invalid"]?.GetValue<string>()) ? 0 : 1));
    }

    public async Task<IReadOnlyList<SyncthingDifferenceItem>> GetDifferencesAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        List<SyncthingDifferenceItem> result = [];
        await AppendDifferencesAsync(
            result,
            "rest/db/need?folder=" + Uri.EscapeDataString(folderId) + "&page=1&perpage=250",
            "Incoming",
            cancellationToken);
        await AppendDifferencesAsync(
            result,
            "rest/db/localchanged?folder=" + Uri.EscapeDataString(folderId) + "&page=1&perpage=250",
            "Local change",
            cancellationToken);
        foreach (SyncthingDeviceConfiguration device in await GetDevicesAsync(cancellationToken))
        {
            try
            {
                await AppendDifferencesAsync(
                    result,
                    "rest/db/remoteneed?folder=" + Uri.EscapeDataString(folderId) +
                    "&device=" + Uri.EscapeDataString(device.DeviceId) +
                    "&page=1&perpage=250",
                    "Outgoing to " + (string.IsNullOrWhiteSpace(device.Name) ? device.DeviceId[..Math.Min(12, device.DeviceId.Length)] : device.Name),
                    cancellationToken);
            }
            catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
            {
                // Older or temporarily unshared devices may not expose remote need information.
            }
        }
        return result
            .OrderBy(item => item.Direction, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<SyncthingLogEntry>> GetLogsAsync(
        CancellationToken cancellationToken = default)
    {
        JsonObject root = await GetObjectAsync("rest/system/log", cancellationToken);
        if (root["messages"] is not JsonArray messages)
        {
            return [];
        }

        return messages.OfType<JsonObject>().Select(message =>
        {
            DateTimeOffset? timestamp = DateTimeOffset.TryParse(
                message["when"]?.GetValue<string>(),
                out DateTimeOffset parsed)
                ? parsed
                : null;
            return new SyncthingLogEntry(
                timestamp,
                message["message"]?.GetValue<string>() ?? string.Empty,
                message["level"]?.GetValue<string>() ?? "Info",
                message["facility"]?.GetValue<string>() ?? "Syncthing");
        }).ToArray();
    }

    public Task ScanFolderAsync(string folderId, CancellationToken cancellationToken = default) =>
        PostEmptyAsync("rest/db/scan?folder=" + Uri.EscapeDataString(folderId), cancellationToken);

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
            rescanIntervalS = folder.RescanIntervalSeconds,
            fsWatcherEnabled = folder.FileWatcherEnabled,
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

    private async Task AppendDifferencesAsync(
        ICollection<SyncthingDifferenceItem> destination,
        string path,
        string direction,
        CancellationToken cancellationToken)
    {
        JsonObject root = await GetObjectAsync(path, cancellationToken);
        IEnumerable<JsonObject> files = new[] { "files", "progress", "queued", "rest" }
            .SelectMany(section => root[section] is JsonArray array
                ? array.OfType<JsonObject>()
                : []);
        foreach (JsonObject file in files)
        {
            DateTimeOffset? modifiedAt = DateTimeOffset.TryParse(
                file["modified"]?.GetValue<string>(),
                out DateTimeOffset parsed)
                ? parsed
                : null;
            destination.Add(new SyncthingDifferenceItem(
                file["name"]?.GetValue<string>() ?? string.Empty,
                direction,
                file["size"]?.GetValue<long>() ?? 0,
                modifiedAt,
                file["deleted"]?.GetValue<bool>() == true,
                file["type"]?.GetValue<string>() ?? "file"));
        }
    }

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
