using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CyRevision.Discord;

public sealed class DiscordWebhookClient : IDisposable
{
    private const int DiscordBlurple = 0x5865F2;
    private const int DiscordGreen = 0x23A55A;
    private static readonly JsonSerializerOptions WebhookJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public DiscordWebhookClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _ownsClient = httpClient is null;
    }

    public Task SendTestAsync(
        DiscordAgentProfile profile,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        string label = ResolveProjectLabel(profile, projectName);
        WebhookPayload payload = new(
            profile.DisplayName.Trim(),
            [new WebhookEmbed(
                $"[{label}] CyRevision connection test",
                "The Discord agent is configured for this channel. No project history was published.",
                DiscordGreen,
                DateTimeOffset.UtcNow,
                NormalizeRepositoryUrl(profile.RepositoryWebUrl),
                new WebhookFooter("CyRevision Discord agent"))],
            new AllowedMentions([]));
        return SendAsync(profile.WebhookUrl, payload, cancellationToken);
    }

    public Task SendProjectUpdateAsync(
        DiscordAgentProfile profile,
        string projectName,
        DiscordProjectSnapshot snapshot,
        IReadOnlyList<DiscordCommit> commits,
        string? previousBranch,
        bool historyRewritten,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        List<WebhookEmbed> embeds = [];
        string label = ResolveProjectLabel(profile, projectName);
        if (profile.NotifyCommits && commits.Count > 0)
        {
            string commitText = BuildCommitDescription(commits, historyRewritten);
            embeds.Add(new WebhookEmbed(
                $"[{label}:{snapshot.Branch}] {commits.Count} new commit{(commits.Count == 1 ? string.Empty : "s")}",
                commitText,
                DiscordBlurple,
                DateTimeOffset.UtcNow,
                NormalizeRepositoryUrl(profile.RepositoryWebUrl),
                new WebhookFooter("CyRevision Discord agent")));
        }

        if (profile.NotifyBranchChanges &&
            !string.IsNullOrWhiteSpace(previousBranch) &&
            !string.Equals(previousBranch, snapshot.Branch, StringComparison.Ordinal))
        {
            embeds.Add(new WebhookEmbed(
                $"[{label}] Active branch changed",
                $"`{Sanitize(previousBranch, 120)}` → `{Sanitize(snapshot.Branch, 120)}`",
                DiscordGreen,
                DateTimeOffset.UtcNow,
                NormalizeRepositoryUrl(profile.RepositoryWebUrl),
                new WebhookFooter("CyRevision Discord agent")));
        }

        if (embeds.Count == 0)
        {
            return Task.CompletedTask;
        }

        WebhookPayload payload = new(profile.DisplayName.Trim(), embeds, new AllowedMentions([]));
        return SendAsync(profile.WebhookUrl, payload, cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task SendAsync(string webhookUrl, WebhookPayload payload, CancellationToken cancellationToken)
    {
        Uri executionUri = DiscordWebhookAddress.CreateExecutionUri(webhookUrl);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            executionUri,
            payload,
            WebhookJsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Discord rejected the webhook request ({(int)response.StatusCode} {response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }
    }

    private static string BuildCommitDescription(IReadOnlyList<DiscordCommit> commits, bool historyRewritten)
    {
        const int visibleCommitLimit = 10;
        IEnumerable<string> lines = commits.Take(visibleCommitLimit).Select(commit =>
            $"`{Sanitize(commit.ShortHash, 12)}` {Sanitize(commit.Subject, 220)} — {Sanitize(commit.AuthorName, 80)}");
        string description = string.Join('\n', lines);
        if (commits.Count > visibleCommitLimit)
        {
            description += $"\n…and {commits.Count - visibleCommitLimit} more commit(s).";
        }

        if (historyRewritten)
        {
            description += "\n\n_Previous baseline was not found; the branch history may have been rewritten._";
        }

        return description.Length <= 4096 ? description : description[..4093] + "…";
    }

    private static string ResolveProjectLabel(DiscordAgentProfile profile, string projectName) =>
        Sanitize(string.IsNullOrWhiteSpace(profile.ProjectLabel) ? projectName : profile.ProjectLabel, 100);

    private static string? NormalizeRepositoryUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;

    private static string Sanitize(string? value, int maximumLength)
    {
        string normalized = Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim().Replace('`', '\'');
        return normalized.Length <= maximumLength ? normalized : normalized[..(maximumLength - 1)] + "…";
    }

    private sealed record WebhookPayload(
        string Username,
        IReadOnlyList<WebhookEmbed> Embeds,
        [property: JsonPropertyName("allowed_mentions")] AllowedMentions AllowedMentions);

    private sealed record WebhookEmbed(
        string Title,
        string Description,
        int Color,
        DateTimeOffset Timestamp,
        string? Url,
        WebhookFooter Footer);

    private sealed record WebhookFooter(string Text);

    private sealed record AllowedMentions(IReadOnlyList<string> Parse);
}
