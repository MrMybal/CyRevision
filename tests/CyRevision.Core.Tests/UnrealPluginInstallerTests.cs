using System.Text.Json;
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
            File.WriteAllText(projectFile, "{\"FileVersion\":3}");
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
            File.WriteAllText(Path.Combine(projectRoot, "Game.uproject"), "{\"FileVersion\":3}");
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

    private static string CreatePayload(string root, string version)
    {
        string payload = Path.Combine(root, "Payload", "CyRevisionUnreal");
        Directory.CreateDirectory(Path.Combine(payload, "Source"));
        File.WriteAllText(Path.Combine(payload, "CyRevisionUnreal.uplugin"),
            $"{{\"VersionName\":\"{version}\"}}");
        File.WriteAllText(Path.Combine(payload, "Source", "module.txt"), "source");
        return payload;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "cyrevision-unreal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
