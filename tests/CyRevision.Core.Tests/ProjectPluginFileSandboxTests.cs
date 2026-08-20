using CyRevision.Desktop.Plugins;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Core.Tests;

public sealed class ProjectPluginFileSandboxTests
{
    [Fact]
    public async Task FileBrokerHonorsProjectRootsPermissionsAndLimits()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyrevision-plugin-sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Content"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Content", "asset.txt"), "asset");
            ProjectPluginFileSandbox sandbox = new(new PluginProjectSandboxPolicy(
                Guid.NewGuid(),
                "test.plugin",
                root,
                PluginProjectPermission.EnumerateProjectFiles | PluginProjectPermission.ReadProjectFiles,
                ["Content"],
                MaximumReadBytes: 64,
                MaximumWriteBytes: 64));

            Assert.Equal("asset", System.Text.Encoding.UTF8.GetString(
                await sandbox.ReadAllBytesAsync("Content/asset.txt")));
            Assert.Contains("Content/asset.txt", await sandbox.EnumerateFilesAsync("Content", "*.txt"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sandbox.ReadAllBytesAsync("../outside.txt"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                sandbox.WriteAllBytesAsync("Content/generated.txt", new byte[] { 1 }));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FileBrokerWritesAtomicallyInsideAnAllowedProjectRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyrevision-plugin-sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ProjectPluginFileSandbox sandbox = new(new PluginProjectSandboxPolicy(
                Guid.NewGuid(),
                "test.plugin",
                root,
                PluginProjectPermission.WriteProjectFiles,
                ["Generated"]));
            await sandbox.WriteAllBytesAsync("Generated/result.bin", new byte[] { 1, 2, 3 });
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(root, "Generated", "result.bin")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
