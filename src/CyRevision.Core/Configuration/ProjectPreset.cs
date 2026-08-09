namespace CyRevision.Core.Configuration;

public enum ProjectPresetKind
{
    GitOnly,
    GitWithPeerSync,
    SyncOnly,
    SyncWithVersions,
    BackupOnly,
    Custom
}

public sealed record ProjectPreset(
    ProjectPresetKind Kind,
    string Name,
    string Description,
    ProjectFeatures Features,
    RetentionPolicy Retention)
{
    public void Validate()
    {
        Features.Validate();
        Retention.Validate();
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
            ProjectPresetKind.BackupOnly,
            "Backup uniquement",
            "Snapshots locaux ou distants, sans Git et sans synchronisation P2P.",
            new ProjectFeatures(false, false, false, true, false),
            RetentionPolicy.KeepForever)
    ];
}

