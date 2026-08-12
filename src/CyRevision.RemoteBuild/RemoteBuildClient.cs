using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CyRevision.RemoteBuild;

public sealed class RemoteBuildClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public RemoteBuildClient(RemoteBuildCredentials credentials, HttpMessageHandler? handler = null)
    {
        _endpoint = RemoteBuildEndpoint.Create(credentials.Profile.Endpoint, credentials.Profile.AllowPrivateHttp);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = _endpoint;
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken.Trim());
    }

    public async Task<RemoteBuildAgentStatus> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<RemoteBuildAgentStatus>("api/v1/health", cancellationToken)
        ?? throw new InvalidDataException("Remote build agent returned an empty health response.");

    public async Task<IReadOnlyList<RemoteBuildProjectDescriptor>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<RemoteBuildProjectDescriptor[]>("api/v1/projects", cancellationToken) ?? [];

    public async Task<RemoteBuildJobStatus> StartAsync(
        RemoteBuildConnectionProfile profile,
        string? snapshotPath,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using MultipartFormDataContent content = new();
        content.Add(new StringContent(profile.RecipeId), "recipeId");
        content.Add(new StringContent(profile.SourceMode.ToString()), "sourceMode");
        content.Add(new StringContent(expectedRevision ?? string.Empty), "expectedRevision");
        FileStream? snapshot = null;
        try
        {
            if (profile.SourceMode == RemoteBuildSourceMode.UploadedSnapshot)
            {
                if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
                    throw new FileNotFoundException("Build snapshot is missing.", snapshotPath);
                snapshot = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
                if (snapshot.Length > profile.MaximumUploadBytes)
                    throw new InvalidOperationException("Build snapshot exceeds the configured upload limit.");
                StreamContent fileContent = new(snapshot);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Add(fileContent, "snapshot", "source.zip");
            }

            using HttpResponseMessage response = await _http.PostAsync(
                $"api/v1/projects/{profile.ProjectId:D}/builds", content, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<RemoteBuildJobStatus>(cancellationToken: cancellationToken)
                   ?? throw new InvalidDataException("Remote build agent returned an empty job response.");
        }
        finally
        {
            if (snapshot is not null)
                await snapshot.DisposeAsync();
        }
    }

    public async Task<RemoteBuildJobStatus> GetJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<RemoteBuildJobStatus>(
            $"api/v1/projects/{projectId:D}/builds/{jobId:D}", cancellationToken)
        ?? throw new KeyNotFoundException("Remote build job was not found.");

    public async Task<string> DownloadArtifactsAsync(
        Guid projectId,
        Guid jobId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(
            $"api/v1/projects/{projectId:D}/builds/{jobId:D}/artifacts",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        string destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            File.Move(temporary, destination, true);
            return destination;
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    public async Task CancelAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.DeleteAsync(
            $"api/v1/projects/{projectId:D}/builds/{jobId:D}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Remote build agent returned {(int)response.StatusCode}: {detail}");
    }

    public void Dispose() => _http.Dispose();
}
