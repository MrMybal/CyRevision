namespace CyRevision.Vpn;

public sealed record VpnFileExchangeProfile(
    Guid ProjectId,
    string ListenAddress,
    int Port,
    string InboxPath,
    string SharedFolderPath,
    bool AllowReceive,
    bool AllowBrowse,
    bool AllowDownload,
    long MaxFileBytes,
    bool StartAutomatically,
    DateTimeOffset UpdatedAt);

public sealed record VpnFileExchangeCredentials(VpnFileExchangeProfile Profile, string AccessToken);

public sealed record VpnSharedFile(string RelativePath, long Size, DateTimeOffset ModifiedAt);

public sealed record VpnFileTransferResult(string Name, long Size, string Sha256, string DestinationPath);

public interface IVpnFileExchangeProfileStore
{
    Task<VpnFileExchangeCredentials?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<VpnFileExchangeCredentials> GetOrCreateAsync(
        VpnProjectProfile vpnProfile,
        string defaultDataDirectory,
        CancellationToken cancellationToken = default);

    Task SaveAsync(VpnFileExchangeCredentials credentials, CancellationToken cancellationToken = default);

    Task<VpnFileExchangeCredentials> RotateTokenAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public static class VpnFileExchangeDefaults
{
    public const int Port = 47842;
    public const long MaxFileBytes = 20L * 1024 * 1024 * 1024;

    public static VpnFileExchangeProfile Create(VpnProjectProfile vpnProfile, string dataDirectory)
    {
        string root = Path.Combine(Path.GetFullPath(dataDirectory), vpnProfile.ProjectId.ToString("N"));
        return new VpnFileExchangeProfile(
            vpnProfile.ProjectId,
            vpnProfile.LocalAddress,
            Port,
            Path.Combine(root, "Inbox"),
            Path.Combine(root, "Shared"),
            true,
            true,
            true,
            MaxFileBytes,
            false,
            DateTimeOffset.UtcNow);
    }
}
