namespace CyRevision.Core.Configuration;

public enum ProjectPresetKind
{
    GitOnly,
    GitWithPeerSync,
    SyncOnly,
    SyncWithVersions,
    SyncWithCommits,
    BackupOnly,
    Custom
}

public sealed record ProjectPreset(
    ProjectPresetKind Kind,
    string Name,
    string Description,
    ProjectFeatures Features,
    RetentionPolicy Retention,
    string? PluginModeId = null,
    string? ProviderPluginId = null,
    IReadOnlyList<string>? WorkspaceTabIds = null,
    string? CategoryLabel = null,
    bool IsAvailable = true,
    string AvailabilitySummary = "")
{
    public bool IsPluginMode =>
        !string.IsNullOrWhiteSpace(PluginModeId) &&
        !string.IsNullOrWhiteSpace(ProviderPluginId);

    public void Validate()
    {
        Features.Validate();
        Retention.Validate();

        if (string.IsNullOrWhiteSpace(PluginModeId) != string.IsNullOrWhiteSpace(ProviderPluginId))
        {
            throw new InvalidOperationException("A plugin mode requires both a mode ID and a provider plugin ID.");
        }
    }
}

public static class ProjectPresets
{
    public static IReadOnlyList<ProjectPreset> All { get; } =
    [
        new(
            ProjectPresetKind.GitOnly,
            "Git uniquement",
            "Gestion Git et LFS complète, sans moteur de synchronisation.",
            new ProjectFeatures(true, true, false, false, false),
            RetentionPolicy.CurrentStateOnly),
        new(
            ProjectPresetKind.GitWithPeerSync,
            "Git + Sync",
            "Git et LFS distribués directement entre les membres autorisés.",
            new ProjectFeatures(true, true, true, false, false),
            RetentionPolicy.CurrentStateOnly),
        new(
            ProjectPresetKind.SyncOnly,
            "Sync uniquement",
            "Synchronisation de l'état actuel, sans dépôt Git.",
            new ProjectFeatures(false, false, true, false, false),
            RetentionPolicy.CurrentStateOnly),
        new(
            ProjectPresetKind.SyncWithVersions,
            "Sync + versions",
            "Synchronisation avec snapshots et conservation configurable.",
            new ProjectFeatures(false, false, true, true, false),
            new RetentionPolicy(RetentionMode.Timeline, 30, TimeSpan.FromDays(90))),
        new(
            ProjectPresetKind.SyncWithCommits,
            "Sync + Commit",
            "Commit-style snapshots without Git. Synchronization only publishes complete, immutable commits.",
            new ProjectFeatures(false, false, true, true, false),
            new RetentionPolicy(RetentionMode.Timeline, 60, TimeSpan.FromDays(180))),
        new(
            ProjectPresetKind.BackupOnly,
            "Backup uniquement",
            "Snapshots locaux ou distants, sans Git et sans synchronisation P2P.",
            new ProjectFeatures(false, false, false, true, false),
            RetentionPolicy.KeepForever)
    ];
}
