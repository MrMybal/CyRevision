using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CyRevision.Vpn;

namespace CyRevision.Core.Tests;

public sealed class WireGuardRuntimeResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CyRevisionWireGuardRuntimeTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void IntegratedRuntime_RequiresEveryFileAndMatchingSha256()
    {
        string directory = Path.Combine(_root, RuntimeInformation.RuntimeIdentifier);
        Directory.CreateDirectory(directory);
        string[] names = OperatingSystem.IsWindows()
            ? ["wireguard.exe", "wg.exe"]
            : ["wireguard-go", "wg", "wg-quick"];
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, "runtime-" + name);
            files[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }

        File.WriteAllText(
            Path.Combine(directory, "runtime.json"),
            JsonSerializer.Serialize(new { version = 1, files }));
        WireGuardRuntimeResolver resolver = new(_root);

        WireGuardInstallation valid = resolver.Detect(VpnBackendMode.IntegratedRuntime);
        Assert.True(valid.CanGenerateKeys);
        Assert.True(valid.CanManageTunnel);
        Assert.Contains("verified", valid.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        File.AppendAllText(Path.Combine(directory, names[0]), "tampered");
        WireGuardInstallation invalid = resolver.Detect(VpnBackendMode.IntegratedRuntime);
        Assert.False(invalid.CanGenerateKeys);
        Assert.Contains("SHA-256", invalid.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
