using System.Diagnostics;
using System.Text.RegularExpressions;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Lore;

public sealed class LoreIntegrationPlugin : ILoreIntegrationPlugin
{
    private static readonly HashSet<string> AllowedProjectCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "branch", "commit", "diff", "lock", "log", "push", "stage", "status", "sync"
    };

    private readonly string? _payloadOverride;
    private CyRevisionPluginContext? _context;
    private string? _payloadDirectory;
    private string _loreExecutable = "lore";

    public LoreIntegrationPlugin() { }

    public LoreIntegrationPlugin(string payloadOverride) => _payloadOverride = payloadOverride;

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.lore",
        "Lore Project Management",
        "0.1.0",
        "Experimental Epic Games Lore CLI workspace management, status, branches, and installable Unreal companion.",
        "Project management");

    public Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _payloadDirectory = ResolvePayloadDirectory(context);
        string settingsPath = GetCliPathSettingsPath();
        if (File.Exists(settingsPath))
        {
            string configured = File.ReadAllText(settingsPath).Trim();
            if (configured.Length > 0) _loreExecutable = configured;
        }
        LoreCliDetection detection = DetectCli(_loreExecutable);
        if (detection.IsAvailable) _loreExecutable = detection.ExecutablePath;
        return Task.CompletedTask;
    }

    public LoreCliDetection DetectCli(string? configuredPath = null)
    {
        IEnumerable<string> candidates = ResolveCliCandidates(configuredPath);
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LoreCommandResult result = RunProcessAsync(candidate, ["--version"], null, TimeSpan.FromSeconds(8), CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (!result.Succeeded) continue;
                _loreExecutable = candidate;
                string version = FirstNonEmptyLine(result.StandardOutput, result.StandardError);
                return new LoreCliDetection(true, candidate, version, $"Lore CLI detected: {version}");
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                // Continue through PATH candidates. Detection must never make the host application fail.
            }
        }

        return new LoreCliDetection(
            false,
            string.IsNullOrWhiteSpace(configuredPath) ? "lore" : configuredPath,
            string.Empty,
            "Lore CLI was not found. Install it from the official Lore release, then select or enter its executable path.");
    }

    public async Task SaveCliPathAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (_context is null) throw new InvalidOperationException("Plugin is not initialized.");
        LoreCliDetection detection = DetectCli(executablePath);
        if (!detection.IsAvailable) throw new FileNotFoundException(detection.Summary, executablePath);
        string path = GetCliPathSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, detection.ExecutablePath, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
        _loreExecutable = detection.ExecutablePath;
    }

    public LoreProjectInspection InspectProject(string path)
    {
        string root = ResolveProjectRoot(path);
        string config = Path.Combine(root, ".lore", "config.toml");
        bool rootExists = Directory.Exists(root);
        bool isLoreProject = rootExists && File.Exists(config);
        (string server, string repository, string branch) = isLoreProject ? ReadLoreConfiguration(config) : (string.Empty, string.Empty, string.Empty);
        bool unreal = rootExists && Directory.EnumerateFiles(root, "*.uproject", SearchOption.TopDirectoryOnly).Any();
        string companion = Path.Combine(root, "Plugins", "CyRevisionLore", "CyRevisionLore.uplugin");
        string? installedVersion = ReadUnrealPluginVersion(companion);
        string summary = !isLoreProject
            ? "This folder is not a Lore workspace (.lore/config.toml was not found). CyRevision will not initialize it automatically."
            : $"Lore workspace detected{(string.IsNullOrWhiteSpace(branch) ? string.Empty : $" on {branch}")}. Standard refresh is read-only; Scan working tree is explicit.";
        if (isLoreProject && Directory.Exists(Path.Combine(root, ".git")))
            summary += " Git and Lore are both present: keep automatic write operations disabled until a project policy is chosen.";

        return new LoreProjectInspection(
            isLoreProject,
            root,
            config,
            server,
            repository,
            branch,
            unreal,
            File.Exists(companion),
            installedVersion,
            summary);
    }

    public Task<LoreCommandResult> ReadStatusAsync(string projectPath, CancellationToken cancellationToken = default) =>
        RunProjectCommandAsync(projectPath, ["status"], cancellationToken);

    public Task<LoreCommandResult> ScanStatusAsync(string projectPath, CancellationToken cancellationToken = default) =>
        RunProjectCommandAsync(projectPath, ["status", "--scan"], cancellationToken);

    public Task<LoreCommandResult> ListBranchesAsync(string projectPath, CancellationToken cancellationToken = default) =>
        RunProjectCommandAsync(projectPath, ["branch", "list"], cancellationToken);

    public async Task<LoreCommandResult> RunProjectCommandAsync(
        string projectPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (arguments.Count == 0 || !AllowedProjectCommands.Contains(arguments[0]))
            return new LoreCommandResult(false, -1, string.Empty, "Command rejected by the CyRevision Lore allow-list.", TimeSpan.Zero, "Lore command rejected.");

        LoreProjectInspection project = InspectProject(projectPath);
        if (!project.IsProject)
            return new LoreCommandResult(false, -1, string.Empty, project.Summary, TimeSpan.Zero, "No Lore workspace selected.");

        LoreCliDetection detection = DetectCli(_loreExecutable);
        if (!detection.IsAvailable)
            return new LoreCommandResult(false, -1, string.Empty, detection.Summary, TimeSpan.Zero, "Lore CLI is unavailable.");

        return await RunProcessAsync(
                detection.ExecutablePath,
                arguments,
                project.ProjectRoot,
                TimeSpan.FromMinutes(5),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LoreUnrealCompanionInstallationResult> InstallOrUpdateUnrealCompanionAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        LoreProjectInspection project = InspectProject(projectPath);
        if (!project.UnrealProjectDetected)
            return Failed(project.ProjectRoot, "Select an Unreal project containing a .uproject file.");
        if (_payloadDirectory is null)
            return Failed(project.ProjectRoot, "The CyRevision package does not contain the Lore Unreal companion payload.");

        string destination = Path.Combine(project.ProjectRoot, "Plugins", "CyRevisionLore");
        string staging = Path.Combine(project.ProjectRoot, "Saved", "CyRevision", "LorePluginStaging", Guid.NewGuid().ToString("N"));
        string? backup = null;
        string version = ReadUnrealPluginVersion(Path.Combine(_payloadDirectory, "CyRevisionLore.uplugin")) ?? "0.1.0";
        try
        {
            await Task.Run(() => CopyDirectory(_payloadDirectory, staging, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(destination))
            {
                backup = Path.Combine(project.ProjectRoot, "Saved", "CyRevision", "PluginBackups", $"CyRevisionLore-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            return new LoreUnrealCompanionInstallationResult(
                true,
                project.ProjectRoot,
                destination,
                backup,
                version,
                backup is null
                    ? $"CyRevision Lore Companion {version} was installed in the Unreal project."
                    : $"CyRevision Lore Companion was updated to {version}; the previous copy is recoverable under Saved/CyRevision/PluginBackups.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!Directory.Exists(destination) && backup is not null && Directory.Exists(backup)) Directory.Move(backup, destination);
            return new LoreUnrealCompanionInstallationResult(false, project.ProjectRoot, destination, backup, version, $"Lore companion installation failed: {exception.Message}");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<LoreCommandResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new LoreCommandResult(false, -1, await outputTask.ConfigureAwait(false), "Lore command timed out.", stopwatch.Elapsed, "Lore command timed out.");
        }

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        bool succeeded = process.ExitCode == 0;
        return new LoreCommandResult(
            succeeded,
            process.ExitCode,
            output,
            error,
            stopwatch.Elapsed,
            succeeded ? $"Lore command completed in {stopwatch.Elapsed.TotalMilliseconds:N0} ms." : $"Lore command failed with exit code {process.ExitCode}.");
    }

    private string? ResolvePayloadDirectory(CyRevisionPluginContext context)
    {
        string[] candidates =
        [
            _payloadOverride ?? string.Empty,
            Path.Combine(context.ApplicationDirectory, "PluginPayloads", "Lore", "CyRevisionLore"),
            Path.GetFullPath(Path.Combine(context.PackageDirectory, "..", "..", "PluginPayloads", "Lore", "CyRevisionLore")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "plugins", "CyRevisionLore"))
        ];
        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, "CyRevisionLore.uplugin")));
    }

    private string GetCliPathSettingsPath() => Path.Combine(
        _context?.ConfigurationDirectory ?? throw new InvalidOperationException("Plugin is not initialized."),
        "plugins",
        "lore",
        "cli-path.txt");

    private static IEnumerable<string> ResolveCliCandidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) yield return configuredPath.Trim();
        string executableName = OperatingSystem.IsWindows() ? "lore.exe" : "lore";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try { candidate = Path.Combine(directory, executableName); }
            catch (ArgumentException) { continue; }
            if (File.Exists(candidate)) yield return candidate;
        }
        yield return "lore";
    }

    private static string ResolveProjectRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Environment.CurrentDirectory;
        string full = Path.GetFullPath(path);
        return File.Exists(full) ? Path.GetDirectoryName(full)! : full;
    }

    private static (string Server, string Repository, string Branch) ReadLoreConfiguration(string path)
    {
        string server = string.Empty;
        string repository = string.Empty;
        string branch = string.Empty;
        foreach (string sourceLine in File.ReadLines(path))
        {
            string line = sourceLine.Trim();
            if (line.StartsWith('#') || !line.Contains('=')) continue;
            int separator = line.IndexOf('=');
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Contains("url", StringComparison.OrdinalIgnoreCase) || key.Contains("server", StringComparison.OrdinalIgnoreCase)) server = value;
            else if (key.Contains("repository", StringComparison.OrdinalIgnoreCase) || key.Equals("repo", StringComparison.OrdinalIgnoreCase)) repository = value;
            else if (key.Contains("branch", StringComparison.OrdinalIgnoreCase)) branch = value;
        }
        return (server, repository, branch);
    }

    private static string? ReadUnrealPluginVersion(string path)
    {
        if (!File.Exists(path)) return null;
        Match match = Regex.Match(File.ReadAllText(path), "\\\"VersionName\\\"\\s*:\\s*\\\"(?<version>[^\\\"]+)\\\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string FirstNonEmptyLine(params string[] values) =>
        values.SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).FirstOrDefault() ?? "version unknown";

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static LoreUnrealCompanionInstallationResult Failed(string root, string message) =>
        new(false, root, string.Empty, null, string.Empty, message);
}
