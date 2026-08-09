using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncthingIsolationOptionsTests
{
    [Fact]
    public void DedicatedLoopbackDirectoriesAreAccepted()
    {
        string root = Path.Combine(Path.GetTempPath(), "CyRevisionSync", Guid.NewGuid().ToString("N"));
        SyncthingIsolationOptions options = new(
            Path.Combine(root, "bin", "syncthing.exe"),
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "exchange"),
            new Uri("http://127.0.0.1:18384"),
            "dedicated-api-key",
            22091);

        options.Validate();
    }

    [Fact]
    public void OverlappingDirectoriesAndRemoteApiAreRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "CyRevisionSync", Guid.NewGuid().ToString("N"));
        SyncthingIsolationOptions overlap = new(
            "syncthing",
            Path.Combine(root, "managed"),
            Path.Combine(root, "managed", "data"),
            Path.Combine(root, "exchange"),
            new Uri("http://127.0.0.1:18384"),
            "key",
            22091);
        SyncthingIsolationOptions remoteApi = overlap with
        {
            ConfigurationDirectory = Path.Combine(root, "config"),
            DataDirectory = Path.Combine(root, "data"),
            ApiEndpoint = new Uri("http://192.168.1.20:18384")
        };

        Assert.Throws<InvalidOperationException>(overlap.Validate);
        Assert.Throws<InvalidOperationException>(remoteApi.Validate);
    }
}
