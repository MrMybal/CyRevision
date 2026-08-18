using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.Perforce;

namespace CyRevision.Core.Tests;

public sealed class PerforceIntegrationPluginTests
{
    [Fact]
    public async Task PluginContributesAProjectScopedPerforceMode()
    {
        using TemporaryDirectory temporary = new();
        await using PerforceIntegrationPlugin plugin = new();

        PluginProjectModeDescriptor mode = Assert.Single(plugin.ProjectModes);
        PluginProjectModeAvailability availability = plugin.EvaluateProjectMode(
            mode.Id,
            new PluginProjectModeContext(Guid.NewGuid(), "Sample", temporary.Path, [plugin.Descriptor.Id]));

        Assert.Equal("perforce", mode.Id);
        Assert.False(mode.Features.GitEnabled);
        Assert.True(mode.Features.BackupEnabled);
        Assert.Contains("PerforceWorkspaceTab", mode.WorkspaceTabIds);
        Assert.True(availability.IsAvailable, availability.Summary);
    }

    [Fact]
    public async Task ProjectSettingsRoundTripWithoutCredentials()
    {
        using TemporaryDirectory temporary = new();
        string configuration = Path.Combine(temporary.Path, "configuration");
        await using PerforceIntegrationPlugin plugin = new();
        await plugin.InitializeAsync(new CyRevisionPluginContext(
            temporary.Path,
            temporary.Path,
            configuration,
            Path.Combine(temporary.Path, "data"),
            "test"));
        PerforceProjectSettings expected = new(
            Guid.NewGuid(), temporary.Path, "p4", "ssl:perforce.example:1666", "alice", "sample-alice", false);

        await plugin.SaveSettingsAsync(expected);
        PerforceProjectSettings? actual = await plugin.LoadSettingsAsync(expected.ProjectId);

        Assert.Equal(expected, actual);
        string settingsText = await File.ReadAllTextAsync(Path.Combine(
            configuration, "plugins", "perforce", "projects", expected.ProjectId.ToString("N") + ".json"));
        Assert.DoesNotContain("password", settingsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ticket", settingsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MutatingCommandsAreRejectedUntilExplicitlyEnabled()
    {
        using TemporaryDirectory temporary = new();
        await using PerforceIntegrationPlugin plugin = new();
        PerforceProjectSettings settings = new(
            Guid.NewGuid(), temporary.Path, "p4", "perforce:1666", "alice", "sample-alice", false);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => plugin.ReconcileAsync(settings));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrevision-perforce-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
