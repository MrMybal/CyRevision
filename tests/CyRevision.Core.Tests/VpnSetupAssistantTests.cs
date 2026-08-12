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
    public void SecureFileExchangeFirewallRuleIsVpnOnlyAndUsesConfiguredPort()
    {
        VpnProjectProfile profile = CreateProfile();
        VpnNetworkSetupService service = new();
        VpnSetupOptions options = new(VpnSetupFeatures.SecureFileExchange) { FileExchangePort = 47999 };

        VpnSetupPlan plan = service.BuildPlan(
            profile,
            options,
            VpnSetupPlatform.Windows,
            VpnFirewallTool.WindowsDefender,
            new VpnNetworkSnapshot("192.168.1.20", "192.168.1.1", "Ethernet"));

        VpnFirewallRule rule = Assert.Single(plan.Rules);
        Assert.Equal("TCP", rule.Protocol);
        Assert.Equal("47999", rule.Ports);
        Assert.Equal(profile.NetworkCidr, rule.RemoteAddress);
        Assert.Equal(profile.LocalAddress, rule.LocalAddress);
        Assert.False(plan.RequiresRouterPortForward);
        Assert.Contains(plan.ComputerSteps, step => step.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SwarmOptionsUpdateChangesKnownFieldsAndCreatesBackup()
    {
        Directory.CreateDirectory(_root);
        string options = Path.Combine(_root, "SwarmAgent.Options.xml");
        await File.WriteAllTextAsync(options,
            "<AgentOptions><CoordinatorRemotingHost>old-host</CoordinatorRemotingHost>" +
            "<AgentGroupName>Old</AgentGroupName><AllowedRemoteAgentGroup>Old</AllowedRemoteAgentGroup>" +
            "<AllowedRemoteAgentNames>OLD*</AllowedRemoteAgentNames><CacheFolder>C:\\Old</CacheFolder></AgentOptions>");
        SwarmProjectProfile profile = new(
            Guid.NewGuid(),
            SwarmNodeRole.Agent,
            "10.80.40.1",
            "cyrev-swarm-test",
            string.Empty,
            string.Empty,
            options,
            "Artists",
            "Workers",
            "WORKER*",
            "C:\\SwarmCache",
            DateTimeOffset.UtcNow);

        SwarmOptionsUpdateResult result = await new SwarmSetupService(new VpnNetworkSetupService())
            .UpdateAgentOptionsAsync(profile);
        string updated = await File.ReadAllTextAsync(options);

        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains("<CoordinatorRemotingHost>cyrev-swarm-test</CoordinatorRemotingHost>", updated);
        Assert.Contains("<AgentGroupName>Artists</AgentGroupName>", updated);
        Assert.Contains("<AllowedRemoteAgentGroup>Workers</AllowedRemoteAgentGroup>", updated);
        Assert.Contains("<AllowedRemoteAgentNames>WORKER*</AllowedRemoteAgentNames>", updated);
        Assert.Contains("<CacheFolder>C:\\SwarmCache</CacheFolder>", updated);
    }

    [Fact]
    public void VpnFolderShareRejectsTraversalAndSymbolicEscape()
    {
        string shared = Path.Combine(_root, "shared");
        Directory.CreateDirectory(shared);
        string valid = VpnFileExchangeService.ResolveSharedPath(shared, Path.Combine("folder", "asset.zip"));

        Assert.StartsWith(Path.GetFullPath(shared), valid, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            VpnFileExchangeService.ResolveSharedPath(shared, Path.Combine("..", "secret.txt")));
        Assert.Throws<InvalidDataException>(() =>
            VpnFileExchangeService.ResolveSharedPath(shared, Path.GetPathRoot(shared) + "secret.txt"));
    }

    [Fact]
    public async Task VpnFileExchangeStoreKeepsTokenSeparateAndRotatesIt()
    {
        Directory.CreateDirectory(_root);
        VpnProjectProfile vpn = CreateProfile();
        JsonVpnFileExchangeProfileStore store = new(Path.Combine(_root, "vpn"));
        VpnFileExchangeCredentials created = await store.GetOrCreateAsync(vpn, Path.Combine(_root, "data"));
        VpnFileExchangeCredentials rotated = await store.RotateTokenAsync(vpn.ProjectId);

        Assert.NotEqual(created.AccessToken, rotated.AccessToken);
        string profilePath = Path.Combine(_root, "vpn", "file-exchange", vpn.ProjectId.ToString("N") + ".json");
        string tokenPath = Path.Combine(_root, "vpn", "file-exchange", vpn.ProjectId.ToString("N") + ".token");
        Assert.DoesNotContain(rotated.AccessToken, await File.ReadAllTextAsync(profilePath), StringComparison.Ordinal);
        Assert.Equal(rotated.AccessToken, (await File.ReadAllTextAsync(tokenPath)).Trim());
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
