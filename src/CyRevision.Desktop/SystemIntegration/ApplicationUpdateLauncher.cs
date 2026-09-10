using System.Diagnostics;
using System.Globalization;
using System.Text;
using CyRevision.Core.Updates;

namespace CyRevision.Desktop.SystemIntegration;

internal sealed record ApplicationUpdateLaunchPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string ScriptPath,
    string ScriptContents);

internal static class ApplicationUpdateLauncher
{
    public static void LaunchAfterExit(string packagePath, int parentProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (parentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        }

        string fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException("The verified update package no longer exists.", fullPackagePath);
        }

        (UpdatePlatform platform, _) = ApplicationUpdateService.DetectCurrentPlatform();
        ApplicationUpdateLaunchPlan plan = CreatePlan(fullPackagePath, parentProcessId, platform);
        string? scriptDirectory = Path.GetDirectoryName(plan.ScriptPath);
        if (!string.IsNullOrWhiteSpace(scriptDirectory))
        {
            Directory.CreateDirectory(scriptDirectory);
        }

        File.WriteAllText(plan.ScriptPath, plan.ScriptContents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = plan.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fullPackagePath) ?? AppContext.BaseDirectory
            };
            foreach (string argument in plan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process? helper = Process.Start(startInfo);
            if (helper is null)
            {
                throw new InvalidOperationException("The external update launcher could not be started.");
            }
        }
        catch
        {
            try
            {
                File.Delete(plan.ScriptPath);
            }
            catch (IOException)
            {
                // Preserve the original launch exception.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the original launch exception.
            }

            throw;
        }
    }

    internal static ApplicationUpdateLaunchPlan CreatePlan(
        string packagePath,
        int parentProcessId,
        UpdatePlatform platform,
        string? scriptPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (parentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        }

        string fullPackagePath = Path.GetFullPath(packagePath);
        string identifier = $"{parentProcessId}-{Guid.NewGuid():N}";
        string parentId = parentProcessId.ToString(CultureInfo.InvariantCulture);

        return platform switch
        {
            UpdatePlatform.Windows => CreateWindowsPlan(
                fullPackagePath,
                parentId,
                scriptPath ?? Path.Combine(Path.GetTempPath(), $"CyRevision-update-{identifier}.ps1")),
            UpdatePlatform.Linux => CreateUnixPlan(
                fullPackagePath,
                parentId,
                scriptPath ?? Path.Combine(Path.GetTempPath(), $"CyRevision-update-{identifier}.sh"),
                "xdg-open"),
            UpdatePlatform.MacOs => CreateUnixPlan(
                fullPackagePath,
                parentId,
                scriptPath ?? Path.Combine(Path.GetTempPath(), $"CyRevision-update-{identifier}.sh"),
                "open"),
            _ => throw new PlatformNotSupportedException("Automatic update installation is not supported on this platform.")
        };
    }

    private static ApplicationUpdateLaunchPlan CreateWindowsPlan(
        string packagePath,
        string parentProcessId,
        string scriptPath)
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string executable = string.IsNullOrWhiteSpace(windowsDirectory)
            ? "powershell.exe"
            : Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        const string script = """
            param(
                [Parameter(Mandatory = $true)][int]$ParentProcessId,
                [Parameter(Mandatory = $true)][string]$PackagePath
            )

            $ErrorActionPreference = 'Stop'
            try {
                Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 250
                Start-Process -FilePath $PackagePath
            }
            finally {
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            }
            """;
        string[] arguments =
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-ParentProcessId",
            parentProcessId,
            "-PackagePath",
            packagePath
        ];
        return new ApplicationUpdateLaunchPlan(executable, arguments, scriptPath, script);
    }

    private static ApplicationUpdateLaunchPlan CreateUnixPlan(
        string packagePath,
        string parentProcessId,
        string scriptPath,
        string openCommand)
    {
        string script = $$"""
            #!/bin/sh
            parent_pid="$1"
            package_path="$2"
            while kill -0 "$parent_pid" 2>/dev/null; do
                sleep 1
            done
            {{openCommand}} "$package_path"
            result=$?
            rm -f -- "$0"
            exit $result
            """;
        string[] arguments = [scriptPath, parentProcessId, packagePath];
        return new ApplicationUpdateLaunchPlan("/bin/sh", arguments, scriptPath, script);
    }
}
