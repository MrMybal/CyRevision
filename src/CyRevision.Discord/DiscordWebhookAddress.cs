namespace CyRevision.Discord;

public static class DiscordWebhookAddress
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "ptb.discord.com",
        "canary.discord.com"
    };

    public static bool TryCreate(string? value, out Uri? webhookUri)
    {
        webhookUri = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? candidate) ||
            !candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !AllowedHosts.Contains(candidate.Host) ||
            candidate.Port != 443 ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        string[] segments = candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int webhookIndex = Array.FindIndex(segments, segment =>
            segment.Equals("webhooks", StringComparison.OrdinalIgnoreCase));
        bool hasApiPrefix = webhookIndex >= 1 &&
                            (segments[webhookIndex - 1].Equals("api", StringComparison.OrdinalIgnoreCase) ||
                             webhookIndex >= 2 &&
                             segments[webhookIndex - 1].StartsWith('v') &&
                             segments[webhookIndex - 2].Equals("api", StringComparison.OrdinalIgnoreCase));
        if (webhookIndex < 1 || webhookIndex + 2 >= segments.Length ||
            !hasApiPrefix ||
            !segments[webhookIndex + 1].All(char.IsDigit) ||
            segments[webhookIndex + 2].Length < 20 ||
            segments.Length != webhookIndex + 3)
        {
            return false;
        }

        UriBuilder normalized = new(candidate)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        webhookUri = normalized.Uri;
        return true;
    }

    public static Uri CreateExecutionUri(string value)
    {
        if (!TryCreate(value, out Uri? webhookUri))
        {
            throw new InvalidDataException("The Discord webhook URL is invalid.");
        }

        return new UriBuilder(webhookUri!) { Query = "wait=true" }.Uri;
    }
}
