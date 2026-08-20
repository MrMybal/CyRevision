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
    string? BackupStorePath = null,
    string? ColdArchivePath = null,
    int? ColdArchiveAfterDays = null,
    int? SidebarOrder = null,
    string? AccentColor = null,
    string[]? EnabledPluginIds = null,
    ProjectPresetKind? OperatingMode = null,
    string? BackupArchiveProfile = null,
    bool RemoveArchivedGitBranches = false,
    bool RemoveArchivedHotBackups = false,
    string? GitArchiveProfile = null,
    string? PluginOperatingModeId = null,
    string? PluginOperatingModeProviderId = null,
    bool StartSyncAutomatically = false,
    bool StartVpnAutomatically = false,
    bool ProjectNotificationsEnabled = true,
    string? SidebarGroup = null)
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

        if (!string.IsNullOrWhiteSpace(ColdArchivePath) && !Path.IsPathFullyQualified(ColdArchivePath))
        {
            throw new InvalidOperationException("The cold archive path must be absolute.");
        }

        if (ColdArchiveAfterDays is <= 0)
        {
            throw new InvalidOperationException("The cold archive age must be greater than zero.");
        }

        if (SidebarOrder is < 0)
        {
            throw new InvalidOperationException("The sidebar order cannot be negative.");
        }

        if (AccentColor is not null &&
            (AccentColor.Length != 7 || AccentColor[0] != '#' ||
             !AccentColor.AsSpan(1).ToString().All(Uri.IsHexDigit)))
        {
            throw new InvalidOperationException("The project accent color must use the #RRGGBB format.");
        }

        if (EnabledPluginIds is not null &&
            (EnabledPluginIds.Any(string.IsNullOrWhiteSpace) ||
             EnabledPluginIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != EnabledPluginIds.Length))
        {
            throw new InvalidOperationException("Project plugin IDs must be non-empty and unique.");
        }

        if (string.IsNullOrWhiteSpace(PluginOperatingModeId) !=
            string.IsNullOrWhiteSpace(PluginOperatingModeProviderId))
        {
            throw new InvalidOperationException("A plugin operating mode requires both a mode ID and a provider plugin ID.");
        }

        if (!string.IsNullOrWhiteSpace(PluginOperatingModeProviderId) &&
            EnabledPluginIds is not null &&
            !EnabledPluginIds.Contains(PluginOperatingModeProviderId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The provider of a plugin operating mode must be enabled for the project.");
        }

        if (SidebarGroup is { Length: > 80 })
            throw new InvalidOperationException("The project group name cannot exceed 80 characters.");
    }
}
