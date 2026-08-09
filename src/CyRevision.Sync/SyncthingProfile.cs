namespace CyRevision.Sync;

public sealed record SyncthingProfile(
    Guid ProjectId,
    string ExecutablePath,
    string ConfigurationDirectory,
    string DataDirectory,
    string ExchangeDirectory,
    Uri ApiEndpoint,
    string ApiKey,
    string FolderId)
{
    public SyncthingIsolationOptions ToIsolationOptions(bool enabled = true) => new(
        ExecutablePath,
        ConfigurationDirectory,
        DataDirectory,
        ExchangeDirectory,
        ApiEndpoint,
        ApiKey,
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
