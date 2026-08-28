using CyRevision.Desktop.SystemIntegration;

namespace CyRevision.Core.Tests;

public sealed class ApplicationPreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-preferences-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Default_shortcuts_are_valid_and_unique()
    {
        ApplicationPreferences preferences = ApplicationPreferences.Default.Normalize();

        Assert.Equal(ShortcutCatalog.Definitions.Count, preferences.KeyboardShortcuts.Count);
        Assert.Equal(
            preferences.KeyboardShortcuts.Count,
            preferences.KeyboardShortcuts.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(preferences.KeyboardShortcuts.Values, value => Assert.True(ShortcutCatalog.TryParse(value, out _)));
    }

    [Fact]
    public void Save_and_load_preserve_application_preferences()
    {
        ApplicationPreferencesStore store = new(_root);
        Dictionary<string, string> shortcuts = ShortcutCatalog.CreateDefaultBindings();
        shortcuts["refresh"] = "Ctrl+R";
        ApplicationPreferences expected = new(
            ApplicationPreferences.CurrentSchemaVersion,
            Path.Combine(_root, "cache"),
            "midnight-blue",
            ConfirmBeforeExit: false,
            AutomaticRepositoryRefresh: false,
            shortcuts);

        store.Save(expected);
        ApplicationPreferences actual = store.Load();

        Assert.Equal(expected.CacheDirectory, actual.CacheDirectory);
        Assert.Equal("midnight-blue", actual.ThemePreset);
        Assert.False(actual.ConfirmBeforeExit);
        Assert.False(actual.AutomaticRepositoryRefresh);
        Assert.Equal("Ctrl+R", actual.KeyboardShortcuts["refresh"]);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_safe_defaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "application-preferences.json"), "{broken");

        ApplicationPreferences actual = new ApplicationPreferencesStore(_root).Load();

        Assert.Equal(InterfaceThemeService.DefaultPresetId, actual.ThemePreset);
        Assert.True(actual.ConfirmBeforeExit);
        Assert.True(actual.AutomaticRepositoryRefresh);
    }

    [Fact]
    public void Normalize_repairs_unknown_theme_and_invalid_shortcut()
    {
        Dictionary<string, string> shortcuts = ShortcutCatalog.CreateDefaultBindings();
        shortcuts["refresh"] = "not-a-gesture";
        ApplicationPreferences preferences = new(
            0,
            "  cache  ",
            "unknown",
            true,
            true,
            shortcuts);

        ApplicationPreferences normalized = preferences.Normalize();

        Assert.Equal(ApplicationPreferences.CurrentSchemaVersion, normalized.SchemaVersion);
        Assert.Equal("cache", normalized.CacheDirectory);
        Assert.Equal(InterfaceThemeService.DefaultPresetId, normalized.ThemePreset);
        Assert.Equal("F5", normalized.KeyboardShortcuts["refresh"]);
    }

    [Fact]
    public void Resolve_cache_directory_uses_custom_full_path_or_default()
    {
        string fallback = Path.Combine(_root, "default");
        ApplicationPreferences custom = ApplicationPreferences.Default with
        {
            CacheDirectory = Path.Combine(_root, "custom")
        };

        Assert.Equal(Path.GetFullPath(custom.CacheDirectory), ApplicationPreferencesStore.ResolveCacheDirectory(custom, fallback));
        Assert.Equal(fallback, ApplicationPreferencesStore.ResolveCacheDirectory(ApplicationPreferences.Default, fallback));
    }

    [Fact]
    public void Normalize_fills_workspace_defaults_and_preserves_notification_policy()
    {
        ApplicationPreferences preferences = ApplicationPreferences.Default with
        {
            DefaultWorkspacePresetByMode = new Dictionary<string, string>
            {
                ["GitOnly"] = "Developer",
                ["SyncOnly"] = "unknown"
            },
            DefaultProjectNotificationsEnabled = false,
            NotifyOnWarnings = false
        };

        ApplicationPreferences normalized = preferences.Normalize();

        Assert.Equal("Developer", normalized.WorkspacePresetFor(CyRevision.Core.Configuration.ProjectPresetKind.GitOnly));
        Assert.Equal("Team & network", normalized.WorkspacePresetFor(CyRevision.Core.Configuration.ProjectPresetKind.SyncOnly));
        Assert.False(normalized.DefaultProjectNotificationsEnabled);
        Assert.False(normalized.NotifyOnWarnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}