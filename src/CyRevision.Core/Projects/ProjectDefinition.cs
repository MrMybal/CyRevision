using CyRevision.Core.Configuration;

namespace CyRevision.Core.Projects;

public sealed record ProjectDefinition(
    Guid Id,
    string Name,
    string RootPath,
    ProjectFeatures Features,
    RetentionPolicy Retention)
{
    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("A project ID is required.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("A project name is required.");
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException("A project root path is required.");
        }

        Features.Validate();
        Retention.Validate();
    }
}

