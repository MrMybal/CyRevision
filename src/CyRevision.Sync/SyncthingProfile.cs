namespace CyRevision.Sync;

public sealed record SyncthingSharedFolder(
    Guid Id,
    string Name,
    string Path,
    string FolderId,
    SyncthingFolderMode Mode,
    bool Enabled = true,
    int RescanIntervalSeconds = 60,
    bool FileWatcherEnabled = true);

public sealed record SyncthingProfile(
    Guid ProjectId,
    string ExecutablePath,
    string ConfigurationDirectory,
    string DataDirectory,
    string ExchangeDirectory,
    Uri ApiEndpoint,
    string ApiKey,
    int ListenPort,
    string FolderId)
{
    public IReadOnlyList<SyncthingSharedFolder> SharedFolders { get; init; } = [];

    public SyncthingFolderMode FolderMode { get; init; } = SyncthingFolderMode.SendReceive;

    public int RescanIntervalSeconds { get; init; } = 60;

    public bool FileWatcherEnabled { get; init; } = true;

    public SyncthingIsolationOptions ToIsolationOptions(bool enabled = true) => new(
        ExecutablePath,
        ConfigurationDirectory,
        DataDirectory,
        ExchangeDirectory,
        ApiEndpoint,
        ApiKey,
        ListenPort,
        enabled);
}

public interface ISyncthingProfileStore
{
    Task<SyncthingProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<SyncthingProfile> CreateOrUpdateAsync(
        Guid projectId,
        string executablePath,
        string exchangeDirectory,
        CancellationToken cancellationToken = default);

    Task<SyncthingProfile> SaveAsync(
        SyncthingProfile profile,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default);
}
