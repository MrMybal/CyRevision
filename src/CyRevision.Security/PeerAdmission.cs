namespace CyRevision.Security;

public enum PeerRole
{
    Owner,
    Administrator,
    Contributor,
    ReadOnly,
    Backup,
    EncryptedArchive
}

public sealed record DeviceIdentity(
    Guid UserId,
    Guid DeviceId,
    string DisplayName,
    string SyncthingDeviceId,
    string SigningPublicKey);

public sealed record PeerInvitation(
    Guid InvitationId,
    Guid ProjectId,
    PeerRole Role,
    DateTimeOffset ExpiresAt,
    string OneTimeTokenHash,
    Guid IssuedByUserId);

public sealed record MembershipCertificate(
    Guid ProjectId,
    DeviceIdentity Device,
    PeerRole Role,
    long MembershipEpoch,
    DateTimeOffset IssuedAt,
    string AdministratorSignature);

public interface IPeerAdmissionService
{
    Task<PeerInvitation> CreateInvitationAsync(
        Guid projectId,
        PeerRole role,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<MembershipCertificate> ApproveDeviceAsync(
        PeerInvitation invitation,
        DeviceIdentity device,
        string verificationCode,
        CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(Guid projectId, Guid deviceId, CancellationToken cancellationToken = default);
}

