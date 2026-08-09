using System.Net;

namespace CyRevision.Sync;

public sealed record SyncthingIsolationOptions(
    string ExecutablePath,
    string ConfigurationDirectory,
    string DataDirectory,
    string ExchangeDirectory,
    Uri ApiEndpoint,
    string ApiKey,
    int ListenPort,
    bool Enabled = true)
{
    public void Validate()
    {
        string configurationPath = Path.GetFullPath(ConfigurationDirectory);
        string dataPath = Path.GetFullPath(DataDirectory);
        string exchangePath = Path.GetFullPath(ExchangeDirectory);

        if (PathsOverlap(configurationPath, dataPath) ||
            PathsOverlap(configurationPath, exchangePath) ||
            PathsOverlap(dataPath, exchangePath))
        {
            throw new InvalidOperationException("Syncthing configuration, data and exchange directories must be separate and non-overlapping.");
        }

        if (!IsLoopback(ApiEndpoint.Host))
        {
            throw new InvalidOperationException("The managed Syncthing API must only listen on loopback.");
        }


        if (ApiEndpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("The managed Syncthing API endpoint must use HTTP or HTTPS.");
        }

        if (Enabled && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("A dedicated Syncthing API key is required.");
        }

        if (Enabled && ListenPort is <= 0 or > 65535)
        {
            throw new InvalidOperationException("A dedicated Syncthing transport port is required.");
        }

        if (ApiEndpoint.Port == ListenPort)
        {
            throw new InvalidOperationException("Syncthing API and transport ports must be different.");
        }
    }

    public string LogPath => Path.Combine(DataDirectory, "cyrevision-syncthing.log");

    private static bool PathsOverlap(string left, string right)
    {
        string normalizedLeft = Path.TrimEndingDirectorySeparator(left);
        string normalizedRight = Path.TrimEndingDirectorySeparator(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase) ||
               normalizedLeft.StartsWith(normalizedRight + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedRight.StartsWith(normalizedLeft + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
    }
}
