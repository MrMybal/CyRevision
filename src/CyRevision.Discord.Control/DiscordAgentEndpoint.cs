using System.Net;

namespace CyRevision.Discord.Control;

public static class DiscordAgentEndpoint
{
    public static Uri Create(string value, bool allowPrivateHttp)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? endpoint) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("The autonomous agent endpoint is invalid.");
        }

        if (endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !IsLoopback(endpoint.Host) &&
            !allowPrivateHttp)
        {
            throw new InvalidDataException(
                "Remote HTTP is disabled. Use HTTPS, or explicitly allow HTTP only inside a trusted WireGuard VPN.");
        }

        UriBuilder normalized = new(endpoint)
        {
            Path = endpoint.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return normalized.Uri;
    }

    public static bool IsLoopback(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
    }
}
