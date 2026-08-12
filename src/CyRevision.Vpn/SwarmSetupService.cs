using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

namespace CyRevision.Vpn;

public sealed class SwarmSetupService
{
    public const int AgentPort = 8008;
    public const int CoordinatorPort = 8009;

    private static readonly string[] OptionFields =
    [
        "CoordinatorRemotingHost",
        "AgentGroupName",
        "AllowedRemoteAgentGroup",
        "AllowedRemoteAgentNames",
        "CacheFolder"
    ];

    private readonly VpnNetworkSetupService _networkSetup;

    public SwarmSetupService(VpnNetworkSetupService networkSetup)
    {
        _networkSetup = networkSetup;
    }

    public static (string AgentPath, string CoordinatorPath, string OptionsPath) DiscoverDefaultPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        List<string> dotNetFolders = [];
        string? overrideRoot = Environment.GetEnvironmentVariable("UE_ENGINE_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            dotNetFolders.Add(Path.Combine(overrideRoot, "Engine", "Binaries", "DotNET"));
            dotNetFolders.Add(Path.Combine(overrideRoot, "Binaries", "DotNET"));
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string epic = Path.Combine(programFiles, "Epic Games");
        if (Directory.Exists(epic))
        {
            try
            {
                dotNetFolders.AddRange(Directory.EnumerateDirectories(epic, "UE_*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => Path.Combine(path, "Engine", "Binaries", "DotNET")));
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        string launcherManifest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (File.Exists(launcherManifest))
        {
            try
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(launcherManifest));
                if (manifest.RootElement.TryGetProperty("InstallationList", out JsonElement installations))
                {
                    dotNetFolders.AddRange(installations.EnumerateArray()
                        .Select(item => item.TryGetProperty("InstallLocation", out JsonElement location)
                            ? location.GetString()
                            : null)
                        .Where(location => !string.IsNullOrWhiteSpace(location))
                        .Select(location => Path.Combine(location!, "Engine", "Binaries", "DotNET"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase));
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }

        string? folder = dotNetFolders.FirstOrDefault(path => File.Exists(Path.Combine(path, "SwarmAgent.exe")));
        if (folder is null)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        string agent = Path.Combine(folder, "SwarmAgent.exe");
        string coordinator = Path.Combine(folder, "SwarmCoordinator.exe");
        string options = Path.Combine(folder, "SwarmAgent.Options.xml");
        return (agent, File.Exists(coordinator) ? coordinator : string.Empty, File.Exists(options) ? options : string.Empty);
    }

    public async Task<SwarmDiagnosticReport> DiagnoseAsync(
        SwarmProjectProfile profile,
        VpnProjectProfile vpnProfile,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        VpnProfileValidator.Validate(vpnProfile);
        List<SwarmDiagnosticCheck> checks = [];

        checks.Add(OperatingSystem.IsWindows()
            ? Passed("Platform", "Windows supports Unreal Swarm.")
            : Failed("Platform", "Epic currently supports Unreal Swarm on Windows only.",
                "Use this computer only as a WireGuard peer or file-transfer host; run Swarm Agent/Coordinator on Windows."));

        IPAddress localAddress = IPAddress.Parse(vpnProfile.LocalAddress);
        bool localAddressPresent = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Any(item => item.Address.Equals(localAddress));
        checks.Add(localAddressPresent
            ? Passed("VPN address", $"{vpnProfile.LocalAddress} is assigned to a local interface.")
            : Failed("VPN address", $"{vpnProfile.LocalAddress} is not assigned on this computer.",
                $"Start the CyRevision tunnel '{vpnProfile.InterfaceName}', then run the test again."));

        checks.Add(File.Exists(profile.SwarmAgentPath)
            ? Passed("Swarm Agent", profile.SwarmAgentPath)
            : Failed("Swarm Agent", "SwarmAgent.exe was not found.",
                "Select SwarmAgent.exe from the matching Unreal Engine installation (Engine/Binaries/DotNET)."));

        if (profile.Role == SwarmNodeRole.CoordinatorAndAgent)
        {
            checks.Add(File.Exists(profile.SwarmCoordinatorPath)
                ? Passed("Swarm Coordinator", profile.SwarmCoordinatorPath)
                : Failed("Swarm Coordinator", "SwarmCoordinator.exe was not found.",
                    "Select SwarmCoordinator.exe from the same Unreal Engine version as the agents."));
        }

        checks.Add(InspectOptions(profile));
        checks.Add(await InspectDnsAsync(profile, cancellationToken));

        VpnSetupOptions setupOptions = new(VpnSetupFeatures.UnrealSwarm);
        VpnSetupPlan firewall = await _networkSetup.InspectAsync(vpnProfile, setupOptions, cancellationToken);
        checks.Add(firewall.RulesAlreadyApplied switch
        {
            true => Passed("Firewall", $"TCP {AgentPort}-{CoordinatorPort} is allowed only from {vpnProfile.NetworkCidr}."),
            false => Failed("Firewall", "The CyRevision Swarm firewall rule is missing.",
                "Use Apply firewall rules in CyRevision. Do not forward ports 8008/8009 on the router."),
            _ => Warning("Firewall", "CyRevision could not confirm the firewall rule on this platform.",
                string.Join(" ", firewall.ComputerSteps))
        });

        string target = profile.Role == SwarmNodeRole.CoordinatorAndAgent
            ? vpnProfile.LocalAddress
            : profile.CoordinatorAddress;
        if (!string.IsNullOrWhiteSpace(target))
        {
            foreach (int port in new[] { AgentPort, CoordinatorPort })
            {
                bool connected = await CanConnectAsync(target, port, TimeSpan.FromSeconds(2), cancellationToken);
                string name = $"TCP {port}";
                if (connected)
                {
                    checks.Add(Passed(name, $"{target}:{port} accepted a connection."));
                }
                else if (profile.Role == SwarmNodeRole.CoordinatorAndAgent)
                {
                    checks.Add(Warning(name, $"Nothing is listening on {target}:{port} yet.",
                        "Start Swarm Coordinator and Swarm Agent, then test again."));
                }
                else
                {
                    checks.Add(Failed(name, $"The coordinator did not answer on {target}:{port}.",
                        "Check the WireGuard handshake, start Swarm Coordinator on the host, verify the VPN-only firewall rule, and make sure no other process owns the port."));
                }
            }
        }

        int passed = checks.Count(check => check.State == SwarmCheckState.Passed);
        int failed = checks.Count(check => check.State == SwarmCheckState.Failed);
        int warnings = checks.Count(check => check.State == SwarmCheckState.Warning);
        string summary = $"{passed} passed · {warnings} warning(s) · {failed} failed";
        return new SwarmDiagnosticReport(checks, summary);
    }

    public async Task<SwarmOptionsUpdateResult> UpdateAgentOptionsAsync(
        SwarmProjectProfile profile,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        if (!File.Exists(profile.OptionsPath))
        {
            throw new FileNotFoundException(
                "SwarmAgent.Options.xml does not exist yet. Start Swarm Agent once, close it, then select the generated options file.",
                profile.OptionsPath);
        }

        XDocument document;
        await using (FileStream input = new(profile.OptionsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            document = await XDocument.LoadAsync(input, LoadOptions.PreserveWhitespace, cancellationToken);
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["CoordinatorRemotingHost"] = profile.CoordinatorAlias.Length > 0
                ? profile.CoordinatorAlias
                : profile.CoordinatorAddress,
            ["AgentGroupName"] = profile.AgentGroupName,
            ["AllowedRemoteAgentGroup"] = profile.AllowedRemoteAgentGroup,
            ["AllowedRemoteAgentNames"] = profile.AllowedRemoteAgentNames,
            ["CacheFolder"] = profile.CacheFolder
        };
        List<string> updated = [];
        foreach (string name in OptionFields)
        {
            if (string.IsNullOrWhiteSpace(values[name]))
            {
                continue;
            }

            XElement? element = document.Descendants().FirstOrDefault(item =>
                item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                continue;
            }
            element.Value = values[name];
            updated.Add(name);
        }

        if (!updated.Contains("CoordinatorRemotingHost", StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The selected XML file does not contain CoordinatorRemotingHost. Open Swarm Agent, save its Settings tab once, close it, and retry.");
        }

        string backup = profile.OptionsPath + ".cyrevision-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak";
        string temporary = profile.OptionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(profile.OptionsPath, backup, overwrite: false);
        try
        {
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await document.SaveAsync(output, SaveOptions.DisableFormatting, cancellationToken);
            }
            File.Move(temporary, profile.OptionsPath, true);
        }
        finally
        {
            File.Delete(temporary);
        }
        return new SwarmOptionsUpdateResult(profile.OptionsPath, backup, updated);
    }

    public async Task ApplyLocalDnsAliasAsync(
        SwarmProjectProfile profile,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Automatic local DNS aliases are available on Windows for Swarm. Use your system hosts/DNS administration on this platform.");
        }

        string begin = $"# CyRevision Swarm {profile.ProjectId:N} BEGIN";
        string end = $"# CyRevision Swarm {profile.ProjectId:N} END";
        string pattern = Regex.Escape(begin) + @"\r?\n.*?" + Regex.Escape(end) + @"\r?\n?";
        string script =
            "$p=Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts';" +
            "$c=Get-Content -LiteralPath $p -Raw -ErrorAction Stop;" +
            $"$c=[regex]::Replace($c,'{EscapePowerShellSingleQuoted(pattern)}',''," +
            "[System.Text.RegularExpressions.RegexOptions]::Singleline);" +
            $"$block='`r`n{EscapePowerShellSingleQuoted(begin)}`r`n{profile.CoordinatorAddress}`t{profile.CoordinatorAlias}`r`n{EscapePowerShellSingleQuoted(end)}`r`n';" +
            "Set-Content -LiteralPath $p -Value ($c.TrimEnd()+$block) -Encoding ascii -ErrorAction Stop;" +
            "ipconfig /flushdns | Out-Null";
        await RunElevatedPowerShellAsync(script, cancellationToken);
    }

    public async Task RemoveLocalDnsAliasAsync(
        SwarmProjectProfile profile,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Automatic local DNS alias removal is available on Windows only.");
        }

        string begin = $"# CyRevision Swarm {profile.ProjectId:N} BEGIN";
        string end = $"# CyRevision Swarm {profile.ProjectId:N} END";
        string pattern = Regex.Escape(begin) + @"\r?\n.*?" + Regex.Escape(end) + @"\r?\n?";
        string script =
            "$p=Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts';" +
            "$c=Get-Content -LiteralPath $p -Raw -ErrorAction Stop;" +
            $"$c=[regex]::Replace($c,'{EscapePowerShellSingleQuoted(pattern)}',''," +
            "[System.Text.RegularExpressions.RegexOptions]::Singleline);" +
            "Set-Content -LiteralPath $p -Value ($c.TrimEnd()+[Environment]::NewLine) -Encoding ascii -ErrorAction Stop;" +
            "ipconfig /flushdns | Out-Null";
        await RunElevatedPowerShellAsync(script, cancellationToken);
    }

    public void LaunchAgent(SwarmProjectProfile profile) => Launch(profile.SwarmAgentPath, "Swarm Agent");

    public void LaunchCoordinator(SwarmProjectProfile profile)
    {
        if (profile.Role != SwarmNodeRole.CoordinatorAndAgent)
        {
            throw new InvalidOperationException("This computer is configured as an Agent, not as the Coordinator host.");
        }
        Launch(profile.SwarmCoordinatorPath, "Swarm Coordinator");
    }

    public static void ValidateProfile(SwarmProjectProfile profile)
    {
        if (profile.ProjectId == Guid.Empty)
        {
            throw new InvalidDataException("The Swarm profile does not reference a project.");
        }
        if (!IPAddress.TryParse(profile.CoordinatorAddress, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException("Coordinator address must be an IPv4 address inside the project VPN.");
        }
        if (!Regex.IsMatch(profile.CoordinatorAlias, @"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$"))
        {
            throw new InvalidDataException("The local coordinator alias must be a single valid DNS label.");
        }
        foreach (string value in new[] { profile.AgentGroupName, profile.AllowedRemoteAgentGroup, profile.AllowedRemoteAgentNames })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.ContainsAny('\r', '\n'))
            {
                throw new InvalidDataException("Swarm group and agent filters must be non-empty single-line values.");
            }
        }
    }

    private static SwarmDiagnosticCheck InspectOptions(SwarmProjectProfile profile)
    {
        if (!File.Exists(profile.OptionsPath))
        {
            return Warning("Agent options", "SwarmAgent.Options.xml was not found.",
                "Start Swarm Agent once, save Settings, close it, then select the generated SwarmAgent.Options.xml file.");
        }

        try
        {
            XDocument document = XDocument.Load(profile.OptionsPath);
            XElement? coordinator = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName.Equals("CoordinatorRemotingHost", StringComparison.OrdinalIgnoreCase));
            string expected = profile.CoordinatorAlias.Length > 0 ? profile.CoordinatorAlias : profile.CoordinatorAddress;
            return coordinator?.Value.Equals(expected, StringComparison.OrdinalIgnoreCase) == true
                ? Passed("Agent options", $"CoordinatorRemotingHost is {expected}.")
                : Failed("Agent options", "CoordinatorRemotingHost does not match this Swarm VPN profile.",
                    "Close Swarm Agent, then use Apply Agent configuration. CyRevision creates a backup before changing XML.");
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
        {
            return Failed("Agent options", exception.Message,
                "Close Swarm Agent and select a valid SwarmAgent.Options.xml file.");
        }
    }

    private static async Task<SwarmDiagnosticCheck> InspectDnsAsync(
        SwarmProjectProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(profile.CoordinatorAlias, cancellationToken);
            return addresses.Any(address => address.ToString() == profile.CoordinatorAddress)
                ? Passed("Local DNS", $"{profile.CoordinatorAlias} resolves to {profile.CoordinatorAddress}.")
                : Failed("Local DNS", $"{profile.CoordinatorAlias} resolves to a different address.",
                    "Apply the project-owned local DNS alias, or use the coordinator VPN IP directly.");
        }
        catch (SocketException)
        {
            return Failed("Local DNS", $"{profile.CoordinatorAlias} does not resolve.",
                "Apply the project-owned local DNS alias, or use the coordinator VPN IP directly.");
        }
    }

    private static async Task<bool> CanConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, timeoutCancellation.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static void Launch(string path, string displayName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{displayName} executable was not found.", path);
        }
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path)
        });
    }

    private static async Task RunElevatedPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        string executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        try
        {
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Administrator authorization was cancelled.", exception, cancellationToken);
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The elevated local DNS update failed with exit code {process.ExitCode}.");
        }
    }

    private static string EscapePowerShellSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static SwarmDiagnosticCheck Passed(string name, string detail) =>
        new(name, SwarmCheckState.Passed, detail);

    private static SwarmDiagnosticCheck Warning(string name, string detail, string remediation) =>
        new(name, SwarmCheckState.Warning, detail, remediation);

    private static SwarmDiagnosticCheck Failed(string name, string detail, string remediation) =>
        new(name, SwarmCheckState.Failed, detail, remediation);
}

file static class SwarmStringExtensions
{
    public static bool ContainsAny(this string value, params char[] characters) => characters.Any(value.Contains);
}
