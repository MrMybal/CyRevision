using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CyRevision.PullRequests;

public sealed class PullRequestProviderException(
    HttpStatusCode statusCode,
    string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class GitHubPullRequestService : IPullRequestService
{
    private const string ApiVersion = "2026-03-10";
    private readonly HttpClient _httpClient;

    public GitHubPullRequestService(HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public bool TryResolveRepository(
        string remoteUrl,
        string? apiBaseUrl,
        out PullRequestRepository? repository) =>
        GitRemoteRepositoryParser.TryParseGitHub(remoteUrl, apiBaseUrl, out repository);

    public async Task<IReadOnlyList<PullRequestSummary>> ListAsync(
        PullRequestRepository repository,
        PullRequestStateFilter state,
        string? token,
        CancellationToken cancellationToken = default)
    {
        string stateValue = state.ToString().ToLowerInvariant();
        IReadOnlyList<JsonElement> items = await GetPagedArrayAsync(
            repository,
            $"pulls?state={stateValue}&sort=updated&direction=desc",
            token,
            cancellationToken);
        return items.Select(ParseSummary).ToArray();
    }

    public async Task<PullRequestDetails> GetDetailsAsync(
        PullRequestRepository repository,
        int number,
        string? token,
        CancellationToken cancellationToken = default)
    {
        EnsureNumber(number);
        using JsonDocument pull = await SendJsonAsync(
            repository,
            HttpMethod.Get,
            $"pulls/{number}",
            token,
            null,
            cancellationToken);
        IReadOnlyList<JsonElement> files = await GetPagedArrayAsync(
            repository, $"pulls/{number}/files", token, cancellationToken);
        IReadOnlyList<JsonElement> commits = await GetPagedArrayAsync(
            repository, $"pulls/{number}/commits", token, cancellationToken);
        IReadOnlyList<JsonElement> reviews = await GetPagedArrayAsync(
            repository, $"pulls/{number}/reviews", token, cancellationToken);
        IReadOnlyList<JsonElement> comments = await GetPagedArrayAsync(
            repository, $"issues/{number}/comments", token, cancellationToken);
        JsonElement root = pull.RootElement;
        return new PullRequestDetails(
            ParseSummary(root),
            GetString(root, "body"),
            GetInt(root, "additions"),
            GetInt(root, "deletions"),
            GetInt(root, "changed_files"),
            GetInt(root, "commits"),
            GetInt(root, "comments") + GetInt(root, "review_comments"),
            commits.Select(ParseCommit).ToArray(),
            files.Select(ParseFile).ToArray(),
            reviews.Select(ParseReview).ToArray(),
            comments.Select(ParseComment).ToArray());
    }

    public async Task<PullRequestSummary> CreateAsync(
        PullRequestRepository repository,
        CreatePullRequestRequest request,
        string token,
        CancellationToken cancellationToken = default)
    {
        RequireToken(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HeadBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseBranch);
        using JsonDocument document = await SendJsonAsync(
            repository,
            HttpMethod.Post,
            "pulls",
            token,
            new
            {
                title = request.Title.Trim(),
                body = request.Body.Trim(),
                head = request.HeadBranch.Trim(),
                @base = request.BaseBranch.Trim(),
                draft = request.IsDraft,
                maintainer_can_modify = request.MaintainerCanModify
            },
            cancellationToken);
        return ParseSummary(document.RootElement);
    }

    public async Task AddCommentAsync(
        PullRequestRepository repository,
        int number,
        string body,
        string token,
        CancellationToken cancellationToken = default)
    {
        EnsureNumber(number);
        RequireToken(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        using JsonDocument _ = await SendJsonAsync(
            repository,
            HttpMethod.Post,
            $"issues/{number}/comments",
            token,
            new { body = body.Trim() },
            cancellationToken);
    }

    public async Task SubmitReviewAsync(
        PullRequestRepository repository,
        int number,
        string body,
        PullRequestReviewAction action,
        string token,
        CancellationToken cancellationToken = default)
    {
        EnsureNumber(number);
        RequireToken(token);
        if (action is PullRequestReviewAction.Comment or PullRequestReviewAction.RequestChanges)
            ArgumentException.ThrowIfNullOrWhiteSpace(body);
        string eventName = action switch
        {
            PullRequestReviewAction.Approve => "APPROVE",
            PullRequestReviewAction.RequestChanges => "REQUEST_CHANGES",
            _ => "COMMENT"
        };
        using JsonDocument _ = await SendJsonAsync(
            repository,
            HttpMethod.Post,
            $"pulls/{number}/reviews",
            token,
            new { body = body.Trim(), @event = eventName },
            cancellationToken);
    }

    public async Task<MergePullRequestResult> MergeAsync(
        PullRequestRepository repository,
        int number,
        PullRequestMergeMethod method,
        string token,
        CancellationToken cancellationToken = default)
    {
        EnsureNumber(number);
        RequireToken(token);
        using JsonDocument document = await SendJsonAsync(
            repository,
            HttpMethod.Put,
            $"pulls/{number}/merge",
            token,
            new { merge_method = method.ToString().ToLowerInvariant() },
            cancellationToken);
        JsonElement root = document.RootElement;
        MergePullRequestResult result = new(
            GetBool(root, "merged"),
            GetString(root, "message"),
            GetNullableString(root, "sha"));
        if (!result.Merged)
            throw new PullRequestProviderException(HttpStatusCode.Conflict,
                string.IsNullOrWhiteSpace(result.Message) ? "GitHub did not merge the pull request." : result.Message);
        return result;
    }

    public async Task SetStateAsync(
        PullRequestRepository repository,
        int number,
        bool open,
        string token,
        CancellationToken cancellationToken = default)
    {
        EnsureNumber(number);
        RequireToken(token);
        using JsonDocument _ = await SendJsonAsync(
            repository,
            HttpMethod.Patch,
            $"pulls/{number}",
            token,
            new { state = open ? "open" : "closed" },
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<IReadOnlyList<JsonElement>> GetPagedArrayAsync(
        PullRequestRepository repository,
        string relativePath,
        string? token,
        CancellationToken cancellationToken)
    {
        List<JsonElement> result = [];
        for (int page = 1; page <= 30; page++)
        {
            string separator = relativePath.Contains('?') ? "&" : "?";
            using JsonDocument document = await SendJsonAsync(
                repository,
                HttpMethod.Get,
                $"{relativePath}{separator}per_page=100&page={page}",
                token,
                null,
                cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new PullRequestProviderException(HttpStatusCode.UnprocessableEntity, "GitHub returned an invalid list response.");
            JsonElement[] pageItems = document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
            result.AddRange(pageItems);
            if (pageItems.Length < 100) break;
        }
        return result;
    }

    private async Task<JsonDocument> SendJsonAsync(
        PullRequestRepository repository,
        HttpMethod method,
        string relativePath,
        string? token,
        object? body,
        CancellationToken cancellationToken)
    {
        Uri endpoint = BuildEndpoint(repository, relativePath);
        using HttpRequestMessage request = new(method, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        string applicationVersion = typeof(GitHubPullRequestService).Assembly.GetName().Version?.ToString(3) ?? "0.1.13";
        request.Headers.UserAgent.ParseAdd($"CyRevision/{applicationVersion}");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        if (body is not null) request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                string authenticationHint = string.IsNullOrWhiteSpace(token)
                    ? "The repository may be private. Sign in through Git Credential Manager or provide a session token with repository read access."
                    : "Verify that the token can access this repository and that organization SSO is authorized.";
                throw new PullRequestProviderException(
                    response.StatusCode,
                    $"GitHub API: repository or pull requests not found. {authenticationHint}");
            }
            throw new PullRequestProviderException(response.StatusCode, ParseError(payload, response.StatusCode));
        }
        return string.IsNullOrWhiteSpace(payload)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(payload);
    }

    private static Uri BuildEndpoint(PullRequestRepository repository, string relativePath)
    {
        string owner = Uri.EscapeDataString(repository.Owner);
        string name = Uri.EscapeDataString(repository.Name);
        return new Uri(repository.ApiBaseUri, $"repos/{owner}/{name}/{relativePath.TrimStart('/')}");
    }

    private static PullRequestSummary ParseSummary(JsonElement item)
    {
        string htmlUrl = GetString(item, "html_url");
        return new PullRequestSummary(
            GetInt(item, "number"),
            GetString(item, "title"),
            GetString(item, "state"),
            GetBool(item, "draft"),
            GetNestedString(item, "user", "login"),
            GetNestedString(item, "head", "ref"),
            GetNestedString(item, "base", "ref"),
            GetDate(item, "created_at"),
            GetDate(item, "updated_at"),
            Uri.TryCreate(htmlUrl, UriKind.Absolute, out Uri? uri) ? uri : new Uri("https://github.com/"),
            GetBool(item, "merged"),
            GetNullableBool(item, "mergeable"));
    }

    private static PullRequestFile ParseFile(JsonElement item)
    {
        string htmlUrl = GetString(item, "blob_url");
        return new PullRequestFile(
            GetString(item, "filename"),
            GetString(item, "status"),
            GetInt(item, "additions"),
            GetInt(item, "deletions"),
            GetInt(item, "changes"),
            GetString(item, "patch"),
            Uri.TryCreate(htmlUrl, UriKind.Absolute, out Uri? uri) ? uri : null);
    }

    private static PullRequestReview ParseReview(JsonElement item) => new(
        GetLong(item, "id"),
        GetNestedString(item, "user", "login"),
        GetString(item, "state"),
        GetString(item, "body"),
        GetNullableDate(item, "submitted_at"));

    private static PullRequestComment ParseComment(JsonElement item)
    {
        string htmlUrl = GetString(item, "html_url");
        return new PullRequestComment(
            GetLong(item, "id"),
            GetNestedString(item, "user", "login"),
            GetString(item, "body"),
            GetDate(item, "created_at"),
            Uri.TryCreate(htmlUrl, UriKind.Absolute, out Uri? uri) ? uri : null);
    }

    private static PullRequestCommit ParseCommit(JsonElement item)
    {
        JsonElement commit = item.TryGetProperty("commit", out JsonElement nestedCommit)
            ? nestedCommit
            : default;
        JsonElement author = commit.ValueKind == JsonValueKind.Object &&
                             commit.TryGetProperty("author", out JsonElement nestedAuthor)
            ? nestedAuthor
            : default;
        string message = commit.ValueKind == JsonValueKind.Object ? GetString(commit, "message") : string.Empty;
        string subject = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;
        return new PullRequestCommit(
            GetString(item, "sha"),
            author.ValueKind == JsonValueKind.Object ? GetString(author, "name") : GetNestedString(item, "author", "login"),
            author.ValueKind == JsonValueKind.Object ? GetString(author, "email") : string.Empty,
            author.ValueKind == JsonValueKind.Object ? GetDate(author, "date") : DateTimeOffset.MinValue,
            subject);
    }

    private static string ParseError(string payload, HttpStatusCode statusCode)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            string message = GetString(document.RootElement, "message");
            return string.IsNullOrWhiteSpace(message)
                ? $"GitHub API returned {(int)statusCode} {statusCode}."
                : $"GitHub API: {message}";
        }
        catch (JsonException)
        {
            return $"GitHub API returned {(int)statusCode} {statusCode}.";
        }
    }

    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? GetNullableString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string GetNestedString(JsonElement item, string parent, string name) =>
        item.TryGetProperty(parent, out JsonElement nested) ? GetString(nested, name) : string.Empty;

    private static int GetInt(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;

    private static long GetLong(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long number) ? number : 0;

    private static bool GetBool(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True;

    private static bool? GetNullableBool(JsonElement item, string name) =>
        !item.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset GetDate(JsonElement item, string name) =>
        DateTimeOffset.TryParse(GetString(item, name), out DateTimeOffset date) ? date : DateTimeOffset.MinValue;

    private static DateTimeOffset? GetNullableDate(JsonElement item, string name) =>
        DateTimeOffset.TryParse(GetString(item, name), out DateTimeOffset date) ? date : null;

    private static void EnsureNumber(int number)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
    }

    private static void RequireToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("A GitHub token is required for this operation.");
    }
}
