namespace CyRevision.PullRequests;

public static class GitRemoteRepositoryParser
{
    public static bool TryParseGitHub(
        string remoteUrl,
        string? apiBaseUrl,
        out PullRequestRepository? repository)
    {
        repository = null;
        if (string.IsNullOrWhiteSpace(remoteUrl)) return false;

        string value = remoteUrl.Trim();
        string host;
        string path;
        if (TryParseScpLike(value, out host, out path))
        {
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                 uri.Scheme is "https" or "http" or "ssh" or "git")
        {
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        if (host.Contains("gitlab", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("bitbucket", StringComparison.OrdinalIgnoreCase)) return false;
        string[] segments = path.Trim('/', '\\')
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2) return false;
        string owner = segments[^2];
        string name = segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[^1][..^4]
            : segments[^1];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)) return false;

        Uri webBase = new($"https://{host.TrimEnd('/')}/", UriKind.Absolute);
        Uri apiBase;
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            if (!Uri.TryCreate(EnsureTrailingSlash(apiBaseUrl.Trim()), UriKind.Absolute, out Uri? customApiBase)) return false;
            apiBase = customApiBase;
        }
        else
        {
            apiBase = host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? new Uri("https://api.github.com/", UriKind.Absolute)
                : new Uri($"https://{host}/api/v3/", UriKind.Absolute);
        }

        repository = new PullRequestRepository("GitHub", host, owner, name, webBase, apiBase, value);
        return true;
    }

    private static bool TryParseScpLike(string value, out string host, out string path)
    {
        host = string.Empty;
        path = string.Empty;
        int at = value.IndexOf('@');
        int colon = value.IndexOf(':', Math.Max(0, at + 1));
        if (at < 0 || colon <= at + 1 || value.Contains("://", StringComparison.Ordinal)) return false;
        host = value[(at + 1)..colon];
        path = value[(colon + 1)..];
        return host.Length > 0 && path.Length > 0;
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
}
