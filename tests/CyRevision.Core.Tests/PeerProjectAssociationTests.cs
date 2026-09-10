using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Security;

namespace CyRevision.Core.Tests;

public sealed class PeerProjectAssociationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionAssociationTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IndependentLocalProjectJoinsSharedIdentityOnlyWithExpectedSignedGrant()
    {
        using FileDeviceIdentityStore owner = await FileDeviceIdentityStore.OpenOrCreateAsync(Path.Combine(_root, "owner"), "Owner", "OWNER");
        using FileDeviceIdentityStore peer = await FileDeviceIdentityStore.OpenOrCreateAsync(Path.Combine(_root, "peer"), "Peer", "PEER");
        Guid sharedId = Guid.NewGuid();
        ProjectPreset preset = ProjectPresets.All.First(item => item.Features.GitEnabled && item.Features.PeerSyncEnabled);
        ProjectDefinition local = new(Guid.NewGuid(), "Local clone", _root, preset.Features, preset.Retention);
        JsonPeerAdmissionService admission = new(Path.Combine(_root, "admission"), owner);
        PeerInvitationPackage invite = await admission.CreateInvitationAsync(sharedId, PeerRole.Contributor, TimeSpan.FromHours(1));
        PeerInvitationOffer offer = PeerExchangeCodec.ImportInvitation(PeerExchangeCodec.ExportInvitation(invite));
        PeerProjectAssociation.ValidateInvitation(local, offer);
        Assert.Equal(local.Id, local.SyncProjectId); // Preparing the request grants nothing.
        MembershipCertificate certificate = await admission.ApproveDeviceAsync(invite.Invitation, invite.OneTimeToken, peer.Identity, invite.VerificationCode);
        PeerMembershipGrant grant = new(invite.Invitation.InvitationId, certificate, owner.Identity);
        ProjectDefinition joined = PeerProjectAssociation.AcceptGrant(local, offer, grant, peer.Identity);
        Assert.Equal(local.Id, joined.Id);
        Assert.Equal(local.RootPath, joined.RootPath);
        Assert.Equal(sharedId, joined.SyncProjectId);
        string json = System.Text.Json.JsonSerializer.Serialize(joined);
        Assert.Equal(sharedId, System.Text.Json.JsonSerializer.Deserialize<ProjectDefinition>(json)!.SyncProjectId);
        Assert.Throws<UnauthorizedAccessException>(() => PeerProjectAssociation.AcceptGrant(local, offer,
            grant with { Certificate = certificate with { Role = PeerRole.Owner } }, peer.Identity));
        Assert.Throws<UnauthorizedAccessException>(() => PeerProjectAssociation.AcceptGrant(local, offer, grant, owner.Identity));
        Assert.Throws<UnauthorizedAccessException>(() => PeerProjectAssociation.AcceptGrant(
            local with { SharedSyncProjectId = Guid.NewGuid() }, offer, grant, peer.Identity));
        Assert.Throws<UnauthorizedAccessException>(() => PeerProjectAssociation.ValidateInvitation(local,
            offer with { OneTimeToken = "tampered" }));
        Assert.Throws<UnauthorizedAccessException>(() => PeerProjectAssociation.ValidateInvitation(local,
            offer with { Invitation = invite.Invitation with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) } }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
