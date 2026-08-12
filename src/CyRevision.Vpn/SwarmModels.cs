namespace CyRevision.Vpn;

public enum SwarmNodeRole
{
    Agent,
    CoordinatorAndAgent
}

public enum SwarmCheckState
{
    Passed,
    Warning,
    Failed,
    Skipped
}

public sealed record SwarmProjectProfile(
    Guid ProjectId,
    SwarmNodeRole Role,
    string CoordinatorAddress,
    string CoordinatorAlias,
    string SwarmAgentPath,
    string SwarmCoordinatorPath,
    string OptionsPath,
    string AgentGroupName,
    string AllowedRemoteAgentGroup,
    string AllowedRemoteAgentNames,
    string CacheFolder,
    DateTimeOffset UpdatedAt);

public sealed record SwarmDiagnosticCheck(
    string Name,
    SwarmCheckState State,
    string Detail,
    string? Remediation = null);

public sealed record SwarmDiagnosticReport(
    IReadOnlyList<SwarmDiagnosticCheck> Checks,
    string Summary)
{
    public bool Ready => Checks.All(check => check.State is SwarmCheckState.Passed or SwarmCheckState.Skipped);
}

public sealed record SwarmOptionsUpdateResult(
    string OptionsPath,
    string BackupPath,
    IReadOnlyList<string> UpdatedFields);

public interface ISwarmProfileStore
{
    Task<SwarmProjectProfile?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task SaveAsync(SwarmProjectProfile profile, CancellationToken cancellationToken = default);
}

public static class SwarmProfileFactory
{
    public static SwarmProjectProfile CreateDefault(VpnProjectProfile vpnProfile)
    {
        VpnPeerDefinition? coordinator = vpnProfile.Peers.FirstOrDefault(peer =>
            peer.Enabled && peer.Capabilities.HasFlag(VpnNodeCapabilities.SwarmCoordinator));
        bool localCoordinator = vpnProfile.LocalCapabilities.HasFlag(VpnNodeCapabilities.SwarmCoordinator);
        string coordinatorAddress = localCoordinator
            ? vpnProfile.LocalAddress
            : coordinator?.TunnelAddress ?? string.Empty;
        string alias = "cyrev-swarm-" + vpnProfile.ProjectId.ToString("N")[..8];
        (string agent, string host, string options) = SwarmSetupService.DiscoverDefaultPaths();
        return new SwarmProjectProfile(
            vpnProfile.ProjectId,
            localCoordinator ? SwarmNodeRole.CoordinatorAndAgent : SwarmNodeRole.Agent,
            coordinatorAddress,
            alias,
            agent,
            host,
            options,
            "Default",
            "DefaultDeployed",
            "*",
            string.Empty,
            DateTimeOffset.UtcNow);
    }
}
