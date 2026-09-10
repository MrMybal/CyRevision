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
        Assert.Equal(FeatureMaturity.Beta, preset.Maturity);
        Assert.Equal("BETA", preset.MaturityLabel);
    }

    [Fact]
    public void SyncOnlyDoesNotRequireGit()
    {
        ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == ProjectPresetKind.SyncOnly);

        Assert.True(preset.Features.PeerSyncEnabled);
        Assert.False(preset.Features.GitEnabled);
        Assert.False(preset.Features.LfsEnabled);
        Assert.Equal(FeatureMaturity.Alpha, preset.Maturity);
    }

    [Fact]
    public void SyncCommitPublishesImmutableVersionsWithoutGit()
    {
        ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == ProjectPresetKind.SyncWithCommits);

        Assert.True(preset.Features.PeerSyncEnabled);
        Assert.True(preset.Features.BackupEnabled);
        Assert.False(preset.Features.GitEnabled);
        Assert.Equal(RetentionMode.Timeline, preset.Retention.Mode);
        Assert.Equal(FeatureMaturity.Alpha, preset.Maturity);
    }

    [Fact]
    public void EveryNonGitOnlyBuiltInModeIsAlpha()
    {
        foreach (ProjectPreset preset in ProjectPresets.All.Where(item => item.Kind != ProjectPresetKind.GitOnly))
        {
            Assert.Equal(FeatureMaturity.Alpha, preset.Maturity);
            Assert.True(preset.IsAlpha);
        }
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
