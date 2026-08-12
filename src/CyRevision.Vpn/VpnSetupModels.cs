namespace CyRevision.Vpn;

[Flags]
public enum VpnSetupFeatures
{
    None = 0,
    AcceptIncomingTunnel = 1,
    UnrealSwarm = 2,
    CyRevisionControlApi = 4,
    SecureFileExchange = 8,
    RemoteBuildAgent = 16
}

public enum VpnSetupPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public enum VpnFirewallTool
{
    WindowsDefender,
    MacOSApplicationFirewall,
    Ufw,
    Firewalld,
    Manual
}

public sealed record VpnSetupOptions(VpnSetupFeatures Features)
{
    public int FileExchangePort { get; init; } = VpnFileExchangeDefaults.Port;

    public int RemoteBuildPort { get; init; } = 47841;

    public bool AcceptIncomingTunnel => Features.HasFlag(VpnSetupFeatures.AcceptIncomingTunnel);

    public bool AllowUnrealSwarm => Features.HasFlag(VpnSetupFeatures.UnrealSwarm);

    public bool AllowCyRevisionControlApi => Features.HasFlag(VpnSetupFeatures.CyRevisionControlApi);

    public bool AllowSecureFileExchange => Features.HasFlag(VpnSetupFeatures.SecureFileExchange);

    public bool AllowRemoteBuildAgent => Features.HasFlag(VpnSetupFeatures.RemoteBuildAgent);
}

public sealed record VpnNetworkSnapshot(
    string? LocalIpv4Address,
    string? DefaultGateway,
    string? InterfaceName);

public sealed record VpnFirewallRule(
    string Name,
    string DisplayName,
    string Protocol,
    string Ports,
    string? RemoteAddress,
    string Purpose)
{
    public string? LocalAddress { get; init; }
}

public sealed record VpnFirewallCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    string Preview,
    bool RequiresElevation);

public sealed record VpnSetupPlan(
    VpnSetupPlatform Platform,
    VpnFirewallTool FirewallTool,
    VpnNetworkSnapshot Network,
    VpnSetupOptions Options,
    IReadOnlyList<VpnFirewallRule> Rules,
    IReadOnlyList<VpnFirewallCommand> ApplyCommands,
    IReadOnlyList<VpnFirewallCommand> RemoveCommands,
    bool CanApplyAutomatically,
    bool? RulesAlreadyApplied,
    Uri? RouterAdminUri,
    IReadOnlyList<string> ComputerSteps,
    IReadOnlyList<string> RouterSteps,
    IReadOnlyList<string> Warnings)
{
    public bool RequiresRouterPortForward => Options.AcceptIncomingTunnel;
}

public sealed record VpnPeerConnectivity(
    Guid PeerId,
    string DisplayName,
    string TunnelAddress,
    DateTimeOffset? LastHandshakeAt,
    bool RecentHandshake);

public sealed record VpnConnectivityReport(
    bool TunnelResponding,
    IReadOnlyList<VpnPeerConnectivity> Peers,
    string Summary);
