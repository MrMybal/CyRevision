using System.Text.Json;
using CyRevision.Security;
using CyRevision.Vpn;

namespace CyRevision.Core.Tests;

public sealed class VpnSetupAssistantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CyRevisionVpnSetupTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ClientOnlyPlanDoesNotOpenInboundPortsOrRequireRouterChanges()
    {
        VpnProjectProfile profile = CreateProfile();
        VpnNetworkSetupService service = new();

        VpnSetupPlan plan = service.BuildPlan(
            profile,
            new VpnSetupOptions(VpnSetupFeatures.None),
            VpnSetupPlatform.Windows,
            VpnFirewallTool.WindowsDefender,
            new VpnNetworkSnapshot("192.168.1.20", "192.168.1.1", "Ethernet"));

        Assert.Empty(plan.Rules);
        Assert.Empty(plan.ApplyCommands);
        Assert.NotEmpty(plan.RemoveCommands);
        Assert.False(plan.RequiresRouterPortForward);
        Assert.Contains(plan.RouterSteps, step => step.Contains("No modem/router change", StringComparison.Ordinal));
    }

    [Fact]
    public void HostPlanRestrictsServicesToVpnAndForwardsOnlyWireGuardUdp()
    {
        VpnProjectProfile profile = CreateProfile();
        VpnNetworkSetupService service = new();
        VpnSetupFeatures features = VpnSetupFeatures.AcceptIncomingTunnel |
                                    VpnSetupFeatures.UnrealSwarm |
                                    VpnSetupFeatures.CyRevisionControlApi;

        VpnSetupPlan plan = service.BuildPlan(
            profile,
            new VpnSetupOptions(features),
            VpnSetupPlatform.Windows,
            VpnFirewallTool.WindowsDefender,
            new VpnNetworkSnapshot("192.168.1.20", "192.168.1.1", "Ethernet"));

        Assert.Equal(3, plan.Rules.Count);
        VpnFirewallRule tunnel = Assert.Single(plan.Rules.Where(rule => rule.Protocol == "UDP"));
        Assert.Equal(profile.ListenPort.ToString(), tunnel.Ports);
        Assert.Null(tunnel.RemoteAddress);
        Assert.All(
            plan.Rules.Where(rule => rule.Protocol == "TCP"),
            rule => Assert.Equal(profile.NetworkCidr, rule.RemoteAddress));
        Assert.Contains(plan.RouterSteps, step => step.Contains($"external {profile.ListenPort}", StringComparison.Ordinal));
        Assert.Contains(plan.RouterSteps, step => step.Contains("Do not forward Swarm", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.RouterSteps, step => step.Contains("47831 ->", StringComparison.Ordinal));
    }

    [Fact]
    public void LinuxPlansUseDirectUfwArgumentsWithoutShellComposition()
    {
        VpnProjectProfile profile = CreateProfile();
        VpnNetworkSetupService service = new();
        VpnSetupPlan plan = service.BuildPlan(
            profile,
            new VpnSetupOptions(VpnSetupFeatures.AcceptIncomingTunnel | VpnSetupFeatures.UnrealSwarm),
            VpnSetupPlatform.Linux,
            VpnFirewallTool.Ufw,
            new VpnNetworkSnapshot("192.168.1.20", "192.168.1.1", "eth0"));

        Assert.True(plan.CanApplyAutomatically);
        Assert.All(plan.ApplyCommands, command => Assert.DoesNotContain("sh", Path.GetFileName(command.Executable), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.ApplyCommands, command => command.Arguments.Contains($"{profile.ListenPort}/udp"));
        Assert.Contains(plan.ApplyCommands, command => command.Arguments.Contains(profile.NetworkCidr));
    }

    [Fact]
    public void LatestHandshakeParserKeepsNeverConnectedAndValidTimestampsSeparate()
    {
        string connectedKey = Convert.ToBase64String(Enumerable.Repeat((byte)4, 32).ToArray());
        string waitingKey = Convert.ToBase64String(Enumerable.Repeat((byte)8, 32).ToArray());
        long timestamp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();

        IReadOnlyDictionary<string, DateTimeOffset?> parsed = VpnNetworkSetupService.ParseLatestHandshakes(
            $"{connectedKey}\t{timestamp}\n{waitingKey}\t0\ninvalid-line");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(timestamp), parsed[connectedKey]);
        Assert.Null(parsed[waitingKey]);
        Assert.Equal(2, parsed.Count);
    }

    [Fact]
    public async Task SyncExchangePublishesOnlySignedPublicPayloadAndRejectsTampering()
    {
        Directory.CreateDirectory(_root);
        VpnProjectProfile profile = CreateProfile() with { PublicEndpoint = "vpn.example.test:51820" };
        using FileDeviceIdentityStore identity = await FileDeviceIdentityStore.OpenOrCreateAsync(
            Path.Combine(_root, "identity"),
            "Owner",
            "vpn:owner");
        SignedVpnInvitation invitation = VpnPeerExchangeCodec.CreateInvitation(
            profile,
            identity,
            VpnNodeCapabilities.GeneralAccess,
            TimeSpan.FromHours(1));
        string payload = VpnPeerExchangeCodec.ExportInvitation(invitation);
        VpnSyncExchangeService service = new();
        string exchange = Path.Combine(_root, "exchange");

        VpnSyncMessage published = await service.PublishAsync(exchange, payload);
        IReadOnlyList<VpnSyncMessage> messages = await service.ListAsync(exchange);

        VpnSyncMessage loaded = Assert.Single(messages);
        Assert.Equal(published.Envelope.MessageId, loaded.Envelope.MessageId);
        Assert.Equal(payload, service.LoadPayload(loaded));
        Assert.DoesNotContain("privateKey", await File.ReadAllTextAsync(loaded.Path), StringComparison.OrdinalIgnoreCase);

        VpnSyncEnvelope tampered = loaded.Envelope with { Payload = loaded.Envelope.Payload + " " };
        await File.WriteAllTextAsync(loaded.Path, JsonSerializer.Serialize(tampered));
        Assert.Empty(await service.ListAsync(exchange));
    }

    [Fact]
    public async Task SyncExchangeRejectsSecretShapedFieldsBeforePublishing()
    {
        VpnSyncExchangeService service = new();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(
            Path.Combine(_root, "exchange"),
            "{\"privateKey\":\"must-never-sync\"}"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private VpnProjectProfile CreateProfile()
    {
        Guid projectId = Guid.NewGuid();
        return VpnProfileFactory.CreateDefault(projectId, Path.Combine(_root, "private.key")) with
        {
            PublicKey = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray()),
            NetworkCidr = "10.80.40.0/24",
            LocalAddress = "10.80.40.1",
            ListenPort = 51820
        };
    }
}
