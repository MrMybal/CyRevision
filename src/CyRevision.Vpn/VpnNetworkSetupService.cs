using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace CyRevision.Vpn;

public sealed class VpnNetworkSetupService
{
    public const int CyRevisionControlPort = 47831;

    public async Task<VpnSetupPlan> InspectAsync(
        VpnProjectProfile profile,
        VpnSetupOptions options,
        CancellationToken cancellationToken = default)
    {
        VpnProfileValidator.Validate(profile);
        VpnSetupPlatform platform = DetectPlatform();
        VpnFirewallTool firewall = DetectFirewallTool(platform);
        VpnNetworkSnapshot network = DetectNetwork();
        VpnSetupPlan plan = BuildPlan(profile, options, platform, firewall, network);
        bool? applied = await DetectRulesAsync(plan, cancellationToken);
        return plan with { RulesAlreadyApplied = applied };
    }

    public VpnSetupPlan BuildPlan(
        VpnProjectProfile profile,
        VpnSetupOptions options,
        VpnSetupPlatform platform,
        VpnFirewallTool firewall,
        VpnNetworkSnapshot network)
    {
        VpnProfileValidator.Validate(profile);
        List<VpnFirewallRule> rules = BuildRules(profile, options);
        List<string> computerSteps = BuildComputerSteps(profile, options, platform, firewall, rules);
        List<string> routerSteps = BuildRouterSteps(profile, options, network);
        List<string> warnings = BuildWarnings(options, network);
        (IReadOnlyList<VpnFirewallCommand> apply, _, bool automatic) =
            BuildCommands(platform, firewall, rules);
        VpnSetupOptions allOptions = new(
            VpnSetupFeatures.AcceptIncomingTunnel |
            VpnSetupFeatures.UnrealSwarm |
            VpnSetupFeatures.CyRevisionControlApi);
        List<VpnFirewallRule> allProjectRules = BuildRules(profile, allOptions);
        (_, IReadOnlyList<VpnFirewallCommand> remove, bool cleanupAutomatic) =
            BuildCommands(platform, firewall, allProjectRules);
        Uri? routerUri = TryCreateRouterUri(network.DefaultGateway);
        return new VpnSetupPlan(
            platform,
            firewall,
            network,
            options,
            rules,
            apply,
            remove,
            automatic && cleanupAutomatic,
            null,
            routerUri,
            computerSteps,
            routerSteps,
            warnings);
    }

    public async Task ApplyFirewallAsync(VpnSetupPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.Platform == VpnSetupPlatform.Windows &&
            plan.RemoveCommands.Count == 1 &&
            plan.ApplyCommands.Count == 1)
        {
            VpnFirewallCommand cleanup = plan.RemoveCommands[0];
            VpnFirewallCommand apply = plan.ApplyCommands[0];
            string combinedScript = cleanup.Arguments[^1] + "; " + apply.Arguments[^1];
            VpnFirewallCommand combined = apply with
            {
                Arguments = [.. apply.Arguments.Take(apply.Arguments.Count - 1), combinedScript],
                Preview = cleanup.Preview + Environment.NewLine + apply.Preview
            };
            await ExecuteCommandsAsync([combined], cancellationToken);
            return;
        }

        await ExecuteCommandsAsync(plan.RemoveCommands, cancellationToken, ignoreFailures: true);
        if (plan.ApplyCommands.Count > 0)
        {
            await ExecuteCommandsAsync(plan.ApplyCommands, cancellationToken);
        }
    }

    public Task RemoveFirewallAsync(VpnSetupPlan plan, CancellationToken cancellationToken = default) =>
        ExecuteCommandsAsync(plan.RemoveCommands, cancellationToken, ignoreFailures: true);

    public async Task<VpnConnectivityReport> TestConnectivityAsync(
        VpnProjectProfile profile,
        CancellationToken cancellationToken = default)
    {
        VpnProfileValidator.Validate(profile);
        string wg = WireGuardKeyService.RequireExecutable(profile.WgExecutablePath, "wg");
        foreach (VpnPeerDefinition peer in profile.Peers.Where(peer => peer.Enabled))
        {
            await SendPrivatePingAsync(peer.TunnelAddress, cancellationToken);
        }

        ProcessResult result = await WireGuardKeyService.RunAsync(
            wg,
            ["show", profile.InterfaceName, "latest-handshakes"],
            null,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return new VpnConnectivityReport(
                false,
                [],
                "The CyRevision tunnel is not active or WireGuard could not read its handshake state.");
        }

        IReadOnlyDictionary<string, DateTimeOffset?> handshakes = ParseLatestHandshakes(result.StandardOutput);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        VpnPeerConnectivity[] peers = profile.Peers.Where(peer => peer.Enabled).Select(peer =>
        {
            handshakes.TryGetValue(peer.PublicKey, out DateTimeOffset? handshake);
            bool recent = handshake is not null && now - handshake <= TimeSpan.FromMinutes(5);
            return new VpnPeerConnectivity(
                peer.PeerId,
                peer.DisplayName,
                peer.TunnelAddress,
                handshake,
                recent);
        }).ToArray();
        int connected = peers.Count(peer => peer.RecentHandshake);
        string summary = peers.Length == 0
            ? "The tunnel is active but no VPN peer is configured yet."
            : connected == peers.Length
                ? $"All {connected} configured peer(s) have a recent WireGuard handshake."
                : $"{connected}/{peers.Length} peer(s) have a recent handshake. Check the endpoint, UDP firewall and router forwarding for the others.";
        return new VpnConnectivityReport(true, peers, summary);
    }

    public static IReadOnlyDictionary<string, DateTimeOffset?> ParseLatestHandshakes(string output)
    {
        Dictionary<string, DateTimeOffset?> handshakes = new(StringComparer.Ordinal);
        foreach (string line in (output ?? string.Empty).Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split(
                ['\t', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || !long.TryParse(fields[1], out long seconds) ||
                seconds < 0 || seconds > 253402300799)
            {
                continue;
            }

            handshakes[fields[0]] = seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return handshakes;
    }

    public static VpnSetupPlatform DetectPlatform() => OperatingSystem.IsWindows()
        ? VpnSetupPlatform.Windows
        : OperatingSystem.IsMacOS()
            ? VpnSetupPlatform.MacOS
            : OperatingSystem.IsLinux()
                ? VpnSetupPlatform.Linux
                : VpnSetupPlatform.Unsupported;

    public static VpnNetworkSnapshot DetectNetwork()
    {
        List<(NetworkInterface Adapter, IPAddress Address, IPAddress? Gateway)> candidates = [];
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            IPAddress? gateway = properties.GatewayAddresses
                .Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork &&
                                           !address.Equals(IPAddress.Any));
            foreach (IPAddress address in properties.UnicastAddresses
                         .Select(item => item.Address)
                         .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                                           !IPAddress.IsLoopback(address) &&
                                           !address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
            {
                candidates.Add((adapter, address, gateway));
            }
        }

        (NetworkInterface Adapter, IPAddress Address, IPAddress? Gateway) selected = candidates
            .OrderByDescending(item => item.Gateway is not null)
            .ThenByDescending(item => IsPrivateAddress(item.Address))
            .FirstOrDefault();
        return selected.Adapter is null
            ? new VpnNetworkSnapshot(null, null, null)
            : new VpnNetworkSnapshot(
                selected.Address.ToString(),
                selected.Gateway?.ToString(),
                selected.Adapter.Name);
    }

    private static VpnFirewallTool DetectFirewallTool(VpnSetupPlatform platform)
    {
        if (platform == VpnSetupPlatform.Windows)
        {
            return VpnFirewallTool.WindowsDefender;
        }

        if (platform == VpnSetupPlatform.MacOS)
        {
            return VpnFirewallTool.MacOSApplicationFirewall;
        }

        if (platform == VpnSetupPlatform.Linux)
        {
            if (FindExecutable("ufw") is not null)
            {
                return VpnFirewallTool.Ufw;
            }

            if (FindExecutable("firewall-cmd") is not null)
            {
                return VpnFirewallTool.Firewalld;
            }
        }

        return VpnFirewallTool.Manual;
    }

    private static List<VpnFirewallRule> BuildRules(VpnProjectProfile profile, VpnSetupOptions options)
    {
        string prefix = $"CyRevision-VPN-{profile.ProjectId:N}";
        List<VpnFirewallRule> rules = [];
        if (options.AcceptIncomingTunnel)
        {
            rules.Add(new VpnFirewallRule(
                prefix + "-Tunnel",
                $"CyRevision WireGuard {profile.ProjectId:N}",
                "UDP",
                profile.ListenPort.ToString(),
                null,
                "WireGuard tunnel entry point"));
        }

        if (options.AllowUnrealSwarm)
        {
            rules.Add(new VpnFirewallRule(
                prefix + "-Swarm",
                "CyRevision Unreal Swarm over VPN",
                "TCP",
                "8008-8009",
                profile.NetworkCidr,
                "Unreal Swarm coordinator and agent traffic inside the VPN"));
        }

        if (options.AllowCyRevisionControlApi)
        {
            rules.Add(new VpnFirewallRule(
                prefix + "-Control",
                "CyRevision control API over VPN",
                "TCP",
                CyRevisionControlPort.ToString(),
                profile.NetworkCidr,
                "Authenticated CyRevision services inside the VPN"));
        }

        return rules;
    }

    private static List<string> BuildComputerSteps(
        VpnProjectProfile profile,
        VpnSetupOptions options,
        VpnSetupPlatform platform,
        VpnFirewallTool firewall,
        IReadOnlyCollection<VpnFirewallRule> rules)
    {
        List<string> steps =
        [
            "Keep the WireGuard private key on this device only.",
            $"Use the dedicated interface '{profile.InterfaceName}' and private network {profile.NetworkCidr}."
        ];
        if (rules.Count == 0)
        {
            steps.Add("Client-only mode needs no inbound firewall or router port forwarding rule.");
        }
        else
        {
            steps.Add($"Apply only the {rules.Count} generated CyRevision firewall rule(s).");
        }

        if (platform == VpnSetupPlatform.MacOS)
        {
            steps.Add("Open System Settings > Network > Firewall > Options and allow the signed WireGuard application or service.");
            steps.Add("If 'Block all incoming connections' is enabled, the Mac cannot host the tunnel until WireGuard is allowed.");
        }

        if (platform == VpnSetupPlatform.Linux && firewall == VpnFirewallTool.Ufw)
        {
            steps.Add("CyRevision adds UFW rules but never enables UFW automatically; review remote SSH access before enabling an inactive firewall.");
        }

        if (platform == VpnSetupPlatform.Linux && firewall == VpnFirewallTool.Firewalld)
        {
            steps.Add("firewalld must already be running; CyRevision adds permanent rules but never starts or enables the service.");
        }

        if (options.AllowUnrealSwarm)
        {
            steps.Add("Swarm ports 8008-8009 are restricted to the VPN subnet and must never be forwarded by the router.");
        }

        return steps;
    }

    private static List<string> BuildRouterSteps(
        VpnProjectProfile profile,
        VpnSetupOptions options,
        VpnNetworkSnapshot network)
    {
        if (!options.AcceptIncomingTunnel)
        {
            return
            [
                "No modem/router change is required for this client-only peer.",
                "Keep PersistentKeepalive enabled when this peer connects from behind NAT."
            ];
        }

        string localAddress = network.LocalIpv4Address ?? "this computer's LAN IPv4 address";
        return
        [
            $"Reserve {localAddress} for this computer in the router DHCP settings.",
            $"Create one UDP port-forward: external {profile.ListenPort} -> {localAddress}:{profile.ListenPort}.",
            "Do not forward Swarm or CyRevision control ports; they are reachable through the VPN only.",
            $"Set the project public endpoint to your public IP or dynamic-DNS name followed by :{profile.ListenPort}.",
            "Test from another network, for example a phone using mobile data; many routers do not support NAT loopback.",
            "If the router WAN address is private or in 100.64.0.0/10, the connection may use CGNAT; use a public server/relay peer or request a public IP."
        ];
    }

    private static List<string> BuildWarnings(VpnSetupOptions options, VpnNetworkSnapshot network)
    {
        List<string> warnings = [];
        if (network.LocalIpv4Address is null)
        {
            warnings.Add("No active private IPv4 address was detected. Connect this computer to its normal LAN first.");
        }

        if (options.AcceptIncomingTunnel && network.DefaultGateway is null)
        {
            warnings.Add("No default gateway was detected, so CyRevision cannot open the router administration page.");
        }

        warnings.Add("CyRevision never enables UPnP automatically and never changes router settings without the user.");
        return warnings;
    }

    private static (IReadOnlyList<VpnFirewallCommand> Apply, IReadOnlyList<VpnFirewallCommand> Remove, bool Automatic)
        BuildCommands(
            VpnSetupPlatform platform,
            VpnFirewallTool firewall,
            IReadOnlyList<VpnFirewallRule> rules)
    {
        if (rules.Count == 0)
        {
            return ([], [], true);
        }

        return platform switch
        {
            VpnSetupPlatform.Windows => BuildWindowsCommands(rules),
            VpnSetupPlatform.Linux when firewall == VpnFirewallTool.Ufw => BuildUfwCommands(rules),
            VpnSetupPlatform.Linux when firewall == VpnFirewallTool.Firewalld => BuildFirewalldCommands(rules),
            _ => ([], [], false)
        };
    }

    private static (IReadOnlyList<VpnFirewallCommand>, IReadOnlyList<VpnFirewallCommand>, bool)
        BuildWindowsCommands(IReadOnlyList<VpnFirewallRule> rules)
    {
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        string applyScript = string.Join("; ", rules.Select(rule =>
        {
            string remote = rule.RemoteAddress is null ? string.Empty : $" -RemoteAddress '{rule.RemoteAddress}'";
            return $"Remove-NetFirewallRule -Name '{rule.Name}' -ErrorAction SilentlyContinue; " +
                   $"New-NetFirewallRule -Name '{rule.Name}' -DisplayName '{rule.DisplayName}' " +
                   $"-Group 'CyRevision' -Direction Inbound -Action Allow -Profile Any " +
                   $"-Protocol {rule.Protocol} -LocalPort '{rule.Ports}'{remote} | Out-Null";
        }));
        string removeScript = string.Join("; ", rules.Select(rule =>
            $"Remove-NetFirewallRule -Name '{rule.Name}' -ErrorAction SilentlyContinue"));
        return
        (
            [CreatePowerShellCommand(powershell, applyScript, rules, "Apply")],
            [CreatePowerShellCommand(powershell, removeScript, rules, "Remove")],
            true
        );
    }

    private static VpnFirewallCommand CreatePowerShellCommand(
        string powershell,
        string script,
        IReadOnlyList<VpnFirewallRule> rules,
        string verb) => new(
        powershell,
        ["-NoProfile", "-NonInteractive", "-Command", script],
        $"{verb} Windows Defender Firewall rules: {string.Join(", ", rules.Select(rule => rule.Name))}",
        true);

    private static (IReadOnlyList<VpnFirewallCommand>, IReadOnlyList<VpnFirewallCommand>, bool)
        BuildUfwCommands(IReadOnlyList<VpnFirewallRule> rules)
    {
        string executable = FindExecutable("ufw") ?? "ufw";
        List<VpnFirewallCommand> apply = [];
        List<VpnFirewallCommand> remove = [];
        foreach (VpnFirewallRule rule in rules)
        {
            string port = rule.Ports.Replace('-', ':');
            List<string> ruleArguments = ["allow"];
            if (rule.RemoteAddress is not null)
            {
                ruleArguments.AddRange(["from", rule.RemoteAddress, "to", "any", "port", port, "proto", rule.Protocol.ToLowerInvariant()]);
            }
            else
            {
                ruleArguments.Add($"{port}/{rule.Protocol.ToLowerInvariant()}");
            }

            List<string> applyArguments = [.. ruleArguments, "comment", rule.Name];
            apply.Add(new VpnFirewallCommand(executable, applyArguments, $"ufw {string.Join(' ', applyArguments)}", true));
            remove.Add(new VpnFirewallCommand(
                executable,
                ["--force", "delete", .. ruleArguments],
                $"ufw --force delete {string.Join(' ', ruleArguments)}",
                true));
        }

        return (apply, remove, true);
    }

    private static (IReadOnlyList<VpnFirewallCommand>, IReadOnlyList<VpnFirewallCommand>, bool)
        BuildFirewalldCommands(IReadOnlyList<VpnFirewallRule> rules)
    {
        string executable = FindExecutable("firewall-cmd") ?? "firewall-cmd";
        List<VpnFirewallCommand> apply = [];
        List<VpnFirewallCommand> remove = [];
        foreach (VpnFirewallRule rule in rules)
        {
            string specification = rule.RemoteAddress is null
                ? $"--add-port={rule.Ports}/{rule.Protocol.ToLowerInvariant()}"
                : $"--add-rich-rule=rule family=ipv4 source address={rule.RemoteAddress} port port={rule.Ports} protocol={rule.Protocol.ToLowerInvariant()} accept";
            string removal = specification.Replace("--add-", "--remove-", StringComparison.Ordinal);
            apply.Add(new VpnFirewallCommand(executable, ["--permanent", specification], $"firewall-cmd --permanent {specification}", true));
            remove.Add(new VpnFirewallCommand(executable, ["--permanent", removal], $"firewall-cmd --permanent {removal}", true));
        }

        apply.Add(new VpnFirewallCommand(executable, ["--reload"], "firewall-cmd --reload", true));
        remove.Add(new VpnFirewallCommand(executable, ["--reload"], "firewall-cmd --reload", true));
        return (apply, remove, true);
    }

    private static async Task<bool?> DetectRulesAsync(VpnSetupPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Rules.Count == 0)
        {
            return true;
        }

        if (plan.Platform != VpnSetupPlatform.Windows)
        {
            return null;
        }

        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        string names = string.Join(',', plan.Rules.Select(rule => $"'{rule.Name}'"));
        string script = $"$names=@({names}); $found=@(Get-NetFirewallRule -Name $names -ErrorAction SilentlyContinue); " +
                        "if ($found.Count -eq $names.Count) { exit 0 } else { exit 1 }";
        try
        {
            ProcessResult result = await WireGuardKeyService.RunAsync(
                powershell,
                ["-NoProfile", "-NonInteractive", "-Command", script],
                null,
                cancellationToken);
            return result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task ExecuteCommandsAsync(
        IReadOnlyList<VpnFirewallCommand> commands,
        CancellationToken cancellationToken,
        bool ignoreFailures = false)
    {
        if (commands.Count == 0)
        {
            if (ignoreFailures)
            {
                return;
            }

            throw new InvalidOperationException(
                "Automatic firewall configuration is unavailable on this platform. Follow the generated manual steps.");
        }

        foreach (VpnFirewallCommand command in commands)
        {
            int exitCode = command.RequiresElevation
                ? await RunElevatedAsync(command, cancellationToken)
                : (await WireGuardKeyService.RunAsync(
                    command.Executable,
                    command.Arguments,
                    null,
                    cancellationToken)).ExitCode;
            if (exitCode != 0 && !ignoreFailures)
            {
                throw new InvalidOperationException($"Firewall command failed with exit code {exitCode}: {command.Preview}");
            }
        }
    }

    private static async Task SendPrivatePingAsync(string address, CancellationToken cancellationToken)
    {
        string? ping = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe")
            : FindExecutable("ping");
        if (string.IsNullOrWhiteSpace(ping) || !File.Exists(ping))
        {
            return;
        }

        IReadOnlyList<string> arguments = OperatingSystem.IsWindows()
            ? ["-n", "1", "-w", "1500", address]
            : OperatingSystem.IsMacOS()
                ? ["-c", "1", "-W", "1500", address]
                : ["-c", "1", "-W", "2", address];
        try
        {
            _ = await WireGuardKeyService.RunAsync(ping, arguments, null, cancellationToken);
        }
        catch (IOException)
        {
            // Reading existing handshake state remains useful when ping is unavailable.
        }
    }

    private static async Task<int> RunElevatedAsync(
        VpnFirewallCommand command,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo
            {
                FileName = command.Executable,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }
        else
        {
            string pkexec = FindExecutable("pkexec")
                            ?? throw new InvalidOperationException(
                                "pkexec is unavailable. Copy the generated firewall commands into an administrator terminal.");
            startInfo = new ProcessStartInfo
            {
                FileName = pkexec,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(command.Executable);
        }

        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                                    ?? throw new InvalidOperationException("The firewall administration process could not start.");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Firewall administrator authorization was cancelled.", exception, cancellationToken);
        }
    }

    private static string? FindExecutable(string name)
    {
        string executableName = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name + ".exe"
            : name;
        string? path = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string directory in new[] { "/usr/sbin", "/usr/bin", "/sbin", "/bin" })
        {
            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Uri? TryCreateRouterUri(string? gateway)
    {
        if (!IPAddress.TryParse(gateway, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !IsPrivateAddress(address))
        {
            return null;
        }

        return new Uri($"http://{address}/");
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
               (bytes[0] == 10 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 168);
    }
}
