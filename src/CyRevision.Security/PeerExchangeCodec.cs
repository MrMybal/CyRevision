using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Security;

public static class PeerExchangeCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ExportInvitation(PeerInvitationPackage package) =>
        JsonSerializer.Serialize(
            new PeerInvitationOffer(package.Invitation, package.OneTimeToken, package.IssuerIdentity),
            JsonOptions);

    public static PeerInvitationOffer ImportInvitation(string value) =>
        JsonSerializer.Deserialize<PeerInvitationOffer>(value, JsonOptions)
        ?? throw new InvalidDataException("The invitation exchange is invalid.");

    public static string ExportJoinRequest(PeerJoinRequest request) =>
        JsonSerializer.Serialize(request, JsonOptions);

    public static PeerJoinRequest ImportJoinRequest(string value) =>
        JsonSerializer.Deserialize<PeerJoinRequest>(value, JsonOptions)
        ?? throw new InvalidDataException("The peer join request is invalid.");

    public static string ExportMembershipGrant(PeerMembershipGrant grant) =>
        JsonSerializer.Serialize(grant, JsonOptions);

    public static PeerMembershipGrant ImportMembershipGrant(string value) =>
        JsonSerializer.Deserialize<PeerMembershipGrant>(value, JsonOptions)
        ?? throw new InvalidDataException("The membership grant is invalid.");

    public static bool VerifyGrant(PeerMembershipGrant grant)
    {
        if (grant.IssuerIdentity.DeviceId == Guid.Empty || grant.IssuerIdentity.UserId == Guid.Empty)
        {
            return false;
        }

        try
        {
            using ECDsa publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(grant.IssuerIdentity.SigningPublicKey), out _);
            byte[] payload = BuildCertificatePayload(grant.Certificate);
            return publicKey.VerifyData(
                payload,
                Convert.FromBase64String(grant.Certificate.AdministratorSignature),
                HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static byte[] BuildCertificatePayload(MembershipCertificate certificate) =>
        Encoding.UTF8.GetBytes(string.Join('\n',
            certificate.ProjectId.ToString("N"),
            certificate.Device.UserId.ToString("N"),
            certificate.Device.DeviceId.ToString("N"),
            certificate.Device.DisplayName,
            certificate.Device.SyncthingDeviceId,
            certificate.Device.SigningPublicKey,
            certificate.Role.ToString(),
            certificate.MembershipEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            certificate.IssuedAt.ToUniversalTime().ToString("O")));
}
