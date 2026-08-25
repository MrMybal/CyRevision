using System.Net.Http.Json;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.ClickUp;

public sealed class ClickUpIntegrationPlugin : IWorkItemIntegrationPlugin
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _configurationDirectory;

    public ClickUpIntegrationPlugin()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    public ClickUpIntegrationPlugin(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private ClickUpIntegrationPlugin(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.clickup",
        "ClickUp Tasks",
        "0.1.0",
        "List and search ClickUp Workspace tasks and attach stable links to commit and pull-request drafts.",
        "Project management");

    public WorkItemProviderDescriptor Provider { get; } = new(
        "clickup",
        "ClickUp",
        "ClickUp REST API v2. Uses a personal token or an OAuth access token for the session.",
        "https://api.clickup.com/api/v2",
        "CLICKUP_API_TOKEN",
        "Workspace ID",
        "Required, for example 1234567",
        "Account",
        "Optional label");

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
            WorkItemConnectionSettings? settings = await JsonSerializer.DeserializeAsync<WorkItemConnectionSettings>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return settings is null ? CreateDefault(projectId) : Normalize(settings with { ProjectId = projectId });
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
            using HttpRequestMessage request = CreateRequest(settings, sessionToken, HttpMethod.Get, "/team");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
            using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
            string workspace = FindWorkspaceName(payload.RootElement, settings.ScopeId);
            if (workspace.Length == 0 && !string.IsNullOrWhiteSpace(settings.ScopeId))
                return new(false, $"Workspace {settings.ScopeId} is not available to this ClickUp token.");
            return new(true, workspace.Length == 0 ? "Connected to ClickUp." : $"Connected to {workspace}.");
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
        settings = Normalize(settings);
        if (string.IsNullOrWhiteSpace(settings.ScopeId))
            throw new InvalidOperationException("Enter the ClickUp Workspace ID.");
        maximumResults = Math.Clamp(maximumResults, 1, 100);
        string path = $"/team/{Uri.EscapeDataString(settings.ScopeId)}/task" +
                      "?page=0&order_by=updated&reverse=true&subtasks=true&include_closed=true";
        using HttpRequestMessage request = CreateRequest(settings, sessionToken, HttpMethod.Get, path);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false));
        if (!payload.RootElement.TryGetProperty("tasks", out JsonElement tasks) || tasks.ValueKind != JsonValueKind.Array)
            return [];

        string term = query.Trim();
        List<WorkItemReference> results = [];
        foreach (JsonElement task in tasks.EnumerateArray())
        {
            string id = GetString(task, "id") ?? string.Empty;
            string key = GetString(task, "custom_id") ?? id;
            string title = GetString(task, "name") ?? string.Empty;
            string status = task.TryGetProperty("status", out JsonElement statusObject)
                ? GetString(statusObject, "status") ?? string.Empty
                : string.Empty;
            string list = task.TryGetProperty("list", out JsonElement listObject)
                ? GetString(listObject, "name") ?? string.Empty
                : string.Empty;
            string folder = task.TryGetProperty("folder", out JsonElement folderObject)
                ? GetString(folderObject, "name") ?? string.Empty
                : string.Empty;
            string space = task.TryGetProperty("space", out JsonElement spaceObject)
                ? GetString(spaceObject, "name") ?? string.Empty
                : string.Empty;
            if (term.Length > 0 && !new[] { id, key, title, status, list, folder, space }
                    .Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
                continue;
            string url = GetString(task, "url") ?? $"https://app.clickup.com/t/{Uri.EscapeDataString(id)}";
            results.Add(new WorkItemReference(
                Provider.Id,
                Provider.Name,
                id,
                key,
                title.Trim(),
                status.Trim(),
                url));
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
        settings = Normalize(settings);
        string identifier = ExtractTaskIdentifier(identifierOrUrl);
        if (identifier.Length == 0) return null;
        using HttpRequestMessage request = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Get,
            BuildTaskPath(settings, identifier));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));

        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return ParseWorkItem(payload.RootElement);
    }

    public async Task<IReadOnlyList<WorkItemTransitionOption>> GetTransitionsAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        WorkItemReference workItem,
        CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        using HttpRequestMessage taskRequest = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Get,
            BuildTaskPath(settings, workItem.Id));
        using HttpResponseMessage taskResponse = await _httpClient.SendAsync(taskRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!taskResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(taskResponse, cancellationToken).ConfigureAwait(false));
        using JsonDocument taskPayload = JsonDocument.Parse(
            await taskResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        string listId = taskPayload.RootElement.TryGetProperty("list", out JsonElement list)
            ? GetString(list, "id") ?? string.Empty
            : string.Empty;
        if (listId.Length == 0) return [];

        using HttpRequestMessage listRequest = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Get,
            $"/list/{Uri.EscapeDataString(listId)}");
        using HttpResponseMessage listResponse = await _httpClient.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!listResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(listResponse, cancellationToken).ConfigureAwait(false));
        using JsonDocument listPayload = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!listPayload.RootElement.TryGetProperty("statuses", out JsonElement statuses) ||
            statuses.ValueKind != JsonValueKind.Array)
            return [];

        return statuses.EnumerateArray().Select(status =>
        {
            string name = GetString(status, "status") ?? string.Empty;
            string type = GetString(status, "type") ?? string.Empty;
            bool completion = type.Equals("closed", StringComparison.OrdinalIgnoreCase) ||
                              IsCompletionName(name);
            return new WorkItemTransitionOption(name, name, completion);
        }).Where(status => status.Name.Length > 0).ToArray();
    }

    public async Task<WorkItemStatusUpdateResult> ApplyTransitionAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        WorkItemReference workItem,
        WorkItemTransitionOption transition,
        CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        using HttpRequestMessage request = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Put,
            $"/task/{Uri.EscapeDataString(workItem.Id)}");
        request.Content = JsonContent.Create(new { status = transition.Name });
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
        return new WorkItemStatusUpdateResult(
            workItem with { Status = transition.Name },
            workItem.Status,
            transition.Name);
    }

    private WorkItemReference ParseWorkItem(JsonElement task)
    {
        string id = GetString(task, "id") ?? string.Empty;
        string key = GetString(task, "custom_id") ?? id;
        string title = GetString(task, "name") ?? string.Empty;
        string status = task.TryGetProperty("status", out JsonElement statusObject)
            ? GetString(statusObject, "status") ?? string.Empty
            : string.Empty;
        string url = GetString(task, "url") ?? $"https://app.clickup.com/t/{Uri.EscapeDataString(id)}";
        return new WorkItemReference(
            Provider.Id,
            Provider.Name,
            id,
            key,
            title.Trim(),
            status.Trim(),
            url);
    }

    private static string BuildTaskPath(WorkItemConnectionSettings settings, string identifier)
    {
        string escaped = Uri.EscapeDataString(identifier);
        return identifier.Contains('-', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(settings.ScopeId)
            ? $"/task/{escaped}?custom_task_ids=true&team_id={Uri.EscapeDataString(settings.ScopeId)}"
            : $"/task/{escaped}";
    }

    private static string ExtractTaskIdentifier(string value)
    {
        string candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
            candidate = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        int separator = candidate.IndexOfAny([' ', '?', '#']);
        if (separator >= 0) candidate = candidate[..separator];
        return Uri.UnescapeDataString(candidate).Trim();
    }

    private static bool IsCompletionName(string name) =>
        name.Contains("done", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("close", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("resolve", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("termin", StringComparison.OrdinalIgnoreCase);
    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
        return ValueTask.CompletedTask;
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

    private HttpRequestMessage CreateRequest(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        HttpMethod method,
        string path)
    {
        settings = Normalize(settings);
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out Uri? baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Enter a valid ClickUp API base URL.");
        string token = ResolveToken(settings, sessionToken);
        Uri requestUri = new(settings.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);
        HttpRequestMessage request = new(method, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

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

    private string GetSettingsPath(Guid projectId)
    {
        if (string.IsNullOrWhiteSpace(_configurationDirectory))
            throw new InvalidOperationException("The ClickUp plugin is not initialized.");
        return Path.Combine(_configurationDirectory, projectId.ToString("N") + ".json");
    }

    private static string FindWorkspaceName(JsonElement root, string workspaceId)
    {
        if (!root.TryGetProperty("teams", out JsonElement teams) || teams.ValueKind != JsonValueKind.Array)
            return string.Empty;
        foreach (JsonElement team in teams.EnumerateArray())
        {
            string id = GetString(team, "id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(workspaceId) || string.Equals(id, workspaceId, StringComparison.Ordinal))
                return GetString(team, "name") ?? id;
        }
        return string.Empty;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.Number
                ? property.GetRawText()
                : null;

    private static async Task<string> CreateApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > 600) detail = detail[..600] + "…";
        return $"ClickUp API returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim();
    }
}
