namespace CyRevision.RemoteBuild;

public enum RemoteBuildSourceMode
{
    ExistingWorkspace,
    UploadedSnapshot
}

public enum RemoteBuildJobState
{
    Queued,
    Preparing,
    Running,
    Packaging,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record RemoteBuildRecipe(
    string Id,
    string DisplayName,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyList<string> ArtifactPatterns,
    int TimeoutMinutes)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new InvalidOperationException("Build recipe IDs may contain only letters, numbers, dots, dashes, and underscores.");
        if (string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Executable))
            throw new InvalidOperationException("Every build recipe requires a name and executable.");
        ValidateRelativePath(WorkingDirectory, "recipe working directory");
        if (TimeoutMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Build timeout must be between 1 and 1440 minutes.");
        if (ArtifactPatterns.Count == 0)
            throw new InvalidOperationException("Every build recipe must declare at least one artifact pattern.");
        foreach (string pattern in ArtifactPatterns)
            ValidateRelativePath(pattern, "artifact pattern", allowWildcards: true);
    }

    internal static void ValidateRelativePath(string value, string label, bool allowWildcards = false)
    {
        string normalized = (value ?? string.Empty).Replace('\\', '/').Trim();
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized) ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..") ||
            (!allowWildcards && normalized.IndexOfAny(['*', '?']) >= 0))
            throw new InvalidOperationException($"The {label} must be a safe relative path.");
    }
}

public sealed record RemoteBuildAgentProject(
    Guid ProjectId,
    string ProjectName,
    string WorkspaceRoot,
    bool AllowUploadedSnapshots,
    long MaximumSnapshotBytes,
    IReadOnlyList<RemoteBuildRecipe> Recipes)
{
    public void Validate()
    {
        if (ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(ProjectName))
            throw new InvalidOperationException("Remote build projects require an ID and name.");
        if (string.IsNullOrWhiteSpace(WorkspaceRoot))
            throw new InvalidOperationException("Remote build projects require a workspace root.");
        if (MaximumSnapshotBytes is < 1024 or > 2L * 1024 * 1024 * 1024 * 1024)
            throw new InvalidOperationException("Maximum snapshot bytes are outside the supported range.");
        if (Recipes.Count == 0)
            throw new InvalidOperationException("Remote build projects require at least one allowlisted recipe.");
        foreach (RemoteBuildRecipe recipe in Recipes)
            recipe.Validate();
        if (Recipes.Select(recipe => recipe.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Recipes.Count)
            throw new InvalidOperationException("Remote build recipe IDs must be unique within a project.");
    }
}

public sealed record RemoteBuildAgentConfiguration(
    string JobsRoot,
    int MaximumParallelJobs,
    int CompletedJobRetentionHours,
    IReadOnlyList<RemoteBuildAgentProject> Projects)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(JobsRoot))
            throw new InvalidOperationException("A remote build jobs root is required.");
        if (MaximumParallelJobs is < 1 or > 32)
            throw new InvalidOperationException("Maximum parallel jobs must be between 1 and 32.");
        if (CompletedJobRetentionHours is < 1 or > 8760)
            throw new InvalidOperationException("Completed job retention must be between 1 and 8760 hours.");
        foreach (RemoteBuildAgentProject project in Projects)
            project.Validate();
        if (Projects.Select(project => project.ProjectId).Distinct().Count() != Projects.Count)
            throw new InvalidOperationException("Remote build project IDs must be unique.");
    }
}

public sealed record RemoteBuildConnectionProfile(
    Guid ProjectId,
    string Endpoint,
    string RecipeId,
    RemoteBuildSourceMode SourceMode,
    string ArtifactDestination,
    long MaximumUploadBytes,
    bool AllowPrivateHttp)
{
    public static RemoteBuildConnectionProfile CreateDefault(Guid projectId, string artifactDestination) => new(
        projectId,
        "http://127.0.0.1:47841",
        string.Empty,
        RemoteBuildSourceMode.ExistingWorkspace,
        artifactDestination,
        100L * 1024 * 1024 * 1024,
        false);
}

public sealed record RemoteBuildCredentials(RemoteBuildConnectionProfile Profile, string AccessToken);

public sealed record RemoteBuildRecipeDescriptor(string Id, string DisplayName, int TimeoutMinutes);

public sealed record RemoteBuildProjectDescriptor(
    Guid ProjectId,
    string ProjectName,
    bool AllowsUploadedSnapshots,
    IReadOnlyList<RemoteBuildRecipeDescriptor> Recipes);

public sealed record RemoteBuildAgentStatus(
    string Service,
    string Version,
    DateTimeOffset StartedAt,
    int RunningJobs,
    int ConfiguredProjects);

public sealed record RemoteBuildJobStatus(
    Guid JobId,
    Guid ProjectId,
    string RecipeId,
    RemoteBuildSourceMode SourceMode,
    RemoteBuildJobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string Message,
    string LogTail,
    bool HasArtifacts);

public sealed record RemoteBuildSnapshotResult(
    string ArchivePath,
    int FileCount,
    long SourceBytes,
    string Revision,
    bool HasLocalChanges);
