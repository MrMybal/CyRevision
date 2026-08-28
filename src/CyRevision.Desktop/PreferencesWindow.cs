using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CyRevision.Core.Configuration;
using CyRevision.Desktop.SystemIntegration;

namespace CyRevision.Desktop;

internal sealed record PreferencesDialogResult(
    ApplicationPreferences Application,
    DesktopBehaviorPreferences Desktop);

internal sealed class PreferencesWindow : Window
{
    private readonly ApplicationPreferences _initialApplication;
    private readonly DesktopBehaviorPreferences _initialDesktop;
    private readonly string _defaultCacheDirectory;
    private readonly string _activeCacheDirectory;
    private string _pendingCacheMoveSource;
    private bool _preferencesSaved;
    private readonly TextBox _cacheDirectory;
    private readonly ComboBox _themePreset;
    private readonly TextBlock _themeDescription;
    private readonly Border _themePreview;
    private readonly CheckBox _confirmBeforeExit;
    private readonly CheckBox _automaticRepositoryRefresh;
    private readonly CheckBox _launchAtLogin;
    private readonly CheckBox _startHiddenAtLogin;
    private readonly CheckBox _closeToTray;
    private readonly CheckBox _showTrayIcon;
    private readonly CheckBox _defaultProjectNotificationsEnabled;
    private readonly CheckBox _notifyOnFailures;
    private readonly CheckBox _notifyOnWarnings;
    private readonly CheckBox _notifyOnSuccesses;
    private readonly TextBlock _cacheUsage;
    private readonly TextBlock _cacheStatus;
    private readonly Button _refreshCacheButton;
    private readonly Button _purgeCacheButton;
    private readonly Button _moveCacheButton;
    private readonly TextBlock _validation;
    private readonly Dictionary<string, TextBox> _shortcutEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<ProjectPresetKind, ComboBox> _workspacePresetEditors = [];

    public PreferencesWindow(
        ApplicationPreferences application,
        DesktopBehaviorPreferences desktop,
        ApplicationPaths defaultPaths,
        string activeCacheDirectory)
    {
        _initialApplication = application.Normalize();
        _initialDesktop = desktop;
        _defaultCacheDirectory = defaultPaths.CacheDirectory;
        _activeCacheDirectory = ApplicationCacheService.NormalizeSafeRoot(activeCacheDirectory);
        _pendingCacheMoveSource = _initialApplication.PendingCacheMoveSource;

        Title = "CyRevision preferences";
        Width = 940;
        Height = 740;
        MinWidth = 760;
        MinHeight = 600;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#1E1F22");

        _cacheDirectory = new TextBox
        {
            Text = _initialApplication.CacheDirectory,
            PlaceholderText = _defaultCacheDirectory,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _themePreset = new ComboBox
        {
            ItemsSource = InterfaceThemeService.Presets,
            SelectedItem = InterfaceThemeService.GetPreset(_initialApplication.ThemePreset),
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _themeDescription = MutedText(string.Empty);
        _themePreview = new Border
        {
            Height = 72,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12)
        };
        _confirmBeforeExit = Check("Ask for confirmation before quitting CyRevision", _initialApplication.ConfirmBeforeExit);
        _automaticRepositoryRefresh = Check("Refresh Git changes automatically when project files change", _initialApplication.AutomaticRepositoryRefresh);
        _launchAtLogin = Check("Launch at system login", _initialDesktop.LaunchAtLogin);
        _startHiddenAtLogin = Check("Start hidden in the system tray", _initialDesktop.StartHiddenAtLogin);
        _closeToTray = Check("Close the main window to the system tray", _initialDesktop.CloseToTray);
        _showTrayIcon = Check("Show the system tray icon", _initialDesktop.ShowTrayIcon);
        _defaultProjectNotificationsEnabled = Check(
            "Enable project notifications when a new project is added",
            _initialApplication.DefaultProjectNotificationsEnabled);
        _notifyOnFailures = Check("Notify failed operations", _initialApplication.NotifyOnFailures);
        _notifyOnWarnings = Check("Notify cancelled or warning-like operations", _initialApplication.NotifyOnWarnings);
        _notifyOnSuccesses = Check("Notify successful important operations", _initialApplication.NotifyOnSuccesses);
        _cacheUsage = new TextBlock
        {
            Text = "Measuring current cache…",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _cacheStatus = MutedText($"Active cache: {_activeCacheDirectory}");
        _refreshCacheButton = SecondaryButton("Refresh size");
        _purgeCacheButton = DangerButton("Purge cache…");
        _moveCacheButton = SecondaryButton("Move cache…");
        _refreshCacheButton.Click += async (_, _) => await RefreshCacheUsageAsync();
        _purgeCacheButton.Click += async (_, _) => await PurgeCacheAsync();
        _moveCacheButton.Click += async (_, _) => await ScheduleCacheMoveAsync();
        _validation = new TextBlock
        {
            Foreground = Brush("#FFB86C"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        TabControl tabs = new()
        {
            Items =
            {
                new TabItem { Header = "General", Content = BuildGeneralTab() },
                new TabItem { Header = "Project defaults", Content = BuildProjectDefaultsTab() },
                new TabItem { Header = "Notifications", Content = BuildNotificationsTab() },
                new TabItem { Header = "Storage", Content = BuildStorageTab() },
                new TabItem { Header = "Appearance", Content = BuildAppearanceTab() },
                new TabItem { Header = "Keyboard shortcuts", Content = BuildShortcutsTab() }
            }
        };

        Button cancel = SecondaryButton("Cancel");
        Button save = PrimaryButton("Save preferences");
        cancel.Click += (_, _) => Close(null);
        save.Click += OnSaveClick;

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save }
        };
        Grid footer = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(_validation);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);

        Grid root = new()
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12
        };
        root.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = "Preferences", FontSize = 21, FontWeight = FontWeight.SemiBold },
                MutedText("Application behavior, project defaults, cache, colors and keyboard shortcuts are saved for this computer.")
            }
        });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        _cacheDirectory.TextChanged += (_, _) =>
        {
            if (!string.Equals(
                    _cacheDirectory.Text?.Trim(),
                    ApplicationPreferencesStore.ResolveCacheDirectory(
                        _initialApplication,
                        _defaultCacheDirectory),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                _pendingCacheMoveSource = string.Empty;
            }
        };
        _themePreset.SelectionChanged += (_, _) => UpdateThemePreview();
        _showTrayIcon.IsCheckedChanged += (_, _) => UpdateDesktopDependencies();
        _launchAtLogin.IsCheckedChanged += (_, _) => UpdateDesktopDependencies();
        Opened += async (_, _) => await RefreshCacheUsageAsync();
        Closed += (_, _) =>
        {
            if (!_preferencesSaved)
                InterfaceThemeService.Apply(_initialApplication.ThemePreset);
        };
        UpdateThemePreview();
        UpdateDesktopDependencies();
    }

    private Control BuildGeneralTab()
    {
        StackPanel stack = PageStack();
        stack.Children.Add(SectionTitle("Application"));
        stack.Children.Add(_confirmBeforeExit);
        stack.Children.Add(_automaticRepositoryRefresh);
        stack.Children.Add(MutedText("Automatic refresh is debounced and ignores generated folders such as Binaries, Intermediate, Saved and DerivedDataCache."));
        stack.Children.Add(SectionTitle("Desktop integration", 14));
        stack.Children.Add(_showTrayIcon);
        stack.Children.Add(_closeToTray);
        stack.Children.Add(_launchAtLogin);
        stack.Children.Add(_startHiddenAtLogin);
        stack.Children.Add(MutedText("Disabling the tray icon also disables close-to-tray and hidden startup so the app cannot become unreachable."));
        return Scroll(stack);
    }

    private Control BuildProjectDefaultsTab()
    {
        StackPanel stack = PageStack();
        stack.Children.Add(SectionTitle("Default workspace layout by project mode"));
        stack.Children.Add(MutedText(
            "These presets are used only the first time a project is added. Each project can still customize its visible tabs afterwards."));

        foreach (ProjectPresetKind mode in Enum.GetValues<ProjectPresetKind>())
        {
            string title = ProjectPresets.All.FirstOrDefault(preset => preset.Kind == mode)?.Name
                           ?? "Plugin / custom mode";
            ComboBox selector = new()
            {
                ItemsSource = ApplicationPreferences.WorkspacePresetNames,
                SelectedItem = _initialApplication.WorkspacePresetFor(mode),
                MinWidth = 230,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _workspacePresetEditors[mode] = selector;
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("220,*"),
                ColumnSpacing = 14,
                MinHeight = 42
            };
            row.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(selector, 1);
            row.Children.Add(selector);
            stack.Children.Add(row);
        }

        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = Brush("#202633"),
            Child = new TextBlock
            {
                Text = "Plugin-provided modes use the Plugin / custom default, then expose the tabs declared by the active project plugin.",
                Foreground = Brush("#9CC7FF"),
                TextWrapping = TextWrapping.Wrap
            }
        });
        return Scroll(stack);
    }

    private Control BuildNotificationsTab()
    {
        StackPanel stack = PageStack();
        stack.Children.Add(SectionTitle("New projects"));
        stack.Children.Add(_defaultProjectNotificationsEnabled);
        stack.Children.Add(MutedText("Existing projects keep their own notification switch in Overview > Notifications."));
        stack.Children.Add(SectionTitle("Operation notifications", 14));
        stack.Children.Add(_notifyOnFailures);
        stack.Children.Add(_notifyOnWarnings);
        stack.Children.Add(_notifyOnSuccesses);
        stack.Children.Add(MutedText(
            "The activity center always keeps running tasks. These options only control which completed operations are promoted as project notifications."));
        return Scroll(stack);
    }

    private Control BuildStorageTab()
    {
        Button browse = SecondaryButton("Browse…");
        Button useDefault = SecondaryButton("Use default");
        browse.Click += async (_, _) => await PickCacheDirectoryAsync();
        useDefault.Click += (_, _) =>
        {
            _cacheDirectory.Text = string.Empty;
            _pendingCacheMoveSource = string.Empty;
            _cacheStatus.Text = "The system default will be used next launch; current cache files are left untouched.";
        };

        Grid cacheRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        cacheRow.Children.Add(_cacheDirectory);
        Grid.SetColumn(browse, 1);
        cacheRow.Children.Add(browse);
        Grid.SetColumn(useDefault, 2);
        cacheRow.Children.Add(useDefault);

        StackPanel cacheActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _refreshCacheButton, _purgeCacheButton, _moveCacheButton }
        };

        StackPanel stack = PageStack();
        stack.Children.Add(SectionTitle("Current cache usage"));
        stack.Children.Add(_cacheUsage);
        stack.Children.Add(_cacheStatus);
        stack.Children.Add(cacheActions);
        stack.Children.Add(SectionTitle("Cache location", 14));
        stack.Children.Add(MutedText("Git/LFS inspections, update downloads and temporary previews use this folder. Leave it empty to use the system default."));
        stack.Children.Add(cacheRow);
        stack.Children.Add(MutedText($"Default: {_defaultCacheDirectory}"));
        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = Brush("#202633"),
            Child = new TextBlock
            {
                Text = "Changing the path alone leaves existing cache files untouched. “Move cache…” schedules a verified migration before services start on the next launch.",
                Foreground = Brush("#9CC7FF"),
                TextWrapping = TextWrapping.Wrap
            }
        });
        return Scroll(stack);
    }

    private Control BuildAppearanceTab()
    {
        Button apply = PrimaryButton("Apply now");
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Click += (_, _) =>
        {
            InterfaceThemePreset preset = SelectedThemePreset();
            InterfaceThemeService.Apply(preset.Id);
            _validation.Text = $"Theme “{preset.Name}” applied. Save preferences to keep it after closing.";
        };

        StackPanel stack = PageStack();
        stack.Children.Add(SectionTitle("Interface color preset"));
        stack.Children.Add(_themePreset);
        stack.Children.Add(_themeDescription);
        stack.Children.Add(_themePreview);
        stack.Children.Add(apply);
        stack.Children.Add(MutedText("Apply previews the theme immediately. Cancel restores the previous theme; Save keeps the selection for future launches."));
        return Scroll(stack);
    }

    private Control BuildShortcutsTab()
    {
        StackPanel rows = new() { Spacing = 5 };
        foreach (ShortcutDefinition definition in ShortcutCatalog.Definitions)
        {
            TextBox editor = new()
            {
                Text = _initialApplication.KeyboardShortcuts.TryGetValue(definition.Id, out string? gesture)
                    ? gesture
                    : definition.DefaultGesture,
                MinWidth = 150
            };
            _shortcutEditors[definition.Id] = editor;
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("180,*,165"),
                ColumnSpacing = 10,
                MinHeight = 42
            };
            row.Children.Add(new TextBlock
            {
                Text = definition.Name,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            TextBlock description = MutedText(definition.Description);
            description.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(description, 1);
            row.Children.Add(description);
            Grid.SetColumn(editor, 2);
            row.Children.Add(editor);
            rows.Children.Add(row);
        }

        Button reset = SecondaryButton("Reset all shortcuts");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Click += (_, _) =>
        {
            foreach (ShortcutDefinition definition in ShortcutCatalog.Definitions)
                _shortcutEditors[definition.Id].Text = definition.DefaultGesture;
        };

        StackPanel stack = PageStack();
        stack.Children.Add(SectionTitle("Keyboard shortcuts"));
        stack.Children.Add(MutedText("Use forms such as Ctrl+O, Ctrl+Shift+F, Alt+1 or F5. Each shortcut must be valid and unique."));
        stack.Children.Add(rows);
        stack.Children.Add(reset);
        return Scroll(stack);
    }

    private async Task PickCacheDirectoryAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the CyRevision cache folder",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            _cacheDirectory.Text = folders[0].Path.LocalPath;
            _pendingCacheMoveSource = string.Empty;
            _cacheStatus.Text = "The new location will be used next launch; current cache files are left untouched.";
        }
    }

    private async Task RefreshCacheUsageAsync()
    {
        SetCacheButtonsEnabled(false);
        _cacheUsage.Text = "Measuring current cache…";
        try
        {
            ApplicationCacheUsage usage = await ApplicationCacheService.MeasureAsync(_activeCacheDirectory);
            _cacheUsage.Text = usage.Summary;
            _cacheStatus.Text = $"Active cache: {_activeCacheDirectory}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _cacheUsage.Text = "Cache size unavailable";
            _cacheStatus.Text = exception.Message;
        }
        finally
        {
            SetCacheButtonsEnabled(true);
        }
    }

    private async Task PurgeCacheAsync()
    {
        bool confirmed = await ShowConfirmationAsync(
            "Purge the CyRevision cache?",
            $"Cached previews, Git/LFS inspections and temporary downloads under this exact folder will be removed:{Environment.NewLine}{Environment.NewLine}{_activeCacheDirectory}{Environment.NewLine}{Environment.NewLine}Repository files, configuration and project data are not touched.",
            "Purge cache");
        if (!confirmed) return;

        SetCacheButtonsEnabled(false);
        _cacheUsage.Text = "Purging cache…";
        try
        {
            ApplicationCacheOperationResult result = await ApplicationCacheService.PurgeAsync(_activeCacheDirectory);
            _cacheStatus.Text = result.Message;
            await RefreshCacheUsageAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _cacheStatus.Text = $"Cache purge failed: {exception.Message}";
        }
        finally
        {
            SetCacheButtonsEnabled(true);
        }
    }

    private async Task ScheduleCacheMoveAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the new CyRevision cache folder",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        string destination;
        try
        {
            destination = ApplicationCacheService.NormalizeSafeRoot(folders[0].Path.LocalPath);
            ApplicationCacheService.ValidateMove(_activeCacheDirectory, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _cacheStatus.Text = exception.Message;
            return;
        }

        bool confirmed = await ShowConfirmationAsync(
            "Move the CyRevision cache?",
            $"At the next launch, CyRevision will copy and verify the current cache before removing its old files.{Environment.NewLine}{Environment.NewLine}From:{Environment.NewLine}{_activeCacheDirectory}{Environment.NewLine}{Environment.NewLine}To:{Environment.NewLine}{destination}",
            "Schedule move");
        if (!confirmed) return;

        _cacheDirectory.Text = destination;
        _pendingCacheMoveSource = _activeCacheDirectory;
        _cacheStatus.Text = "Cache move scheduled for the next launch. Save preferences to keep this change.";
    }

    private void SetCacheButtonsEnabled(bool enabled)
    {
        _refreshCacheButton.IsEnabled = enabled;
        _purgeCacheButton.IsEnabled = enabled;
        _moveCacheButton.IsEnabled = enabled;
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#25262A")
        };
        Button cancel = SecondaryButton("Cancel");
        Button confirm = DangerButton(confirmText);
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm }
                }
            }
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        Dictionary<string, string> shortcuts = new(StringComparer.Ordinal);
        Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShortcutDefinition definition in ShortcutCatalog.Definitions)
        {
            string value = _shortcutEditors[definition.Id].Text?.Trim() ?? string.Empty;
            if (!ShortcutCatalog.TryParse(value, out _))
            {
                _validation.Text = $"Invalid shortcut for “{definition.Name}”: {value}";
                return;
            }

            string normalized = ShortcutCatalog.NormalizeGesture(value);
            if (owners.TryGetValue(normalized, out string? owner))
            {
                _validation.Text = $"Shortcut {normalized} is already assigned to {owner}.";
                return;
            }

            owners[normalized] = definition.Name;
            shortcuts[definition.Id] = normalized;
        }

        string cacheDirectory = _cacheDirectory.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cacheDirectory))
        {
            try
            {
                cacheDirectory = ApplicationCacheService.NormalizeSafeRoot(cacheDirectory);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                _validation.Text = $"The cache folder path is not valid: {exception.Message}";
                return;
            }
        }

        Dictionary<string, string> workspacePresets = [];
        foreach ((ProjectPresetKind mode, ComboBox selector) in _workspacePresetEditors)
        {
            workspacePresets[mode.ToString()] =
                selector.SelectedItem as string ?? ApplicationPreferences.Default.WorkspacePresetFor(mode);
        }

        InterfaceThemePreset preset = SelectedThemePreset();
        bool showTray = _showTrayIcon.IsChecked == true;
        ApplicationPreferences application = new(
            ApplicationPreferences.CurrentSchemaVersion,
            cacheDirectory,
            preset.Id,
            _confirmBeforeExit.IsChecked == true,
            _automaticRepositoryRefresh.IsChecked == true,
            shortcuts,
            _pendingCacheMoveSource,
            workspacePresets,
            _defaultProjectNotificationsEnabled.IsChecked == true,
            _notifyOnFailures.IsChecked == true,
            _notifyOnWarnings.IsChecked == true,
            _notifyOnSuccesses.IsChecked == true);
        DesktopBehaviorPreferences desktop = new(
            LaunchAtLogin: _launchAtLogin.IsChecked == true,
            StartHiddenAtLogin: showTray && _launchAtLogin.IsChecked == true && _startHiddenAtLogin.IsChecked == true,
            CloseToTray: showTray && _closeToTray.IsChecked == true,
            ShowTrayIcon: showTray);
        _preferencesSaved = true;
        Close(new PreferencesDialogResult(application, desktop));
    }

    private void UpdateDesktopDependencies()
    {
        bool showTray = _showTrayIcon.IsChecked == true;
        _closeToTray.IsEnabled = showTray;
        _launchAtLogin.IsEnabled = true;
        _startHiddenAtLogin.IsEnabled = showTray && _launchAtLogin.IsChecked == true;
    }

    private void UpdateThemePreview()
    {
        InterfaceThemePreset preset = SelectedThemePreset();
        _themeDescription.Text = preset.Description;
        _themePreview.Background = Brush(preset.Surface);
        _themePreview.BorderBrush = Brush(preset.Border);
        _themePreview.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Swatch(preset.Background), Swatch(preset.Surface), Swatch(preset.Card),
                Swatch(preset.Accent), Swatch(preset.AccentBright), Swatch(preset.ForegroundStrong)
            }
        };
    }

    private InterfaceThemePreset SelectedThemePreset() =>
        _themePreset.SelectedItem as InterfaceThemePreset
        ?? InterfaceThemeService.GetPreset(_initialApplication.ThemePreset);

    private static Border Swatch(string color) => new()
    {
        Width = 42,
        Height = 42,
        CornerRadius = new CornerRadius(5),
        Background = Brush(color),
        BorderBrush = Brush("#69717C"),
        BorderThickness = new Thickness(1)
    };

    private static StackPanel PageStack() => new()
    {
        Margin = new Thickness(16),
        Spacing = 9
    };

    private static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

    private static TextBlock SectionTitle(string text, double topMargin = 0) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, topMargin, 0, 2)
    };

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = Brush("#9B9DA3"),
        TextWrapping = TextWrapping.Wrap,
        FontSize = 10.5
    };

    private static CheckBox Check(string text, bool value) => new()
    {
        Content = text,
        IsChecked = value
    };

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Background = Brush("#3574F0"),
        Foreground = Brushes.White,
        Padding = new Thickness(12, 6),
        CornerRadius = new CornerRadius(5),
        FontWeight = FontWeight.SemiBold
    };

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        Background = Brush("#393B40"),
        Foreground = Brush("#DFE1E5"),
        Padding = new Thickness(10, 6),
        CornerRadius = new CornerRadius(5)
    };

    private static Button DangerButton(string text) => new()
    {
        Content = text,
        Background = Brush("#5B2A33"),
        Foreground = Brush("#FFB1BE"),
        Padding = new Thickness(10, 6),
        CornerRadius = new CornerRadius(5),
        FontWeight = FontWeight.SemiBold
    };

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}