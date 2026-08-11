using System.Globalization;
using System.Text;
using System.Text.Json;
using CyRevision.Security;

namespace CyRevision.Vpn;

public sealed record VpnInvitation(
    Guid InvitationId,
    Guid ProjectId,
    string NetworkCidr,
    string AssignedAddress,
    VpnPeerDefinition IssuerPeer,
    VpnNodeCapabilities AllowedCapabilities,
    DateTimeOffset ExpiresAt);

public sealed record SignedVpnInvitation(
    VpnInvitation Invitation,
    DeviceIdentity IssuerIdentity,
    string Signature);

public sealed record VpnJoinResponse(
    SignedVpnInvitation InvitationOffer,
    VpnPeerDefinition JoiningPeer,
    DeviceIdentity JoiningIdentity,
    string Signature);

public static class VpnPeerExchangeCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SignedVpnInvitation CreateInvitation(
        VpnProjectProfile profile,
        IDeviceIdentityStore issuerIdentity,
        VpnNodeCapabilities allowedCapabilities,
        TimeSpan lifetime)
    {
        VpnProfileValidator.Validate(profile);
        if (string.IsNullOrWhiteSpace(profile.PublicKey))
        {
            throw new InvalidOperationException("Générez d'abord les clés WireGuard du projet.");
        }

        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Une invitation VPN doit expirer sous sept jours.");
        }

        VpnPeerDefinition issuerPeer = new(
            issuerIdentity.Identity.DeviceId,
            issuerIdentity.Identity.DisplayName,
            profile.PublicKey,
            profile.LocalAddress,
            profile.PublicEndpoint,
            [],
            string.IsNullOrWhiteSpace(profile.PublicEndpoint) ? 0 : 25,
            profile.LocalCapabilities);
        VpnInvitation invitation = new(
            Guid.NewGuid(),
            profile.ProjectId,
            profile.NetworkCidr,
            VpnProfileFactory.FindAvailableAddress(profile),
            issuerPeer,
            allowedCapabilities,
            DateTimeOffset.UtcNow.Add(lifetime));
        return new SignedVpnInvitation(
            invitation,
            issuerIdentity.Identity,
            issuerIdentity.Sign(BuildInvitationPayload(invitation)));
    }

    public static VpnJoinResponse CreateJoinResponse(
        SignedVpnInvitation offer,
        VpnProjectProfile joiningProfile,
        IDeviceIdentityStore joiningIdentity,
        VpnNodeCapabilities capabilities)
    {
        ValidateInvitation(offer);
        if ((capabilities & ~offer.Invitation.AllowedCapabilities) != 0)
        {
            throw new UnauthorizedAccessException("Les capacités demandées dépassent celles de l'invitation.");
        }

        if (!string.Equals(joiningProfile.NetworkCidr, offer.Invitation.NetworkCidr, StringComparison.Ordinal) ||
            !string.Equals(joiningProfile.LocalAddress, offer.Invitation.AssignedAddress, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Le profil local n'a pas encore appliqué l'adresse de cette invitation.");
        }

        VpnProfileValidator.ValidateWireGuardKey(joiningProfile.PublicKey, "publique du pair");
        VpnPeerDefinition joiningPeer = new(
            joiningIdentity.Identity.DeviceId,
            joiningIdentity.Identity.DisplayName,
            joiningProfile.PublicKey,
            joiningProfile.LocalAddress,
            joiningProfile.PublicEndpoint,
            [],
            string.IsNullOrWhiteSpace(joiningProfile.PublicEndpoint) ? 0 : 25,
            capabilities);
        byte[] payload = BuildJoinResponsePayload(offer.Invitation.InvitationId, joiningPeer, joiningIdentity.Identity);
        return new VpnJoinResponse(offer, joiningPeer, joiningIdentity.Identity, joiningIdentity.Sign(payload));
    }

    public static VpnPeerDefinition ValidateJoinResponse(
        VpnJoinResponse response,
        Guid projectId,
        DeviceIdentity localIssuerIdentity)
    {
        ValidateJoinResponseSignature(response, projectId);
        bool sameIssuer = response.InvitationOffer.IssuerIdentity.DeviceId == localIssuerIdentity.DeviceId &&
                          string.Equals(
                              response.InvitationOffer.IssuerIdentity.SigningPublicKey,
                              localIssuerIdentity.SigningPublicKey,
                              StringComparison.Ordinal);
        if (!sameIssuer)
        {
            throw new UnauthorizedAccessException("Cette réponse vise une invitation créée par un autre appareil.");
        }

        return response.JoiningPeer;
    }

    public static void ValidateJoinResponseSignature(VpnJoinResponse response, Guid? projectId = null)
    {
        if (response is null || response.InvitationOffer is null || response.JoiningPeer is null ||
            response.JoiningIdentity is null || string.IsNullOrWhiteSpace(response.Signature))
        {
            throw new InvalidDataException("La réponse VPN est incomplète.");
        }

        ValidateInvitation(response.InvitationOffer, projectId);
        VpnInvitation invitation = response.InvitationOffer.Invitation;
        if (!string.Equals(invitation.AssignedAddress, response.JoiningPeer.TunnelAddress, StringComparison.Ordinal) ||
            response.JoiningPeer.PeerId != response.JoiningIdentity.DeviceId ||
            (response.JoiningPeer.Capabilities & ~invitation.AllowedCapabilities) != 0)
        {
            throw new UnauthorizedAccessException("La réponse VPN modifie l'adresse ou les droits accordés.");
        }

        byte[] payload = BuildJoinResponsePayload(invitation.InvitationId, response.JoiningPeer, response.JoiningIdentity);
        if (!PeerExchangeCodec.VerifySignature(response.JoiningIdentity, payload, response.Signature))
        {
            throw new UnauthorizedAccessException("La signature du pair VPN est invalide.");
        }
    }

    public static VpnProjectProfile ApplyInvitation(VpnProjectProfile localProfile, SignedVpnInvitation offer)
    {
        ValidateInvitation(offer);
        bool sameExistingHub = localProfile.Peers.Count == 1 &&
                               localProfile.Peers[0].PeerId == offer.Invitation.IssuerPeer.PeerId &&
                               string.Equals(localProfile.NetworkCidr, offer.Invitation.NetworkCidr, StringComparison.Ordinal);
        if (localProfile.Peers.Count > 0 && !sameExistingHub)
        {
            throw new InvalidOperationException(
                "Ce profil appartient déjà à un autre VPN. Utilisez un projet distinct ou retirez d'abord ses pairs.");
        }

        VpnProjectProfile updated = localProfile with
        {
            NetworkCidr = offer.Invitation.NetworkCidr,
            LocalAddress = offer.Invitation.AssignedAddress,
            Peers = [offer.Invitation.IssuerPeer],
            UpdatedAt = DateTimeOffset.UtcNow
        };
        VpnProfileValidator.Validate(updated);
        return updated;
    }

    public static void ValidateInvitation(SignedVpnInvitation offer, Guid? projectId = null)
    {
        if (offer is null || offer.Invitation is null || offer.IssuerIdentity is null ||
            string.IsNullOrWhiteSpace(offer.Signature) || offer.Invitation.IssuerPeer is null)
        {
            throw new InvalidDataException("L'invitation VPN est incomplète.");
        }

        if ((projectId is not null && offer.Invitation.ProjectId != projectId) ||
            offer.Invitation.InvitationId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("L'invitation VPN cible un autre projet.");
        }

        if (offer.Invitation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("L'invitation VPN a expiré.");
        }

        if (offer.Invitation.IssuerPeer.PeerId != offer.IssuerIdentity.DeviceId ||
            !PeerExchangeCodec.VerifySignature(
                offer.IssuerIdentity,
                BuildInvitationPayload(offer.Invitation),
                offer.Signature))
        {
            throw new UnauthorizedAccessException("La signature de l'invitation VPN est invalide.");
        }

        VpnProfileValidator.ValidateWireGuardKey(offer.Invitation.IssuerPeer.PublicKey, "publique de l'invitant");
        VpnProfileValidator.ParseCidr(offer.Invitation.NetworkCidr);
    }

    public static string ExportInvitation(SignedVpnInvitation invitation) =>
        JsonSerializer.Serialize(invitation, JsonOptions);

    public static SignedVpnInvitation ImportInvitation(string value) =>
        JsonSerializer.Deserialize<SignedVpnInvitation>(value, JsonOptions)
        ?? throw new InvalidDataException("L'invitation VPN est invalide.");

    public static string ExportJoinResponse(VpnJoinResponse response) =>
        JsonSerializer.Serialize(response, JsonOptions);

    public static VpnJoinResponse ImportJoinResponse(string value) =>
        JsonSerializer.Deserialize<VpnJoinResponse>(value, JsonOptions)
        ?? throw new InvalidDataException("La réponse VPN est invalide.");

    private static byte[] BuildInvitationPayload(VpnInvitation invitation) => Encoding.UTF8.GetBytes(string.Join('\n',
        invitation.InvitationId.ToString("N"),
        invitation.ProjectId.ToString("N"),
        invitation.NetworkCidr,
        invitation.AssignedAddress,
        BuildPeerPayload(invitation.IssuerPeer),
        ((int)invitation.AllowedCapabilities).ToString(CultureInfo.InvariantCulture),
        invitation.ExpiresAt.ToUniversalTime().ToString("O")));

    private static byte[] BuildJoinResponsePayload(
        Guid invitationId,
        VpnPeerDefinition peer,
        DeviceIdentity identity) => Encoding.UTF8.GetBytes(string.Join('\n',
        invitationId.ToString("N"),
        BuildPeerPayload(peer),
        identity.UserId.ToString("N"),
        identity.DeviceId.ToString("N"),
        identity.DisplayName,
        identity.SigningPublicKey));

    private static string BuildPeerPayload(VpnPeerDefinition peer) => string.Join('|',
        peer.PeerId.ToString("N"),
        peer.DisplayName,
        peer.PublicKey,
        peer.TunnelAddress,
        peer.Endpoint ?? string.Empty,
        string.Join(',', peer.RoutedSubnets.OrderBy(route => route, StringComparer.Ordinal)),
        peer.PersistentKeepaliveSeconds.ToString(CultureInfo.InvariantCulture),
        ((int)peer.Capabilities).ToString(CultureInfo.InvariantCulture),
        peer.Enabled ? "1" : "0");
}
