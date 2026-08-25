using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Jira;

public sealed class JiraIntegrationPlugin : IWorkItemIntegrationPlugin
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _configurationDirectory;

    public JiraIntegrationPlugin()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    public JiraIntegrationPlugin(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private JiraIntegrationPlugin(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.jira",
        "Jira Tasks",
        "0.1.0",
        "Search Jira Cloud issues and attach stable task links to commit and pull-request drafts.",
        "Project management");

    public WorkItemProviderDescriptor Provider { get; } = new(
        "jira",
        "Jira",
        "Jira Cloud REST API v3. Uses an Atlassian account email and API token.",
        "https://your-domain.atlassian.net",
        "JIRA_API_TOKEN",
        "Project key",
        "Optional, for example GAME",
        "Account email",
        "name@company.com");

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
            using HttpRequestMessage request = CreateRequest(
                settings,
                sessionToken,
                HttpMethod.Get,
                "/rest/api/3/myself");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
            using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
            string name = GetString(payload.RootElement, "displayName") ??
                          GetString(payload.RootElement, "emailAddress") ??
                          "Jira account";
            return new(true, $"Connected as {name}.");
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
        maximumResults = Math.Clamp(maximumResults, 1, 100);
        string currentJql = string.IsNullOrWhiteSpace(settings.ScopeId)
            ? string.Empty
            : $"project = \"{EscapeJql(settings.ScopeId)}\" ORDER BY updated DESC";
        string path = "/rest/api/3/issue/picker?showSubTasks=true" +
                      $"&query={Uri.EscapeDataString(query.Trim())}" +
                      (currentJql.Length == 0 ? string.Empty : $"&currentJQL={Uri.EscapeDataString(currentJql)}");
        using HttpRequestMessage request = CreateRequest(settings, sessionToken, HttpMethod.Get, path);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));

        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false));
        List<WorkItemReference> results = [];
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        if (payload.RootElement.TryGetProperty("sections", out JsonElement sections) &&
            sections.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement section in sections.EnumerateArray())
            {
                if (!section.TryGetProperty("issues", out JsonElement issues) || issues.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement issue in issues.EnumerateArray())
                {
                    string key = GetString(issue, "key")?.Trim() ?? string.Empty;
                    if (key.Length == 0 || !keys.Add(key)) continue;
                    string title = GetString(issue, "summaryText") ?? GetString(issue, "summary") ?? string.Empty;
                    results.Add(new WorkItemReference(
                        Provider.Id,
                        Provider.Name,
                        GetString(issue, "id") ?? key,
                        key,
                        title.Trim(),
                        GetString(section, "label") ?? "Available",
                        $"{settings.BaseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(key)}"));
                    if (results.Count >= maximumResults) return results;
                }
            }
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
        string key = ExtractIssueKey(identifierOrUrl);
        if (key.Length == 0) return null;
        using HttpRequestMessage request = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Get,
            $"/rest/api/3/issue/{Uri.EscapeDataString(key)}?fields=summary,status");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));

        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        JsonElement root = payload.RootElement;
        JsonElement fields = root.TryGetProperty("fields", out JsonElement fieldObject)
            ? fieldObject
            : default;
        string resolvedKey = GetString(root, "key") ?? key;
        string title = fields.ValueKind == JsonValueKind.Object
            ? GetString(fields, "summary") ?? string.Empty
            : string.Empty;
        string status = fields.ValueKind == JsonValueKind.Object &&
                        fields.TryGetProperty("status", out JsonElement statusObject)
            ? GetString(statusObject, "name") ?? string.Empty
            : string.Empty;
        return new WorkItemReference(
            Provider.Id,
            Provider.Name,
            GetString(root, "id") ?? resolvedKey,
            resolvedKey,
            title.Trim(),
            status.Trim(),
            $"{settings.BaseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(resolvedKey)}");
    }

    public async Task<IReadOnlyList<WorkItemTransitionOption>> GetTransitionsAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        WorkItemReference workItem,
        CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        string key = ExtractIssueKey(workItem.DisplayKey);
        using HttpRequestMessage request = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Get,
            $"/rest/api/3/issue/{Uri.EscapeDataString(key)}/transitions");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));

        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!payload.RootElement.TryGetProperty("transitions", out JsonElement transitions) ||
            transitions.ValueKind != JsonValueKind.Array)
            return [];

        return transitions.EnumerateArray().Select(transition =>
        {
            string id = GetString(transition, "id") ?? string.Empty;
            string name = GetString(transition, "name") ?? id;
            string category = transition.TryGetProperty("to", out JsonElement target) &&
                              target.TryGetProperty("statusCategory", out JsonElement statusCategory)
                ? GetString(statusCategory, "key") ?? string.Empty
                : string.Empty;
            bool completion = category.Equals("done", StringComparison.OrdinalIgnoreCase) ||
                              IsCompletionName(name);
            return new WorkItemTransitionOption(id, name, completion);
        }).Where(transition => transition.Id.Length > 0).ToArray();
    }

    public async Task<WorkItemStatusUpdateResult> ApplyTransitionAsync(
        WorkItemConnectionSettings settings,
        string? sessionToken,
        WorkItemReference workItem,
        WorkItemTransitionOption transition,
        CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        string key = ExtractIssueKey(workItem.DisplayKey);
        using HttpRequestMessage request = CreateRequest(
            settings,
            sessionToken,
            HttpMethod.Post,
            $"/rest/api/3/issue/{Uri.EscapeDataString(key)}/transitions");
        request.Content = JsonContent.Create(new { transition = new { id = transition.Id } });
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await CreateApiErrorAsync(response, cancellationToken).ConfigureAwait(false));
        return new WorkItemStatusUpdateResult(
            workItem with { Status = transition.Name },
            workItem.Status,
            transition.Name);
    }

    private static string ExtractIssueKey(string value)
    {
        string candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int browse = Array.FindIndex(segments, segment => segment.Equals("browse", StringComparison.OrdinalIgnoreCase));
            candidate = browse >= 0 && browse + 1 < segments.Length ? segments[browse + 1] : segments.LastOrDefault() ?? string.Empty;
        }
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
            throw new InvalidOperationException("Enter a valid Jira site URL.");
        if (string.IsNullOrWhiteSpace(settings.AccountName))
            throw new InvalidOperationException("Enter the Atlassian account email used by Jira.");
        string token = ResolveToken(settings, sessionToken);
        HttpRequestMessage request = new(method, new Uri(baseUri, path));
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AccountName}:{token}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
            throw new InvalidOperationException("The Jira plugin is not initialized.");
        return Path.Combine(_configurationDirectory, projectId.ToString("N") + ".json");
    }

    private static string EscapeJql(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

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
        return $"Jira API returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim();
    }
}
