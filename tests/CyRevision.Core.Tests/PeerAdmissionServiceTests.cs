using CyRevision.Security;

namespace CyRevision.Core.Tests;

public sealed class PeerAdmissionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionSecurityTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvitationIsOneTimeSignedAndRevocable()
    {
        using FileDeviceIdentityStore administrator = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "administrator"),
            "Administrator",
            "ADMIN-SYNCTHING-ID");
        using FileDeviceIdentityStore peer = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "peer"),
            "Artist workstation",
            "PEER-SYNCTHING-ID");
        JsonPeerAdmissionService service = new(Path.Combine(_root, "admission"), administrator);
        Guid projectId = Guid.NewGuid();

        PeerInvitationPackage package = await service.CreateInvitationAsync(
            projectId,
            PeerRole.Contributor,
            TimeSpan.FromHours(1));
        string exportedInvitation = PeerExchangeCodec.ExportInvitation(package);
        Assert.DoesNotContain(package.VerificationCode, exportedInvitation, StringComparison.Ordinal);
        Assert.Equal(package.Invitation, PeerExchangeCodec.ImportInvitation(exportedInvitation).Invitation);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveDeviceAsync(
            package.Invitation,
            package.OneTimeToken,
            peer.Identity,
            "000000"));

        MembershipCertificate certificate = await service.ApproveDeviceAsync(
            package.Invitation,
            package.OneTimeToken,
            peer.Identity,
            package.VerificationCode);
        Assert.True(service.VerifyCertificate(certificate));
        Assert.True(PeerExchangeCodec.VerifyGrant(new PeerMembershipGrant(
            package.Invitation.InvitationId,
            certificate,
            administrator.Identity)));
        Assert.Single(await service.GetMembersAsync(projectId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveDeviceAsync(
            package.Invitation,
            package.OneTimeToken,
            peer.Identity,
            package.VerificationCode));

        await service.RevokeDeviceAsync(projectId, peer.Identity.DeviceId);
        Assert.Empty(await service.GetMembersAsync(projectId));
    }

    [Fact]
    public async Task DeviceIdentityPersistsAndStillSigns()
    {
        string path = Path.Combine(_root, "persisted");
        DeviceIdentity original;
        string signature;
        byte[] payload = "CyRevision"u8.ToArray();
        using (FileDeviceIdentityStore first = await FileDeviceIdentityStore.OpenOrCreateAsync(path, "Owner", "DEVICE-ID"))
        {
            original = first.Identity;
            signature = first.Sign(payload);
        }

        using FileDeviceIdentityStore reopened = await FileDeviceIdentityStore.OpenOrCreateAsync(path, "ignored", "ignored");
        Assert.Equal(original, reopened.Identity);
        Assert.True(reopened.Verify(payload, signature));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, true);
    }
}
