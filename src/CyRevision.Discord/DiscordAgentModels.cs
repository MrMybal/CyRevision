namespace CyRevision.Discord;

public sealed record DiscordAgentProfile(
    Guid ProjectId,
    string WebhookUrl,
    string DisplayName = "CyRevision",
    string? ProjectLabel = null,
    string? RepositoryWebUrl = null,
    int PollIntervalSeconds = 30,
    bool NotifyCommits = true,
    bool NotifyBranchChanges = true,
    bool StartAutomatically = false)
{
    public void Validate()
    {
        if (ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("A project ID is required.");
        }

        if (!DiscordWebhookAddress.TryCreate(WebhookUrl, out _))
        {
            throw new InvalidDataException("The Discord webhook URL is invalid.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length > 80)
        {
            throw new InvalidDataException("The Discord display name must contain 1 to 80 characters.");
        }

        if (ProjectLabel?.Trim().Length > 100)
        {
            throw new InvalidDataException("The Discord project label must not exceed 100 characters.");
        }

        if (PollIntervalSeconds is < 15 or > 3600)
        {
            throw new InvalidDataException("The Discord polling interval must be between 15 and 3600 seconds.");
        }

        if (!string.IsNullOrWhiteSpace(RepositoryWebUrl) &&
            (!Uri.TryCreate(RepositoryWebUrl.Trim(), UriKind.Absolute, out Uri? repositoryUri) ||
             !repositoryUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The optional repository page must be an HTTPS URL.");
        }
    }
}

public sealed record DiscordAgentCheckpoint(
    Guid ProjectId,
    string? LastAnnouncedCommitHash,
    string? LastBranch,
    DateTimeOffset? LastCheckedAt = null,
    DateTimeOffset? LastNotificationAt = null);

public sealed record DiscordCommit(
    string Hash,
    string ShortHash,
    string AuthorName,
    DateTimeOffset AuthoredAt,
    string Subject);

public sealed record DiscordProjectSnapshot(
    string Branch,
    string? HeadHash,
    IReadOnlyList<DiscordCommit> Commits);

public enum DiscordAgentRuntimeState
{
    Stopped,
    Starting,
    Watching,
    Sending,
    Error
}

public sealed record DiscordAgentStatus(
    DiscordAgentRuntimeState State,
    string Details,
    string? Branch = null,
    DateTimeOffset? LastCheckedAt = null,
    DateTimeOffset? LastNotificationAt = null);
