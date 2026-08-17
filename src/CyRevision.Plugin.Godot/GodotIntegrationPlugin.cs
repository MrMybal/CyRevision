using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Godot;

public sealed class GodotIntegrationPlugin : GameEngineIntegrationPluginBase
{
    private const int Port = 47834;
    private readonly string? _sourceOverride;

    public GodotIntegrationPlugin() { }

    public GodotIntegrationPlugin(string sourceOverride) => _sourceOverride = sourceOverride;

    public override CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.godot",
        "Godot Integration",
        "0.1.0",
        "Optional Godot 4 project detection, autonomous Editor dock installer, and authenticated local CyRevision bridge.",
        "Game engines");

    public override GameEngineKind Engine => GameEngineKind.Godot;

    protected override int BridgePort => Port;

    protected override string ConnectionsDirectoryName => "godot";

    protected override string? ResolvePayloadDirectory(CyRevisionPluginContext context)
    {
        string[] candidates =
        [
            _sourceOverride ?? string.Empty,
            Path.Combine(context.ApplicationDirectory, "PluginPayloads", "Godot", "CyRevisionGodot"),
            Path.GetFullPath(Path.Combine(context.PackageDirectory, "..", "..", "PluginPayloads", "Godot", "CyRevisionGodot")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "plugins", "CyRevisionGodot"))
        ];
        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, "plugin.cfg")));
    }

    protected override GameEngineProjectInspection InspectProjectCore(string path, string? payloadDirectory) =>
        GodotProjectPluginInstaller.Inspect(path, payloadDirectory);

    protected override GameEnginePluginInstallationResult InstallEditorPlugin(
        string projectPath,
        string? payloadDirectory,
        CancellationToken cancellationToken) =>
        GodotProjectPluginInstaller.InstallOrUpdate(projectPath, payloadDirectory, cancellationToken);

    protected override void WriteBridgeSettings(string projectRoot, string endpoint, string token, string executablePath) =>
        GodotProjectPluginInstaller.WriteBridgeSettings(projectRoot, endpoint, token, executablePath);
}

public static class GodotProjectPluginInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static IReadOnlyList<string> SupportedVersions { get; } = ["Godot 4.0", "4.1", "4.2", "4.3", "4.4", "4.5", "4.6"];

    public static GameEngineProjectInspection Inspect(string path, string? payloadDirectory)
    {
        string? root = ResolveProjectRoot(path);
        if (root is null)
            return Invalid(path, "Select a Godot project containing project.godot.");

        string projectFile = Path.Combine(root, "project.godot");
        string text = File.ReadAllText(projectFile);
        string version = ReadGodotVersion(text);
        bool compatible = version == "4.x" || version.StartsWith("4.", StringComparison.OrdinalIgnoreCase);
        string destination = Path.Combine(root, "addons", "cyrevision");
        string? installedVersion = ReadPluginVersion(Path.Combine(destination, "plugin.cfg"));
        string? bundledVersion = payloadDirectory is null ? null : ReadPluginVersion(Path.Combine(payloadDirectory, "plugin.cfg"));
        bool update = installedVersion is not null && bundledVersion is not null && !string.Equals(installedVersion, bundledVersion, StringComparison.OrdinalIgnoreCase);
        string name = ReadProjectName(text) ?? Path.GetFileName(root);
        return new GameEngineProjectInspection(
            GameEngineKind.Godot,
            true,
            root,
            name,
            version,
            compatible,
            compatible ? $"Compatible Godot {version} project." : $"Godot {version} is not supported by this first companion release.",
            SupportedVersions,
            installedVersion is not null,
            installedVersion,
            bundledVersion,
            update,
            payloadDirectory is null
                ? "The CyRevisionGodot companion payload is missing from this CyRevision package."
                : installedVersion is null
                    ? "Godot project detected. The autonomous CyRevision Editor dock is not installed."
                    : update
                        ? $"CyRevisionGodot {installedVersion} is installed; {bundledVersion} is available."
                        : $"CyRevisionGodot {installedVersion} is installed and ready to link.");
    }

    public static GameEnginePluginInstallationResult InstallOrUpdate(
        string projectPath,
        string? payloadDirectory,
        CancellationToken cancellationToken = default)
    {
        GameEngineProjectInspection inspection = Inspect(projectPath, payloadDirectory);
        if (!inspection.IsValid || !inspection.IsCompatible || payloadDirectory is null)
            return new GameEnginePluginInstallationResult(false, GameEngineKind.Godot, inspection.ProjectRoot, string.Empty, null, string.Empty, inspection.Summary);

        string destination = Path.Combine(inspection.ProjectRoot, "addons", "cyrevision");
        string staging = Path.Combine(inspection.ProjectRoot, ".godot", "cyrevision", "plugin-staging", Guid.NewGuid().ToString("N"));
        string? backup = null;
        string version = inspection.BundledPluginVersion ?? "0.1.0";
        try
        {
            CopyDirectory(payloadDirectory, staging, cancellationToken);
            if (Directory.Exists(destination))
            {
                backup = Path.Combine(inspection.ProjectRoot, ".godot", "cyrevision", "plugin-backups", $"cyrevision-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            EnableEditorPlugin(inspection.ProjectRoot);
            return new GameEnginePluginInstallationResult(
                true, GameEngineKind.Godot, inspection.ProjectRoot, destination, backup, version,
                backup is null
                    ? $"CyRevisionGodot {version} was installed and enabled under addons/cyrevision."
                    : $"CyRevisionGodot was updated to {version}; the previous addon was backed up under .godot/cyrevision.");
        }
        catch (Exception exception)
        {
            if (!Directory.Exists(destination) && backup is not null && Directory.Exists(backup)) Directory.Move(backup, destination);
            return new GameEnginePluginInstallationResult(false, GameEngineKind.Godot, inspection.ProjectRoot, destination, backup, version, $"Godot companion installation failed: {exception.Message}");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public static void WriteBridgeSettings(string projectRoot, string endpoint, string token, string executablePath)
    {
        string directory = Path.Combine(projectRoot, ".godot", "cyrevision");
        Directory.CreateDirectory(directory);
        WriteAtomic(Path.Combine(directory, "bridge.json"), JsonSerializer.Serialize(
            new { schemaVersion = 1, engine = "godot", url = endpoint, token, executablePath }, JsonOptions));
    }

    private static GameEngineProjectInspection Invalid(string path, string message) => new(
        GameEngineKind.Godot, false, path, string.Empty, string.Empty, false, message, SupportedVersions,
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
            if (File.Exists(Path.Combine(directory.FullName, "project.godot"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static string ReadGodotVersion(string text)
    {
        Match match = Regex.Match(text, "features\\s*=\\s*PackedStringArray\\([^\\r\\n]*?\\\"(?<version>4\\.[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["version"].Value : "4.x";
    }

    private static string? ReadProjectName(string text)
    {
        Match match = Regex.Match(text, "^config/name\\s*=\\s*\\\"(?<name>[^\\\"]+)\\\"", RegexOptions.Multiline);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string? ReadPluginVersion(string path)
    {
        try
        {
            Match match = Regex.Match(File.ReadAllText(path), "^version\\s*=\\s*\\\"(?<version>[^\\\"]+)\\\"", RegexOptions.Multiline);
            return match.Success ? match.Groups["version"].Value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void EnableEditorPlugin(string root)
    {
        string path = Path.Combine(root, "project.godot");
        string text = File.ReadAllText(path);
        const string pluginPath = "res://addons/cyrevision/plugin.cfg";
        if (text.Contains(pluginPath, StringComparison.Ordinal)) return;
        string backupDirectory = Path.Combine(root, ".godot", "cyrevision");
        Directory.CreateDirectory(backupDirectory);
        File.Copy(path, Path.Combine(backupDirectory, "project.godot.before-plugin.bak"), true);
        const string section = "[editor_plugins]";
        if (!text.Contains(section, StringComparison.Ordinal))
        {
            text = text.TrimEnd() + Environment.NewLine + Environment.NewLine + section + Environment.NewLine +
                   $"enabled=PackedStringArray(\"{pluginPath}\")" + Environment.NewLine;
        }
        else
        {
            int start = text.IndexOf(section, StringComparison.Ordinal) + section.Length;
            int next = text.IndexOf("\n[", start, StringComparison.Ordinal);
            int end = next < 0 ? text.Length : next;
            string block = text[start..end];
            Match enabled = Regex.Match(block, "enabled\\s*=\\s*PackedStringArray\\((?<items>[^)]*)\\)");
            if (enabled.Success)
            {
                string items = enabled.Groups["items"].Value.Trim();
                string replacement = $"enabled=PackedStringArray({(items.Length == 0 ? string.Empty : items + ", ")}\"{pluginPath}\")";
                block = block.Remove(enabled.Index, enabled.Length).Insert(enabled.Index, replacement);
                text = text[..start] + block + text[end..];
            }
            else
            {
                text = text.Insert(start, Environment.NewLine + $"enabled=PackedStringArray(\"{pluginPath}\")");
            }
        }
        WriteAtomic(path, text);
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

    private static void WriteAtomic(string path, string content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}
