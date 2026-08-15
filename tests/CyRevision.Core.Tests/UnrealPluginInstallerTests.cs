using System.Text.Json;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class UnrealPluginInstallerTests
{
    [Fact]
    public void InstallCopiesPayloadEnablesPluginAndKeepsProjectBackup()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string payload = CreatePayload(root, "0.3.0");
            string projectRoot = Path.Combine(root, "Game");
            Directory.CreateDirectory(projectRoot);
            string projectFile = Path.Combine(projectRoot, "Game.uproject");
            CreateCppProject(projectRoot, projectFile, "5.5");
            UnrealProjectPluginInstaller installer = new(payload);

            var result = installer.InstallOrUpdate(projectFile);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(File.Exists(Path.Combine(result.DestinationDirectory, "CyRevisionUnreal.uplugin")));
            Assert.True(File.Exists(projectFile + ".cyrevision.bak"));
            using JsonDocument project = JsonDocument.Parse(File.ReadAllText(projectFile));
            JsonElement plugin = project.RootElement.GetProperty("Plugins").EnumerateArray().Single();
            Assert.Equal("CyRevisionUnreal", plugin.GetProperty("Name").GetString());
            Assert.True(plugin.GetProperty("Enabled").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UpdateMovesPreviousPluginToRecoverableBackup()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string payload = CreatePayload(root, "0.3.0");
            string projectRoot = Path.Combine(root, "Game");
            Directory.CreateDirectory(projectRoot);
            CreateCppProject(projectRoot, Path.Combine(projectRoot, "Game.uproject"), "5.5");
            string installed = Path.Combine(projectRoot, "Plugins", "CyRevisionUnreal");
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "CyRevisionUnreal.uplugin"),
                "{\"VersionName\":\"0.2.0\"}");
            File.WriteAllText(Path.Combine(installed, "previous.txt"), "recover me");
            UnrealProjectPluginInstaller installer = new(payload);

            var result = installer.InstallOrUpdate(projectRoot);

            Assert.True(result.Succeeded, result.Message);
            Assert.NotNull(result.BackupDirectory);
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "previous.txt")));
            Assert.Equal("0.3.0", UnrealProjectPluginInstaller.ReadPluginVersion(result.DestinationDirectory));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InspectionReportsEngineProjectKindAndSourceCompatibility()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string payload = CreatePayload(root, "0.6.0");
            string projectRoot = Path.Combine(root, "CppGame");
            Directory.CreateDirectory(projectRoot);
            string projectFile = Path.Combine(projectRoot, "CppGame.uproject");
            CreateCppProject(projectRoot, projectFile, "4.27");

            UnrealProjectInspection inspection = new UnrealProjectPluginInstaller(payload).Inspect(projectFile);

            Assert.True(inspection.IsCompatible, inspection.CompatibilityStatus);
            Assert.Equal("4.27", inspection.EngineVersion);
            Assert.Equal(UnrealProjectKind.Cpp, inspection.ProjectKind);
            Assert.Equal(UnrealPluginInstallMode.Source, inspection.InstallMode);
            Assert.Contains("5.8", inspection.SupportedEngineVersions);
            Assert.Empty(inspection.AvailablePrecompiledVersions);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BlueprintOnlyProjectRequiresExactPrecompiledVariant()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string payload = CreatePayload(root, "0.6.0");
            string projectRoot = Path.Combine(root, "BlueprintGame");
            Directory.CreateDirectory(projectRoot);
            string projectFile = Path.Combine(projectRoot, "BlueprintGame.uproject");
            File.WriteAllText(projectFile, "{\"FileVersion\":3,\"EngineAssociation\":\"5.5\"}");
            UnrealProjectPluginInstaller installer = new(payload);

            UnrealProjectInspection blocked = installer.Inspect(projectFile);
            Assert.False(blocked.IsCompatible);
            Assert.Equal(UnrealProjectKind.BlueprintOnly, blocked.ProjectKind);

            string variant = Path.Combine(payload, "Variants", "UE5.5", PlatformDirectory(), "CyRevisionUnreal");
            Directory.CreateDirectory(Path.Combine(variant, "Binaries"));
            File.WriteAllText(Path.Combine(variant, "CyRevisionUnreal.uplugin"), "{\"VersionName\":\"0.6.0\"}");
            File.WriteAllText(Path.Combine(variant, "Binaries", "module.bin"), "precompiled");

            UnrealProjectInspection compatible = installer.Inspect(projectFile);
            Assert.True(compatible.IsCompatible, compatible.CompatibilityStatus);
            Assert.Equal(UnrealPluginInstallMode.Precompiled, compatible.InstallMode);
            Assert.Equal(PlatformDirectory(), compatible.PrecompiledPlatform);
            Assert.Contains("5.5", compatible.AvailablePrecompiledVersions);
            UnrealPluginInstallationResult result = installer.InstallOrUpdate(projectFile);
            Assert.True(result.Succeeded, result.Message);
            Assert.True(File.Exists(Path.Combine(result.DestinationDirectory, "Binaries", "module.bin")));
            Assert.False(Directory.Exists(Path.Combine(result.DestinationDirectory, "Source")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreatePayload(string root, string version)
    {
        string payload = Path.Combine(root, "Payload", "CyRevisionUnreal");
        Directory.CreateDirectory(Path.Combine(payload, "Source"));
        File.WriteAllText(Path.Combine(payload, "CyRevisionUnreal.uplugin"),
            $"{{\"VersionName\":\"{version}\"}}");
        File.WriteAllText(Path.Combine(payload, "Source", "module.txt"), "source");
        return payload;
    }

    private static void CreateCppProject(string projectRoot, string projectFile, string engineVersion)
    {
        File.WriteAllText(
            projectFile,
            $"{{\"FileVersion\":3,\"EngineAssociation\":\"{engineVersion}\"}}");
        string source = Path.Combine(projectRoot, "Source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Game.Target.cs"), "// native target");
    }

    private static string PlatformDirectory() =>
        OperatingSystem.IsWindows() ? "Win64" : OperatingSystem.IsMacOS() ? "Mac" : "Linux";

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "cyrevision-unreal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
