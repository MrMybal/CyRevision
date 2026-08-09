using System.Diagnostics;

namespace CyRevision.Vpn;

public sealed class WireGuardKeyService
{
    public WireGuardInstallation DetectInstallation()
    {
        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string directory = Path.Combine(programFiles, "WireGuard");
            return new WireGuardInstallation(
                Existing(Path.Combine(directory, "wireguard.exe")),
                Existing(Path.Combine(directory, "wg.exe")),
                null);
        }

        return new WireGuardInstallation(
            null,
            FindInPath("wg"),
            FindInPath("wg-quick"));
    }

    public async Task<(string PublicKey, string PrivateKeyPath)> GenerateKeyPairAsync(
        string wgExecutablePath,
        string privateKeyPath,
        CancellationToken cancellationToken = default)
    {
        string executable = RequireExecutable(wgExecutablePath, "wg");
        ProcessResult generated = await RunAsync(executable, ["genkey"], null, cancellationToken);
        EnsureSuccess(generated, "WireGuard n'a pas pu générer la clé privée.");
        string privateKey = generated.StandardOutput.Trim();
        VpnProfileValidator.ValidateWireGuardKey(privateKey, "privée");

        ProcessResult publicResult = await RunAsync(executable, ["pubkey"], privateKey + Environment.NewLine, cancellationToken);
        EnsureSuccess(publicResult, "WireGuard n'a pas pu calculer la clé publique.");
        string publicKey = publicResult.StandardOutput.Trim();
        VpnProfileValidator.ValidateWireGuardKey(publicKey, "publique");

        string path = Path.GetFullPath(privateKeyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, privateKey + Environment.NewLine, cancellationToken);
            File.Move(temporary, path, true);
            JsonVpnProfileStore.Restrict(path);
        }
        finally
        {
            File.Delete(temporary);
        }

        return (publicKey, path);
    }

    internal static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException($"Impossible de lancer '{executable}'.");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    internal static string RequireExecutable(string? path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !IsCommandName(path)))
        {
            throw new FileNotFoundException($"L'exécutable WireGuard '{name}' est introuvable.", path);
        }

        return path;
    }

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{message} {result.StandardError.Trim()}".Trim());
        }
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private static bool IsCommandName(string value) =>
        !value.Contains(Path.DirectorySeparatorChar) && !value.Contains(Path.AltDirectorySeparatorChar);

    private static string? FindInPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
