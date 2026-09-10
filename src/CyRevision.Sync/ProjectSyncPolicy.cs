using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;

namespace CyRevision.Sync;

public static class ProjectSyncPolicy
{
    public static bool OwnsRuntime(Guid? projectId, Guid? profileProjectId, Guid? engineProjectId) =>
        projectId is { } id && id != Guid.Empty && profileProjectId == id && engineProjectId == id;

    public static string ResolveExchangeDirectory(ProjectDefinition project, string dataDirectory, string? sourceFolder = null) =>
        project.Features.GitEnabled
            ? Path.Combine(dataDirectory, "git-exchange", project.Id.ToString("N"))
            : project.OperatingMode == ProjectPresetKind.SyncWithCommits
                ? Path.Combine(dataDirectory, "sync-commit-exchange", project.Id.ToString("N"))
                : sourceFolder ?? project.RootPath;

    public static string ResolveStateDirectory(ProjectDefinition project, string dataDirectory, string category)
    {
        string local = Path.Combine(dataDirectory, category, project.Id.ToString("N"));
        // Keep legacy state intact when a local project joins a different shared identity.
        return project.SyncProjectId == project.Id ? local : Path.Combine(local, "shared", project.SyncProjectId.ToString("N"));
    }
}
