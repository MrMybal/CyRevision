using Avalonia.Input;
using CyRevision.Core.Configuration;

namespace CyRevision.Desktop.SystemIntegration;

internal sealed record ApplicationPreferences(
    int SchemaVersion,
    string CacheDirectory,
    string ThemePreset,
    bool ConfirmBeforeExit,
    bool AutomaticRepositoryRefresh,
    IReadOnlyDictionary<string, string> KeyboardShortcuts,
    string PendingCacheMoveSource = "",
    IReadOnlyDictionary<string, string>? DefaultWorkspacePresetByMode = null,
    bool DefaultProjectNotificationsEnabled = true,
    bool NotifyOnFailures = true,
    bool NotifyOnWarnings = true,
    bool NotifyOnSuccesses = true)
{
    public const int CurrentSchemaVersion = 2;

    public static IReadOnlyList<string> WorkspacePresetNames { get; } =
    [
        "Full workspace",
        "Git essentials",
        "Developer",
        "Unreal production",
        "Game engines",
        "Team & network",
        "Minimal"
    ];

    public static ApplicationPreferences Default { get; } = new(
        CurrentSchemaVersion,
        string.Empty,
        InterfaceThemeService.DefaultPresetId,
        ConfirmBeforeExit: true,
        AutomaticRepositoryRefresh: true,
        ShortcutCatalog.CreateDefaultBindings(),
        DefaultWorkspacePresetByMode: CreateDefaultWorkspacePresets());

    public string WorkspacePresetFor(ProjectPresetKind mode)
    {
        IReadOnlyDictionary<string, string> presets =
            DefaultWorkspacePresetByMode ?? CreateDefaultWorkspacePresets();
        return presets.TryGetValue(mode.ToString(), out string? preset) &&
               WorkspacePresetNames.Contains(preset, StringComparer.Ordinal)
            ? preset
            : CreateDefaultWorkspacePresets()[mode.ToString()];
    }

    public ApplicationPreferences Normalize()
    {
        Dictionary<string, string> shortcuts = ShortcutCatalog.CreateDefaultBindings();
        foreach ((string id, string gesture) in KeyboardShortcuts ?? new Dictionary<string, string>())
        {
            if (ShortcutCatalog.Definitions.Any(definition => definition.Id == id) &&
                ShortcutCatalog.TryParse(gesture, out _))
            {
                shortcuts[id] = ShortcutCatalog.NormalizeGesture(gesture);
            }
        }

        Dictionary<string, string> workspacePresets = CreateDefaultWorkspacePresets();
        foreach ((string mode, string preset) in DefaultWorkspacePresetByMode ?? new Dictionary<string, string>())
        {
            if (Enum.TryParse(mode, ignoreCase: false, out ProjectPresetKind parsedMode) &&
                WorkspacePresetNames.Contains(preset, StringComparer.Ordinal))
            {
                workspacePresets[parsedMode.ToString()] = preset;
            }
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            CacheDirectory = CacheDirectory?.Trim() ?? string.Empty,
            PendingCacheMoveSource = PendingCacheMoveSource?.Trim() ?? string.Empty,
            ThemePreset = InterfaceThemeService.IsKnownPreset(ThemePreset)
                ? ThemePreset
                : InterfaceThemeService.DefaultPresetId,
            KeyboardShortcuts = shortcuts,
            DefaultWorkspacePresetByMode = workspacePresets
        };
    }

    public static Dictionary<string, string> CreateDefaultWorkspacePresets() => new(StringComparer.Ordinal)
    {
        [ProjectPresetKind.GitOnly.ToString()] = "Git essentials",
        [ProjectPresetKind.GitWithPeerSync.ToString()] = "Full workspace",
        [ProjectPresetKind.SyncOnly.ToString()] = "Team & network",
        [ProjectPresetKind.SyncWithVersions.ToString()] = "Team & network",
        [ProjectPresetKind.SyncWithCommits.ToString()] = "Team & network",
        [ProjectPresetKind.BackupOnly.ToString()] = "Full workspace",
        [ProjectPresetKind.Custom.ToString()] = "Full workspace"
    };
}

internal sealed record ShortcutDefinition(string Id, string Name, string Description, string DefaultGesture);

internal static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> Definitions { get; } =
    [
        new("preferences", "Open preferences", "Open Edit > Preferences.", "Ctrl+,"),
        new("add-folder", "Add folder", "Add a regular project folder.", "Ctrl+Shift+O"),
        new("global-search", "Find in files", "Focus the global code search.", "Ctrl+Shift+F"),
        new("refresh", "Refresh project", "Refresh tracked and untracked changes.", "F5"),
        new("open-repository", "Open repository", "Add an existing local repository.", "Ctrl+O"),
        new("clone-repository", "Clone repository", "Clone a remote repository to a local folder.", "Ctrl+Shift+N"),
        new("create-repository", "Create repository", "Create a new local repository.", "Ctrl+N"),
        new("changes", "Show changes", "Open the Git changes workspace.", "Ctrl+1"),
        new("history", "Show history", "Open the Git history workspace.", "Ctrl+2"),
        new("branches", "Show branches", "Open the branch workspace.", "Ctrl+3"),
        new("pull-requests", "Show pull requests", "Open the pull request workspace.", "Ctrl+4"),
        new("fetch", "Fetch", "Fetch remote references.", "Ctrl+Alt+F"),
        new("pull", "Pull", "Pull the current branch.", "Ctrl+Alt+L"),
        new("push", "Push", "Push the current branch.", "Ctrl+Alt+P"),
        new("exit", "Quit CyRevision", "Ask to close CyRevision.", "Ctrl+Q")
    ];

    public static Dictionary<string, string> CreateDefaultBindings() =>
        Definitions.ToDictionary(definition => definition.Id, definition => definition.DefaultGesture, StringComparer.Ordinal);

    public static bool TryParse(string? value, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            gesture = KeyGesture.Parse(value.Trim());
            return gesture is not null;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    public static string NormalizeGesture(string value) =>
        TryParse(value, out KeyGesture? gesture) ? gesture!.ToString() : value.Trim();
}