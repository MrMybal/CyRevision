using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

public sealed class UnrealProjectPluginInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] SupportedVersions = UnrealPluginCompatibility.SupportedEngineVersions.ToArray();
    private readonly string _sourceDirectory;

    public static IReadOnlyList<string> SupportedEngineVersions => SupportedVersions;
    public static string CurrentPlatform => GetPlatformDirectoryName();

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
                string.Empty,
                null,
                UnrealProjectKind.Unknown,
                UnrealPluginInstallMode.Unavailable,
                false,
                "No Unreal project is selected.",
                SupportedVersions,
                CurrentPlatform,
                GetAvailablePrecompiledVersions(),
                false,
                null,
                bundledVersion,
                false,
                "Select a folder containing one .uproject file, or select the .uproject file directly.");
        }

        string root = Path.GetDirectoryName(projectFile)!;
        IReadOnlyList<string> availablePrecompiledVersions = GetAvailablePrecompiledVersions();
        (string engineAssociation, string? engineVersion) = ReadEngineVersion(projectFile);
        UnrealProjectKind projectKind = DetectProjectKind(root);
        bool versionSupported = engineVersion is not null && SupportedVersions.Contains(engineVersion);
        string? precompiledDirectory = engineVersion is null ? null : ResolvePrecompiledDirectory(engineVersion);
        UnrealPluginInstallMode installMode = projectKind switch
        {
            UnrealProjectKind.Cpp when versionSupported => UnrealPluginInstallMode.Source,
            UnrealProjectKind.BlueprintOnly when versionSupported && precompiledDirectory is not null =>
                UnrealPluginInstallMode.Precompiled,
            _ => UnrealPluginInstallMode.Unavailable
        };
        bool compatible = installMode is not UnrealPluginInstallMode.Unavailable;
        string compatibilityStatus = BuildCompatibilityStatus(
            engineAssociation,
            engineVersion,
            projectKind,
            installMode,
            versionSupported);
        string destination = Path.Combine(root, "Plugins", "CyRevisionUnreal");
        string? installedVersion = ReadPluginVersion(destination);
        bool installed = installedVersion is not null;
        bool updateAvailable = installed && bundledVersion is not null &&
                               CompareVersions(installedVersion!, bundledVersion) < 0;
        string installationSummary = !installed
            ? "The bundled CyRevisionUnreal Editor plugin can be installed in this project."
            : updateAvailable
                ? $"CyRevisionUnreal {installedVersion} is installed; bundled version {bundledVersion} is available."
                : $"CyRevisionUnreal {installedVersion} is installed and up to date.";
        string summary = $"{compatibilityStatus} {installationSummary} Supported Unreal versions: {string.Join(", ", SupportedVersions)}.";

        return new UnrealProjectInspection(
            true,
            root,
            projectFile,
            Path.GetFileNameWithoutExtension(projectFile),
            engineAssociation,
            engineVersion,
            projectKind,
            installMode,
            compatible,
            compatibilityStatus,
            SupportedVersions,
            CurrentPlatform,
            availablePrecompiledVersions,
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

        if (!inspection.IsCompatible)
        {
            return new UnrealPluginInstallationResult(
                false,
                inspection.ProjectRoot,
                string.Empty,
                null,
                string.Empty,
                inspection.CompatibilityStatus);
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
            string installationSource = inspection.InstallMode switch
            {
                UnrealPluginInstallMode.Source => _sourceDirectory,
                UnrealPluginInstallMode.Precompiled when inspection.EngineVersion is not null =>
                    ResolvePrecompiledDirectory(inspection.EngineVersion)
                    ?? throw new InvalidOperationException(
                        $"The precompiled Unreal {inspection.EngineVersion} package is not bundled for {GetPlatformDirectoryName()}."),
                _ => throw new InvalidOperationException(inspection.CompatibilityStatus)
            };
            CopyPluginTree(
                installationSource,
                staging,
                inspection.InstallMode,
                cancellationToken);
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
                    ? $"CyRevisionUnreal {bundledVersion} was installed and enabled in {FormatInstallMode(inspection.InstallMode)} mode."
                    : $"CyRevisionUnreal was updated to {bundledVersion} in {FormatInstallMode(inspection.InstallMode)} mode; the previous copy was backed up.");
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

    private static (string Association, string? Version) ReadEngineVersion(string projectFile)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(projectFile));
            string association = document.RootElement.TryGetProperty("EngineAssociation", out JsonElement value)
                ? value.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            string? directVersion = NormalizeEngineVersion(association);
            if (directVersion is not null)
            {
                return (association, directVersion);
            }

            string? engineRoot = ResolveRegisteredEngineRoot(association);
            string? registeredVersion = engineRoot is null ? null : ReadEngineBuildVersion(engineRoot);
            return (association, registeredVersion);
        }
        catch (JsonException)
        {
            return (string.Empty, null);
        }
    }

    private static string? NormalizeEngineVersion(string association)
    {
        if (string.IsNullOrWhiteSpace(association)) return null;
        string[] components = association.Trim().TrimStart('U', 'E').Split('.', '-', '_');
        return components.Length >= 2 &&
               int.TryParse(components[0], out int major) &&
               int.TryParse(components[1], out int minor)
            ? $"{major}.{minor}"
            : null;
    }

    private static string? ResolveRegisteredEngineRoot(string association)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(association)) return null;
        try
        {
            using RegistryKey? builds = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Epic Games\Unreal Engine\Builds");
            return builds?.GetValue(association) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadEngineBuildVersion(string engineRoot)
    {
        string path = Path.Combine(engineRoot, "Engine", "Build", "Build.version");
        if (!File.Exists(path)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("MajorVersion", out JsonElement major) &&
                   document.RootElement.TryGetProperty("MinorVersion", out JsonElement minor)
                ? $"{major.GetInt32()}.{minor.GetInt32()}"
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static UnrealProjectKind DetectProjectKind(string projectRoot)
    {
        string source = Path.Combine(projectRoot, "Source");
        if (!Directory.Exists(source)) return UnrealProjectKind.BlueprintOnly;
        try
        {
            bool hasNativeCode = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Any(file =>
                file.EndsWith(".Target.cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".Build.cs", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file).Equals(".cpp", StringComparison.OrdinalIgnoreCase));
            return hasNativeCode ? UnrealProjectKind.Cpp : UnrealProjectKind.BlueprintOnly;
        }
        catch
        {
            return UnrealProjectKind.Unknown;
        }
    }

    private string? ResolvePrecompiledDirectory(string engineVersion)
    {
        string candidate = Path.Combine(
            _sourceDirectory,
            "Variants",
            $"UE{engineVersion}",
            GetPlatformDirectoryName(),
            "CyRevisionUnreal");
        return File.Exists(Path.Combine(candidate, "CyRevisionUnreal.uplugin")) &&
               Directory.Exists(Path.Combine(candidate, "Binaries"))
            ? candidate
            : null;
    }

    private IReadOnlyList<string> GetAvailablePrecompiledVersions()
    {
        return SupportedVersions
            .Where(version => ResolvePrecompiledDirectory(version) is not null)
            .ToArray();
    }

    private static string GetPlatformDirectoryName()
    {
        if (OperatingSystem.IsWindows()) return "Win64";
        if (OperatingSystem.IsMacOS()) return "Mac";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    private static string BuildCompatibilityStatus(
        string association,
        string? engineVersion,
        UnrealProjectKind projectKind,
        UnrealPluginInstallMode installMode,
        bool versionSupported)
    {
        if (engineVersion is null)
        {
            string detected = string.IsNullOrWhiteSpace(association) ? "missing" : association;
            return $"Compatibility blocked: the Unreal version could not be resolved from EngineAssociation '{detected}'.";
        }
        if (!versionSupported)
        {
            return $"Compatibility blocked: Unreal {engineVersion} is outside the supported range 4.27-5.8.";
        }
        if (projectKind is UnrealProjectKind.Cpp && installMode is UnrealPluginInstallMode.Source)
        {
            return $"Compatible: Unreal {engineVersion} C++ project; source plugin installation selected.";
        }
        if (projectKind is UnrealProjectKind.BlueprintOnly && installMode is UnrealPluginInstallMode.Precompiled)
        {
            return $"Compatible: Unreal {engineVersion} Blueprint-only project; exact {GetPlatformDirectoryName()} binary selected.";
        }
        if (projectKind is UnrealProjectKind.BlueprintOnly)
        {
            return $"Compatibility blocked: this Blueprint-only project requires the exact Unreal {engineVersion} {GetPlatformDirectoryName()} binary, which is not bundled.";
        }
        return "Compatibility blocked: the project type could not be determined safely.";
    }

    private static string FormatInstallMode(UnrealPluginInstallMode mode) => mode switch
    {
        UnrealPluginInstallMode.Source => "C++ source",
        UnrealPluginInstallMode.Precompiled => "Blueprint-compatible precompiled",
        _ => "unavailable"
    };

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

    private static void CopyPluginTree(
        string source,
        string destination,
        UnrealPluginInstallMode installMode,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file);
            string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (first.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Saved", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Variants", StringComparison.OrdinalIgnoreCase) ||
                (installMode is UnrealPluginInstallMode.Source &&
                 first.Equals("Binaries", StringComparison.OrdinalIgnoreCase)) ||
                (installMode is UnrealPluginInstallMode.Precompiled &&
                 first.Equals("Source", StringComparison.OrdinalIgnoreCase)))
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
