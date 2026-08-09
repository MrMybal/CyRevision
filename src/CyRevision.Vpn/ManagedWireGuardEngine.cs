using System.Diagnostics;

namespace CyRevision.Vpn;

public sealed class ManagedWireGuardEngine
{
    private readonly string _rootDirectory;
    private readonly WireGuardConfigService _configuration;

    public ManagedWireGuardEngine(string rootDirectory, WireGuardConfigService configuration)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _configuration = configuration;
    }

    public async Task<VpnEngineStatus> GetStatusAsync(VpnProjectProfile profile, CancellationToken cancellationToken = default)
    {
        VpnProfileValidator.Validate(profile);
        string? wg = profile.WgExecutablePath;
        if (string.IsNullOrWhiteSpace(wg))
        {
            return new VpnEngineStatus(VpnRuntimeState.Unavailable, "L'outil WireGuard wg est introuvable.", profile.InterfaceName);
        }

        ProcessResult status = await WireGuardKeyService.RunAsync(wg, ["show", profile.InterfaceName], null, cancellationToken);
        if (status.ExitCode == 0)
        {
            bool owned = File.Exists(GetOwnershipMarker(profile));
            return new VpnEngineStatus(
                owned ? VpnRuntimeState.Running : VpnRuntimeState.Collision,
                owned ? "Tunnel VPN CyRevision actif." : "Cette interface existe mais n'appartient pas à CyRevision.",
                profile.InterfaceName,
                _configuration.GetConfigurationPath(profile));
        }

        return new VpnEngineStatus(
            VpnRuntimeState.Stopped,
            "Tunnel VPN arrêté.",
            profile.InterfaceName,
            _configuration.GetConfigurationPath(profile));
    }

    public async Task<VpnEngineStatus> StartAsync(VpnProjectProfile profile, CancellationToken cancellationToken = default)
    {
        VpnProfileValidator.Validate(profile);
        VpnEngineStatus before = await GetStatusAsync(profile, cancellationToken);
        if (before.State == VpnRuntimeState.Running)
        {
            return before;
        }

        if (before.State == VpnRuntimeState.Collision)
        {
            throw new InvalidOperationException(before.Message);
        }

        string marker = GetOwnershipMarker(profile);
        if (OperatingSystem.IsWindows() && !File.Exists(marker) && await WindowsServiceExistsAsync(profile.InterfaceName, cancellationToken))
        {
            throw new InvalidOperationException("Un service WireGuard du même nom existe déjà et n'appartient pas à CyRevision.");
        }

        string configurationPath = await _configuration.WriteAsync(profile, cancellationToken);
        int exitCode;
        if (OperatingSystem.IsWindows())
        {
            string executable = WireGuardKeyService.RequireExecutable(profile.WireGuardExecutablePath, "wireguard.exe");
            exitCode = await RunElevatedAsync(executable, ["/installtunnelservice", configurationPath], cancellationToken);
        }
        else
        {
            string executable = WireGuardKeyService.RequireExecutable(profile.WgQuickExecutablePath, "wg-quick");
            ProcessResult result = await WireGuardKeyService.RunAsync(executable, ["up", configurationPath], null, cancellationToken);
            exitCode = result.ExitCode;
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"wg-quick n'a pas démarré le tunnel. {result.StandardError.Trim()}".Trim());
            }
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"WireGuard a retourné le code {exitCode} pendant l'activation.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        await File.WriteAllTextAsync(marker, $"{profile.ProjectId:N}\n{profile.InterfaceName}\n", cancellationToken);
        JsonVpnProfileStore.Restrict(marker);
        return new VpnEngineStatus(VpnRuntimeState.Running, "Tunnel VPN CyRevision actif.", profile.InterfaceName, configurationPath);
    }

    public async Task<VpnEngineStatus> StopAsync(VpnProjectProfile profile, CancellationToken cancellationToken = default)
    {
        string marker = GetOwnershipMarker(profile);
        if (!File.Exists(marker))
        {
            throw new InvalidOperationException("CyRevision refuse d'arrêter ce tunnel car son marqueur de propriété est absent.");
        }

        if (OperatingSystem.IsWindows())
        {
            string executable = WireGuardKeyService.RequireExecutable(profile.WireGuardExecutablePath, "wireguard.exe");
            int exitCode = await RunElevatedAsync(executable, ["/uninstalltunnelservice", profile.InterfaceName], cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"WireGuard a retourné le code {exitCode} pendant l'arrêt.");
            }
        }
        else
        {
            string executable = WireGuardKeyService.RequireExecutable(profile.WgQuickExecutablePath, "wg-quick");
            ProcessResult result = await WireGuardKeyService.RunAsync(
                executable,
                ["down", _configuration.GetConfigurationPath(profile)],
                null,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"wg-quick n'a pas arrêté le tunnel. {result.StandardError.Trim()}".Trim());
            }
        }

        File.Delete(marker);
        return new VpnEngineStatus(VpnRuntimeState.Stopped, "Tunnel VPN arrêté.", profile.InterfaceName);
    }

    private string GetOwnershipMarker(VpnProjectProfile profile) =>
        Path.Combine(_rootDirectory, "runtime", profile.ProjectId.ToString("N"), profile.InterfaceName + ".owned");

    private static async Task<bool> WindowsServiceExistsAsync(string interfaceName, CancellationToken cancellationToken)
    {
        ProcessResult result = await WireGuardKeyService.RunAsync(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
            ["query", "WireGuardTunnel$" + interfaceName],
            null,
            cancellationToken);
        return result.ExitCode == 0;
    }

    private static async Task<int> RunElevatedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                                    ?? throw new InvalidOperationException("Impossible de lancer WireGuard avec les droits administrateur.");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("L'autorisation administrateur WireGuard a été annulée.", exception, cancellationToken);
        }
    }
}
