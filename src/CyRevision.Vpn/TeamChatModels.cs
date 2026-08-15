namespace CyRevision.Vpn;

public enum TeamChatTransport
{
    Vpn,
    SyncFolder
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
    bool EncryptStoredConversations = false);

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
    string DeliveryDetail = "");

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
    DateTimeOffset RefreshedAt);

public static class TeamChatDefaults
{
    public const int Port = 47843;
    public const long MaxAttachmentBytes = 50L * 1024 * 1024;
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
