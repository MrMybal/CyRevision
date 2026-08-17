using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.Unreal;

internal sealed class UnrealAssetInspectionService : IDisposable
{
    private const int SchemaVersion = 4;
    private const string RenderAttemptMarkerName = "render-attempted";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly string _settingsPath;
    private readonly UnrealBuildService _buildService;
    private readonly Func<string, UnrealProjectInspection> _inspectProject;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly SemaphoreSlim _generationGate = new(1, 1);
    private Dictionary<string, UnrealAssetInspectionOptions>? _settings;

    public UnrealAssetInspectionService(
        string configurationDirectory,
        UnrealBuildService buildService,
        Func<string, UnrealProjectInspection> inspectProject)
    {
        _settingsPath = Path.Combine(
            Path.GetFullPath(configurationDirectory),
            "plugins",
            "unreal",
            "asset-inspection.json");
        _buildService = buildService;
        _inspectProject = inspectProject;
    }

    public async Task<UnrealAssetInspectionOptions> LoadOptionsAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        string key = NormalizeProjectRoot(projectPath);
        await _settingsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSettingsLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _settings!.TryGetValue(key, out UnrealAssetInspectionOptions? options)
                ? NormalizeOptions(options)
                : UnrealAssetInspectionOptions.Default;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public async Task SaveOptionsAsync(
        string projectPath,
        UnrealAssetInspectionOptions options,
        CancellationToken cancellationToken)
    {
        string key = NormalizeProjectRoot(projectPath);
        UnrealAssetInspectionOptions normalized = NormalizeOptions(options);
        await _settingsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSettingsLoadedAsync(cancellationToken).ConfigureAwait(false);
            _settings![key] = normalized;
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            string temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (FileStream stream = new(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             32 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, _settings, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            File.Move(temporary, _settingsPath, true);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public Task<UnrealAssetInspectionCacheStatus> GetCacheStatusAsync(
        string projectPath,
        CancellationToken cancellationToken) =>
        Task.Run(() => InspectCache(projectPath, cancellationToken), cancellationToken);

    public Task<UnrealAssetInspectionCacheStatus> ClearCacheAsync(
        string projectPath,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            string cacheRoot = GetCacheRoot(projectPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
            return InspectCache(projectPath, cancellationToken);
        }, cancellationToken);

    public async Task<FilePresentationResult?> TryCreatePreviewAsync(
        FilePreviewRequest request,
        CancellationToken cancellationToken)
    {
        UnrealAssetInspectionOptions options = await LoadOptionsAsync(request.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!options.Enabled) return null;

        UnrealProjectInspection inspection = _inspectProject(request.ProjectRoot);
        if (!inspection.IsValid || !inspection.IsCompatible || !inspection.IsEditorPluginInstalled || inspection.UpdateAvailable)
            return null;

        InspectionEntry? entry = await EnsureInspectionAsync(
                inspection,
                request.ProjectRoot,
                request.FilePath,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (entry is null) return null;
        Dictionary<string, string> metadata = await ReadMetadataAsync(entry.ManifestPath, cancellationToken).ConfigureAwait(false);
        metadata["Cached preview"] = File.Exists(entry.ImagePath) ? "Yes" : "No";
        metadata["Resolution"] = $"{options.PreviewResolution} px";
        File.SetLastAccessTimeUtc(entry.ManifestPath, DateTime.UtcNow);
        string className = metadata.GetValueOrDefault("class", "Unreal asset");
        string text = FormatMetadata(metadata);
        return File.Exists(entry.ImagePath)
            ? new FilePresentationResult(
                "cyrevision.unreal.files",
                FilePresentationKind.Image,
                $"Unreal headless preview · {className} · {options.PreviewResolution} px",
                text,
                entry.ImagePath,
                metadata)
            : new FilePresentationResult(
                "cyrevision.unreal.files",
                FilePresentationKind.Metadata,
                $"Unreal headless inspection · {className}",
                text,
                Metadata: metadata);
    }

    public async Task<FilePresentationResult?> TryCreateDiffAsync(
        FileDiffRequest request,
        CancellationToken cancellationToken)
    {
        UnrealAssetInspectionOptions options = await LoadOptionsAsync(request.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!options.Enabled) return null;
        UnrealProjectInspection inspection = _inspectProject(request.ProjectRoot);
        if (!inspection.IsValid || !inspection.IsCompatible || !inspection.IsEditorPluginInstalled || inspection.UpdateAvailable)
            return null;

        InspectionEntry? baseline = await EnsureInspectionAsync(
                inspection,
                request.ProjectRoot,
                request.BaselinePath,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        InspectionEntry? candidate = await EnsureInspectionAsync(
                inspection,
                request.ProjectRoot,
                request.CandidatePath,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (baseline is null || candidate is null) return null;

        string baselineJson = await File.ReadAllTextAsync(baseline.ManifestPath, cancellationToken).ConfigureAwait(false);
        string candidateJson = await File.ReadAllTextAsync(candidate.ManifestPath, cancellationToken).ConfigureAwait(false);
        BlueprintSemanticDiffResult? blueprintDiff = BlueprintSemanticDiff.Compare(
            baselineJson,
            candidateJson,
            request.RelativePath);
        MaterialSemanticDiffResult? materialDiff = blueprintDiff is null
            ? MaterialSemanticDiff.Compare(baselineJson, candidateJson, request.RelativePath)
            : null;
        UnrealMetadataSemanticDiffResult? metadataDiff = blueprintDiff is null && materialDiff is null
            ? UnrealMetadataSemanticDiff.Compare(baselineJson, candidateJson, request.RelativePath)
            : null;
        if (blueprintDiff is null && materialDiff is null && metadataDiff is null) return null;

        string summary = blueprintDiff?.Summary ?? materialDiff?.Summary ?? metadataDiff!.Summary;
        string text = blueprintDiff?.Text ?? materialDiff?.Text ?? metadataDiff!.Text;
        IReadOnlyDictionary<string, string> metadata =
            blueprintDiff?.Metadata ?? materialDiff?.Metadata ?? metadataDiff!.Metadata;

        File.SetLastAccessTimeUtc(baseline.ManifestPath, DateTime.UtcNow);
        File.SetLastAccessTimeUtc(candidate.ManifestPath, DateTime.UtcNow);
        return File.Exists(candidate.ImagePath)
            ? new FilePresentationResult(
                "cyrevision.unreal.files",
                FilePresentationKind.Image,
                summary,
                text,
                candidate.ImagePath,
                metadata)
            : new FilePresentationResult(
                "cyrevision.unreal.files",
                FilePresentationKind.Metadata,
                summary,
                text,
                Metadata: metadata);
    }

    private async Task<InspectionEntry?> EnsureInspectionAsync(
        UnrealProjectInspection inspection,
        string projectRoot,
        string filePath,
        UnrealAssetInspectionOptions options,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return null;
        string cacheDirectory = BuildEntryDirectory(projectRoot, filePath, options);
        string manifestPath = Path.Combine(cacheDirectory, "inspection.json");
        string imagePath = Path.Combine(cacheDirectory, "preview.png");
        if (!File.Exists(manifestPath))
        {
            await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(manifestPath))
                {
                    Directory.CreateDirectory(cacheDirectory);
                    await RunInspectorAsync(
                            inspection,
                            TryBuildObjectPath(projectRoot, filePath),
                            filePath,
                            cacheDirectory,
                            options,
                            allowRendering: false,
                            cancellationToken)
                        .ConfigureAwait(false);

                    // Loading Blueprint graphs, package metadata and embedded thumbnails does not
                    // need an Unreal rendering device. Only start the expensive shader/render path
                    // for meshes that actually need a generated thumbnail. The marker prevents a
                    // failed render from starting ShaderCompileWorker again on every selection.
                    string renderAttemptMarker = Path.Combine(cacheDirectory, RenderAttemptMarkerName);
                    if (options.RenderMeshThumbnails &&
                        File.Exists(manifestPath) &&
                        !File.Exists(imagePath) &&
                        !File.Exists(renderAttemptMarker) &&
                        await IsMeshManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false))
                    {
                        await File.WriteAllTextAsync(
                                renderAttemptMarker,
                                DateTimeOffset.UtcNow.ToString("O"),
                                cancellationToken)
                            .ConfigureAwait(false);
                        try
                        {
                            await RunInspectorAsync(
                                    inspection,
                                    TryBuildObjectPath(projectRoot, filePath),
                                    filePath,
                                    cacheDirectory,
                                    options,
                                    allowRendering: true,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Keep the fast metadata result when optional mesh rendering times out.
                        }
                    }
                    await TrimCacheAsync(projectRoot, options.CacheBudgetBytes, cacheDirectory, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _generationGate.Release();
            }
        }
        return File.Exists(manifestPath)
            ? new InspectionEntry(cacheDirectory, manifestPath, imagePath)
            : null;
    }

    private async Task RunInspectorAsync(
        UnrealProjectInspection inspection,
        string? objectPath,
        string packageFile,
        string outputDirectory,
        UnrealAssetInspectionOptions options,
        bool allowRendering,
        CancellationToken cancellationToken)
    {
        UnrealBuildDiscovery discovery = await _buildService.DiscoverAsync(inspection.ProjectFile, cancellationToken)
            .ConfigureAwait(false);
        UnrealEngineInstallation? engine = discovery.Engines.FirstOrDefault();
        if (engine is null) return;
        string? executable = ResolveEditorCommandletExecutable(engine);
        if (executable is null) return;

        string logPath = Path.Combine(outputDirectory, "inspection.log");
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = inspection.ProjectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(inspection.ProjectFile);
        start.ArgumentList.Add("-run=CyRevisionAssetInspect");
        if (objectPath is not null)
            start.ArgumentList.Add($"-Asset={objectPath}");
        else
            start.ArgumentList.Add($"-PackageFile={Path.GetFullPath(packageFile)}");
        start.ArgumentList.Add($"-Output={outputDirectory}");
        start.ArgumentList.Add($"-Resolution={options.PreviewResolution}");
        start.ArgumentList.Add($"-RenderMesh={(options.RenderMeshThumbnails ? 1 : 0)}");
        start.ArgumentList.Add($"-RenderThumbnail={(allowRendering ? 1 : 0)}");
        start.ArgumentList.Add("-unattended");
        start.ArgumentList.Add("-nop4");
        start.ArgumentList.Add("-nosplash");
        if (allowRendering)
        {
            start.ArgumentList.Add("-AllowCommandletRendering");
            start.ArgumentList.Add("-RenderOffscreen");
        }
        else
        {
            start.ArgumentList.Add("-NullRHI");
        }

        using Process process = new() { StartInfo = start };
        if (!process.Start()) return;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
        string output = await stdout.ConfigureAwait(false);
        string errors = await stderr.ConfigureAwait(false);
        await File.WriteAllTextAsync(
                logPath,
                output + (string.IsNullOrWhiteSpace(errors) ? string.Empty : Environment.NewLine + errors),
                cancellationToken)
            .ConfigureAwait(false);
        if (process.ExitCode != 0 && !File.Exists(Path.Combine(outputDirectory, "inspection.json")))
            Directory.Delete(outputDirectory, true);
    }

    private async Task EnsureSettingsLoadedAsync(CancellationToken cancellationToken)
    {
        if (_settings is not null) return;
        if (!File.Exists(_settingsPath))
        {
            _settings = new Dictionary<string, UnrealAssetInspectionOptions>(PathComparer);
            return;
        }
        try
        {
            await using FileStream stream = new(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            Dictionary<string, UnrealAssetInspectionOptions>? loaded =
                await JsonSerializer.DeserializeAsync<Dictionary<string, UnrealAssetInspectionOptions>>(
                        stream,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            _settings = new Dictionary<string, UnrealAssetInspectionOptions>(loaded ?? [], PathComparer);
        }
        catch (JsonException)
        {
            _settings = new Dictionary<string, UnrealAssetInspectionOptions>(PathComparer);
        }
    }

    private static UnrealAssetInspectionOptions NormalizeOptions(UnrealAssetInspectionOptions options) => options with
    {
        PreviewResolution = Math.Clamp(options.PreviewResolution, 128, 2048),
        CacheBudgetBytes = Math.Clamp(options.CacheBudgetBytes, 256L * 1024 * 1024, 100L * 1024 * 1024 * 1024),
        CommandTimeoutSeconds = Math.Clamp(options.CommandTimeoutSeconds, 30, 1800)
    };

    private static string BuildEntryDirectory(
        string projectPath,
        string filePath,
        UnrealAssetInspectionOptions options)
    {
        FileInfo file = new(filePath);
        string fingerprint = string.Join('|',
            SchemaVersion,
            Path.GetFullPath(filePath),
            file.Exists ? file.Length : 0,
            file.Exists ? file.LastWriteTimeUtc.Ticks : 0,
            options.PreviewResolution,
            options.RenderMeshThumbnails);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))).ToLowerInvariant();
        return Path.Combine(GetCacheRoot(projectPath), hash[..2], hash);
    }

    private static async Task<Dictionary<string, string>> ReadMetadataAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
        FlattenJson(document.RootElement, string.Empty, metadata);
        return metadata;
    }

    private static async Task<bool> IsMeshManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("assetKind", out JsonElement kind)) return false;
        return kind.GetString() is "Static mesh" or "Skeletal mesh";
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> destination)
    {
        if (destination.Count >= 250) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                FlattenJson(property.Value, key, destination);
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] values = element.EnumerateArray().ToArray();
            if (values.Any(value => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array))
                destination[prefix + ".count"] = values.Length.ToString();
            else
                destination[prefix] = string.Join(", ", values.Take(80).Select(ValueText));
            return;
        }
        destination[prefix] = ValueText(element);
    }

    private static string ValueText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText()
    };

    private static string FormatMetadata(IReadOnlyDictionary<string, string> metadata) => string.Join(
        Environment.NewLine,
        metadata.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Key}: {item.Value}"));

    private static string? TryBuildObjectPath(string projectPath, string filePath)
    {
        string root = NormalizeProjectRoot(projectPath);
        string full = Path.GetFullPath(filePath);
        string prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison)) return null;
        string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        string package;
        if (relative.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
        {
            package = "/Game/" + relative["Content/".Length..];
        }
        else if (relative.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int contentIndex = Array.FindIndex(parts, part => part.Equals("Content", StringComparison.OrdinalIgnoreCase));
            if (contentIndex < 2 || contentIndex == parts.Length - 1) return null;
            string pluginName = parts[contentIndex - 1];
            package = "/" + pluginName + "/" + string.Join('/', parts.Skip(contentIndex + 1));
        }
        else return null;
        package = package[..^Path.GetExtension(package).Length];
        string assetName = package[(package.LastIndexOf('/') + 1)..];
        return package + "." + assetName;
    }

    private static string? ResolveEditorCommandletExecutable(UnrealEngineInstallation engine)
    {
        bool unrealFive = !engine.Version.StartsWith("4.", StringComparison.Ordinal);
        string baseName = unrealFive ? "UnrealEditor-Cmd" : "UE4Editor-Cmd";
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(engine.RootPath, "Engine", "Binaries", "Win64", baseName + ".exe")]
            : OperatingSystem.IsMacOS()
                ? [
                    Path.Combine(engine.RootPath, "Engine", "Binaries", "Mac", baseName),
                    Path.Combine(engine.RootPath, "Engine", "Binaries", "Mac", baseName + ".app", "Contents", "MacOS", baseName)
                ]
                : [Path.Combine(engine.RootPath, "Engine", "Binaries", "Linux", baseName)];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static UnrealAssetInspectionCacheStatus InspectCache(
        string projectPath,
        CancellationToken cancellationToken)
    {
        string root = GetCacheRoot(projectPath);
        if (!Directory.Exists(root))
            return new UnrealAssetInspectionCacheStatus(root, 0, 0, null, "Cache is empty.");
        long size = 0;
        int entries = 0;
        DateTimeOffset? latest = null;
        foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = new(filePath);
            size += file.Length;
            if (file.Name.Equals("inspection.json", StringComparison.OrdinalIgnoreCase)) entries++;
            if (latest is null || file.LastWriteTimeUtc > latest.Value.UtcDateTime)
                latest = file.LastWriteTimeUtc;
        }
        return new UnrealAssetInspectionCacheStatus(
            root,
            size,
            entries,
            latest,
            $"{entries:N0} cached asset(s) · {FormatBytes(size)} · {root}");
    }

    private static async Task TrimCacheAsync(
        string projectPath,
        long budgetBytes,
        string preserveDirectory,
        CancellationToken cancellationToken)
    {
        string root = GetCacheRoot(projectPath);
        if (!Directory.Exists(root)) return;
        List<CacheEntry> entries = [];
        foreach (string manifest in Directory.EnumerateFiles(root, "inspection.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = Path.GetDirectoryName(manifest)!;
            long size = Directory.EnumerateFiles(directory).Sum(file => new FileInfo(file).Length);
            DateTime access = File.GetLastAccessTimeUtc(manifest);
            entries.Add(new CacheEntry(directory, size, access));
        }
        long total = entries.Sum(entry => entry.Size);
        foreach (CacheEntry entry in entries.OrderBy(entry => entry.LastAccessUtc))
        {
            if (total <= budgetBytes) break;
            if (entry.Directory.Equals(preserveDirectory, PathComparison)) continue;
            Directory.Delete(entry.Directory, true);
            total -= entry.Size;
            await Task.Yield();
        }
    }

    private static string NormalizeProjectRoot(string projectPath)
    {
        string full = Path.GetFullPath(projectPath);
        if (File.Exists(full) && Path.GetExtension(full).Equals(".uproject", StringComparison.OrdinalIgnoreCase))
            full = Path.GetDirectoryName(full)!;
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static string GetCacheRoot(string projectPath) => Path.Combine(
        NormalizeProjectRoot(projectPath),
        ".cyrevision",
        "cache",
        "unreal-assets");

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public void Dispose()
    {
        _settingsGate.Dispose();
        _generationGate.Dispose();
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record CacheEntry(string Directory, long Size, DateTime LastAccessUtc);
    private sealed record InspectionEntry(string Directory, string ManifestPath, string ImagePath);
}
