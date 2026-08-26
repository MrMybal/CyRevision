using System.Net.Http.Json;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.CyTask;

public sealed class CyTaskIntegrationPlugin : IWorkItemIntegrationPlugin
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _configurationDirectory;

    public CyTaskIntegrationPlugin()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    public CyTaskIntegrationPlugin(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private CyTaskIntegrationPlugin(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.cytask",
        "CyTask Tickets",
        "0.1.0",
        "Search CyTask tickets, attach stable links to revisions and update workflow states after merge.",
        "Project management");

    public WorkItemProviderDescriptor Provider { get; } = new(
        "cytask",
        "CyTask",
        "CyTask REST API v1. Uses a write-scoped API token or a native session token.",
        "https://cytask.local",
        "CYTASK_API_TOKEN",
        "Project ID",
        "Required CyTask project UUID",
        "Account",
        "Optional server label");

    public Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default)
    {
        _configurationDirectory = Path.Combine(context.ConfigurationDirectory, "work-items", Provider.Id);
        return Task.CompletedTask;
    }

    public async Task<WorkItemConnectionSettings> LoadConnectionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string path = GetSettingsPath(projectId);
        if (!File.Exists(path)) return CreateDefault(projectId);
        try
        {
            await using FileStream stream = File.OpenRead(path);
            WorkItemConnectionSettings? settings =
                await JsonSerializer.DeserializeAsync<WorkItemConnectionSettings>(
                    stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return settings is null
                ? CreateDefault(projectId)
                : Normalize(settings with { ProjectId = projectId });
        }
        catch (JsonException)
        {
            return CreateDefault(projectId);
        }
    }

    public async Task SaveConnectionAsync(
        WorkItemConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        _ = ValidateBaseUri(settings.BaseUrl);
        _ = ParseProjectId(settings.ScopeId);
        string path = GetSettingsPath(settings.ProjectId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        File.Move(temporary, path, true);
    }

    public async Task<WorkItemConnectionTestResult> TestConnectionAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Guid projectId = ParseProjectId(settings.ScopeId);
            using HttpRequestMessage request = CreateRequest(settings, sessionToken, HttpMethod.Get, "/api/v1/projects");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));

            using JsonDocument payload = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (payload.RootElement.ValueKind != JsonValueKind.Array)
                return new(false, "CyTask returned an invalid project list.");
            JsonElement? project = payload.RootElement.EnumerateArray().FirstOrDefault(
                candidate => GetGuid(candidate, "id") == projectId);
            if (project is null || project.Value.ValueKind == JsonValueKind.Undefined)
                return new(false, $"Project {projectId} is not available to this CyTask token.");
            return new(true, $"Connected to {GetString(project.Value, "name") ?? projectId.ToString()}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, exception.Message);
        }
    }

    public async Task<IReadOnlyList<WorkItemReference>> SearchAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        string query,
        int maximumResults = 50,
        CancellationToken cancellationToken = default)
    {
        Guid projectId = ParseProjectId(settings.ScopeId);
        maximumResults = Math.Clamp(maximumResults, 1, 100);
        using HttpRequestMessage request = CreateRequest(
            settings, sessionToken, HttpMethod.Get, $"/api/v1/projects/{projectId}/tasks");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));

        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (payload.RootElement.ValueKind != JsonValueKind.Array) return [];

        string term = query.Trim();
        List<WorkItemReference> results = [];
        foreach (JsonElement task in payload.RootElement.EnumerateArray())
        {
            string key = GetString(task, "key") ?? string.Empty;
            string title = GetString(task, "title") ?? string.Empty;
            string status = GetString(task, "status") ?? string.Empty;
            string description = GetString(task, "description") ?? string.Empty;
            if (term.Length > 0 && !new[] { key, title, status, description }
                    .Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
                continue;
            WorkItemReference? item = ParseWorkItem(settings, task);
            if (item is not null) results.Add(item);
            if (results.Count >= maximumResults) break;
        }
        return results;
    }

    public async Task<WorkItemReference?> ResolveAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        string identifierOrUrl,
        CancellationToken cancellationToken = default)
    {
        string identifier = ExtractIdentifier(identifierOrUrl);
        if (identifier.Length == 0) return null;
        if (!Guid.TryParse(identifier, out Guid taskId))
        {
            return (await SearchAsync(settings, sessionToken, identifier, 100, cancellationToken)
                    .ConfigureAwait(false))
                .FirstOrDefault(item => string.Equals(item.Key, identifier, StringComparison.OrdinalIgnoreCase));
        }

        using HttpRequestMessage request = CreateRequest(
            settings, sessionToken, HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return payload.RootElement.TryGetProperty("task", out JsonElement task)
            ? ParseWorkItem(settings, task)
            : null;
    }

    public async Task<IReadOnlyList<WorkItemTransitionOption>> GetTransitionsAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        WorkItemReference workItem,
        CancellationToken cancellationToken = default)
    {
        Guid projectId = ParseProjectId(settings.ScopeId);
        using HttpRequestMessage request = CreateRequest(
            settings, sessionToken, HttpMethod.Get, $"/api/v1/projects/{projectId}/statuses");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (payload.RootElement.ValueKind != JsonValueKind.Array) return [];

        return payload.RootElement.EnumerateArray().Select(status =>
        {
            string id = GetString(status, "key") ?? string.Empty;
            string name = GetString(status, "name") ?? id;
            return new WorkItemTransitionOption(id, name, IsCompletion(id, name));
        }).Where(option => option.Id.Length > 0).ToArray();
    }

    public async Task<WorkItemStatusUpdateResult> ApplyTransitionAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        WorkItemReference workItem,
        WorkItemTransitionOption transition,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(workItem.Id, out Guid taskId))
            throw new InvalidOperationException("The CyTask work item ID is invalid.");

        using HttpRequestMessage currentRequest = CreateRequest(
            settings, sessionToken, HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        using HttpResponseMessage currentResponse = await _httpClient.SendAsync(currentRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!currentResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(currentResponse, cancellationToken)
                .ConfigureAwait(false));
        using JsonDocument currentPayload = JsonDocument.Parse(
            await currentResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!currentPayload.RootElement.TryGetProperty("task", out JsonElement task))
            throw new InvalidOperationException("CyTask returned an invalid task.");

        string[] assigneeIds = task.TryGetProperty("assignees", out JsonElement assignees)
            && assignees.ValueKind == JsonValueKind.Array
            ? assignees.EnumerateArray()
                .Select(assignee => GetString(assignee, "userId"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray()
            : [];
        object update = new
        {
            title = GetString(task, "title") ?? workItem.Title,
            description = GetString(task, "description") ?? string.Empty,
            status = transition.Id,
            priority = GetString(task, "priority") ?? "normal",
            dueAt = GetString(task, "dueAt"),
            assigneeIds,
            expectedRevision = GetInt64(task, "revision")
        };

        using HttpRequestMessage updateRequest = CreateRequest(
            settings, sessionToken, HttpMethod.Patch, $"/api/v1/tasks/{taskId}");
        updateRequest.Content = JsonContent.Create(update);
        using HttpResponseMessage updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!updateResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(updateResponse, cancellationToken)
                .ConfigureAwait(false));

        return new(
            workItem with { Status = transition.Name },
            workItem.Status,
            transition.Name);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private WorkItemReference? ParseWorkItem(WorkItemConnectionSettings settings, JsonElement task)
    {
        string? id = GetString(task, "id");
        string? key = GetString(task, "key");
        if (!Guid.TryParse(id, out _) || string.IsNullOrWhiteSpace(key)) return null;
        string baseUrl = ValidateBaseUri(settings.BaseUrl).GetLeftPart(UriPartial.Authority);
        string configuredPath = ValidateBaseUri(settings.BaseUrl).AbsolutePath.TrimEnd('/');
        string applicationRoot = baseUrl + configuredPath;
        return new(
            Provider.Id,
            Provider.Name,
            id,
            key,
            GetString(task, "title")?.Trim() ?? string.Empty,
            GetString(task, "status")?.Trim() ?? string.Empty,
            applicationRoot.TrimEnd('/') + "/#/tasks/" + id);
    }

    private HttpRequestMessage CreateRequest(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        HttpMethod method,
        string path)
    {
        Uri baseUri = ValidateBaseUri(settings.BaseUrl);
        string token = ResolveToken(settings, sessionToken);
        Uri requestUri = new(baseUri.ToString().TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);
        HttpRequestMessage request = new(method, requestUri);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static Uri ValidateBaseUri(string candidate)
    {
        if (!Uri.TryCreate(candidate.Trim().TrimEnd('/'), UriKind.Absolute, out Uri? uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("Enter a valid CyTask HTTPS server URL.");
        return uri;
    }

    private static Guid ParseProjectId(string scopeId) =>
        Guid.TryParse(scopeId.Trim(), out Guid projectId)
            ? projectId
            : throw new InvalidOperationException("Enter the CyTask project UUID.");

    private static string ResolveToken(WorkItemConnectionSettings settings, string? sessionToken)
    {
        string? token = string.IsNullOrWhiteSpace(sessionToken)
            ? Environment.GetEnvironmentVariable(settings.TokenEnvironmentVariable)
            : sessionToken.Trim();
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException(
                $"Enter a session token or define the {settings.TokenEnvironmentVariable} environment variable.")
            : token;
    }

    private WorkItemConnectionSettings CreateDefault(Guid projectId) => new(
        projectId,
        Provider.DefaultBaseUrl,
        string.Empty,
        string.Empty,
        Provider.DefaultTokenEnvironmentVariable);

    private WorkItemConnectionSettings Normalize(WorkItemConnectionSettings settings) => settings with
    {
        BaseUrl = settings.BaseUrl.Trim().TrimEnd('/'),
        ScopeId = settings.ScopeId.Trim(),
        AccountName = settings.AccountName.Trim(),
        TokenEnvironmentVariable = string.IsNullOrWhiteSpace(settings.TokenEnvironmentVariable)
            ? Provider.DefaultTokenEnvironmentVariable
            : settings.TokenEnvironmentVariable.Trim()
    };

    private string GetSettingsPath(Guid projectId)
    {
        if (string.IsNullOrWhiteSpace(_configurationDirectory))
            throw new InvalidOperationException("The CyTask plugin is not initialized.");
        return Path.Combine(_configurationDirectory, projectId.ToString("N") + ".json");
    }

    private static string ExtractIdentifier(string value)
    {
        string candidate = value.Trim();
        int tasks = candidate.IndexOf("/tasks/", StringComparison.OrdinalIgnoreCase);
        if (tasks >= 0) candidate = candidate[(tasks + "/tasks/".Length)..];
        int separator = candidate.IndexOfAny([' ', '?', '#', '/']);
        if (separator >= 0) candidate = candidate[..separator];
        return Uri.UnescapeDataString(candidate).Trim();
    }

    private static bool IsCompletion(string id, string name) =>
        id.Equals("done", StringComparison.OrdinalIgnoreCase)
        || id.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
        || name.Contains("done", StringComparison.OrdinalIgnoreCase)
        || name.Contains("close", StringComparison.OrdinalIgnoreCase)
        || name.Contains("complete", StringComparison.OrdinalIgnoreCase)
        || name.Contains("termin", StringComparison.OrdinalIgnoreCase)
        || name.Contains("annul", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static Guid GetGuid(JsonElement element, string propertyName) =>
        Guid.TryParse(GetString(element, propertyName), out Guid value) ? value : Guid.Empty;

    private static long GetInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : throw new InvalidOperationException($"CyTask field {propertyName} is invalid.");

    private static async Task<string> CreateApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > 600) detail = detail[..600] + "…";
        return $"CyTask API returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim();
    }
}
