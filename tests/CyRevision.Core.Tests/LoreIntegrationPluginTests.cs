using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Lore;

namespace CyRevision.Core.Tests;

public sealed class LoreIntegrationPluginTests
{
    [Fact]
    public async Task InspectionReadsLoreConfigurationWithoutRunningScan()
    {
        using TemporaryDirectory temporary = new();
        string lore = Path.Combine(temporary.Path, ".lore");
        Directory.CreateDirectory(lore);
        await File.WriteAllTextAsync(
            Path.Combine(lore, "config.toml"),
            "server_url = \"lore://127.0.0.1:41337\"\nrepository = \"sample-game\"\nbranch = \"main\"\n");
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "Sample.uproject"), "{\"FileVersion\":3}");
        await using LoreIntegrationPlugin plugin = new();

        LoreProjectInspection inspection = plugin.InspectProject(temporary.Path);

        Assert.True(inspection.IsProject);
        Assert.True(inspection.UnrealProjectDetected);
        Assert.Equal("lore://127.0.0.1:41337", inspection.ServerUrl);
        Assert.Equal("sample-game", inspection.RepositoryName);
        Assert.Equal("main", inspection.CurrentBranch);
    }

    [Fact]
    public async Task PluginContributesLoreModeAndChecksTheSelectedProject()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(Path.Combine(temporary.Path, ".lore"));
        await File.WriteAllTextAsync(
            Path.Combine(temporary.Path, ".lore", "config.toml"),
            "repository = \"sample-game\"\nbranch = \"dev\"\n");
        await using LoreIntegrationPlugin plugin = new();

        PluginProjectModeDescriptor mode = Assert.Single(plugin.ProjectModes);
        PluginProjectModeAvailability availability = plugin.EvaluateProjectMode(
            mode.Id,
            new PluginProjectModeContext(
                Guid.NewGuid(),
                "Sample",
                temporary.Path,
                [plugin.Descriptor.Id]));

        Assert.Equal("lore", mode.Id);
        Assert.False(mode.Features.GitEnabled);
        Assert.True(mode.Features.BackupEnabled);
        Assert.Contains("LoreWorkspaceTab", mode.WorkspaceTabIds);
        Assert.True(availability.IsAvailable, availability.Summary);
        Assert.Contains("dev", availability.Summary);
    }

    [Fact]
    public async Task LoreModeExplainsWhyARegularFolderIsUnavailable()
    {
        using TemporaryDirectory temporary = new();
        await using LoreIntegrationPlugin plugin = new();

        PluginProjectModeAvailability availability = plugin.EvaluateProjectMode(
            "lore",
            new PluginProjectModeContext(Guid.NewGuid(), "Regular", temporary.Path, [plugin.Descriptor.Id]));

        Assert.False(availability.IsAvailable);
        Assert.Contains(".lore/config.toml", availability.Summary);
    }

    [Fact]
    public async Task UnrealCompanionInstallerCopiesPayloadAndKeepsPreviousCopy()
    {
        using TemporaryDirectory temporary = new();
        string payload = Path.Combine(temporary.Path, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "Source"));
        await File.WriteAllTextAsync(Path.Combine(payload, "CyRevisionLore.uplugin"), "{\"VersionName\":\"0.1.0\"}");
        await File.WriteAllTextAsync(Path.Combine(payload, "Source", "module.txt"), "new");

        string project = Path.Combine(temporary.Path, "project");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "Game.uproject"), "{\"FileVersion\":3}");
        string installed = Path.Combine(project, "Plugins", "CyRevisionLore");
        Directory.CreateDirectory(installed);
        await File.WriteAllTextAsync(Path.Combine(installed, "old.txt"), "recover me");

        await using LoreIntegrationPlugin plugin = new(payload);
        await plugin.InitializeAsync(new CyRevisionPluginContext(
            temporary.Path,
            temporary.Path,
            Path.Combine(temporary.Path, "config"),
            Path.Combine(temporary.Path, "data"),
            "test"));

        LoreUnrealCompanionInstallationResult result = await plugin.InstallOrUpdateUnrealCompanionAsync(project);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(Path.Combine(result.DestinationDirectory, "CyRevisionLore.uplugin")));
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "old.txt")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrevision-lore-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
