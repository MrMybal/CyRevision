using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Vpn;

public sealed class WireGuardRuntimeResolver
{
    private readonly string _runtimeRoot;
    private readonly WireGuardKeyService _systemDetector;

    public WireGuardRuntimeResolver(string? runtimeRoot = null, WireGuardKeyService? systemDetector = null)
    {
        _runtimeRoot = Path.GetFullPath(runtimeRoot ?? Path.Combine(AppContext.BaseDirectory, "VpnRuntime"));
        _systemDetector = systemDetector ?? new WireGuardKeyService();
    }

    public string RuntimeRoot => _runtimeRoot;

    public WireGuardInstallation Detect(VpnBackendMode mode)
    {
        if (mode == VpnBackendMode.SystemInstallation)
        {
            return _systemDetector.DetectInstallation() with { BackendMode = mode };
        }

        string runtimeDirectory = ResolveRuntimeDirectory();
        string[] requiredFiles = OperatingSystem.IsWindows()
            ? ["wireguard.exe", "wg.exe"]
            : ["wireguard-go", "wg", "wg-quick"];
        if (!VerifyRuntime(runtimeDirectory, requiredFiles, out string validationMessage))
        {
            return new WireGuardInstallation(null, null, null)
            {
                BackendMode = mode,
                RuntimeDirectory = runtimeDirectory,
                ValidationMessage = validationMessage
            };
        }

        if (OperatingSystem.IsWindows())
        {
            return new WireGuardInstallation(
                Existing(Path.Combine(runtimeDirectory, "wireguard.exe")),
                Existing(Path.Combine(runtimeDirectory, "wg.exe")),
                null)
            {
                BackendMode = mode,
                RuntimeDirectory = runtimeDirectory,
                ValidationMessage = validationMessage
            };
        }

        return new WireGuardInstallation(
            null,
            Existing(Path.Combine(runtimeDirectory, "wg")),
            Existing(Path.Combine(runtimeDirectory, "wg-quick")))
        {
            BackendMode = mode,
            RuntimeDirectory = runtimeDirectory,
            UserspaceExecutablePath = Existing(Path.Combine(runtimeDirectory, "wireguard-go")),
            ValidationMessage = validationMessage
        };
    }

    public string ResolveRuntimeDirectory()
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        string exact = Path.Combine(_runtimeRoot, rid);
        if (Directory.Exists(exact))
        {
            return exact;
        }

        string platform = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";
        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return Path.Combine(_runtimeRoot, $"{platform}-{architecture}");
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private static bool VerifyRuntime(
        string runtimeDirectory,
        IReadOnlyCollection<string> requiredFiles,
        out string message)
    {
        string manifestPath = Path.Combine(runtimeDirectory, "runtime.json");
        if (!File.Exists(manifestPath))
        {
            message = "runtime.json is missing.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("files", out JsonElement files) ||
                files.ValueKind != JsonValueKind.Object)
            {
                message = "runtime.json has no file checksum map.";
                return false;
            }

            foreach (string name in requiredFiles)
            {
                string path = Path.Combine(runtimeDirectory, name);
                if (!File.Exists(path) ||
                    !files.TryGetProperty(name, out JsonElement hashElement) ||
                    hashElement.ValueKind != JsonValueKind.String)
                {
                    message = $"Required integrated runtime file '{name}' is missing.";
                    return false;
                }

                string expected = hashElement.GetString()?.Trim() ?? string.Empty;
                string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"SHA-256 validation failed for integrated runtime file '{name}'.";
                    return false;
                }
            }

            message = "Integrated runtime files verified with SHA-256.";
            return true;
        }
        catch (JsonException)
        {
            message = "runtime.json is invalid.";
            return false;
        }
        catch (IOException exception)
        {
            message = exception.Message;
            return false;
        }
    }
}
