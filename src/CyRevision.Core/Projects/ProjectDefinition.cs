using CyRevision.Core.Configuration;

namespace CyRevision.Core.Projects;

public sealed record ProjectDefinition(
    Guid Id,
    string Name,
    string RootPath,
    ProjectFeatures Features,
    RetentionPolicy Retention,
    string? StandardRemoteUrl = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? LastOpenedAt = null,
    string? BackupStorePath = null)
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

        if (StandardRemoteUrl is not null &&
            !Uri.TryCreate(StandardRemoteUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("The standard remote URL is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(BackupStorePath) && !Path.IsPathFullyQualified(BackupStorePath))
        {
            throw new InvalidOperationException("The backup store path must be absolute.");
        }
    }
}
