using System.Text.Json;
using CyRevision.Core.Configuration;
using CyRevision.Desktop.SystemIntegration;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

/// <summary>Uses the same persistent preferences and cache policy as the classic shell.</summary>
public sealed class NativePreferencesActions
{
    private readonly MainWindowViewModel? _model;
    private readonly ApplicationPaths _defaultPaths;
    private readonly ApplicationPaths _activePaths;
    private readonly Func<ApplicationPreferences, Task> _applyApplication;
    private readonly Func<DesktopBehaviorPreferences, Task> _applyDesktop;
    private readonly bool _nativeTraySupported;
    private ApplicationPreferences _application;
    private DesktopBehaviorPreferences _desktop;
    private CancellationTokenSource? _cacheCancellation;
    private ApplicationCacheUsage? _cacheUsage;
    private string _cacheStatus = "Measure the active cache to show its current usage.";
    private long _revision;
    private const string CacheMarker = ".cyrevision-cache-root";

    internal NativePreferencesActions(
        MainWindowViewModel? model,
        ApplicationPaths defaultPaths,
        ApplicationPaths activePaths,
        ApplicationPreferences application,
        DesktopBehaviorPreferences desktop,
        Func<ApplicationPreferences, Task> applyApplication,
        Func<DesktopBehaviorPreferences, Task> applyDesktop,
        bool nativeTraySupported = false)
    {
        _model = model;
        _defaultPaths = defaultPaths;
        _activePaths = activePaths;
        _application = application.Normalize();
        _desktop = desktop;
        _applyApplication = applyApplication;
        _applyDesktop = applyDesktop;
        _nativeTraySupported = nativeTraySupported;
        _model?.ConfigureNotificationPreferences(_application);
    }

    public static bool Handles(string command) => command is "prefs.get" or "prefs.save" or "prefs.measureCache" or
        "prefs.purgeCache" or "prefs.scheduleCacheMove" or "prefs.resetShortcuts" or "prefs.cancel";

    public bool AutomaticRepositoryRefresh => _application.AutomaticRepositoryRefresh;
    public bool ConfirmBeforeExit => _application.ConfirmBeforeExit;

    public object Snapshot() => new
    {
        revision = _revision,
        application = _application,
        desktop = _desktop,
        activeCacheDirectory = _activePaths.CacheDirectory,
        defaultCacheDirectory = _defaultPaths.CacheDirectory,
        pendingRestart = !PathsEqual(ApplicationPreferencesStore.ResolveCacheDirectory(_application, _defaultPaths.CacheDirectory), _activePaths.CacheDirectory),
        busy = _cacheCancellation is not null,
        cacheUsage = _cacheUsage is null ? null : new { _cacheUsage.Bytes, _cacheUsage.Files, _cacheUsage.Skipped, _cacheUsage.Summary },
        cacheStatus = _cacheStatus,
        themePresets = InterfaceThemeService.Presets.Select(theme => new { theme.Id, theme.Name, theme.Description }),
        workspacePresetNames = ApplicationPreferences.WorkspacePresetNames,
        modes = ProjectPresets.All.Select(preset => new { id = preset.Kind.ToString(), preset.Name, preset.Description }),
        shortcuts = ShortcutCatalog.Definitions,
        capabilities = new { nativeTray = _nativeTraySupported, closeToTray = _nativeTraySupported, startHidden = _nativeTraySupported }
    };

    public void Cancel() => _cacheCancellation?.Cancel();

    public async Task<object?> ExecuteAsync(JsonElement request)
    {
        string command = Text(request, "command");
        if (command == "prefs.get") return Snapshot();
        if (command == "prefs.cancel") { Cancel(); return new { message = "Cache operation cancellation requested." }; }
        if (_cacheCancellation is not null) throw new InvalidOperationException("A cache operation is already running.");
        switch (command)
        {
            case "prefs.save":
            {
                JsonElement app = request.TryGetProperty("application", out var suppliedApplication) ? suppliedApplication : default;
                JsonElement desktop = request.TryGetProperty("desktop", out var suppliedDesktop) ? suppliedDesktop : default;
                ApplicationPreferences application = MergeApplication(app);
                DesktopBehaviorPreferences behavior = MergeDesktop(desktop);
                await SaveAsync(application, behavior);
                return Snapshot();
            }
            case "prefs.resetShortcuts":
                await SaveAsync(_application with { KeyboardShortcuts = ShortcutCatalog.CreateDefaultBindings() }, _desktop);
                return Snapshot();
            case "prefs.scheduleCacheMove":
            {
                Confirm(request);
                RequireActiveRoot(request);
                string destination = ValidateCacheTarget(Text(request, "destination"), requireDedicatedDirectory: true);
                ApplicationCacheService.ValidateMove(_activePaths.CacheDirectory, destination);
                EnsureDedicatedDirectory(destination);
                await SaveAsync(_application with { CacheDirectory = destination, PendingCacheMoveSource = _activePaths.CacheDirectory }, _desktop);
                _cacheStatus = "Cache migration is saved for the next launch. Existing files remain in the active cache until then.";
                return Snapshot();
            }
            case "prefs.measureCache":
            case "prefs.purgeCache":
            {
                if (command == "prefs.purgeCache") { Confirm(request); RequireActiveRoot(request); }
                string root = ValidateCacheTarget(_activePaths.CacheDirectory, requireDedicatedDirectory: false);
                using CancellationTokenSource cancellation = new();
                _cacheCancellation = cancellation;
                try
                {
                    if (command == "prefs.purgeCache")
                    {
                        ApplicationCacheOperationResult result = await ApplicationCacheService.PurgeAsync(root, cancellation.Token);
                        _cacheStatus = result.Message;
                    }
                    _cacheUsage = await ApplicationCacheService.MeasureAsync(root, cancellation.Token);
                    if (command == "prefs.measureCache") _cacheStatus = "Measured the current active cache. Repository data was not scanned.";
                    return Snapshot();
                }
                catch (OperationCanceledException)
                {
                    _cacheStatus = "Cache operation cancelled. Completed removals, if any, are reflected in the next measurement.";
                    return Snapshot();
                }
                finally { _cacheCancellation = null; }
            }
            default: throw new ArgumentException("Unknown preferences command.");
        }
    }

    private async Task SaveAsync(ApplicationPreferences application, DesktopBehaviorPreferences desktop)
    {
        ApplicationPreferences previous = _application;
        if (!string.IsNullOrWhiteSpace(application.CacheDirectory)) EnsureDedicatedDirectory(application.CacheDirectory);
        try
        {
            await _applyApplication(application);
            if (desktop != _desktop) await _applyDesktop(desktop);
        }
        catch
        {
            try { await _applyApplication(previous); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            throw;
        }
        _application = application.Normalize();
        _desktop = desktop;
        _revision++;
        _model?.ConfigureNotificationPreferences(_application);
    }

    private ApplicationPreferences MergeApplication(JsonElement supplied)
    {
        if (supplied.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
            throw new ArgumentException("Application preferences must be an object.");
        string cache = String(supplied, "cacheDirectory", _application.CacheDirectory).Trim();
        if (!string.IsNullOrWhiteSpace(cache)) cache = ValidateCacheTarget(cache, requireDedicatedDirectory: true);
        string theme = String(supplied, "themePreset", _application.ThemePreset);
        if (!InterfaceThemeService.IsKnownPreset(theme)) throw new ArgumentException("Unknown interface color preset.");
        Dictionary<string, string> shortcuts = new(_application.KeyboardShortcuts, StringComparer.Ordinal);
        if (Try(supplied, "keyboardShortcuts", out var bindings))
        {
            if (bindings.ValueKind != JsonValueKind.Object) throw new ArgumentException("Keyboard shortcuts must be an object.");
            foreach (JsonProperty binding in bindings.EnumerateObject())
            {
                if (!ShortcutCatalog.Definitions.Any(definition => definition.Id == binding.Name) || binding.Value.ValueKind != JsonValueKind.String)
                    throw new ArgumentException("Unknown shortcut binding.");
                shortcuts[binding.Name] = binding.Value.GetString() ?? "";
            }
        }
        shortcuts = ValidateShortcuts(shortcuts);
        Dictionary<string, string> presets = new(_application.DefaultWorkspacePresetByMode ?? ApplicationPreferences.CreateDefaultWorkspacePresets(), StringComparer.Ordinal);
        if (Try(supplied, "defaultWorkspacePresetByMode", out var suppliedPresets))
        {
            if (suppliedPresets.ValueKind != JsonValueKind.Object) throw new ArgumentException("Workspace presets must be an object.");
            foreach (JsonProperty item in suppliedPresets.EnumerateObject())
            {
                string? preset = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
                if (!Enum.TryParse<ProjectPresetKind>(item.Name, ignoreCase: false, out var mode) || !Enum.IsDefined(mode) ||
                    preset is null || !ApplicationPreferences.WorkspacePresetNames.Contains(preset, StringComparer.Ordinal))
                    throw new ArgumentException("Unknown mode or default workspace preset.");
                presets[item.Name] = preset;
            }
        }
        return _application with
        {
            CacheDirectory = cache,
            PendingCacheMoveSource = PathsEqual(cache, _application.CacheDirectory) ? _application.PendingCacheMoveSource : "",
            ThemePreset = theme,
            ConfirmBeforeExit = Boolean(supplied, "confirmBeforeExit", _application.ConfirmBeforeExit),
            AutomaticRepositoryRefresh = Boolean(supplied, "automaticRepositoryRefresh", _application.AutomaticRepositoryRefresh),
            KeyboardShortcuts = shortcuts,
            DefaultWorkspacePresetByMode = presets,
            DefaultProjectNotificationsEnabled = Boolean(supplied, "defaultProjectNotificationsEnabled", _application.DefaultProjectNotificationsEnabled),
            NotifyOnFailures = Boolean(supplied, "notifyOnFailures", _application.NotifyOnFailures),
            NotifyOnWarnings = Boolean(supplied, "notifyOnWarnings", _application.NotifyOnWarnings),
            NotifyOnSuccesses = Boolean(supplied, "notifyOnSuccesses", _application.NotifyOnSuccesses)
        };
    }

    private DesktopBehaviorPreferences MergeDesktop(JsonElement supplied)
    {
        if (supplied.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) throw new ArgumentException("Desktop preferences must be an object.");
        bool tray = Boolean(supplied, "showTrayIcon", _desktop.ShowTrayIcon);
        bool launch = Boolean(supplied, "launchAtLogin", _desktop.LaunchAtLogin);
        bool hidden = Boolean(supplied, "startHiddenAtLogin", _desktop.StartHiddenAtLogin);
        bool close = Boolean(supplied, "closeToTray", _desktop.CloseToTray);
        if (!_nativeTraySupported && (tray && !_desktop.ShowTrayIcon || hidden && !_desktop.StartHiddenAtLogin || close && !_desktop.CloseToTray))
            throw new InvalidOperationException("The native shell does not implement tray residence or hidden startup yet. These options cannot be enabled here.");
        return new(launch, tray && launch && hidden, tray && close, tray);
    }

    public static Dictionary<string, string> ValidateShortcuts(IReadOnlyDictionary<string, string> bindings)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShortcutDefinition definition in ShortcutCatalog.Definitions)
        {
            string value = bindings.TryGetValue(definition.Id, out string? gesture) ? gesture : definition.DefaultGesture;
            if (!ShortcutCatalog.TryParse(value, out _)) throw new ArgumentException($"Invalid shortcut for {definition.Name}.");
            string normalized = ShortcutCatalog.NormalizeGesture(value);
            if (owners.TryGetValue(normalized, out string? owner)) throw new ArgumentException($"Shortcut {normalized} is already assigned to {owner}.");
            owners[normalized] = definition.Name;
            result[definition.Id] = normalized;
        }
        return result;
    }

    private string ValidateCacheTarget(string target, bool requireDedicatedDirectory)
    {
        string root = ApplicationCacheService.NormalizeSafeRoot(target);
        for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A managed cache cannot be a filesystem link or have a linked parent.");
        IEnumerable<string> projects = _model?.Projects.Select(project => project.RootPath) ?? [];
        foreach (string project in projects)
            if (Overlaps(root, project)) throw new IOException("A cache must be separate from every project directory.");
        foreach (string protectedRoot in new[] { _activePaths.ConfigurationDirectory, _activePaths.BackupDirectory, _activePaths.VpnDirectory,
                     _activePaths.TeamChatDirectory, _activePaths.ManagedSyncthingDirectory, _activePaths.LfsManagementDirectory, _activePaths.RemoteBuildDirectory })
            if (Overlaps(root, protectedRoot)) throw new IOException("The selected cache overlaps application configuration or persistent project data.");
        if (PathsEqual(root, _activePaths.DataDirectory) || Contains(root, _activePaths.DataDirectory))
            throw new IOException("The application data directory cannot be used as a cache root.");
        if (requireDedicatedDirectory && !PathsEqual(root, _activePaths.CacheDirectory) && !PathsEqual(root, _defaultPaths.CacheDirectory) &&
            Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !File.Exists(Path.Combine(root, CacheMarker)))
            throw new IOException("Choose an empty folder or a dedicated CyRevision cache folder, not a folder containing other files.");
        return root;
    }

    private void EnsureDedicatedDirectory(string root)
    {
        if (PathsEqual(root, _activePaths.CacheDirectory) || PathsEqual(root, _defaultPaths.CacheDirectory)) return;
        Directory.CreateDirectory(root);
        string marker = Path.Combine(root, CacheMarker);
        if (!File.Exists(marker)) File.WriteAllText(marker, "CyRevision generated cache\n");
    }
    private void RequireActiveRoot(JsonElement request)
    {
        if (!PathsEqual(Text(request, "expectedRoot"), _activePaths.CacheDirectory))
            throw new InvalidOperationException("The active cache changed. Refresh preferences and confirm its exact path again.");
    }
    private static void Confirm(JsonElement request)
    {
        if (!request.TryGetProperty("confirmed", out var value) || value.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Confirm this cache operation first.");
    }
    private static bool PathsEqual(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(a) ? "." : a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(b) ? "." : b)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Contains(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Overlaps(string first, string second) => PathsEqual(first, second) || Contains(first, second) || Contains(second, first);
    private static bool Try(JsonElement element, string name, out JsonElement value) { value = default; return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value); }
    private static string Text(JsonElement request, string name) => String(request, name, "");
    private static string String(JsonElement element, string name, string fallback) => !Try(element, name, out var value) ? fallback :
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : throw new ArgumentException($"{name} must be text.");
    private static bool Boolean(JsonElement element, string name, bool fallback) => !Try(element, name, out var value) ? fallback :
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ArgumentException($"{name} must be a boolean.");
}
