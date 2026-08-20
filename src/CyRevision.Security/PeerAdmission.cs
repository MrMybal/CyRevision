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

public sealed record PeerInvitationPackage(
    PeerInvitation Invitation,
    string OneTimeToken,
    string VerificationCode,
    DeviceIdentity IssuerIdentity);

public sealed record PeerInvitationOffer(
    PeerInvitation Invitation,
    string OneTimeToken,
    DeviceIdentity IssuerIdentity);

public sealed record PeerJoinRequest(
    PeerInvitationOffer InvitationOffer,
    DeviceIdentity Device,
    string VerificationCode);

public sealed record PeerMembershipGrant(
    Guid InvitationId,
    MembershipCertificate Certificate,
    DeviceIdentity IssuerIdentity);

public sealed record MembershipCertificate(
    Guid ProjectId,
    DeviceIdentity Device,
    PeerRole Role,
    long MembershipEpoch,
    DateTimeOffset IssuedAt,
    string AdministratorSignature);

public interface IPeerAdmissionService
{
    Task<PeerInvitationPackage> CreateInvitationAsync(
        Guid projectId,
        PeerRole role,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<MembershipCertificate> ApproveDeviceAsync(
        PeerInvitation invitation,
        string oneTimeToken,
        DeviceIdentity device,
        string verificationCode,
        CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(Guid projectId, Guid deviceId, CancellationToken cancellationToken = default);

    Task<MembershipCertificate> UpdateDeviceRoleAsync(
        Guid projectId,
        Guid deviceId,
        PeerRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipCertificate>> GetMembersAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    bool VerifyCertificate(MembershipCertificate certificate);
}
