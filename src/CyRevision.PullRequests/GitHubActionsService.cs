using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CyRevision.PullRequests;

public sealed class GitHubActionsService : ICiWorkflowService, IDisposable
{
    private const string ApiVersion = "2022-11-28";
    private readonly HttpClient _httpClient;

    public GitHubActionsService(HttpMessageHandler? handler = null) =>
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);

    public bool TryResolveRepository(string remoteUrl, string? apiBaseUrl, out PullRequestRepository? repository) =>
        GitRemoteRepositoryParser.TryParseGitHub(remoteUrl, apiBaseUrl, out repository);

    public async Task<IReadOnlyList<CiWorkflow>> ListWorkflowsAsync(
        PullRequestRepository repository,
        string? token,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await SendForJsonAsync(
            repository,
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/actions/workflows?per_page=100",
            token,
            null,
            cancellationToken);
        if (!document.RootElement.TryGetProperty("workflows", out JsonElement workflows)) return [];
        return workflows.EnumerateArray().Select(item => new CiWorkflow(
            item.GetProperty("id").GetInt64(),
            ReadString(item, "name", "Workflow"),
            ReadString(item, "path", string.Empty),
            ReadString(item, "state", string.Empty))).ToArray();
    }

    public async Task<IReadOnlyList<CiWorkflowRun>> ListRunsAsync(
        PullRequestRepository repository,
        string? token,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await SendForJsonAsync(
            repository,
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/actions/runs?per_page=50",
            token,
            null,
            cancellationToken);
        if (!document.RootElement.TryGetProperty("workflow_runs", out JsonElement runs)) return [];
        return runs.EnumerateArray().Select(ParseRun).ToArray();
    }

    public async Task<CiWorkflowRunDetails> GetRunDetailsAsync(
        PullRequestRepository repository,
        CiWorkflowRun run,
        string? token,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await SendForJsonAsync(
            repository,
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/actions/runs/{run.Id}/jobs?per_page=100",
            token,
            null,
            cancellationToken);
        if (!document.RootElement.TryGetProperty("jobs", out JsonElement jobs))
            return new CiWorkflowRunDetails(run, []);

        CiWorkflowJob[] result = jobs.EnumerateArray().Select(job =>
        {
            IReadOnlyList<CiWorkflowStep> steps = job.TryGetProperty("steps", out JsonElement stepItems)
                ? stepItems.EnumerateArray().Select(step => new CiWorkflowStep(
                    step.TryGetProperty("number", out JsonElement number) ? number.GetInt32() : 0,
                    ReadString(step, "name", "Step"),
                    ReadString(step, "status", string.Empty),
                    ReadString(step, "conclusion", string.Empty),
                    ReadDate(step, "started_at"),
                    ReadDate(step, "completed_at"))).ToArray()
                : [];
            return new CiWorkflowJob(
                job.GetProperty("id").GetInt64(),
                ReadString(job, "name", "Job"),
                ReadString(job, "status", string.Empty),
                ReadString(job, "conclusion", string.Empty),
                ReadDate(job, "started_at"),
                ReadDate(job, "completed_at"),
                ReadUri(job, "html_url"),
                steps);
        }).ToArray();
        return new CiWorkflowRunDetails(run, result);
    }

    public Task DispatchAsync(
        PullRequestRepository repository,
        CiWorkflow workflow,
        string gitRef,
        IReadOnlyDictionary<string, string> inputs,
        string token,
        CancellationToken cancellationToken = default) => SendWithoutContentAsync(
            repository,
            HttpMethod.Post,
            $"repos/{repository.Owner}/{repository.Name}/actions/workflows/{workflow.Id}/dispatches",
            token,
            JsonSerializer.Serialize(new { @ref = gitRef, inputs }),
            cancellationToken);

    public Task RerunFailedJobsAsync(
        PullRequestRepository repository,
        long runId,
        string token,
        CancellationToken cancellationToken = default) => SendWithoutContentAsync(
            repository,
            HttpMethod.Post,
            $"repos/{repository.Owner}/{repository.Name}/actions/runs/{runId}/rerun-failed-jobs",
            token,
            "{}",
            cancellationToken);

    public Task CancelRunAsync(
        PullRequestRepository repository,
        long runId,
        string token,
        CancellationToken cancellationToken = default) => SendWithoutContentAsync(
            repository,
            HttpMethod.Post,
            $"repos/{repository.Owner}/{repository.Name}/actions/runs/{runId}/cancel",
            token,
            "{}",
            cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<JsonDocument> SendForJsonAsync(
        PullRequestRepository repository,
        HttpMethod method,
        string relativePath,
        string? token,
        string? body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(repository, method, relativePath, token, body);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
    }

    private async Task SendWithoutContentAsync(
        PullRequestRepository repository,
        HttpMethod method,
        string relativePath,
        string token,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("A GitHub token is required to control CI workflows.");
        using HttpRequestMessage request = CreateRequest(repository, method, relativePath, token, body);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
    }

    private static HttpRequestMessage CreateRequest(
        PullRequestRepository repository,
        HttpMethod method,
        string relativePath,
        string? token,
        string? body)
    {
        Uri uri = new(repository.ApiBaseUri, relativePath);
        HttpRequestMessage request = new(method, uri);
        request.Headers.UserAgent.ParseAdd("CyRevision/0.1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode) return;
        string message = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            message = ReadString(document.RootElement, "message", string.Empty);
        }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PullRequestProviderException(response.StatusCode,
                "GitHub Actions was not found or the current token cannot read this repository.");
        throw new PullRequestProviderException(response.StatusCode,
            string.IsNullOrWhiteSpace(message)
                ? $"GitHub Actions returned {(int)response.StatusCode} {response.StatusCode}."
                : $"GitHub Actions: {message}");
    }

    private static CiWorkflowRun ParseRun(JsonElement item) => new(
        item.GetProperty("id").GetInt64(),
        ReadString(item, "name", "Workflow run"),
        ReadString(item, "event", string.Empty),
        ReadString(item, "status", string.Empty),
        ReadString(item, "conclusion", string.Empty),
        ReadString(item, "head_branch", string.Empty),
        ReadString(item, "head_sha", string.Empty),
        ReadDate(item, "created_at") ?? DateTimeOffset.MinValue,
        ReadDate(item, "updated_at") ?? DateTimeOffset.MinValue,
        ReadUri(item, "html_url") ?? new Uri("https://github.com/"));

    private static string ReadString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static DateTimeOffset? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), out DateTimeOffset result)
            ? result
            : null;

    private static Uri? ReadUri(JsonElement element, string property) =>
        Uri.TryCreate(ReadString(element, property, string.Empty), UriKind.Absolute, out Uri? uri) ? uri : null;
}
