namespace CyRevision.Sync;

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

    Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default);
}
