using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

internal sealed class UnrealBuildService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex TargetClassPattern = new(@"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)Target\b", RegexOptions.Compiled);
    private static readonly Regex TargetTypePattern = new(@"\bType\s*=\s*TargetType\.(?<type>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private readonly string _profilesRoot;
    private readonly string _logsRoot;
    private readonly ConcurrentDictionary<string, UnrealEngineCacheEntry> _engineCache = new(PathComparer);

    public UnrealBuildService(string configurationDirectory, string dataDirectory)
    {
        _profilesRoot = Path.Combine(Path.GetFullPath(configurationDirectory), "unreal-builds");
        _logsRoot = Path.Combine(Path.GetFullPath(dataDirectory), "unreal-builds", "logs");
    }

    public async Task<UnrealBuildDiscovery> DiscoverAsync(
        string projectPath,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        string projectFile = ResolveProjectFile(projectPath)
                             ?? throw new FileNotFoundException("Select an Unreal .uproject file first.", projectPath);
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        string fingerprint = BuildDiscoveryFingerprint(projectFile, projectRoot);
        if (!forceRefresh)
        {
            UnrealDiscoveryCacheDocument? cached = await LoadDiscoveryCacheAsync(projectRoot, cancellationToken).ConfigureAwait(false);
            if (cached is not null && cached.Fingerprint == fingerprint &&
                DateTimeOffset.UtcNow - cached.CapturedAt < TimeSpan.FromMinutes(30))
            {
                return cached.Discovery with
                {
                    IsCached = true,
                    CapturedAt = cached.CapturedAt,
                    Summary = cached.Discovery.Summary + " · restored from .cyrevision cache"
                };
            }
        }
        string association = ReadEngineAssociation(projectFile);
        List<string> roots = DiscoverEngineRoots().ToList();
        List<UnrealEngineInstallation> engines = [];
        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnrealEngineInstallation? installation = await InspectEngineCachedAsync(root, cancellationToken).ConfigureAwait(false);
            if (installation is not null && UnrealPluginCompatibility.SupportedEngineVersions.Contains(
                    ToMinorVersion(installation.Version), StringComparer.OrdinalIgnoreCase))
                engines.Add(installation);
        }

        engines = engines
            .GroupBy(engine => Path.TrimEndingDirectorySeparator(engine.RootPath), PathComparer)
            .Select(group => group.First())
            .OrderByDescending(engine => AssociationMatches(association, engine))
            .ThenBy(engine => ParseVersion(engine.Version))
            .ToList();
        IReadOnlyList<UnrealBuildTargetDescriptor> targets = DiscoverTargets(projectFile, projectRoot);
        string summary = engines.Count == 0
            ? "No compatible Unreal Engine installation was detected. Add an engine root or register it with Epic Launcher."
            : $"{engines.Count} compatible engine installation(s) and {targets.Count} build target(s) detected for {Path.GetFileName(projectFile)}.";
        UnrealBuildDiscovery discovery = new(projectFile, engines, targets, summary, false, DateTimeOffset.UtcNow);
        await SaveDiscoveryCacheAsync(projectRoot, new UnrealDiscoveryCacheDocument(fingerprint, DateTimeOffset.UtcNow, discovery), cancellationToken)
            .ConfigureAwait(false);
        return discovery;
    }

    public async Task<UnrealBuildProfile?> LoadProfileAsync(Guid projectId, CancellationToken cancellationToken)
    {
        string path = ProfilePath(projectId);
        if (!File.Exists(path)) return null;
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, true);
        return await JsonSerializer.DeserializeAsync<UnrealBuildProfile>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveProfileAsync(UnrealBuildProfile profile, CancellationToken cancellationToken)
    {
        if (profile.ProjectId == Guid.Empty) throw new InvalidDataException("The Unreal build profile needs a project ID.");
        if (profile.TimeoutMinutes is < 1 or > 1440) throw new InvalidDataException("Build timeout must be between 1 and 1440 minutes.");
        Directory.CreateDirectory(_profilesRoot);
        string path = ProfilePath(profile.ProjectId);
        string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, true))
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
    }

    public async Task<IReadOnlyList<UnrealBuildProfile>> LoadPresetsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        string path = PresetsPath(projectId);
        if (!File.Exists(path)) return [];
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, true);
            return await JsonSerializer.DeserializeAsync<IReadOnlyList<UnrealBuildProfile>>(stream, JsonOptions, cancellationToken)
                       .ConfigureAwait(false) ?? [];
        }
        catch (JsonException) { return []; }
    }

    public async Task SavePresetAsync(UnrealBuildProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.PresetName)) throw new InvalidDataException("A build preset name is required.");
        List<UnrealBuildProfile> presets = (await LoadPresetsAsync(profile.ProjectId, cancellationToken).ConfigureAwait(false)).ToList();
        presets.RemoveAll(item => item.PresetName.Equals(profile.PresetName, StringComparison.OrdinalIgnoreCase));
        presets.Add(profile with { UpdatedAt = DateTimeOffset.UtcNow });
        await WriteJsonAtomicAsync(PresetsPath(profile.ProjectId), presets.OrderBy(item => item.PresetName).ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeletePresetAsync(Guid projectId, string presetName, CancellationToken cancellationToken)
    {
        List<UnrealBuildProfile> presets = (await LoadPresetsAsync(projectId, cancellationToken).ConfigureAwait(false)).ToList();
        presets.RemoveAll(item => item.PresetName.Equals(presetName.Trim(), StringComparison.OrdinalIgnoreCase));
        await WriteJsonAtomicAsync(PresetsPath(projectId), presets, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UnrealBuildResult> RunAsync(
        UnrealBuildRequest request,
        IProgress<UnrealBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        string projectFile = Path.GetFullPath(request.ProjectFile);
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        string safeTarget = SanitizeName(request.Target.TargetName);
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string logFolder = Path.Combine(_logsRoot, request.ProjectId.ToString("N"));
        Directory.CreateDirectory(logFolder);
        string logPath = Path.Combine(logFolder,
            $"{timestamp}-UE{request.Engine.Version}-{safeTarget}-{request.Platform}.log");
        string outputRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(projectRoot, "Saved", "CyRevision", "Builds")
            : Path.GetFullPath(request.OutputDirectory);
        string outputPath = Path.Combine(outputRoot,
            $"UE{request.Engine.Version}-{safeTarget}-{request.Platform}-{timestamp}");
        Directory.CreateDirectory(outputRoot);

        (string executable, IReadOnlyList<string> arguments) = BuildCommand(request, outputPath);
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = request.Engine.RootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        ConfigureEnvironment(start, request);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await using FileStream logStream = new(logPath, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, true);
        await using StreamWriter writer = new(logStream) { AutoFlush = true };
        await writer.WriteLineAsync($"CyRevision Unreal build started {DateTimeOffset.UtcNow:O}");
        await writer.WriteLineAsync($"Engine: {request.Engine.Version} | {request.Engine.RootPath}");
        await writer.WriteLineAsync($"Target: {request.Target.DisplayName} | {request.Platform} | {request.Configuration}");
        await writer.WriteLineAsync($"Command: {executable} {string.Join(' ', arguments.Select(QuoteForLog))}");
        progress?.Report(new UnrealBuildProgress(DateTimeOffset.Now, "system",
            $"Starting UE {request.Engine.Version} · {request.Target.DisplayName} · {request.Platform}"));

        using Process process = new() { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException($"Unable to start Unreal build tool: {executable}");
        using SemaphoreSlim writerGate = new(1, 1);
        ConcurrentQueue<UnrealBuildDiagnostic> diagnostics = new();
        Task stdout = CopyOutputAsync(process.StandardOutput, writer, writerGate, "stdout", string.Empty, progress, diagnostics, cancellationToken);
        Task stderr = CopyOutputAsync(process.StandardError, writer, writerGate, "stderr", "[stderr] ", progress, diagnostics, cancellationToken);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(request.TimeoutMinutes));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (!cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"Unreal build exceeded the {request.TimeoutMinutes}-minute timeout. Log: {logPath}");
            throw;
        }

        stopwatch.Stop();
        await writer.WriteLineAsync($"Exit code: {process.ExitCode}");
        await writer.WriteLineAsync($"Duration: {stopwatch.Elapsed}");
        bool succeeded = process.ExitCode == 0;
        UnrealBuildDiagnostic[] parsedDiagnostics = diagnostics.ToArray();
        int warningCount = parsedDiagnostics.Count(item => item.Severity == UnrealBuildDiagnosticSeverity.Warning);
        int errorCount = parsedDiagnostics.Count(item => item.Severity == UnrealBuildDiagnosticSeverity.Error);
        string summary = succeeded
            ? $"UE {request.Engine.Version} {request.Platform} build succeeded in {FormatDuration(stopwatch.Elapsed)} · {warningCount} warning(s)."
            : $"UE {request.Engine.Version} {request.Platform} build failed with exit code {process.ExitCode} · {errorCount} error(s).";
        progress?.Report(new UnrealBuildProgress(DateTimeOffset.Now, "system", summary));
        return new UnrealBuildResult(
            succeeded,
            process.ExitCode,
            request.Engine.Version,
            request.Target.TargetName,
            request.Platform,
            stopwatch.Elapsed,
            logPath,
            outputPath,
            summary,
            parsedDiagnostics,
            warningCount,
            errorCount);
    }

    private static (string Executable, IReadOnlyList<string> Arguments) BuildCommand(
        UnrealBuildRequest request,
        string outputPath)
    {
        if (request.Target.Kind == UnrealBuildTargetKind.Plugin)
        {
            return (request.Engine.RunUatPath,
            [
                "BuildPlugin",
                $"-Plugin={Path.GetFullPath(request.Target.SourcePath)}",
                $"-Package={outputPath}",
                $"-TargetPlatforms={request.Platform}",
                "-Rocket",
                "-Unattended",
                "-UTF8Output"
            ]);
        }

        if (request.CookAndPackage)
        {
            return (request.Engine.RunUatPath,
            [
                "BuildCookRun",
                $"-project={Path.GetFullPath(request.ProjectFile)}",
                "-noP4",
                "-build",
                "-cook",
                "-stage",
                "-package",
                $"-target={request.Target.TargetName}",
                $"-targetplatform={request.Platform}",
                $"-clientconfig={request.Configuration}",
                "-archive",
                $"-archivedirectory={outputPath}",
                "-unattended",
                "-UTF8Output"
            ]);
        }

        return (request.Engine.BuildScriptPath,
        [
            request.Target.TargetName,
            request.Platform.ToString(),
            request.Configuration.ToString(),
            $"-Project={Path.GetFullPath(request.ProjectFile)}",
            "-WaitMutex",
            "-NoHotReloadFromIDE",
            "-UTF8Output"
        ]);
    }

    private static void ConfigureEnvironment(ProcessStartInfo start, UnrealBuildRequest request)
    {
        start.Environment["CYREVISION_UNREAL_BUILD"] = "1";
        start.Environment["CI"] = "true";
        if (request.Platform == UnrealBuildPlatform.Linux)
        {
            string path = SelectToolchainPath(request.LinuxToolchainPath,
                request.Engine.DetectedLinuxToolchainPath, request.AutoConfigureToolchains);
            if (!string.IsNullOrWhiteSpace(path))
            {
                start.Environment["LINUX_MULTIARCH_ROOT"] = Path.GetFullPath(path);
                start.Environment["LINUX_ROOT"] = Path.GetFullPath(path);
            }
        }
        if (request.Platform == UnrealBuildPlatform.Android)
        {
            string sdk = SelectToolchainPath(request.AndroidSdkPath,
                request.Engine.DetectedAndroidSdkPath, request.AutoConfigureToolchains);
            if (!string.IsNullOrWhiteSpace(sdk))
            {
                start.Environment["ANDROID_HOME"] = Path.GetFullPath(sdk);
                start.Environment["ANDROID_SDK_ROOT"] = Path.GetFullPath(sdk);
            }
            if (!string.IsNullOrWhiteSpace(request.AndroidNdkPath))
                start.Environment["NDKROOT"] = Path.GetFullPath(request.AndroidNdkPath);
            if (!string.IsNullOrWhiteSpace(request.JavaHomePath))
                start.Environment["JAVA_HOME"] = Path.GetFullPath(request.JavaHomePath);
        }
    }

    private static string SelectToolchainPath(string configured, string? detected, bool autoConfigure) =>
        !string.IsNullOrWhiteSpace(configured) ? configured : autoConfigure ? detected ?? string.Empty : string.Empty;

    private static async Task CopyOutputAsync(
        StreamReader reader,
        StreamWriter writer,
        SemaphoreSlim writerGate,
        string stream,
        string prefix,
        IProgress<UnrealBuildProgress>? progress,
        ConcurrentQueue<UnrealBuildDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            await writerGate.WaitAsync(cancellationToken);
            try { await writer.WriteLineAsync(prefix + line); }
            finally { writerGate.Release(); }
            progress?.Report(new UnrealBuildProgress(DateTimeOffset.Now, stream, line));
            UnrealBuildDiagnostic? diagnostic = ParseDiagnostic(line);
            if (diagnostic is not null) diagnostics.Enqueue(diagnostic);
        }
    }

    internal static UnrealBuildDiagnostic? ParseDiagnostic(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        Match compiler = Regex.Match(line,
            @"^(?<file>.+?)\((?<line>\d+)(?:,\d+)?\)\s*:\s*(?<severity>fatal error|error|warning)\s*(?<code>[A-Za-z]+\d+)?\s*:\s*(?<message>.+)$",
            RegexOptions.IgnoreCase);
        if (!compiler.Success)
        {
            compiler = Regex.Match(line,
                @"^(?<file>.+?):(?<line>\d+)(?::\d+)?\s*:\s*(?<severity>fatal error|error|warning)\s*:\s*(?<message>.+)$",
                RegexOptions.IgnoreCase);
        }
        if (compiler.Success)
        {
            string severityText = compiler.Groups["severity"].Value;
            UnrealBuildDiagnosticSeverity severity = severityText.Contains("warning", StringComparison.OrdinalIgnoreCase)
                ? UnrealBuildDiagnosticSeverity.Warning
                : UnrealBuildDiagnosticSeverity.Error;
            int? lineNumber = int.TryParse(compiler.Groups["line"].Value, out int parsedLine) ? parsedLine : null;
            return new UnrealBuildDiagnostic(severity, compiler.Groups["code"].Value,
                compiler.Groups["message"].Value.Trim(), compiler.Groups["file"].Value.Trim(), lineNumber, line);
        }
        if (line.Contains("AutomationTool exiting with ExitCode=", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("BUILD FAILED", StringComparison.OrdinalIgnoreCase))
            return new UnrealBuildDiagnostic(UnrealBuildDiagnosticSeverity.Error, "UAT", line.Trim(), string.Empty, null, line);
        return null;
    }

    private static async Task<UnrealEngineInstallation?> InspectEngineAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)); }
        catch { return null; }
        string buildVersion = Path.Combine(root, "Engine", "Build", "Build.version");
        if (!File.Exists(buildVersion)) return null;
        string? version = ReadEngineVersion(buildVersion);
        if (version is null) return null;
        string buildScript = Path.Combine(root, "Engine", "Build", "BatchFiles",
            OperatingSystem.IsWindows() ? "Build.bat" : "Build.sh");
        string runUat = Path.Combine(root, "Engine", "Build", "BatchFiles",
            OperatingSystem.IsWindows() ? "RunUAT.bat" : "RunUAT.sh");
        if (!File.Exists(buildScript) || !File.Exists(runUat)) return null;

        (string toolchain, string clang) = RecommendedLinuxToolchain(version);
        string? linuxRoot = DetectLinuxToolchainRoot(root);
        string? detectedClang = linuxRoot is null ? null : await DetectClangVersionAsync(linuxRoot, cancellationToken);
        bool linuxReady = linuxRoot is not null && VersionMatches(detectedClang, clang);
        string? androidSdk = DetectAndroidSdkRoot();
        bool androidReady = androidSdk is not null &&
                            (Directory.Exists(Path.Combine(androidSdk, "ndk")) ||
                             !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NDKROOT")));
        string summary = $"Linux: {toolchain} / clang {clang} recommended · " +
                         (linuxRoot is null ? "not detected" : $"detected {detectedClang ?? "unknown"}") +
                         $" · Android: {(androidReady ? "SDK/NDK detected" : "SDK/NDK not ready")}";
        return new UnrealEngineInstallation(
            version,
            root,
            buildScript,
            runUat,
            File.Exists(Path.Combine(root, "Engine", "Build", "InstalledBuild.txt")),
            toolchain,
            clang,
            linuxRoot,
            detectedClang,
            linuxReady,
            androidSdk,
            androidReady,
            summary);
    }

    private async Task<UnrealEngineInstallation?> InspectEngineCachedAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)); }
        catch { return null; }
        string versionFile = Path.Combine(root, "Engine", "Build", "Build.version");
        long stamp = File.Exists(versionFile) ? File.GetLastWriteTimeUtc(versionFile).Ticks : 0;
        if (_engineCache.TryGetValue(root, out UnrealEngineCacheEntry? cached) && cached.VersionStamp == stamp)
            return cached.Installation;
        UnrealEngineInstallation? installation = await InspectEngineAsync(root, cancellationToken).ConfigureAwait(false);
        if (installation is not null) _engineCache[root] = new UnrealEngineCacheEntry(stamp, installation);
        return installation;
    }

    private static IEnumerable<string> DiscoverEngineRoots()
    {
        HashSet<string> roots = new(PathComparer);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                if (Directory.Exists(full)) roots.Add(full);
            }
            catch { }
        }

        foreach (string path in (Environment.GetEnvironmentVariable("CYREVISION_UNREAL_ENGINE_ROOTS") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Add(path);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using RegistryKey? builds = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Epic Games\Unreal Engine\Builds");
                if (builds is not null)
                    foreach (string name in builds.GetValueNames()) Add(builds.GetValue(name) as string);
            }
            catch { }
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string launcher = Path.Combine(programData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (File.Exists(launcher))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcher));
                    if (document.RootElement.TryGetProperty("InstallationList", out JsonElement installations))
                        foreach (JsonElement item in installations.EnumerateArray())
                            if (item.TryGetProperty("InstallLocation", out JsonElement location)) Add(location.GetString());
                }
                catch { }
            }
        }

        foreach (string parent in roots.Select(Path.GetDirectoryName).OfType<string>().Distinct(PathComparer).ToArray())
        {
            try
            {
                foreach (string sibling in Directory.EnumerateDirectories(parent!, "UE_*", SearchOption.TopDirectoryOnly)) Add(sibling);
            }
            catch { }
        }
        return roots;
    }

    private static IReadOnlyList<UnrealBuildTargetDescriptor> DiscoverTargets(string projectFile, string projectRoot)
    {
        List<UnrealBuildTargetDescriptor> targets = [];
        string sourceRoot = Path.Combine(projectRoot, "Source");
        if (Directory.Exists(sourceRoot))
        {
            foreach (string targetFile in Directory.EnumerateFiles(sourceRoot, "*.Target.cs", SearchOption.AllDirectories))
            {
                string content;
                try { content = File.ReadAllText(targetFile); }
                catch { continue; }
                string targetName = TargetClassPattern.Match(content) is { Success: true } match
                    ? match.Groups["name"].Value
                    : Path.GetFileName(targetFile).Replace(".Target.cs", string.Empty, StringComparison.OrdinalIgnoreCase);
                string targetType = TargetTypePattern.Match(content) is { Success: true } typeMatch
                    ? typeMatch.Groups["type"].Value
                    : "Unknown";
                targets.Add(new UnrealBuildTargetDescriptor(
                    "project:" + targetName,
                    $"Project · {targetName} ({targetType})",
                    UnrealBuildTargetKind.Project,
                    targetName,
                    targetFile,
                    targetType));
            }
        }

        string projectName = Path.GetFileNameWithoutExtension(projectFile);
        if (targets.Count == 0)
        {
            targets.Add(new UnrealBuildTargetDescriptor(
                "project:" + projectName + "Editor",
                $"Project · {projectName}Editor (auto-detected)",
                UnrealBuildTargetKind.Project,
                projectName + "Editor",
                projectFile,
                "Editor"));
            targets.Add(new UnrealBuildTargetDescriptor(
                "project:" + projectName,
                $"Project · {projectName} game (auto-detected)",
                UnrealBuildTargetKind.Project,
                projectName,
                projectFile,
                "Game"));
        }

        string pluginsRoot = Path.Combine(projectRoot, "Plugins");
        if (Directory.Exists(pluginsRoot))
        {
            foreach (string plugin in Directory.EnumerateFiles(pluginsRoot, "*.uplugin", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(plugin);
                targets.Add(new UnrealBuildTargetDescriptor(
                    "plugin:" + Path.GetRelativePath(projectRoot, plugin).Replace('\\', '/'),
                    $"Plugin · {name}",
                    UnrealBuildTargetKind.Plugin,
                    name,
                    plugin,
                    "Plugin"));
            }
        }
        return targets.OrderBy(target => target.Kind).ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? DetectLinuxToolchainRoot(string engineRoot)
    {
        string? environment = Environment.GetEnvironmentVariable("LINUX_MULTIARCH_ROOT");
        if (!string.IsNullOrWhiteSpace(environment) && Directory.Exists(environment)) return Path.GetFullPath(environment);
        environment = Environment.GetEnvironmentVariable("LINUX_ROOT");
        if (!string.IsNullOrWhiteSpace(environment) && Directory.Exists(environment)) return Path.GetFullPath(environment);
        string autoSdk = Path.Combine(engineRoot, "Engine", "Extras", "ThirdPartyNotUE", "SDKs",
            OperatingSystem.IsWindows() ? "HostWin64" : "HostLinux", "Linux_x64");
        if (!Directory.Exists(autoSdk)) return null;
        return Directory.EnumerateDirectories(autoSdk).OrderByDescending(path => path).FirstOrDefault() ?? autoSdk;
    }

    private static string? DetectAndroidSdkRoot()
    {
        foreach (string variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            string? path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return Path.GetFullPath(path);
        }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string conventional = Path.Combine(local, "Android", "Sdk");
        return Directory.Exists(conventional) ? conventional : null;
    }

    private static async Task<string?> DetectClangVersionAsync(string toolchainRoot, CancellationToken cancellationToken)
    {
        string executableName = OperatingSystem.IsWindows() ? "clang++.exe" : "clang++";
        string[] candidates =
        [
            Path.Combine(toolchainRoot, "bin", executableName),
            Path.Combine(toolchainRoot, "x86_64-unknown-linux-gnu", "bin", executableName)
        ];
        string? executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            try { executable = Directory.EnumerateFiles(toolchainRoot, executableName, SearchOption.AllDirectories).FirstOrDefault(); }
            catch { return null; }
        }
        if (executable is null) return null;
        ProcessStartInfo start = new(executable, "--version")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(start)
                                ?? throw new InvalidOperationException($"Unable to start {executable}.");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Match match = Regex.Match(output, @"clang version\s+(?<version>\d+(?:\.\d+){1,2})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["version"].Value : output.Split('\n').FirstOrDefault()?.Trim();
    }

    private static (string Toolchain, string Clang) RecommendedLinuxToolchain(string engineVersion)
    {
        Version version = ParseVersion(engineVersion);
        if (version.Major < 5 || version <= new Version(5, 0, 1)) return ("v19", "11.0.1");
        if (version <= new Version(5, 1)) return ("v20", "13.0.1");
        if (version <= new Version(5, 2)) return ("v21", "15.0.1");
        if (version <= new Version(5, 4)) return ("v22", "16.0.6");
        if (version <= new Version(5, 5)) return ("v23", "18.1.0");
        if (version <= new Version(5, 6)) return ("v25", "18.1.0");
        return ("v26", "20.1.8");
    }

    private static bool VersionMatches(string? detected, string expected)
    {
        if (string.IsNullOrWhiteSpace(detected)) return false;
        string detectedMajor = detected.Split('.')[0];
        string expectedMajor = expected.Split('.')[0];
        return string.Equals(detectedMajor, expectedMajor, StringComparison.Ordinal);
    }

    private static string? ReadEngineVersion(string buildVersionPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(buildVersionPath));
            int major = document.RootElement.GetProperty("MajorVersion").GetInt32();
            int minor = document.RootElement.GetProperty("MinorVersion").GetInt32();
            int patch = document.RootElement.TryGetProperty("PatchVersion", out JsonElement patchElement)
                ? patchElement.GetInt32()
                : 0;
            return patch > 0 ? $"{major}.{minor}.{patch}" : $"{major}.{minor}";
        }
        catch { return null; }
    }

    private static string ReadEngineAssociation(string projectFile)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(projectFile));
            return document.RootElement.TryGetProperty("EngineAssociation", out JsonElement association)
                ? association.GetString() ?? string.Empty
                : string.Empty;
        }
        catch { return string.Empty; }
    }

    private static bool AssociationMatches(string association, UnrealEngineInstallation engine) =>
        association.Trim().TrimStart('U', 'E').StartsWith(ToMinorVersion(engine.Version),
            StringComparison.OrdinalIgnoreCase) ||
        association.Equals(Path.GetFileName(engine.RootPath), StringComparison.OrdinalIgnoreCase);

    private static string? ResolveProjectFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return null; }
        if (File.Exists(full) && Path.GetExtension(full).Equals(".uproject", StringComparison.OrdinalIgnoreCase)) return full;
        if (!Directory.Exists(full)) return null;
        return Directory.EnumerateFiles(full, "*.uproject", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void ValidateRequest(UnrealBuildRequest request)
    {
        if (request.ProjectId == Guid.Empty) throw new InvalidDataException("The build request needs a project ID.");
        if (!File.Exists(request.ProjectFile)) throw new FileNotFoundException("Unreal project not found.", request.ProjectFile);
        if (!Directory.Exists(request.Engine.RootPath)) throw new DirectoryNotFoundException(request.Engine.RootPath);
        if (request.Target.Kind == UnrealBuildTargetKind.Plugin && !File.Exists(request.Target.SourcePath))
            throw new FileNotFoundException("Unreal plugin descriptor not found.", request.Target.SourcePath);
        if (request.TimeoutMinutes is < 1 or > 1440) throw new InvalidDataException("Build timeout must be between 1 and 1440 minutes.");
        if (request.Platform == UnrealBuildPlatform.Linux && request.AutoConfigureToolchains &&
            string.IsNullOrWhiteSpace(request.LinuxToolchainPath) && !request.Engine.LinuxToolchainReady)
            throw new InvalidOperationException($"UE {request.Engine.Version} needs {request.Engine.RecommendedLinuxToolchain} / clang {request.Engine.RecommendedClangVersion}. Select its cross-compiler folder.");
        if (request.Platform == UnrealBuildPlatform.Android && request.AutoConfigureToolchains &&
            string.IsNullOrWhiteSpace(request.AndroidSdkPath) && !request.Engine.AndroidToolchainReady)
            throw new InvalidOperationException("Android SDK/NDK was not detected. Configure SDK, NDK and Java paths or run Unreal Turnkey first.");
    }

    private static string BuildDiscoveryFingerprint(string projectFile, string projectRoot)
    {
        StringBuilder builder = new();
        AppendFileStamp(builder, projectFile);
        foreach (string folder in new[] { Path.Combine(projectRoot, "Source"), Path.Combine(projectRoot, "Plugins") })
        {
            if (!Directory.Exists(folder)) continue;
            string pattern = folder.EndsWith("Source", StringComparison.OrdinalIgnoreCase) ? "*.Target.cs" : "*.uplugin";
            foreach (string path in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                         .OrderBy(path => path, PathComparer)) AppendFileStamp(builder, path);
        }
        builder.Append(Environment.GetEnvironmentVariable("CYREVISION_UNREAL_ENGINE_ROOTS"));
        builder.Append(Environment.GetEnvironmentVariable("LINUX_MULTIARCH_ROOT"));
        builder.Append(Environment.GetEnvironmentVariable("ANDROID_HOME"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendFileStamp(StringBuilder builder, string path)
    {
        FileInfo info = new(path);
        builder.Append(path).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).AppendLine();
    }

    private static async Task<UnrealDiscoveryCacheDocument?> LoadDiscoveryCacheAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        string path = DiscoveryCachePath(projectRoot);
        if (!File.Exists(path)) return null;
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            return await JsonSerializer.DeserializeAsync<UnrealDiscoveryCacheDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static Task SaveDiscoveryCacheAsync(
        string projectRoot,
        UnrealDiscoveryCacheDocument document,
        CancellationToken cancellationToken) =>
        WriteJsonAtomicAsync(DiscoveryCachePath(projectRoot), document, cancellationToken);

    private static string DiscoveryCachePath(string projectRoot) =>
        Path.Combine(projectRoot, ".cyrevision", "cache", "unreal", "build-discovery.json");

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Environment.ProcessId + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private string ProfilePath(Guid projectId) => Path.Combine(_profilesRoot, projectId.ToString("N") + ".json");

    private string PresetsPath(Guid projectId) => Path.Combine(_profilesRoot, projectId.ToString("N") + ".presets.json");

    private static string SanitizeName(string value) => string.Concat(value.Select(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_'));

    private static string QuoteForLog(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
        : $"{duration.TotalSeconds:F1}s";

    private static Version ParseVersion(string value)
    {
        string normalized = string.Join('.', value.Split('.').Take(3));
        return Version.TryParse(normalized, out Version? version) ? version : new Version();
    }

    private static string ToMinorVersion(string value)
    {
        string[] parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : value;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record UnrealEngineCacheEntry(long VersionStamp, UnrealEngineInstallation Installation);

    private sealed record UnrealDiscoveryCacheDocument(
        string Fingerprint,
        DateTimeOffset CapturedAt,
        UnrealBuildDiscovery Discovery);
}
