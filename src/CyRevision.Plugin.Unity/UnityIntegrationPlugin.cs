using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unity;

public sealed class UnityIntegrationPlugin : GameEngineIntegrationPluginBase
{
    private const int Port = 47833;
    private readonly string? _sourceOverride;

    public UnityIntegrationPlugin() { }

    public UnityIntegrationPlugin(string sourceOverride) => _sourceOverride = sourceOverride;

    public override CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.unity",
        "Unity Integration",
        "0.1.0",
        "Optional Unity project detection, autonomous Editor companion installer, and authenticated local CyRevision bridge.",
        "Game engines");

    public override GameEngineKind Engine => GameEngineKind.Unity;

    protected override int BridgePort => Port;

    protected override string ConnectionsDirectoryName => "unity";

    protected override string? ResolvePayloadDirectory(CyRevisionPluginContext context)
    {
        string[] candidates =
        [
            _sourceOverride ?? string.Empty,
            Path.Combine(context.ApplicationDirectory, "PluginPayloads", "Unity", "CyRevisionUnity"),
            Path.GetFullPath(Path.Combine(context.PackageDirectory, "..", "..", "PluginPayloads", "Unity", "CyRevisionUnity")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "plugins", "CyRevisionUnity"))
        ];
        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, "package.json")));
    }

    protected override GameEngineProjectInspection InspectProjectCore(string path, string? payloadDirectory) =>
        UnityProjectPluginInstaller.Inspect(path, payloadDirectory);

    protected override GameEnginePluginInstallationResult InstallEditorPlugin(
        string projectPath,
        string? payloadDirectory,
        CancellationToken cancellationToken) =>
        UnityProjectPluginInstaller.InstallOrUpdate(projectPath, payloadDirectory, cancellationToken);

    protected override void WriteBridgeSettings(string projectRoot, string endpoint, string token, string executablePath) =>
        UnityProjectPluginInstaller.WriteBridgeSettings(projectRoot, endpoint, token, executablePath);
}

public static class UnityProjectPluginInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static IReadOnlyList<string> SupportedVersions { get; } = ["2021.3 LTS", "2022.3 LTS", "Unity 6 (6000.x)"];

    public static GameEngineProjectInspection Inspect(string path, string? payloadDirectory)
    {
        string? root = ResolveProjectRoot(path);
        if (root is null)
            return Invalid(path, "Select a Unity project containing Assets and ProjectSettings/ProjectVersion.txt.");

        string version = ReadUnityVersion(root);
        bool compatible = version.StartsWith("2021.3", StringComparison.OrdinalIgnoreCase) ||
                          version.StartsWith("2022.3", StringComparison.OrdinalIgnoreCase) ||
                          version.StartsWith("6000.", StringComparison.OrdinalIgnoreCase);
        string destination = Path.Combine(root, "Packages", "com.cyrevision.editor");
        string? installedVersion = ReadPackageVersion(Path.Combine(destination, "package.json"));
        string? bundledVersion = payloadDirectory is null ? null : ReadPackageVersion(Path.Combine(payloadDirectory, "package.json"));
        bool update = installedVersion is not null && bundledVersion is not null && !string.Equals(installedVersion, bundledVersion, StringComparison.OrdinalIgnoreCase);
        string name = Path.GetFileName(root);
        return new GameEngineProjectInspection(
            GameEngineKind.Unity,
            true,
            root,
            name,
            version,
            compatible,
            compatible
                ? $"Compatible Unity project ({version})."
                : $"Unity {version} is outside the validated 2021.3 LTS, 2022.3 LTS, and Unity 6 ranges.",
            SupportedVersions,
            installedVersion is not null,
            installedVersion,
            bundledVersion,
            update,
            payloadDirectory is null
                ? "The CyRevisionUnity companion payload is missing from this CyRevision package."
                : installedVersion is null
                    ? "Unity project detected. The autonomous CyRevision Editor companion is not installed."
                    : update
                        ? $"CyRevisionUnity {installedVersion} is installed; {bundledVersion} is available."
                        : $"CyRevisionUnity {installedVersion} is installed and ready to link.");
    }

    public static GameEnginePluginInstallationResult InstallOrUpdate(
        string projectPath,
        string? payloadDirectory,
        CancellationToken cancellationToken = default)
    {
        GameEngineProjectInspection inspection = Inspect(projectPath, payloadDirectory);
        if (!inspection.IsValid || !inspection.IsCompatible || payloadDirectory is null)
            return new GameEnginePluginInstallationResult(false, GameEngineKind.Unity, inspection.ProjectRoot, string.Empty, null, string.Empty, inspection.Summary);

        string destination = Path.Combine(inspection.ProjectRoot, "Packages", "com.cyrevision.editor");
        string staging = Path.Combine(inspection.ProjectRoot, "Library", "CyRevision", "PluginStaging", Guid.NewGuid().ToString("N"));
        string? backup = null;
        string version = inspection.BundledPluginVersion ?? "0.1.0";
        try
        {
            CopyDirectory(payloadDirectory, staging, cancellationToken);
            if (Directory.Exists(destination))
            {
                backup = Path.Combine(inspection.ProjectRoot, "Library", "CyRevision", "PluginBackups", $"com.cyrevision.editor-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            return new GameEnginePluginInstallationResult(
                true, GameEngineKind.Unity, inspection.ProjectRoot, destination, backup, version,
                backup is null
                    ? $"CyRevisionUnity {version} was installed as an embedded Unity package."
                    : $"CyRevisionUnity was updated to {version}; the previous package was backed up under Library/CyRevision.");
        }
        catch (Exception exception)
        {
            if (!Directory.Exists(destination) && backup is not null && Directory.Exists(backup)) Directory.Move(backup, destination);
            return new GameEnginePluginInstallationResult(false, GameEngineKind.Unity, inspection.ProjectRoot, destination, backup, version, $"Unity companion installation failed: {exception.Message}");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public static void WriteBridgeSettings(string projectRoot, string endpoint, string token, string executablePath)
    {
        string directory = Path.Combine(projectRoot, "Library", "CyRevision");
        Directory.CreateDirectory(directory);
        WriteJsonAtomic(Path.Combine(directory, "bridge.json"), new { schemaVersion = 1, engine = "unity", url = endpoint, token, executablePath });
    }

    private static GameEngineProjectInspection Invalid(string path, string message) => new(
        GameEngineKind.Unity, false, path, string.Empty, string.Empty, false, message, SupportedVersions,
        false, null, null, false, message);

    private static string? ResolveProjectRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return null; }
        DirectoryInfo? directory = File.Exists(full) ? new FileInfo(full).Directory : new DirectoryInfo(full);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                File.Exists(Path.Combine(directory.FullName, "ProjectSettings", "ProjectVersion.txt"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static string ReadUnityVersion(string root)
    {
        string line = File.ReadLines(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"))
            .FirstOrDefault(item => item.StartsWith("m_EditorVersion:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return line.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "Unknown";
    }

    private static string? ReadPackageVersion(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("version", out JsonElement version) ? version.GetString() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void WriteJsonAtomic(string path, object value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}
