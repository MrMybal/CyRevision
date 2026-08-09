using CyRevision.Core.Configuration;

namespace CyRevision.Core.Tests;

public sealed class ProjectPresetTests
{
    [Fact]
    public void EveryBuiltInPresetIsValid()
    {
        foreach (ProjectPreset preset in ProjectPresets.All)
        {
            preset.Validate();
        }
    }

    [Fact]
    public void GitOnlyDoesNotStartPeerSync()
    {
        ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == ProjectPresetKind.GitOnly);

        Assert.True(preset.Features.GitEnabled);
        Assert.False(preset.Features.PeerSyncEnabled);
    }

    [Fact]
    public void SyncOnlyDoesNotRequireGit()
    {
        ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == ProjectPresetKind.SyncOnly);

        Assert.True(preset.Features.PeerSyncEnabled);
        Assert.False(preset.Features.GitEnabled);
        Assert.False(preset.Features.LfsEnabled);
    }

    [Fact]
    public void LfsWithoutGitIsRejected()
    {
        ProjectFeatures features = new(
            GitEnabled: false,
            LfsEnabled: true,
            PeerSyncEnabled: false,
            BackupEnabled: false,
            StandardGitRemoteEnabled: false);

        Assert.Throws<InvalidOperationException>(features.Validate);
    }
}

