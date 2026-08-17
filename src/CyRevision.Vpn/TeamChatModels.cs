namespace CyRevision.Vpn;

public enum TeamChatTransport
{
    Vpn,
    SyncFolder,
    PrivateServer
}

public enum TeamChatDeliveryState
{
    Pending,
    Delivered,
    Failed
}

public sealed record TeamChatProfile(
    Guid ProjectId,
    string DisplayName,
    TeamChatTransport Transport,
    string ListenAddress,
    int Port,
    string PeerEndpoint,
    string AccessToken,
    string SyncFolderPath,
    bool SaveConversations,
    int RetentionDays,
    long MaxAttachmentBytes,
    DateTimeOffset UpdatedAt,
    string ProjectRoot = "",
    bool EncryptStoredConversations = false,
    string ServerBaseUrl = "",
    string ServerApiToken = "",
    bool AllowPrivateServerHttp = false,
    string SelectedChannelId = "general");

public sealed record TeamChatMessage(
    Guid Id,
    Guid ProjectId,
    string Author,
    string Text,
    DateTimeOffset SentAt,
    string AttachmentName,
    long AttachmentSize,
    string AttachmentSha256,
    string AttachmentLocalPath,
    string AttachmentRelativePath,
    TeamChatDeliveryState DeliveryState = TeamChatDeliveryState.Delivered,
    string DeliveryDetail = "",
    string ChannelId = "general")
{
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentName);
}

public sealed record TeamChatChannel(
    string Id,
    string Name,
    string Topic,
    int Position,
    bool IsDefault = false);

public sealed record TeamChatParticipant(
    string DisplayName,
    DateTimeOffset LastSeen,
    bool IsOnline,
    string Transport);

public sealed record TeamChatSnapshot(
    IReadOnlyList<TeamChatMessage> Messages,
    IReadOnlyList<TeamChatParticipant> Participants,
    int FilesScanned,
    int FilesParsed,
    DateTimeOffset RefreshedAt,
    IReadOnlyList<TeamChatChannel>? Channels = null);

public sealed record TeamChatServerSendRequest(
    string Author,
    string ChannelId,
    string Text,
    string AttachmentName,
    byte[]? AttachmentBytes,
    string AttachmentSha256);

public sealed record TeamChatServerCreateChannelRequest(
    string Name,
    string Topic);

public sealed record TeamChatServerAttachment(
    string Name,
    string Sha256,
    byte[] Bytes);

public static class TeamChatDefaults
{
    public const int Port = 47843;
    public const long MaxAttachmentBytes = 50L * 1024 * 1024;

    public static IReadOnlyList<TeamChatChannel> Channels { get; } =
    [
        new("general", "general", "Project-wide discussion", 0, true),
        new("development", "development", "Code, assets and implementation", 10),
        new("builds", "builds", "Builds, CI and releases", 20)
    ];
}

public interface ITeamChatProfileStore
{
    Task<TeamChatProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TeamChatProfile> GetOrCreateAsync(
        Guid projectId,
        string displayName,
        CancellationToken cancellationToken = default);

    Task SaveAsync(TeamChatProfile profile, CancellationToken cancellationToken = default);

    Task<TeamChatProfile> RotateTokenAsync(Guid projectId, CancellationToken cancellationToken = default);
}
