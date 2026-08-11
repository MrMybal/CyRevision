using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CyRevision.Discord;

namespace CyRevision.Discord.Control;

public sealed class DiscordAgentControlClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _apiToken;

    public DiscordAgentControlClient(
        string endpoint,
        string apiToken,
        bool allowPrivateHttp = false,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiToken) || apiToken.Trim().Length < 32)
        {
            throw new InvalidDataException("The autonomous agent control token must contain at least 32 characters.");
        }

        BaseAddress = DiscordAgentEndpoint.Create(endpoint, allowPrivateHttp);
        _apiToken = apiToken.Trim();
        if (httpClient is null)
        {
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _ownsClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public Uri BaseAddress { get; }

    public Task<DiscordAgentHostStatus> GetHostStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync<DiscordAgentHostStatus>(HttpMethod.Get, "api/v1/health", null, cancellationToken);

    public async Task<DiscordAgentPublicStatus?> GetProjectStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<DiscordAgentPublicStatus>(
                HttpMethod.Get,
                $"api/v1/projects/{projectId:D}",
                null,
                cancellationToken);
        }
        catch (DiscordAgentControlException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<DiscordAgentCommandResult> ConfigureAsync(
        DiscordAgentConfigurationRequest configuration,
        CancellationToken cancellationToken = default) =>
        SendAsync<DiscordAgentCommandResult>(
            HttpMethod.Put,
            $"api/v1/projects/{configuration.ProjectId:D}",
            configuration,
            cancellationToken);

    public Task<DiscordAgentCommandResult> StartAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        SendCommandAsync(projectId, "start", HttpMethod.Post, cancellationToken);

    public Task<DiscordAgentCommandResult> StopAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        SendCommandAsync(projectId, "stop", HttpMethod.Post, cancellationToken);

    public Task<DiscordAgentCommandResult> PollNowAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        SendCommandAsync(projectId, "check", HttpMethod.Post, cancellationToken);

    public Task<DiscordAgentCommandResult> SendTestAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        SendCommandAsync(projectId, "test", HttpMethod.Post, cancellationToken);

    public Task<DiscordAgentCommandResult> RemoveAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        SendCommandAsync(projectId, string.Empty, HttpMethod.Delete, cancellationToken);

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private Task<DiscordAgentCommandResult> SendCommandAsync(
        Guid projectId,
        string command,
        HttpMethod method,
        CancellationToken cancellationToken)
    {
        string suffix = string.IsNullOrEmpty(command) ? string.Empty : "/" + command;
        return SendAsync<DiscordAgentCommandResult>(
            method,
            $"api/v1/projects/{projectId:D}{suffix}",
            null,
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(BaseAddress, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string details = await response.Content.ReadAsStringAsync(cancellationToken);
            if (details.Length > 500)
            {
                details = details[..500];
            }

            throw new DiscordAgentControlException(
                response.StatusCode,
                string.IsNullOrWhiteSpace(details)
                    ? $"Autonomous agent returned {(int)response.StatusCode} {response.ReasonPhrase}."
                    : details);
        }

        T? result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidDataException("The autonomous agent returned an empty response.");
    }
}

public sealed class DiscordAgentControlException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
