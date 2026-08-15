namespace CyRevision.Core.Configuration;

public sealed record ApplicationPaths(
    string ConfigurationDirectory,
    string DataDirectory,
    string CacheDirectory)
{
    public string ProjectCatalogPath => Path.Combine(ConfigurationDirectory, "projects.json");

    public string ManagedSyncthingDirectory => Path.Combine(DataDirectory, "syncthing");

    public string VpnDirectory => Path.Combine(DataDirectory, "vpn");

    public string TeamChatDirectory => Path.Combine(DataDirectory, "team-chat");

    public string DiscordDirectory => Path.Combine(ConfigurationDirectory, "discord");

    public string DiscordControlDirectory => Path.Combine(ConfigurationDirectory, "discord-control");

    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public string LfsManagementDirectory => Path.Combine(ConfigurationDirectory, "lfs-management");

    public string RemoteBuildDirectory => Path.Combine(ConfigurationDirectory, "remote-build");

    public static ApplicationPaths CreateDefault()
    {
        string configurationRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(configurationRoot) || string.IsNullOrWhiteSpace(dataRoot))
        {
            string fallbackRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cyrevision");
            return new ApplicationPaths(
                Path.Combine(fallbackRoot, "config"),
                Path.Combine(fallbackRoot, "data"),
                Path.Combine(fallbackRoot, "cache"));
        }

        return new ApplicationPaths(
            Path.Combine(configurationRoot, "CyRevision"),
            Path.Combine(dataRoot, "CyRevision"),
            Path.Combine(dataRoot, "CyRevision", "cache"));
    }
}
