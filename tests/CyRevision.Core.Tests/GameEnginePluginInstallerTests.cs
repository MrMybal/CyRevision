using CyRevision.Plugin.Godot;
using CyRevision.Plugin.Unity;

namespace CyRevision.Core.Tests;

public sealed class GameEnginePluginInstallerTests
{
    [Fact]
    public void Unity_installer_detects_installs_and_writes_private_bridge_settings()
    {
        using TemporaryDirectory project = new();
        using TemporaryDirectory payload = new();
        Directory.CreateDirectory(Path.Combine(project.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(project.Path, "ProjectSettings"));
        File.WriteAllText(Path.Combine(project.Path, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.51f1\n");
        File.WriteAllText(Path.Combine(payload.Path, "package.json"), "{\"name\":\"com.cyrevision.editor\",\"version\":\"0.1.0\"}");
        Directory.CreateDirectory(Path.Combine(payload.Path, "Editor"));
        File.WriteAllText(Path.Combine(payload.Path, "Editor", "CyRevisionWindow.cs"), "// payload");

        var inspection = UnityProjectPluginInstaller.Inspect(project.Path, payload.Path);
        Assert.True(inspection.IsValid);
        Assert.True(inspection.IsCompatible);
        Assert.Equal("2022.3.51f1", inspection.EngineVersion);

        var installation = UnityProjectPluginInstaller.InstallOrUpdate(project.Path, payload.Path);
        Assert.True(installation.Succeeded, installation.Message);
        Assert.True(File.Exists(Path.Combine(project.Path, "Packages", "com.cyrevision.editor", "package.json")));

        UnityProjectPluginInstaller.WriteBridgeSettings(project.Path, "http://127.0.0.1:47833/cyrevision/v1/", "secret", "CyRevision");
        string settings = File.ReadAllText(Path.Combine(project.Path, "Library", "CyRevision", "bridge.json"));
        Assert.Contains("secret", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Godot_installer_detects_installs_enables_and_writes_private_bridge_settings()
    {
        using TemporaryDirectory project = new();
        using TemporaryDirectory payload = new();
        File.WriteAllText(Path.Combine(project.Path, "project.godot"),
            "config_version=5\n\n[application]\nconfig/name=\"Sample\"\nconfig/features=PackedStringArray(\"4.3\")\n");
        File.WriteAllText(Path.Combine(payload.Path, "plugin.cfg"), "[plugin]\nname=\"CyRevision\"\nversion=\"0.1.0\"\nscript=\"cyrevision_plugin.gd\"\n");
        File.WriteAllText(Path.Combine(payload.Path, "cyrevision_plugin.gd"), "@tool\nextends EditorPlugin\n");

        var inspection = GodotProjectPluginInstaller.Inspect(project.Path, payload.Path);
        Assert.True(inspection.IsValid);
        Assert.True(inspection.IsCompatible);

        var installation = GodotProjectPluginInstaller.InstallOrUpdate(project.Path, payload.Path);
        Assert.True(installation.Succeeded, installation.Message);
        Assert.True(File.Exists(Path.Combine(project.Path, "addons", "cyrevision", "plugin.cfg")));
        Assert.Contains("res://addons/cyrevision/plugin.cfg", File.ReadAllText(Path.Combine(project.Path, "project.godot")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project.Path, ".godot", "cyrevision", "project.godot.before-plugin.bak")));

        GodotProjectPluginInstaller.WriteBridgeSettings(project.Path, "http://127.0.0.1:47834/cyrevision/v1/", "secret", "CyRevision");
        string settings = File.ReadAllText(Path.Combine(project.Path, ".godot", "cyrevision", "bridge.json"));
        Assert.Contains("secret", settings, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrevision-engine-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
