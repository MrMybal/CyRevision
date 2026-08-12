using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

public sealed class UnrealProjectPluginInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _sourceDirectory;

    public UnrealProjectPluginInstaller(string sourceDirectory)
    {
        _sourceDirectory = Path.GetFullPath(sourceDirectory);
    }

    public UnrealProjectInspection Inspect(string path)
    {
        string? projectFile = ResolveProjectFile(path);
        string? bundledVersion = ReadPluginVersion(_sourceDirectory);
        if (projectFile is null)
        {
            return new UnrealProjectInspection(
                false,
                NormalizeCandidateRoot(path),
                string.Empty,
                string.Empty,
                false,
                null,
                bundledVersion,
                false,
                "Select a folder containing one .uproject file, or select the .uproject file directly.");
        }

        string root = Path.GetDirectoryName(projectFile)!;
        string destination = Path.Combine(root, "Plugins", "CyRevisionUnreal");
        string? installedVersion = ReadPluginVersion(destination);
        bool installed = installedVersion is not null;
        bool updateAvailable = installed && bundledVersion is not null &&
                               CompareVersions(installedVersion!, bundledVersion) < 0;
        string summary = !installed
            ? "The bundled CyRevisionUnreal Editor plugin can be installed in this project."
            : updateAvailable
                ? $"CyRevisionUnreal {installedVersion} is installed; bundled version {bundledVersion} is available."
                : $"CyRevisionUnreal {installedVersion} is installed and up to date.";

        return new UnrealProjectInspection(
            true,
            root,
            projectFile,
            Path.GetFileNameWithoutExtension(projectFile),
            installed,
            installedVersion,
            bundledVersion,
            updateAvailable,
            summary);
    }

    public UnrealPluginInstallationResult InstallOrUpdate(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        UnrealProjectInspection inspection = Inspect(projectPath);
        if (!inspection.IsValid)
        {
            return new UnrealPluginInstallationResult(
                false, inspection.ProjectRoot, string.Empty, null, string.Empty, inspection.Summary);
        }

        string? bundledVersion = ReadPluginVersion(_sourceDirectory);
        if (bundledVersion is null)
        {
            return new UnrealPluginInstallationResult(
                false,
                inspection.ProjectRoot,
                string.Empty,
                null,
                string.Empty,
                "The release does not contain a valid CyRevisionUnreal payload.");
        }

        string pluginsRoot = Path.Combine(inspection.ProjectRoot, "Plugins");
        string destination = Path.Combine(pluginsRoot, "CyRevisionUnreal");
        string staging = Path.Combine(pluginsRoot, $".cyrevision-unreal-{Guid.NewGuid():N}.tmp");
        string? backup = null;
        Directory.CreateDirectory(pluginsRoot);

        try
        {
            CopyPluginTree(_sourceDirectory, staging, cancellationToken);
            if (Directory.Exists(destination))
            {
                backup = Path.Combine(
                    inspection.ProjectRoot,
                    "Saved",
                    "CyRevision",
                    "PluginBackups",
                    $"CyRevisionUnreal-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }

            Directory.Move(staging, destination);
            EnablePluginInProjectFile(inspection.ProjectFile);
            return new UnrealPluginInstallationResult(
                true,
                inspection.ProjectRoot,
                destination,
                backup,
                bundledVersion,
                backup is null
                    ? $"CyRevisionUnreal {bundledVersion} was installed and enabled."
                    : $"CyRevisionUnreal was updated to {bundledVersion}; the previous copy was backed up.");
        }
        catch (Exception exception)
        {
            if (!Directory.Exists(destination) && backup is not null && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
                backup = null;
            }

            return new UnrealPluginInstallationResult(
                false,
                inspection.ProjectRoot,
                destination,
                backup,
                bundledVersion,
                $"Installation failed: {exception.Message}");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    public static void WriteBridgeSettings(
        string projectRoot,
        string endpoint,
        string token,
        string executablePath)
    {
        string directory = Path.Combine(projectRoot, "Saved", "CyRevision");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "bridge.json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            url = endpoint,
            token,
            executablePath
        }, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    public static string? ResolveProjectFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".uproject", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return Directory.Exists(fullPath)
            ? Directory.GetFiles(fullPath, "*.uproject", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
    }

    public static string? ReadPluginVersion(string directory)
    {
        string descriptor = Path.Combine(directory, "CyRevisionUnreal.uplugin");
        if (!File.Exists(descriptor))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(descriptor));
        return document.RootElement.TryGetProperty("VersionName", out JsonElement version)
            ? version.GetString()
            : null;
    }

    private static string NormalizeCandidateRoot(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? Path.GetDirectoryName(fullPath)! : fullPath;
        }
        catch
        {
            return path;
        }
    }

    private static int CompareVersions(string left, string right) =>
        Version.TryParse(left, out Version? leftVersion) && Version.TryParse(right, out Version? rightVersion)
            ? leftVersion.CompareTo(rightVersion)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

    private static void CopyPluginTree(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file);
            string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (first.Equals("Binaries", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Saved", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void EnablePluginInProjectFile(string projectFile)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(projectFile))?.AsObject()
                          ?? throw new InvalidDataException("The .uproject descriptor is not valid JSON.");
        JsonArray plugins = root["Plugins"] as JsonArray ?? [];
        root["Plugins"] = plugins;
        JsonObject? entry = plugins.OfType<JsonObject>().FirstOrDefault(plugin =>
            string.Equals(plugin["Name"]?.GetValue<string>(), "CyRevisionUnreal", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            plugins.Add(new JsonObject { ["Name"] = "CyRevisionUnreal", ["Enabled"] = true });
        }
        else
        {
            entry["Enabled"] = true;
        }

        File.Copy(projectFile, projectFile + ".cyrevision.bak", true);
        string temporary = projectFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, projectFile, true);
    }
}
