using System.Net;

namespace CyRevision.Sync;

public sealed record SyncthingIsolationOptions(
    string ExecutablePath,
    string ConfigurationDirectory,
    string DataDirectory,
    string ExchangeDirectory,
    Uri ApiEndpoint)
{
    public void Validate()
    {
        string configurationPath = Path.GetFullPath(ConfigurationDirectory);
        string dataPath = Path.GetFullPath(DataDirectory);
        string exchangePath = Path.GetFullPath(ExchangeDirectory);

        if (configurationPath == dataPath || configurationPath == exchangePath || dataPath == exchangePath)
        {
            throw new InvalidOperationException("Syncthing configuration, data and exchange directories must be separate.");
        }

        if (!IsLoopback(ApiEndpoint.Host))
        {
            throw new InvalidOperationException("The managed Syncthing API must only listen on loopback.");
        }
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

