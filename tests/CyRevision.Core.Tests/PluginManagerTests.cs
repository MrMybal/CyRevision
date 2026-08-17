using System.Text.Json;
using CyRevision.Desktop.Plugins;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class PluginManagerTests
{
    [Fact]
    public async Task PackagedPluginCanBeEnabledAndDisabledWithoutApplicationReference()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyrevision-plugin-manager-tests", Guid.NewGuid().ToString("N"));
        string application = Path.Combine(root, "app");
        string package = Path.Combine(application, "Plugins", "CyRevision.UnrealIntegration");
        string configuration = Path.Combine(root, "config");
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(package);
        try
        {
            File.Copy(
                typeof(UnrealIntegrationPlugin).Assembly.Location,
                Path.Combine(package, "CyRevision.Plugin.Unreal.dll"));
            await File.WriteAllTextAsync(Path.Combine(package, "cyrevision-plugin.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                id = "cyrevision.unreal",
                name = "Unreal Engine Integration",
                version = "0.1.0",
                description = "test",
                category = "Game engines",
                entryAssembly = "CyRevision.Plugin.Unreal.dll",
                entryType = "CyRevision.Plugin.Unreal.UnrealIntegrationPlugin",
                enabledByDefault = false
            }));

            await using CyRevisionPluginManager manager = new(application, configuration, data, "0.1.5");
            await manager.InitializeAsync();
            PluginCatalogEntry entry = Assert.Single(manager.Entries);
            Assert.False(entry.IsEnabled);

            await manager.EnableAsync(entry.Id);
            Assert.True(entry.IsEnabled, entry.Status);
            Assert.Null(manager.GetPlugin<IUnrealIntegrationPlugin>());

            manager.SetProjectScope([entry.Id]);
            Assert.NotNull(manager.GetPlugin<IUnrealIntegrationPlugin>());

            await manager.DisableAsync(entry.Id);
            Assert.False(entry.IsEnabled);
            Assert.Null(manager.GetPlugin<IUnrealIntegrationPlugin>());
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Directory.Delete(root, true);
        }
    }
}
