using System.Security.Cryptography;
using System.Text;
using CyRevision.Core.Projects;

namespace CyRevision.Security;

/// <summary>Associates a local catalogue entry only after verifying the expected membership grant.</summary>
public static class PeerProjectAssociation
{
    public static void ValidateInvitation(ProjectDefinition project, PeerInvitationOffer offer)
    {
        if (offer.Invitation.ProjectId == Guid.Empty || offer.Invitation.ExpiresAt <= DateTimeOffset.UtcNow ||
            (project.SharedSyncProjectId is { } shared && shared != offer.Invitation.ProjectId) ||
            !Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(offer.OneTimeToken)))
                .Equals(offer.Invitation.OneTimeTokenHash, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The invitation is expired, invalid, or targets a different shared project.");
    }

    public static ProjectDefinition AcceptGrant(ProjectDefinition project, PeerInvitationOffer pending,
        PeerMembershipGrant grant, DeviceIdentity localIdentity)
    {
        if (grant.Certificate.ProjectId != pending.Invitation.ProjectId ||
            (project.SharedSyncProjectId is { } shared && shared != grant.Certificate.ProjectId) ||
            grant.InvitationId != pending.Invitation.InvitationId ||
            grant.IssuerIdentity != pending.IssuerIdentity ||
            grant.Certificate.Device != localIdentity ||
            grant.Certificate.Role != pending.Invitation.Role ||
            grant.Certificate.IssuedAt > pending.Invitation.ExpiresAt ||
            !PeerExchangeCodec.VerifyGrant(grant))
            throw new UnauthorizedAccessException("The membership grant does not match the invitation, device, or signature.");
        return project with { SharedSyncProjectId = grant.Certificate.ProjectId };
    }
}
