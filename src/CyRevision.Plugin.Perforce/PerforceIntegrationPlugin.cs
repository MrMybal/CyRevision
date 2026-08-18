using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Perforce;

public sealed class PerforceIntegrationPlugin : IPerforceIntegrationPlugin, IProjectModeProvider
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private CyRevisionPluginContext? _context;

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.perforce",
        "Perforce Helix Core",
        "0.1.0",
        "Project-scoped Helix Core workspace, opened files, changelists, history, reconcile, sync and submit management through the official p4 CLI.",
        "Version control");

    public IReadOnlyList<PluginProjectModeDescriptor> ProjectModes { get; } =
    [
        new(
            "perforce",
            "Perforce",
            "Helix Core project management with explicit workspace writes, changelists, file history and recoverable CyRevision backups. Git is not used by this mode.",
            new PluginProjectModeFeatures(false, false, false, true, false),
            new PluginProjectModeRetention(PluginProjectModeRetentionKind.Timeline, 60, 180),
            ["PerforceWorkspaceTab", "BackupsWorkspaceTab", "SolutionExplorerWorkspaceTab", "ConsoleWorkspaceTab"],
            "Perforce")
    ];

    public Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public PluginProjectModeAvailability EvaluateProjectMode(
        string modeId,
        PluginProjectModeContext context)
    {
        if (!string.Equals(modeId, "perforce", StringComparison.OrdinalIgnoreCase))
            return new PluginProjectModeAvailability(false, $"Unknown Perforce mode '{modeId}'.");
        if (!Directory.Exists(context.ProjectRoot))
            return new PluginProjectModeAvailability(false, "The project folder does not exist.");

        return new PluginProjectModeAvailability(
            true,
            "Perforce mode is available. Configure and validate P4PORT, P4USER and P4CLIENT before enabling workspace writes.");
    }

    public PerforceCliDetection DetectCli(string? configuredPath = null)
    {
        foreach (string candidate in ResolveCliCandidates(configuredPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                PerforceCommandResult result = RunProcessAsync(
                        candidate,
                        ["-V"],
                        null,
                        TimeSpan.FromSeconds(8),
                        CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (!result.Succeeded) continue;
                string version = FirstNonEmptyLine(result.StandardOutput, result.StandardError);
                return new PerforceCliDetection(true, candidate, version, $"P4 CLI detected: {version}");
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                // Detection is best-effort and must never take down the host.
            }
        }

        return new PerforceCliDetection(
            false,
            string.IsNullOrWhiteSpace(configuredPath) ? "p4" : configuredPath.Trim(),
            string.Empty,
            "The official p4 CLI was not found. Install Helix Core Command-Line Client or select p4/p4.exe explicitly.");
    }

    public async Task<PerforceProjectSettings?> LoadSettingsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string path = GetSettingsPath(projectId);
        if (!File.Exists(path)) return null;
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PerforceProjectSettings>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSettingsAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: false);
        string path = GetSettingsPath(settings.ProjectId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, path, true);
    }

    public async Task<PerforceConnectionStatus> InspectConnectionAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: true);
        PerforceCliDetection detection = DetectCli(settings.ExecutablePath);
        if (!detection.IsAvailable)
        {
            return new PerforceConnectionStatus(
                false, false, false, false,
                settings.Server, settings.User, settings.Workspace, string.Empty, string.Empty,
                detection.Summary);
        }

        PerforceCommandResult info = await RunP4Async(settings, true, ["info"], cancellationToken).ConfigureAwait(false);
        if (!info.Succeeded)
        {
            return new PerforceConnectionStatus(
                true, false, false, false,
                settings.Server, settings.User, settings.Workspace, string.Empty, string.Empty,
                CombineSummary("P4 server connection failed.", info));
        }

        Dictionary<string, string> tags = ParseFirstTaggedRecord(info.StandardOutput);
        string server = Value(tags, "serverAddress", settings.Server);
        string user = Value(tags, "userName", settings.User);
        string workspace = Value(tags, "clientName", settings.Workspace);
        string root = Value(tags, "clientRoot", string.Empty);
        string serverVersion = Value(tags, "serverVersion", string.Empty);
        bool workspaceValid = !string.IsNullOrWhiteSpace(root) && IsInside(settings.ProjectRoot, root);
        PerforceCommandResult login = await RunP4Async(settings, false, ["login", "-s"], cancellationToken).ConfigureAwait(false);
        bool authenticated = login.Succeeded;

        string summary = authenticated && workspaceValid
            ? $"Connected to {server} as {user}; workspace {workspace} maps {root}."
            : !authenticated
                ? CombineSummary("The server is reachable, but the P4 ticket is missing or expired. Run 'p4 login' in a trusted terminal.", login)
                : $"Workspace {workspace} is reachable, but its root '{root}' does not contain this project.";

        return new PerforceConnectionStatus(
            true, true, authenticated, workspaceValid,
            server, user, workspace, root, serverVersion, summary);
    }

    public async Task<IReadOnlyList<PerforceOpenedFile>> GetOpenedFilesAsync(
        PerforceProjectSettings settings,
        bool includeOtherWorkspaces,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: true);
        List<string> arguments = ["opened"];
        if (includeOtherWorkspaces) arguments.Add("-a");
        arguments.Add(ProjectFilespec(settings));
        PerforceCommandResult result = await RunP4Async(settings, true, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && !LooksLikeEmptyResult(result)) throw CreateCommandException(result);

        return ParseTaggedRecords(result.StandardOutput, "depotFile")
            .Select(tags =>
            {
                string user = Value(tags, "user", settings.User);
                string workspace = Value(tags, "client", settings.Workspace);
                return new PerforceOpenedFile(
                    Value(tags, "depotFile"),
                    Value(tags, "clientFile"),
                    Value(tags, "action"),
                    Value(tags, "change", "default"),
                    Value(tags, "type"),
                    user,
                    workspace,
                    !string.Equals(user, settings.User, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(workspace, settings.Workspace, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<PerforceChangelist>> GetChangelistsAsync(
        PerforceProjectSettings settings,
        string status,
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: true);
        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(status, "submitted", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(status), "Status must be pending or submitted.");
        maximumCount = Math.Clamp(maximumCount, 1, 500);
        PerforceCommandResult result = await RunP4Async(
            settings,
            true,
            ["changes", "-s", status.ToLowerInvariant(), "-c", settings.Workspace, "-m", maximumCount.ToString(CultureInfo.InvariantCulture)],
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && !LooksLikeEmptyResult(result)) throw CreateCommandException(result);

        return ParseTaggedRecords(result.StandardOutput, "change")
            .Select(tags => new PerforceChangelist(
                ParseInt(Value(tags, "change")),
                Value(tags, "status", status),
                Value(tags, "desc").Trim(),
                Value(tags, "user"),
                Value(tags, "client"),
                ParseUnixTime(Value(tags, "time"))))
            .Where(change => change.Number > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PerforceFileRevision>> GetFileHistoryAsync(
        PerforceProjectSettings settings,
        string projectRelativePath,
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: true);
        string path = ResolveProjectPath(settings, projectRelativePath);
        maximumCount = Math.Clamp(maximumCount, 1, 500);
        PerforceCommandResult result = await RunP4Async(
            settings,
            true,
            ["filelog", "-m", maximumCount.ToString(CultureInfo.InvariantCulture), path],
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && !LooksLikeEmptyResult(result)) throw CreateCommandException(result);

        Dictionary<string, string> tags = ParseFirstTaggedRecord(result.StandardOutput);
        List<PerforceFileRevision> revisions = [];
        for (int index = 0; tags.ContainsKey($"rev{index}"); index++)
        {
            revisions.Add(new PerforceFileRevision(
                ParseInt(Value(tags, $"rev{index}")),
                ParseInt(Value(tags, $"change{index}")),
                Value(tags, $"action{index}"),
                Value(tags, $"type{index}"),
                Value(tags, $"user{index}"),
                Value(tags, $"client{index}"),
                ParseUnixTime(Value(tags, $"time{index}")),
                Value(tags, $"desc{index}").Trim()));
        }
        return revisions;
    }

    public Task<PerforceCommandResult> PreviewReconcileAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: true);
        return RunP4Async(settings, false, ["reconcile", "-n", "-l", ProjectFilespec(settings)], cancellationToken);
    }

    public Task<PerforceCommandResult> ReconcileAsync(
        PerforceProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed(settings);
        return RunP4Async(settings, false, ["reconcile", "-ead", "-l", ProjectFilespec(settings)], cancellationToken);
    }

    public Task<PerforceCommandResult> OpenForEditAsync(
        PerforceProjectSettings settings,
        IReadOnlyList<string> projectRelativePaths,
        int? changelist = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed(settings);
        List<string> arguments = ["edit"];
        if (changelist is > 0)
        {
            arguments.Add("-c");
            arguments.Add(changelist.Value.ToString(CultureInfo.InvariantCulture));
        }
        arguments.AddRange(ResolveProjectPaths(settings, projectRelativePaths));
        return RunP4Async(settings, false, arguments, cancellationToken);
    }

    public Task<PerforceCommandResult> RevertAsync(
        PerforceProjectSettings settings,
        IReadOnlyList<string> projectRelativePaths,
        bool unchangedOnly,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed(settings);
        List<string> arguments = ["revert"];
        if (unchangedOnly) arguments.Add("-a");
        arguments.AddRange(ResolveProjectPaths(settings, projectRelativePaths));
        return RunP4Async(settings, false, arguments, cancellationToken);
    }

    public Task<PerforceCommandResult> SubmitAsync(
        PerforceProjectSettings settings,
        int? changelist,
        string description,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed(settings);
        List<string> arguments = ["submit"];
        if (changelist is > 0)
        {
            arguments.Add("-c");
            arguments.Add(changelist.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException("A description is required when submitting the default changelist.");
            arguments.Add("-d");
            arguments.Add(description.Trim());
        }
        return RunP4Async(settings, false, arguments, cancellationToken);
    }

    public Task<PerforceCommandResult> SyncAsync(
        PerforceProjectSettings settings,
        bool previewOnly,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, requireCoordinates: true);
        if (!previewOnly) EnsureWritesAllowed(settings);
        List<string> arguments = ["sync"];
        if (previewOnly) arguments.Add("-n");
        arguments.Add(ProjectFilespec(settings));
        return RunP4Async(settings, false, arguments, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<PerforceCommandResult> RunP4Async(
        PerforceProjectSettings settings,
        bool tagged,
        IReadOnlyList<string> commandArguments,
        CancellationToken cancellationToken)
    {
        List<string> arguments = [];
        if (tagged) arguments.Add("-ztag");
        if (!string.IsNullOrWhiteSpace(settings.Server))
        {
            arguments.Add("-p");
            arguments.Add(settings.Server.Trim());
        }
        if (!string.IsNullOrWhiteSpace(settings.User))
        {
            arguments.Add("-u");
            arguments.Add(settings.User.Trim());
        }
        if (!string.IsNullOrWhiteSpace(settings.Workspace))
        {
            arguments.Add("-c");
            arguments.Add(settings.Workspace.Trim());
        }
        arguments.AddRange(commandArguments);
        return await RunProcessAsync(
            settings.ExecutablePath,
            arguments,
            settings.ProjectRoot,
            TimeSpan.FromMinutes(10),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PerforceCommandResult> RunProcessAsync(
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
            return new PerforceCommandResult(false, -1, await outputTask.ConfigureAwait(false), "P4 command timed out.", stopwatch.Elapsed, "P4 command timed out.");
        }

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        return new PerforceCommandResult(
            process.ExitCode == 0,
            process.ExitCode,
            output,
            error,
            stopwatch.Elapsed,
            process.ExitCode == 0
                ? $"P4 command completed in {stopwatch.Elapsed.TotalMilliseconds:N0} ms."
                : $"P4 command failed with exit code {process.ExitCode}.");
    }

    private string GetSettingsPath(Guid projectId)
    {
        if (_context is null) throw new InvalidOperationException("Plugin is not initialized.");
        return Path.Combine(_context.ConfigurationDirectory, "plugins", "perforce", "projects", projectId.ToString("N") + ".json");
    }

    private static void ValidateSettings(PerforceProjectSettings settings, bool requireCoordinates)
    {
        if (settings.ProjectId == Guid.Empty) throw new InvalidOperationException("A project ID is required.");
        if (string.IsNullOrWhiteSpace(settings.ProjectRoot) || !Path.IsPathFullyQualified(settings.ProjectRoot))
            throw new InvalidOperationException("The Perforce project root must be an absolute path.");
        if (string.IsNullOrWhiteSpace(settings.ExecutablePath))
            throw new InvalidOperationException("Select the official p4 executable.");
        if (requireCoordinates && (string.IsNullOrWhiteSpace(settings.Server) || string.IsNullOrWhiteSpace(settings.User) || string.IsNullOrWhiteSpace(settings.Workspace)))
            throw new InvalidOperationException("P4PORT, P4USER and P4CLIENT are required.");
    }

    private static void EnsureWritesAllowed(PerforceProjectSettings settings)
    {
        ValidateSettings(settings, requireCoordinates: true);
        if (!settings.WriteOperationsEnabled)
            throw new InvalidOperationException("Perforce write operations are disabled for this project. Enable them explicitly after validating the workspace.");
    }

    private static IReadOnlyList<string> ResolveProjectPaths(
        PerforceProjectSettings settings,
        IReadOnlyList<string> relativePaths)
    {
        if (relativePaths.Count == 0) throw new InvalidOperationException("Select at least one project file.");
        return relativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveProjectPath(settings, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveProjectPath(PerforceProjectSettings settings, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("A project-relative path is required.");
        string root = Path.GetFullPath(settings.ProjectRoot);
        string candidate = Path.GetFullPath(Path.IsPathFullyQualified(relativePath)
            ? relativePath
            : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(candidate, root)) throw new InvalidOperationException("Perforce operations are restricted to the selected project root.");
        return candidate;
    }

    private static string ProjectFilespec(PerforceProjectSettings settings) =>
        Path.Combine(Path.GetFullPath(settings.ProjectRoot), "...");

    private static bool IsInside(string candidate, string root)
    {
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedCandidate.Equals(normalizedRoot, PathComparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseTaggedRecords(string output, string boundaryKey)
    {
        List<Dictionary<string, string>> records = [];
        Dictionary<string, string> current = new(StringComparer.OrdinalIgnoreCase);
        foreach (string sourceLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!sourceLine.StartsWith("... ", StringComparison.Ordinal)) continue;
            string payload = sourceLine[4..];
            int separator = payload.IndexOf(' ');
            string key = separator < 0 ? payload : payload[..separator];
            string value = separator < 0 ? string.Empty : payload[(separator + 1)..];
            if (string.Equals(key, boundaryKey, StringComparison.OrdinalIgnoreCase) && current.Count > 0)
            {
                records.Add(current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            current[key] = value;
        }
        if (current.Count > 0) records.Add(current);
        return records;
    }

    private static Dictionary<string, string> ParseFirstTaggedRecord(string output) =>
        ParseTaggedRecords(output, "__never_repeated__").FirstOrDefault()
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static string Value(IReadOnlyDictionary<string, string> tags, string key, string fallback = "") =>
        tags.TryGetValue(key, out string? value) ? value : fallback;

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static DateTimeOffset? ParseUnixTime(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static bool LooksLikeEmptyResult(PerforceCommandResult result) =>
        result.StandardError.Contains("file(s) not opened", StringComparison.OrdinalIgnoreCase) ||
        result.StandardOutput.Contains("file(s) not opened", StringComparison.OrdinalIgnoreCase) ||
        result.StandardError.Contains("no such file", StringComparison.OrdinalIgnoreCase);

    private static Exception CreateCommandException(PerforceCommandResult result) =>
        new InvalidOperationException(CombineSummary(result.Summary, result));

    private static string CombineSummary(string prefix, PerforceCommandResult result)
    {
        string detail = FirstNonEmptyLine(result.StandardError, result.StandardOutput);
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail}";
    }

    private static string FirstNonEmptyLine(params string[] values) =>
        values.SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).FirstOrDefault() ?? string.Empty;

    private static IEnumerable<string> ResolveCliCandidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) yield return configuredPath.Trim();
        string executableName = OperatingSystem.IsWindows() ? "p4.exe" : "p4";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try { candidate = Path.Combine(directory, executableName); }
            catch (ArgumentException) { continue; }
            if (File.Exists(candidate)) yield return candidate;
        }
        yield return executableName;
    }
}
