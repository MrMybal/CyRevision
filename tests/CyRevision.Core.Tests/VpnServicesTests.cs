using CyRevision.Security;
using CyRevision.Vpn;

namespace CyRevision.Core.Tests;

public sealed class VpnServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionVpnTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProfilePersistsAndConfigurationUsesOnlyProjectRoutes()
    {
        Guid projectId = Guid.NewGuid();
        string key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        VpnProjectProfile profile = VpnProfileFactory.CreateDefault(projectId, Path.Combine(_root, "private.key")) with
        {
            PublicKey = key,
            Peers =
            [
                new VpnPeerDefinition(
                    Guid.NewGuid(), "Swarm Coordinator", Convert.ToBase64String(new byte[32]),
                    VpnProfileFactory.FindAvailableAddress(VpnProfileFactory.CreateDefault(projectId, "unused") with { PublicKey = key }),
                    "swarm.example.test:51820", [], 25, VpnNodeCapabilities.SwarmCoordinator)
            ]
        };
        JsonVpnProfileStore store = new(Path.Combine(_root, "vpn"));

        await store.SaveAsync(profile);
        VpnProjectProfile? loaded = await store.GetAsync(projectId);
        string configuration = await new WireGuardConfigService(Path.Combine(_root, "vpn")).RenderAsync(profile);

        Assert.NotNull(loaded);
        Assert.Equal(profile.ProjectId, loaded.ProjectId);
        Assert.Equal(profile.NetworkCidr, loaded.NetworkCidr);
        Assert.Equal(profile.LocalAddress, loaded.LocalAddress);
        Assert.Equal(profile.PublicKey, loaded.PublicKey);
        Assert.Single(loaded.Peers);
        Assert.Equal(profile.Peers[0].PeerId, loaded.Peers[0].PeerId);
        Assert.Equal(profile.Peers[0].PublicKey, loaded.Peers[0].PublicKey);
        Assert.Equal(profile.Peers[0].TunnelAddress, loaded.Peers[0].TunnelAddress);
        Assert.Equal(profile.Peers[0].Capabilities, loaded.Peers[0].Capabilities);
        Assert.Contains($"AllowedIPs = {profile.Peers[0].TunnelAddress}/32", configuration, StringComparison.Ordinal);
        Assert.Contains("Endpoint = swarm.example.test:51820", configuration, StringComparison.Ordinal);
        Assert.Contains("PersistentKeepalive = 25", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0/0", configuration, StringComparison.Ordinal);
        Assert.Contains("<clé privée masquée>", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedVpnInvitationAllowsVpnOnlyPeerAndRejectsCapabilityEscalation()
    {
        Guid projectId = Guid.NewGuid();
        string ownerKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
        string peerKey = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());
        VpnProjectProfile ownerProfile = VpnProfileFactory.CreateDefault(projectId, Path.Combine(_root, "owner.key")) with
        {
            PublicKey = ownerKey,
            PublicEndpoint = "owner.example.test:51900",
            LocalCapabilities = VpnNodeCapabilities.SwarmCoordinator
        };
        VpnProjectProfile joiningProfile = VpnProfileFactory.CreateDefault(Guid.NewGuid(), Path.Combine(_root, "peer.key")) with
        {
            PublicKey = peerKey
        };
        using FileDeviceIdentityStore owner = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "owner"), "Owner", "vpn:owner");
        using FileDeviceIdentityStore peer = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "peer"), "Build machine", "vpn:peer");

        SignedVpnInvitation invitation = VpnPeerExchangeCodec.CreateInvitation(
            ownerProfile, owner, VpnNodeCapabilities.SwarmAgent, TimeSpan.FromHours(1), "CI Agent 01");
        string invitationCode = VpnPeerExchangeCodec.ExportInvitationCode(invitation);
        Assert.StartsWith("CYRVPN1-", invitationCode, StringComparison.Ordinal);
        SignedVpnInvitation imported = VpnPeerExchangeCodec.ImportInvitation(invitationCode);
        joiningProfile = VpnPeerExchangeCodec.ApplyInvitation(joiningProfile, imported);
        VpnJoinResponse response = VpnPeerExchangeCodec.CreateJoinResponse(
            imported, joiningProfile, peer, VpnNodeCapabilities.SwarmAgent);

        VpnPeerDefinition accepted = VpnPeerExchangeCodec.ValidateJoinResponse(response, projectId, owner.Identity);
        Assert.Equal(VpnNodeCapabilities.SwarmAgent, accepted.Capabilities);
        Assert.Equal("CI Agent 01", accepted.DisplayName);
        Assert.Equal(invitation.Invitation.AssignedAddress, accepted.TunnelAddress);
        Assert.Equal(ownerProfile.LocalAddress, joiningProfile.Peers.Single().TunnelAddress);
        Assert.StartsWith("CYRVPNR1-", VpnPeerExchangeCodec.ExportJoinResponseCode(response), StringComparison.Ordinal);

        VpnJoinResponse escalated = response with
        {
            JoiningPeer = response.JoiningPeer with { Capabilities = VpnNodeCapabilities.CiWorker }
        };
        Assert.Throws<UnauthorizedAccessException>(() =>
            VpnPeerExchangeCodec.ValidateJoinResponse(escalated, projectId, owner.Identity));
    }

    [Fact]
    public void DefaultInternetRouteIsRejected()
    {
        Guid projectId = Guid.NewGuid();
        VpnProjectProfile profile = VpnProfileFactory.CreateDefault(projectId, "private.key") with
        {
            PublicKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
            Peers =
            [
                new VpnPeerDefinition(
                    Guid.NewGuid(), "Unsafe", Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()),
                    "10.99.99.2", null, ["0.0.0.0/0"], 0, VpnNodeCapabilities.GeneralAccess)
            ],
            NetworkCidr = "10.99.99.0/24",
            LocalAddress = "10.99.99.1"
        };

        Assert.Throws<InvalidDataException>(() => VpnProfileValidator.Validate(profile));
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
