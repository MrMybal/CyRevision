using System.Runtime.InteropServices;

namespace CyRevision.Sync;

public enum SyncthingRuntimeSource
{
    Missing,
    Bundled,
    Managed,
    SystemPath,
    Custom
}

public sealed record SyncthingRuntimeInstallation(
    string? ExecutablePath,
    SyncthingRuntimeSource Source,
    string RuntimeDirectory,
    string Details)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);
}

public sealed class SyncthingRuntimeResolver
{
    private readonly string _bundledRoot;
    private readonly string _managedRoot;
    private readonly Func<string?> _pathProvider;

    public SyncthingRuntimeResolver(
        string? bundledRoot = null,
        string? managedRoot = null,
        Func<string?>? pathProvider = null)
    {
        _bundledRoot = Path.GetFullPath(bundledRoot ?? Path.Combine(AppContext.BaseDirectory, "SyncthingRuntime"));
        _managedRoot = Path.GetFullPath(managedRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyRevision",
            "syncthing",
            "runtime"));
        _pathProvider = pathProvider ?? (() => Environment.GetEnvironmentVariable("PATH"));
    }

    public string BundledRoot => _bundledRoot;

    public string ManagedRoot => _managedRoot;

    public SyncthingRuntimeInstallation Detect(string? customExecutablePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customExecutablePath))
        {
            string custom = Path.GetFullPath(customExecutablePath);
            if (File.Exists(custom))
            {
                return Found(custom, SyncthingRuntimeSource.Custom, "Custom runtime selected for this project.");
            }
        }

        foreach ((string directory, SyncthingRuntimeSource source, string details) in new[]
                 {
                     (ResolveRuntimeDirectory(_bundledRoot), SyncthingRuntimeSource.Bundled,
                         "Runtime included with CyRevision."),
                     (ResolveRuntimeDirectory(_managedRoot), SyncthingRuntimeSource.Managed,
                         "Runtime managed by CyRevision for the current platform.")
                 })
        {
            string candidate = Path.Combine(directory, ExecutableName);
            if (File.Exists(candidate))
            {
                return Found(candidate, source, details);
            }
        }

        string? fromPath = FindOnPath();
        if (fromPath is not null)
        {
            return Found(fromPath, SyncthingRuntimeSource.SystemPath, "System Syncthing installation detected automatically.");
        }

        string managedDirectory = ResolveRuntimeDirectory(_managedRoot);
        return new SyncthingRuntimeInstallation(
            null,
            SyncthingRuntimeSource.Missing,
            managedDirectory,
            $"No Syncthing runtime was detected. A managed package can be placed in '{managedDirectory}'.");
    }

    public string ResolveRuntimeDirectory(string root)
    {
        string exact = Path.Combine(root, RuntimeInformation.RuntimeIdentifier);
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
        return Path.Combine(root, $"{platform}-{architecture}");
    }

    private static string ExecutableName => OperatingSystem.IsWindows() ? "syncthing.exe" : "syncthing";

    private string? FindOnPath()
    {
        string? path = _pathProvider();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string rawDirectory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(rawDirectory.Trim('"'), ExecutableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Invalid PATH entries are ignored while the remaining entries are inspected.
            }
        }

        return null;
    }

    private static SyncthingRuntimeInstallation Found(
        string executablePath,
        SyncthingRuntimeSource source,
        string details) =>
        new(
            Path.GetFullPath(executablePath),
            source,
            Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
            details);
}
