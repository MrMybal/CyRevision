namespace CyRevision.Core.Projects;

public interface IProjectCatalog
{
    Task<IReadOnlyList<ProjectDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProjectDefinition?> FindByIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task UpsertAsync(ProjectDefinition project, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default);
}

