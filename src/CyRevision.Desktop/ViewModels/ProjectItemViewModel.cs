using CyRevision.Core.Projects;

namespace CyRevision.Desktop.ViewModels;

public sealed class ProjectItemViewModel(ProjectDefinition definition)
{
    public ProjectDefinition Definition { get; private set; } = definition;

    public Guid Id => Definition.Id;

    public string Name => Definition.Name;

    public string RootPath => Definition.RootPath;

    public string Mode
    {
        get
        {
            if (Definition.Features.GitEnabled && Definition.Features.PeerSyncEnabled)
            {
                return "Git + Sync";
            }

            if (Definition.Features.GitEnabled)
            {
                return "Git";
            }

            if (Definition.Features.PeerSyncEnabled && Definition.Features.BackupEnabled)
            {
                return "Sync + versions";
            }

            if (Definition.Features.PeerSyncEnabled)
            {
                return "Sync";
            }

            return "Backup";
        }
    }

    public void Update(ProjectDefinition definition) => Definition = definition;
}

