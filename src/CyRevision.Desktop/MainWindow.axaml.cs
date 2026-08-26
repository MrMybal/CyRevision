using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CyRevision.Code;
using CyRevision.Desktop.Controls;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.SystemIntegration;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;
using CyRevision.Sync;

namespace CyRevision.Desktop;

internal enum WorkspaceCategory
{
    Overview,
    Git,
    Code,
    Backup,
    Sync,
    PluginMode,
    Network,
    Extensions
}

internal sealed record WorkspaceTabVisibilityPreset(
    string Name,
    string Description,
    string BestFor,
    string IncludedTools,
    IReadOnlySet<string>? VisibleTabs,
    bool IsCustom = false)
{
    public string TabCountLabel => VisibleTabs is null ? "Manual selection" : $"{VisibleTabs.Count} tabs";
}

internal sealed class WorkspaceTabVisibilityGroup : INotifyPropertyChanged
{
    public WorkspaceTabVisibilityGroup(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public ObservableCollection<WorkspaceTabVisibilityItem> Items { get; } = [];

    public string VisibleSummary
    {
        get
        {
            int available = Items.Count(item => item.IsAvailableInMode);
            int visible = Items.Count(item => item.IsAvailableInMode && item.IsVisible);
            return $"{visible} / {available} visible";
        }
    }

    public void Add(WorkspaceTabVisibilityItem item)
    {
        Items.Add(item);
        item.PropertyChanged += OnItemPropertyChanged;
        OnPropertyChanged(nameof(VisibleSummary));
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabVisibilityItem.IsVisible) or
            nameof(WorkspaceTabVisibilityItem.IsAvailableInMode))
        {
            OnPropertyChanged(nameof(VisibleSummary));
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class WorkspaceTabVisibilityItem : INotifyPropertyChanged
{
    private bool _isVisible;
    private bool _isAvailableInMode = true;

    public WorkspaceTabVisibilityItem(string id, string name, string category, bool isVisible)
    {
        Id = id;
        Name = name;
        Category = category;
        _isVisible = isVisible;
    }

    public string Id { get; }
    public string Name { get; }
    public string Category { get; }

    public bool IsAvailableInMode
    {
        get => _isAvailableInMode;
        set
        {
            if (_isAvailableInMode == value) return;
            _isAvailableInMode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailableInMode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailabilitySummary)));
        }
    }

    public string AvailabilitySummary => IsAvailableInMode ? Category : $"{Category} · unavailable in current mode";

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? VisibilityChanged;
}

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = null!;
    private UiLocalizer? _uiLocalizer;
    private LocalizationService? _localization;
    private FocusedDiffWindow? _focusedDiffWindow;
    private FocusedDiffWindow? _changesDiffWindow;
    private FocusedDiffWindow? _pullRequestDiffWindow;
    private CiLogWindow? _ciLogWindow;
    private CiLogWindow? _pullRequestCiLogWindow;
    private CommitExplorerWindow? _commitExplorerWindow;
    private BranchFileExplorerWindow? _branchFileExplorerWindow;
    private GitConflictResolverWindow? _gitConflictResolverWindow;
    private readonly List<DetachedWorkspaceWindow> _detachedWorkspaceWindows = [];
    private readonly Dictionary<TabItem, DetachedTabWindow> _detachedTabWindows = [];
    private WorkspaceLayoutPreferencesStore? _workspaceLayoutStore;
    private ProjectWorkspaceStateStore? _projectWorkspaceStateStore;
    private readonly DispatcherTimer _codeRefreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private RepositoryChangeMonitor? _repositoryChangeMonitor;
    private Guid? _activeWorkspaceProjectId;
    private bool _restoringProjectWorkspace;
    private WorkspaceLayoutPreferences _layoutPreferences = WorkspaceLayoutPreferences.Default;
    private HistoryLayoutMode _historyLayout = HistoryLayoutMode.Columns;
    private ChangesLayoutMode _changesLayout = ChangesLayoutMode.Balanced;
    private CodeLayoutMode _codeLayout = CodeLayoutMode.Balanced;
    private WorkspaceCategory _workspaceCategory = WorkspaceCategory.Overview;
    private readonly Dictionary<string, string> _categoryWorkspaceTabs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenWorkspaceTabNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenChangeColumnNames = new(StringComparer.Ordinal);
    private readonly ObservableCollection<WorkspaceTabVisibilityItem> _workspaceTabVisibilityItems = [];
    private readonly ObservableCollection<WorkspaceTabVisibilityGroup> _workspaceTabVisibilityGroups = [];
    private IReadOnlyList<WorkspaceTabVisibilityPreset> _workspaceTabVisibilityPresets = [];
    private string _workspaceTabVisibilityPreset = "Full workspace";
    private bool _updatingWorkspaceTabVisibility;
    private bool _pullRequestDiffFocused;
    private bool _solutionExplorerTreeLayout = true;
    private bool _branchRemovalFlowActive;
    private DataGrid? _lastChangesSelectionGrid;
    private TreeView? _lastChangesSelectionTree;
    private Action<DesktopBehaviorSetting>? _desktopBehaviorToggle;

    public bool StartHidden { get; set; }

    public event EventHandler? ExitRequested;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        LocalizationService localization,
        string configurationDirectory) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.UnrealBuildLogLines.CollectionChanged += OnUnrealBuildLogLinesChanged;
        _viewModel.AiChatMessages.CollectionChanged += OnAiChatMessagesChanged;
        _viewModel.TeamChatMessages.CollectionChanged += OnTeamChatMessagesChanged;
        _viewModel.SharedSyncFolders.CollectionChanged += OnSharedSyncFoldersChanged;
        ChangesFlatTree.AddHandler(TreeViewItem.ExpandedEvent, OnChangesTreeItemExpanded);
        ChangesFolderTree.AddHandler(TreeViewItem.ExpandedEvent, OnChangesTreeItemExpanded);
        SolutionTreePanel.AddHandler(TreeViewItem.ExpandedEvent, OnSolutionTreeItemExpanded);
        _localization = localization;
        _workspaceLayoutStore = new WorkspaceLayoutPreferencesStore(configurationDirectory);
        _projectWorkspaceStateStore = new ProjectWorkspaceStateStore(configurationDirectory);
        _repositoryChangeMonitor = new RepositoryChangeMonitor(OnRepositoryChangesDetected);
        _layoutPreferences = _workspaceLayoutStore.Load();
        HistoryTimelineToggle.IsChecked = _layoutPreferences.ShowTimeline;
        HistoryFilesToggle.IsChecked = _layoutPreferences.ShowFiles;
        HistoryDiffToggle.IsChecked = _layoutPreferences.ShowDiff;
        ChangesDiffToggle.IsChecked = _layoutPreferences.ShowChangesDiff;
        PullRequestDiffToggle.IsChecked = _layoutPreferences.ShowPullRequestDiff;
        _viewModel.IsChangesDiffPreviewEnabled = _layoutPreferences.ShowChangesDiff;
        _viewModel.IsPullRequestDiffPreviewEnabled = _layoutPreferences.ShowPullRequestDiff;
        CodeExplorerPanelToggle.IsChecked = _layoutPreferences.ShowCodeExplorer;
        CodeSymbolsPanelToggle.IsChecked = _layoutPreferences.ShowCodeSymbols;
        CodeResultsPanelToggle.IsChecked = _layoutPreferences.ShowCodeResults;
        ApplyHistoryLayout(_layoutPreferences.HistoryLayout, false);
        ApplyChangesLayout(_layoutPreferences.ChangesLayout, false, true);
        ApplyPullRequestDiffVisibility();
        MainRevisionCompositionView.SetDiffVisibility(
            _layoutPreferences.ShowMultiRestoreDiff,
            _layoutPreferences.ShowCherryPickDiff);
        MainRevisionCompositionView.DiffVisibilityChanged += OnCompositionDiffVisibilityChanged;
        ApplyCodeLayout(_layoutPreferences.CodeLayout, false, true);
        WorkspaceTabs.SelectionChanged += OnWorkspaceTabSelectionChanged;
        ConfigureWorkspaceDetachGestures();
        InitializeWorkspaceTabVisibilityEditor();
        ApplyChangeColumnVisibility();
        ConsoleAndLogsTabs.SelectionChanged += OnConsoleAndLogsTabSelectionChanged;
        _viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _codeRefreshTimer.Tick += OnCodeRefreshTimerTick;
        _codeRefreshTimer.Start();
        ApplyWorkspaceCategory(WorkspaceCategory.Overview, selectDefault: true);
        foreach (GridSplitter splitter in new[]
                 {
                     ChangesWorkspaceSplitter,
                     CodeExplorerSplitter,
                     CodeResultsSplitter,
                     CodeSymbolsSplitter,
                     HistorySplitterOne,
                     HistorySplitterTwo
                 })
        {
            splitter.PointerReleased += OnWorkspaceSplitterPointerReleased;
        }
        _uiLocalizer = new UiLocalizer(this, localization);
        Dispatcher.UIThread.Post(() =>
        {
            ConfigureGlobalDataGridPreferences();
            ApplyAutomaticButtonTooltips();
        }, DispatcherPriority.Background);
        KeyDown += OnWindowKeyDown;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (StartHidden)
        {
            ShowInTaskbar = false;
            Hide();
        }

        await _viewModel.InitializeAsync();
    }

    internal void ConfigureDesktopBehavior(
        DesktopBehaviorPreferences preferences,
        Action<DesktopBehaviorSetting> toggle)
    {
        _desktopBehaviorToggle = toggle;
        LaunchAtLoginMenuItem.IsChecked = preferences.LaunchAtLogin;
        StartHiddenAtLoginMenuItem.IsChecked = preferences.StartHiddenAtLogin;
        StartHiddenAtLoginMenuItem.IsEnabled = preferences.LaunchAtLogin && preferences.ShowTrayIcon;
        CloseToTrayMenuItem.IsChecked = preferences.CloseToTray;
        ShowTrayIconMenuItem.IsChecked = preferences.ShowTrayIcon;
        CloseToTrayMenuItem.IsEnabled = preferences.ShowTrayIcon;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SaveCurrentProjectWorkspaceState();
        _codeRefreshTimer.Stop();
        _codeRefreshTimer.Tick -= OnCodeRefreshTimerTick;
        _repositoryChangeMonitor?.Dispose();
        _repositoryChangeMonitor = null;
        WorkspaceTabs.SelectionChanged -= OnWorkspaceTabSelectionChanged;
        ConsoleAndLogsTabs.SelectionChanged -= OnConsoleAndLogsTabSelectionChanged;
        _viewModel.UnrealBuildLogLines.CollectionChanged -= OnUnrealBuildLogLinesChanged;
        _viewModel.AiChatMessages.CollectionChanged -= OnAiChatMessagesChanged;
        _viewModel.TeamChatMessages.CollectionChanged -= OnTeamChatMessagesChanged;
        _viewModel.SharedSyncFolders.CollectionChanged -= OnSharedSyncFoldersChanged;
        foreach (AiChatMessageViewModel message in _viewModel.AiChatMessages)
            message.PropertyChanged -= OnAiChatMessagePropertyChanged;
        _viewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        _focusedDiffWindow?.Close();
        _focusedDiffWindow = null;
        _changesDiffWindow?.Close();
        _changesDiffWindow = null;
        _pullRequestDiffWindow?.Close();
        _pullRequestDiffWindow = null;
        _ciLogWindow?.Close();
        _ciLogWindow = null;
        _pullRequestCiLogWindow?.Close();
        _pullRequestCiLogWindow = null;
        _commitExplorerWindow?.Close();
        _commitExplorerWindow = null;
        foreach (DetachedWorkspaceWindow window in _detachedWorkspaceWindows.ToArray())
        {
            window.Close();
        }
        _detachedWorkspaceWindows.Clear();
        foreach (DetachedTabWindow window in _detachedTabWindows.Values.ToArray()) window.Close();
        _detachedTabWindows.Clear();
        foreach (WorkspaceTabVisibilityItem item in _workspaceTabVisibilityItems)
            item.VisibilityChanged -= OnWorkspaceTabVisibilityItemChanged;
        _uiLocalizer?.Dispose();
        MainRevisionCompositionView.DiffVisibilityChanged -= OnCompositionDiffVisibilityChanged;
    }

    private void OnUnrealBuildLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (AutoScrollUnrealBuildLogCheckBox.IsChecked != true ||
                _viewModel.UnrealBuildLogLines.Count == 0)
            {
                return;
            }

            UnrealBuildLogList.ScrollIntoView(_viewModel.UnrealBuildLogLines[^1]);
        }, DispatcherPriority.Background);
    }

    private void OnAiChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AiChatMessageViewModel message in e.OldItems)
                message.PropertyChanged -= OnAiChatMessagePropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (AiChatMessageViewModel message in e.NewItems)
                message.PropertyChanged += OnAiChatMessagePropertyChanged;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.AiChatMessages.Count > 0)
                AiChatList.ScrollIntoView(_viewModel.AiChatMessages[^1]);
        }, DispatcherPriority.Background);
    }

    private void OnTeamChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.TeamChatMessages.Count > 0)
                TeamChatMessageList.ScrollIntoView(_viewModel.TeamChatMessages[^1]);
        }, DispatcherPriority.Background);
    }

    private void OnSharedSyncFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => RefreshWorkspaceModeNavigation(selectPrimary: false));

    private void OnAiChatMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AiChatMessageViewModel.Text)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.AiChatMessages.Count > 0)
                AiChatList.ScrollIntoView(_viewModel.AiChatMessages[^1]);
        }, DispatcherPriority.Background);
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedProject))
        {
            SaveCurrentProjectWorkspaceState();
            RestoreSelectedProjectWorkspaceState();
            RefreshWorkspaceModeNavigation(selectPrimary: false);
            ConfigureRepositoryChangeMonitor();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.CodeAutoRefreshFrequency))
        {
            SaveCurrentProjectWorkspaceState();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.ChangeSort))
        {
            SaveCurrentProjectWorkspaceState();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.HasCodeFileSearchResults))
        {
            ApplySolutionExplorerLayout(_solutionExplorerTreeLayout);
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.IsUnrealIntegrationEnabled) or
                 nameof(MainWindowViewModel.IsUnityIntegrationEnabled) or
                 nameof(MainWindowViewModel.IsGodotIntegrationEnabled) or
                 nameof(MainWindowViewModel.IsLoreIntegrationEnabled) or
                 nameof(MainWindowViewModel.IsPerforceIntegrationEnabled) or
                 nameof(MainWindowViewModel.IsCyStorePluginEnabled) or
                 nameof(MainWindowViewModel.IsAiIntegrationEnabled) or
                 nameof(MainWindowViewModel.IsUnrealProjectDetected) or
                 nameof(MainWindowViewModel.IsUnityProjectDetected) or
                 nameof(MainWindowViewModel.IsGodotProjectDetected) or
                 nameof(MainWindowViewModel.IsLoreProjectDetected) or
                 nameof(MainWindowViewModel.HasActivePluginOperatingMode) or
                 nameof(MainWindowViewModel.ActivePluginOperatingModeWorkspaceTabs))
        {
            RefreshWorkspaceModeNavigation(selectPrimary: false);
        }
    }

    private void ConfigureRepositoryChangeMonitor()
    {
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled || !Directory.Exists(project.RootPath))
        {
            _repositoryChangeMonitor?.Stop();
            return;
        }

        _repositoryChangeMonitor?.Start(project.RootPath);
    }

    private void OnRepositoryChangesDetected(RepositoryChangeBatch batch)
    {
        Dispatcher.UIThread.Post(async () => await _viewModel.RefreshDetectedChangesAsync(
            batch.Paths,
            batch.RequiresUntrackedScan,
            batch.GitMetadataChanged,
            batch.WatcherOverflowed));
    }

    private void RestoreSelectedProjectWorkspaceState()
    {
        if (_projectWorkspaceStateStore is null || _viewModel.SelectedProject is null)
        {
            _activeWorkspaceProjectId = null;
            return;
        }

        ProjectWorkspaceState state = _projectWorkspaceStateStore.Get(_viewModel.SelectedProject.Id);
        _activeWorkspaceProjectId = state.ProjectId;
        _restoringProjectWorkspace = true;
        try
        {
            _categoryWorkspaceTabs.Clear();
            if (state.CategoryTabs is not null)
            {
                foreach ((string category, string tabName) in state.CategoryTabs)
                    _categoryWorkspaceTabs[category] = tabName;
            }
            RestoreWorkspaceTabVisibility(state);
            RestoreChangeListPreferences(state);
            _viewModel.CodeAutoRefreshFrequency = state.CodeRefreshFrequency;
            ConsoleAndLogsTabs.SelectedIndex = Math.Clamp(state.ConsoleSection, 0, 1);
            TabItem requestedTab = AllWorkspaceTabs().FirstOrDefault(item => item.Name == state.ActiveTab) ?? ProjectWorkspaceTab;
            TabItem tab = IsWorkspaceTabEnabled(requestedTab) ? requestedTab : PreferredWorkspaceTabForMode();
            ApplyWorkspaceCategory(CategoryFor(tab), selectDefault: false);
            WorkspaceTabs.SelectedItem = tab;
        }
        finally
        {
            _restoringProjectWorkspace = false;
        }

        EnsureCodeWorkspaceForVisibleTab();
        EnsureSelectedWorkspaceData();
    }

    private void SaveCurrentProjectWorkspaceState()
    {
        if (_restoringProjectWorkspace || _projectWorkspaceStateStore is null || _activeWorkspaceProjectId is not Guid projectId)
            return;
        string activeTab = (WorkspaceTabs.SelectedItem as TabItem)?.Name ?? "ProjectWorkspaceTab";
        if (WorkspaceTabs.SelectedItem is TabItem selectedTab)
            _categoryWorkspaceTabs[CategoryFor(selectedTab).ToString()] = activeTab;
        _projectWorkspaceStateStore.Save(new ProjectWorkspaceState(
            projectId,
            activeTab,
            _viewModel.CodeAutoRefreshFrequency,
            Math.Max(0, ConsoleAndLogsTabs.SelectedIndex),
            new Dictionary<string, string>(_categoryWorkspaceTabs, StringComparer.Ordinal),
            _workspaceTabVisibilityPreset,
            _hiddenWorkspaceTabNames.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            _hiddenChangeColumnNames.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            _viewModel.ChangeSort,
            CaptureDataGridColumnWidths(),
            CaptureHiddenDataGridColumns()));
    }

    private void OnWorkspaceTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_restoringProjectWorkspace) return;
        SaveCurrentProjectWorkspaceState();
        if (ReferenceEquals(WorkspaceTabs.SelectedItem, NotificationsWorkspaceTab))
            _viewModel.MarkProjectNotificationsRead();
        EnsureCodeWorkspaceForVisibleTab();
        EnsureSelectedWorkspaceData();
    }

    private async void OnProjectNotificationsEnabledClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await _viewModel.SetProjectNotificationsEnabledAsync(checkBox.IsChecked == true);
    }

    private void OnMarkProjectNotificationsReadClick(object? sender, RoutedEventArgs e) =>
        _viewModel.MarkProjectNotificationsRead();

    private void OnConsoleAndLogsTabSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveCurrentProjectWorkspaceState();

    private void RestoreChangeListPreferences(ProjectWorkspaceState state)
    {
        _hiddenChangeColumnNames.Clear();
        foreach (string column in state.HiddenChangeColumns ?? [])
        {
            if (column is "Checked" or "State" or "Lock" or "Area")
                _hiddenChangeColumnNames.Add(column);
        }

        _viewModel.ChangeSort = _viewModel.ChangeSortOptions.Contains(state.ChangeSort, StringComparer.Ordinal)
            ? state.ChangeSort
            : "Name";
        ApplyChangeColumnVisibility();
        RestoreDataGridColumnWidths(state.DataGridColumnWidths);
        RestoreHiddenDataGridColumns(state.HiddenDataGridColumns);
    }

    private IReadOnlyDictionary<string, DataGrid> PreferenceDataGrids()
    {
        Dictionary<string, DataGrid> result = new(StringComparer.Ordinal);
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        foreach (DataGrid grid in this.GetLogicalDescendants().OfType<DataGrid>())
        {
            string signature = !string.IsNullOrWhiteSpace(grid.Name)
                ? grid.Name
                : "columns:" + string.Join("|", grid.Columns.Select((column, index) => ColumnPreferenceKey(column, index)));
            int occurrence = occurrences.GetValueOrDefault(signature);
            occurrences[signature] = occurrence + 1;
            string key = occurrence == 0 ? signature : $"{signature}#{occurrence}";
            result[key] = grid;
        }
        return result;
    }

    private static string ColumnPreferenceKey(DataGridColumn column, int index) =>
        column.Tag as string ?? column.Header?.ToString() ?? $"Column {index + 1}";

    private Dictionary<string, double[]> CaptureDataGridColumnWidths()
    {
        Dictionary<string, double[]> result = new(StringComparer.Ordinal);
        foreach ((string name, DataGrid grid) in PreferenceDataGrids())
        {
            double[] widths = grid.Columns
                .Select(column => Math.Round(column.ActualWidth, 1))
                .ToArray();
            if (widths.Any(width => width > 0)) result[name] = widths;
        }
        return result;
    }

    private void RestoreDataGridColumnWidths(IReadOnlyDictionary<string, double[]>? preferences)
    {
        if (preferences is null) return;
        foreach ((string name, DataGrid grid) in PreferenceDataGrids())
        {
            if (!preferences.TryGetValue(name, out double[]? widths)) continue;
            for (int index = 0; index < Math.Min(widths.Length, grid.Columns.Count); index++)
            {
                double width = widths[index];
                if (double.IsFinite(width) && width >= 28)
                    grid.Columns[index].Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
            }
        }
    }

    private Dictionary<string, string[]> CaptureHiddenDataGridColumns()
    {
        Dictionary<string, string[]> result = new(StringComparer.Ordinal);
        foreach ((string gridName, DataGrid grid) in PreferenceDataGrids())
        {
            string[] hidden = grid.Columns
                .Select((column, index) => (column, key: ColumnPreferenceKey(column, index)))
                .Where(item => !item.column.IsVisible)
                .Select(item => item.key)
                .ToArray();
            if (hidden.Length > 0) result[gridName] = hidden;
        }
        return result;
    }

    private void RestoreHiddenDataGridColumns(IReadOnlyDictionary<string, string[]>? preferences)
    {
        foreach ((string gridName, DataGrid grid) in PreferenceDataGrids())
        {
            HashSet<string> hidden = preferences?.GetValueOrDefault(gridName)?
                                         .ToHashSet(StringComparer.Ordinal) ?? [];
            for (int index = 0; index < grid.Columns.Count; index++)
            {
                DataGridColumn column = grid.Columns[index];
                string key = ColumnPreferenceKey(column, index);
                if (key is "Name" || key.Contains("always visible", StringComparison.OrdinalIgnoreCase)) continue;
                column.IsVisible = !hidden.Contains(key);
            }
        }
    }

    private void ConfigureGlobalDataGridPreferences()
    {
        foreach (DataGrid grid in PreferenceDataGrids().Values)
        {
            grid.CanUserResizeColumns = true;
            if (grid.ContextMenu is not null) continue;

            MenuItem root = new() { Header = "Visible columns" };
            for (int index = 0; index < grid.Columns.Count; index++)
            {
                DataGridColumn column = grid.Columns[index];
                string key = ColumnPreferenceKey(column, index);
                MenuItem item = new()
                {
                    Header = key,
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = column.IsVisible,
                    Tag = column
                };
                item.Click += (_, _) =>
                {
                    column.IsVisible = item.IsChecked == true;
                    SaveCurrentProjectWorkspaceState();
                };
                root.Items.Add(item);
            }
            grid.ContextMenu = new ContextMenu { ItemsSource = new[] { root } };
        }
    }

    private void ApplyAutomaticButtonTooltips()
    {
        foreach (Button button in this.GetLogicalDescendants().OfType<Button>())
        {
            if (ToolTip.GetTip(button) is not null || button.Content is not string text ||
                string.IsNullOrWhiteSpace(text)) continue;
            ToolTip.SetTip(button, DescribeButtonAction(text));
        }
    }

    private static string DescribeButtonAction(string rawText)
    {
        string text = rawText.Replace("↗", string.Empty, StringComparison.Ordinal).Trim();
        string lower = text.ToLowerInvariant();
        if (lower.Contains("refresh") || lower.Contains("reload"))
            return "Reload this page's data for the selected project.";
        if (lower.Contains("fetch"))
            return "Contact the configured remote and refresh its references without changing the working files.";
        if (lower == "pull")
            return "Download and integrate the current branch from its configured remote.";
        if (lower == "push")
            return "Publish the current branch commits to its configured remote.";
        if (lower.Contains("save") || lower.Contains("apply"))
            return "Save and apply these settings to the selected project.";
        if (lower.Contains("cancel") || lower.Contains("stop"))
            return "Cancel or stop the current operation safely.";
        if (lower.Contains("browse") || lower.Contains("choose") || lower.Contains("open folder"))
            return "Choose or open the related folder without modifying its contents.";
        if (lower.Contains("analy") || lower.Contains("scan"))
            return "Run a read-only analysis and show its progress and results.";
        if (lower.Contains("remove") || lower.Contains("delete") || lower.Contains("clean") || lower.Contains("rollback"))
            return "Review and confirm this potentially destructive action before it changes project data.";
        if (lower.Contains("enable") || lower.Contains("start") || lower.Contains("resume"))
            return "Enable or start this feature for the selected project.";
        if (lower.Contains("disable") || lower.Contains("pause"))
            return "Disable or pause this feature for the selected project.";
        if (lower.Contains("doc") || lower.Contains("help"))
            return "Open the documentation for this page.";
        return $"{text}: run this action for the selected project.";
    }

    private void ApplyChangeColumnVisibility()
    {
        foreach (DataGrid grid in new[] { VersionedChangesGrid, UnversionedChangesGrid })
        {
            foreach (DataGridColumn column in grid.Columns)
            {
                if (column.Tag is not string name) continue;
                column.IsVisible = name == "Name" || !_hiddenChangeColumnNames.Contains(name);
            }
        }
    }

    private void OnChangeColumnsMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu) return;
        foreach (MenuItem root in contextMenu.Items.OfType<MenuItem>())
        {
            foreach (MenuItem item in root.Items.OfType<MenuItem>())
            {
                if (item.Tag is string name)
                    item.IsChecked = !_hiddenChangeColumnNames.Contains(name);
            }
        }
    }

    private void OnChangeColumnVisibilityClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string name } item) return;
        if (item.IsChecked == true) _hiddenChangeColumnNames.Remove(name);
        else _hiddenChangeColumnNames.Add(name);
        ApplyChangeColumnVisibility();
        SaveCurrentProjectWorkspaceState();
    }

    private void OnChangesDataGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (e.Column.Tag is not string sort ||
            !_viewModel.ChangeSortOptions.Contains(sort, StringComparer.Ordinal))
        {
            return;
        }

        _viewModel.ChangeSort = sort;
    }

    private void EnsureCodeWorkspaceForVisibleTab()
    {
        if (ReferenceEquals(WorkspaceTabs.SelectedItem, SolutionExplorerWorkspaceTab) ||
            ReferenceEquals(WorkspaceTabs.SelectedItem, CodeWorkspaceTab))
            _ = _viewModel.EnsureCodeWorkspaceLoadedAsync();
    }

    private void EnsureSelectedWorkspaceData()
    {
        if (WorkspaceTabs.SelectedItem is TabItem { Name: { Length: > 0 } workspaceName })
        {
            _ = _viewModel.EnsureWorkspaceDataLoadedAsync(workspaceName);
        }
    }

    private async void OnCodeRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible || _viewModel.IsCodeWorkspaceLoading ||
            (!ReferenceEquals(WorkspaceTabs.SelectedItem, SolutionExplorerWorkspaceTab) &&
             !ReferenceEquals(WorkspaceTabs.SelectedItem, CodeWorkspaceTab)))
            return;

        TimeSpan? interval = _viewModel.CodeAutoRefreshFrequency switch
        {
            "Low · 5 min" => TimeSpan.FromMinutes(5),
            "10 min" => TimeSpan.FromMinutes(10),
            "30 min" => TimeSpan.FromMinutes(30),
            _ => null
        };
        if (interval is not null && _viewModel.IsCodeWorkspaceRefreshDue(interval.Value))
            await _viewModel.EnsureCodeWorkspaceLoadedAsync(force: true);
    }

    private void OnBalancedChangesLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyChangesLayout(ChangesLayoutMode.Balanced);

    private void OnDiffFocusChangesLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyChangesLayout(ChangesLayoutMode.DiffFocus);

    private void OnCommitFocusChangesLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyChangesLayout(ChangesLayoutMode.CommitFocus);

    private void ApplyChangesLayout(ChangesLayoutMode layout, bool persist = true, bool restoreSavedSize = false)
    {
        _changesLayout = layout;
        ChangesBalancedLayoutToggle.IsChecked = layout == ChangesLayoutMode.Balanced;
        ChangesCommitFocusLayoutToggle.IsChecked = layout == ChangesLayoutMode.CommitFocus;
        ChangesDiffFocusLayoutToggle.IsChecked = layout == ChangesLayoutMode.DiffFocus;
        bool restore = restoreSavedSize && _layoutPreferences.ChangesLayout == layout;
        double listWeight = restore
            ? _layoutPreferences.ChangesListWeight
            : layout switch
            {
                ChangesLayoutMode.DiffFocus => 0.68,
                ChangesLayoutMode.CommitFocus => 1.65,
                _ => 1.05
            };
        double diffWeight = restore
            ? _layoutPreferences.ChangesDiffWeight
            : layout switch
            {
                ChangesLayoutMode.DiffFocus => 1.72,
                ChangesLayoutMode.CommitFocus => 0.95,
                _ => 1.35
            };
        bool showDiff = ChangesDiffToggle.IsChecked == true;
        ChangesDiffPanel.IsVisible = showDiff;
        ChangesWorkspaceSplitter.IsVisible = showDiff;
        ChangesWorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(showDiff ? listWeight : 1, GridUnitType.Star);
        ChangesWorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(showDiff ? 8 : 0);
        ChangesWorkspaceGrid.ColumnDefinitions[2].Width = showDiff
            ? new GridLength(diffWeight, GridUnitType.Star)
            : new GridLength(0);
        if (persist) SaveWorkspaceLayoutPreferences();
    }

    private void OnChangesDiffToggleClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsChangesDiffPreviewEnabled = ChangesDiffToggle.IsChecked == true;
        ApplyChangesLayout(_changesLayout);
    }

    private void OnPullRequestDiffToggleClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsPullRequestDiffPreviewEnabled = PullRequestDiffToggle.IsChecked == true;
        ApplyPullRequestDiffVisibility();
        SaveWorkspaceLayoutPreferences();
    }

    private void ApplyPullRequestDiffVisibility()
    {
        bool visible = PullRequestDiffToggle.IsChecked == true;
        PullRequestDiffPanel.IsVisible = visible;
        PullRequestDiffSplitter.IsVisible = visible;
        if (PullRequestDiffPanel.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
        {
            double filesWeight = _pullRequestDiffFocused ? 0.48 : 0.78;
            double diffWeight = _pullRequestDiffFocused ? 1.52 : 1.22;
            grid.ColumnDefinitions[0].Width = new GridLength(visible ? filesWeight : 1, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(visible ? 8 : 0);
            grid.ColumnDefinitions[2].Width = visible
                ? new GridLength(diffWeight, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private void OnPullRequestBalancedLayoutClick(object? sender, RoutedEventArgs e)
    {
        _pullRequestDiffFocused = false;
        PullRequestBalancedLayoutToggle.IsChecked = true;
        PullRequestDiffFocusLayoutToggle.IsChecked = false;
        ApplyPullRequestDiffVisibility();
    }

    private void OnPullRequestDiffFocusLayoutClick(object? sender, RoutedEventArgs e)
    {
        _pullRequestDiffFocused = true;
        PullRequestBalancedLayoutToggle.IsChecked = false;
        PullRequestDiffFocusLayoutToggle.IsChecked = true;
        PullRequestDiffToggle.IsChecked = true;
        _viewModel.IsPullRequestDiffPreviewEnabled = true;
        ApplyPullRequestDiffVisibility();
    }

    private void OnBalancedCodeLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyCodeLayout(CodeLayoutMode.Balanced);

    private void OnEditorFocusCodeLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyCodeLayout(CodeLayoutMode.EditorFocus);

    private void OnSearchFocusCodeLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyCodeLayout(CodeLayoutMode.SearchFocus);

    private void ApplyCodeLayout(CodeLayoutMode layout, bool persist = true, bool restoreSavedSize = false)
    {
        _codeLayout = layout;
        CodeBalancedLayoutToggle.IsChecked = layout == CodeLayoutMode.Balanced;
        CodeEditorFocusLayoutToggle.IsChecked = layout == CodeLayoutMode.EditorFocus;
        CodeSearchFocusLayoutToggle.IsChecked = layout == CodeLayoutMode.SearchFocus;

        if (!restoreSavedSize)
        {
            CodeExplorerPanelToggle.IsChecked = true;
            CodeSymbolsPanelToggle.IsChecked = true;
            CodeResultsPanelToggle.IsChecked = true;
        }

        (double explorer, double editor, double results, double editorHeight, double symbolsHeight) = layout switch
        {
            CodeLayoutMode.EditorFocus => (0.72, 1.95, 0.55, 1.0, 0.24),
            CodeLayoutMode.SearchFocus => (1.65, 1.05, 0.48, 1.0, 0.30),
            _ => (1.2, 1.25, 0.65, 1.0, 0.32)
        };
        if (restoreSavedSize && _layoutPreferences.CodeLayout == layout)
        {
            explorer = _layoutPreferences.CodeExplorerWeight;
            editor = _layoutPreferences.CodeEditorWeight;
            results = _layoutPreferences.CodeResultsWeight;
            editorHeight = _layoutPreferences.CodeEditorHeightWeight;
            symbolsHeight = _layoutPreferences.CodeSymbolsHeightWeight;
        }

        RefreshCodeDockLayout(explorer, editor, results, editorHeight, symbolsHeight);
        if (persist) SaveWorkspaceLayoutPreferences();
    }

    private void OnCodePanelToggleClick(object? sender, RoutedEventArgs e)
    {
        RefreshCodeDockLayout(
            GetGridWeight(CodeDockWorkspaceGrid.ColumnDefinitions[0], _layoutPreferences.CodeExplorerWeight),
            GetGridWeight(CodeDockWorkspaceGrid.ColumnDefinitions[2], _layoutPreferences.CodeEditorWeight),
            GetGridWeight(CodeDockWorkspaceGrid.ColumnDefinitions[4], _layoutPreferences.CodeResultsWeight),
            GetGridWeight(CodeEditorDockGrid.RowDefinitions[1], _layoutPreferences.CodeEditorHeightWeight),
            GetGridWeight(CodeEditorDockGrid.RowDefinitions[3], _layoutPreferences.CodeSymbolsHeightWeight));
        SaveWorkspaceLayoutPreferences();
    }

    private void RefreshCodeDockLayout(
        double explorerWeight,
        double editorWeight,
        double resultsWeight,
        double editorHeightWeight,
        double symbolsHeightWeight)
    {
        bool showExplorer = CodeExplorerPanelToggle.IsChecked == true;
        bool showSymbols = CodeSymbolsPanelToggle.IsChecked == true;
        bool showResults = CodeResultsPanelToggle.IsChecked == true;
        CodeExplorerPanel.IsVisible = showExplorer;
        CodeExplorerSplitter.IsVisible = showExplorer;
        CodeResultsPanel.IsVisible = showResults;
        CodeResultsSplitter.IsVisible = showResults;
        CodeSymbolsPanel.IsVisible = showSymbols;
        CodeSymbolsSplitter.IsVisible = showSymbols;

        CodeDockWorkspaceGrid.ColumnDefinitions[0].Width = showExplorer
            ? new GridLength(explorerWeight, GridUnitType.Star)
            : new GridLength(0);
        CodeDockWorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(showExplorer ? 8 : 0);
        CodeDockWorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(editorWeight, GridUnitType.Star);
        CodeDockWorkspaceGrid.ColumnDefinitions[3].Width = new GridLength(showResults ? 8 : 0);
        CodeDockWorkspaceGrid.ColumnDefinitions[4].Width = showResults
            ? new GridLength(resultsWeight, GridUnitType.Star)
            : new GridLength(0);
        CodeEditorDockGrid.RowDefinitions[1].Height = new GridLength(editorHeightWeight, GridUnitType.Star);
        CodeEditorDockGrid.RowDefinitions[2].Height = new GridLength(showSymbols ? 8 : 0);
        CodeEditorDockGrid.RowDefinitions[3].Height = showSymbols
            ? new GridLength(symbolsHeightWeight, GridUnitType.Star)
            : new GridLength(0);
    }

    private void OnWorkspaceSplitterPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        SaveWorkspaceLayoutPreferences();

    private void OnCompositionDiffVisibilityChanged(object? sender, EventArgs e) =>
        SaveWorkspaceLayoutPreferences();

    private void OnSaveWorkspaceLayoutClick(object? sender, RoutedEventArgs e) =>
        SaveWorkspaceLayoutPreferences();

    private void OnResetWorkspaceLayoutClick(object? sender, RoutedEventArgs e)
    {
        _layoutPreferences = WorkspaceLayoutPreferences.Default;
        HistoryTimelineToggle.IsChecked = true;
        HistoryFilesToggle.IsChecked = true;
        HistoryDiffToggle.IsChecked = true;
        ChangesDiffToggle.IsChecked = true;
        PullRequestDiffToggle.IsChecked = true;
        _viewModel.IsChangesDiffPreviewEnabled = true;
        _viewModel.IsPullRequestDiffPreviewEnabled = true;
        MainRevisionCompositionView.SetDiffVisibility(true, true);
        CodeExplorerPanelToggle.IsChecked = true;
        CodeSymbolsPanelToggle.IsChecked = true;
        CodeResultsPanelToggle.IsChecked = true;
        ApplyHistoryLayout(HistoryLayoutMode.Columns, false);
        ApplyChangesLayout(ChangesLayoutMode.Balanced, false);
        ApplyPullRequestDiffVisibility();
        ApplyCodeLayout(CodeLayoutMode.Balanced, false);
        SaveWorkspaceLayoutPreferences();
    }

    private void OnColumnsHistoryLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyHistoryLayout(HistoryLayoutMode.Columns);

    private void OnReviewHistoryLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyHistoryLayout(HistoryLayoutMode.Review);

    private void OnDiffFocusedHistoryLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyHistoryLayout(HistoryLayoutMode.DiffFocus);

    private void ApplyHistoryLayout(HistoryLayoutMode layout, bool persist = true)
    {
        _historyLayout = layout;
        HistoryColumnsLayoutToggle.IsChecked = layout == HistoryLayoutMode.Columns;
        HistoryReviewLayoutToggle.IsChecked = layout == HistoryLayoutMode.Review;
        HistoryDiffFocusLayoutToggle.IsChecked = layout == HistoryLayoutMode.DiffFocus;
        RefreshHistoryWorkspaceLayout();
        if (persist)
        {
            SaveHistoryLayoutPreferences();
        }
    }

    private void OnHistoryPanelToggleClick(object? sender, RoutedEventArgs e)
    {
        if (HistoryTimelineToggle.IsChecked != true &&
            HistoryFilesToggle.IsChecked != true &&
            HistoryDiffToggle.IsChecked != true && sender is ToggleButton toggle)
        {
            toggle.IsChecked = true;
        }

        RefreshHistoryWorkspaceLayout();
        SaveHistoryLayoutPreferences();
    }

    private void RefreshHistoryWorkspaceLayout()
    {
        Control[] panels = [HistoryTimelinePanel, HistoryFilesPanel, HistoryDiffPanel];
        bool[] visible =
        [
            HistoryTimelineToggle.IsChecked == true,
            HistoryFilesToggle.IsChecked == true,
            HistoryDiffToggle.IsChecked == true
        ];

        for (int index = 0; index < panels.Length; index++)
        {
            panels[index].IsVisible = visible[index];
        }

        ResetHistoryWorkspaceGrid();
        int[] visibleIndices = Enumerable.Range(0, visible.Length).Where(index => visible[index]).ToArray();
        if (visibleIndices.Length < 3)
        {
            ApplyFlowHistoryLayout(panels, visibleIndices);
            return;
        }

        switch (_historyLayout)
        {
            case HistoryLayoutMode.Review:
                ApplyReviewHistoryLayout();
                break;
            case HistoryLayoutMode.DiffFocus:
                ApplyDiffFocusedHistoryLayout();
                break;
            default:
                ApplyColumnsHistoryLayout();
                break;
        }
    }

    private void ResetHistoryWorkspaceGrid()
    {
        foreach (ColumnDefinition column in HistoryWorkspaceGrid.ColumnDefinitions)
        {
            column.Width = new GridLength(0);
        }

        foreach (RowDefinition row in HistoryWorkspaceGrid.RowDefinitions)
        {
            row.Height = new GridLength(0);
        }

        HistorySplitterOne.IsVisible = false;
        HistorySplitterTwo.IsVisible = false;
    }

    private void ApplyColumnsHistoryLayout()
    {
        bool restore = _layoutPreferences.HistoryLayout == HistoryLayoutMode.Columns;
        SetHistoryColumn(0, restore ? _layoutPreferences.HistoryFirstWeight : 0.9, GridUnitType.Star);
        SetHistoryColumn(1, 6);
        SetHistoryColumn(2, restore ? _layoutPreferences.HistorySecondWeight : 1.25, GridUnitType.Star);
        SetHistoryColumn(3, 6);
        SetHistoryColumn(4, restore ? _layoutPreferences.HistoryThirdWeight : 1.15, GridUnitType.Star);
        SetHistoryRow(0, 1, GridUnitType.Star);
        PlaceHistoryPanel(HistoryTimelinePanel, 0, 0);
        PlaceHistoryPanel(HistoryFilesPanel, 0, 2);
        PlaceHistoryPanel(HistoryDiffPanel, 0, 4);
        ConfigureVerticalHistorySplitter(HistorySplitterOne, 1);
        ConfigureVerticalHistorySplitter(HistorySplitterTwo, 3);
    }

    private void ApplyReviewHistoryLayout()
    {
        bool restore = _layoutPreferences.HistoryLayout == HistoryLayoutMode.Review;
        SetHistoryColumn(0, restore ? _layoutPreferences.HistoryFirstWeight : 0.78, GridUnitType.Star);
        SetHistoryColumn(1, 6);
        SetHistoryColumn(2, restore ? _layoutPreferences.HistorySecondWeight : 1.72, GridUnitType.Star);
        SetHistoryRow(0, restore ? _layoutPreferences.HistoryTopWeight : 0.82, GridUnitType.Star);
        SetHistoryRow(1, 6);
        SetHistoryRow(2, restore ? _layoutPreferences.HistoryBottomWeight : 1.18, GridUnitType.Star);
        PlaceHistoryPanel(HistoryTimelinePanel, 0, 0, 3);
        PlaceHistoryPanel(HistoryFilesPanel, 0, 2);
        PlaceHistoryPanel(HistoryDiffPanel, 2, 2);
        ConfigureVerticalHistorySplitter(HistorySplitterOne, 1, 3);
        ConfigureHorizontalHistorySplitter(HistorySplitterTwo, 1, 2);
    }

    private void ApplyDiffFocusedHistoryLayout()
    {
        bool restore = _layoutPreferences.HistoryLayout == HistoryLayoutMode.DiffFocus;
        SetHistoryColumn(0, restore ? _layoutPreferences.HistoryFirstWeight : 0.72, GridUnitType.Star);
        SetHistoryColumn(1, 6);
        SetHistoryColumn(2, restore ? _layoutPreferences.HistorySecondWeight : 1.78, GridUnitType.Star);
        SetHistoryRow(0, restore ? _layoutPreferences.HistoryTopWeight : 1, GridUnitType.Star);
        SetHistoryRow(1, 6);
        SetHistoryRow(2, restore ? _layoutPreferences.HistoryBottomWeight : 1, GridUnitType.Star);
        PlaceHistoryPanel(HistoryFilesPanel, 0, 0);
        PlaceHistoryPanel(HistoryTimelinePanel, 2, 0);
        PlaceHistoryPanel(HistoryDiffPanel, 0, 2, 3);
        ConfigureVerticalHistorySplitter(HistorySplitterOne, 1, 3);
        ConfigureHorizontalHistorySplitter(HistorySplitterTwo, 1, 0);
    }

    private void ApplyFlowHistoryLayout(IReadOnlyList<Control> panels, IReadOnlyList<int> visibleIndices)
    {
        SetHistoryRow(0, 1, GridUnitType.Star);
        for (int visibleIndex = 0; visibleIndex < visibleIndices.Count; visibleIndex++)
        {
            int column = visibleIndex * 2;
            int panelIndex = visibleIndices[visibleIndex];
            double weight = panelIndex == 2 ? 1.35 : 1;
            SetHistoryColumn(column, weight, GridUnitType.Star);
            PlaceHistoryPanel(panels[panelIndex], 0, column);
        }

        if (visibleIndices.Count >= 2)
        {
            SetHistoryColumn(1, 6);
            ConfigureVerticalHistorySplitter(HistorySplitterOne, 1);
        }
    }

    private void ConfigureVerticalHistorySplitter(GridSplitter splitter, int column, int rowSpan = 1)
    {
        splitter.IsVisible = true;
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        splitter.ShowsPreview = false;
        splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        splitter.Width = 6;
        splitter.Height = double.NaN;
        splitter.Margin = new Thickness(1, 10);
        splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Grid.SetRow(splitter, 0);
        Grid.SetRowSpan(splitter, rowSpan);
        Grid.SetColumn(splitter, column);
        Grid.SetColumnSpan(splitter, 1);
    }

    private void ConfigureHorizontalHistorySplitter(GridSplitter splitter, int row, int column)
    {
        splitter.IsVisible = true;
        splitter.ResizeDirection = GridResizeDirection.Rows;
        splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        splitter.ShowsPreview = false;
        splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        splitter.Width = double.NaN;
        splitter.Height = 6;
        splitter.Margin = new Thickness(10, 1);
        splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Grid.SetRow(splitter, row);
        Grid.SetRowSpan(splitter, 1);
        Grid.SetColumn(splitter, column);
        Grid.SetColumnSpan(splitter, 1);
    }

    private static void PlaceHistoryPanel(Control panel, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(panel, row);
        Grid.SetRowSpan(panel, rowSpan);
        Grid.SetColumn(panel, column);
        Grid.SetColumnSpan(panel, columnSpan);
    }

    private void SetHistoryColumn(int index, double value, GridUnitType unit = GridUnitType.Pixel) =>
        HistoryWorkspaceGrid.ColumnDefinitions[index].Width = new GridLength(value, unit);

    private void SetHistoryRow(int index, double value, GridUnitType unit = GridUnitType.Pixel) =>
        HistoryWorkspaceGrid.RowDefinitions[index].Height = new GridLength(value, unit);

    private void SaveHistoryLayoutPreferences() => SaveWorkspaceLayoutPreferences();

    private void SaveWorkspaceLayoutPreferences()
    {
        _layoutPreferences = new WorkspaceLayoutPreferences(
            _historyLayout,
            HistoryTimelineToggle.IsChecked == true,
            HistoryFilesToggle.IsChecked == true,
            HistoryDiffToggle.IsChecked == true,
            _changesLayout,
            _codeLayout,
            CodeExplorerPanelToggle.IsChecked == true,
            CodeSymbolsPanelToggle.IsChecked == true,
            CodeResultsPanelToggle.IsChecked == true,
            GetGridWeight(ChangesWorkspaceGrid.ColumnDefinitions[0], _layoutPreferences.ChangesListWeight),
            GetGridWeight(ChangesWorkspaceGrid.ColumnDefinitions[2], _layoutPreferences.ChangesDiffWeight),
            GetGridWeight(CodeDockWorkspaceGrid.ColumnDefinitions[0], _layoutPreferences.CodeExplorerWeight),
            GetGridWeight(CodeDockWorkspaceGrid.ColumnDefinitions[2], _layoutPreferences.CodeEditorWeight),
            GetGridWeight(CodeDockWorkspaceGrid.ColumnDefinitions[4], _layoutPreferences.CodeResultsWeight),
            GetGridWeight(CodeEditorDockGrid.RowDefinitions[1], _layoutPreferences.CodeEditorHeightWeight),
            GetGridWeight(CodeEditorDockGrid.RowDefinitions[3], _layoutPreferences.CodeSymbolsHeightWeight),
            GetGridWeight(HistoryWorkspaceGrid.ColumnDefinitions[0], _layoutPreferences.HistoryFirstWeight),
            GetGridWeight(HistoryWorkspaceGrid.ColumnDefinitions[2], _layoutPreferences.HistorySecondWeight),
            GetGridWeight(HistoryWorkspaceGrid.ColumnDefinitions[4], _layoutPreferences.HistoryThirdWeight),
            GetGridWeight(HistoryWorkspaceGrid.RowDefinitions[0], _layoutPreferences.HistoryTopWeight),
            GetGridWeight(HistoryWorkspaceGrid.RowDefinitions[2], _layoutPreferences.HistoryBottomWeight),
            ChangesDiffToggle.IsChecked == true,
            PullRequestDiffToggle.IsChecked == true,
            MainRevisionCompositionView.IsMultiRestoreDiffVisible,
            MainRevisionCompositionView.IsCherryPickDiffVisible);
        _workspaceLayoutStore?.Save(_layoutPreferences);
    }

    private static double GetGridWeight(ColumnDefinition definition, double fallback) =>
        definition.Width.IsStar && double.IsFinite(definition.Width.Value) && definition.Width.Value > 0
            ? definition.Width.Value
            : fallback;

    private static double GetGridWeight(RowDefinition definition, double fallback) =>
        definition.Height.IsStar && double.IsFinite(definition.Height.Value) && definition.Height.Value > 0
            ? definition.Height.Value
            : fallback;


    private void OnChangeTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TreeView tree) return;
        _lastChangesSelectionGrid = null;
        _lastChangesSelectionTree = tree;
        if (tree.SelectedItems.Count == 0)
        {
            _viewModel.SelectedChangeTreeNode = null;
            return;
        }
        GitChangeTreeNode? node = e.AddedItems.OfType<GitChangeTreeNode>().LastOrDefault()
                                  ?? tree.SelectedItem as GitChangeTreeNode;
        if (node is null) return;
        _viewModel.SelectedChangeTreeNode = node;
        if (node.IsPlaceholder)
        {
            GitChangeTreeNode? parent = FindChangeTreeParent(_viewModel.ChangeTree, node);
            parent?.EnsureChildrenLoaded();
            return;
        }
        if (node.IsDirectory)
        {
            node.EnsureChildrenLoaded();
            return;
        }
        if (node.Change is { } change)
        {
            _viewModel.SelectedChange = change;
        }
    }

    private void OnChangesGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        _lastChangesSelectionGrid = grid;
        _lastChangesSelectionTree = null;
        if (grid.SelectedItem is GitChangeViewModel change)
        {
            _viewModel.SelectedChangeTreeNode = null;
            _viewModel.SelectedChange = change;
        }
    }

    private void OnChangesGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid ||
            !e.GetCurrentPoint(grid).Properties.IsRightButtonPressed ||
            e.Source is not Control source ||
            source.FindAncestorOfType<DataGridRow>() is not { DataContext: GitChangeViewModel change })
        {
            return;
        }

        _lastChangesSelectionGrid = grid;
        _lastChangesSelectionTree = null;
        _viewModel.SelectedChangeTreeNode = null;
        if (!grid.SelectedItems.Contains(change))
        {
            grid.SelectedItems.Clear();
            grid.SelectedItem = change;
        }
        _viewModel.SelectedChange = change;
    }

    private void OnChangesTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TreeView tree ||
            !e.GetCurrentPoint(tree).Properties.IsRightButtonPressed ||
            e.Source is not Control source ||
            source.FindAncestorOfType<TreeViewItem>() is not { DataContext: GitChangeTreeNode node } item)
        {
            return;
        }

        _lastChangesSelectionGrid = null;
        _lastChangesSelectionTree = tree;
        if (!tree.SelectedItems.Contains(node))
        {
            tree.UnselectAll();
            item.IsSelected = true;
        }
        _viewModel.SelectedChangeTreeNode = node;
        if (node.Change is { } change) _viewModel.SelectedChange = change;
    }

    private void OnChangesTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem { DataContext: GitChangeTreeNode node })
        {
            node.EnsureChildrenLoaded();
        }
    }

    private void OnChangeTreeIncludeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: GitChangeTreeNode node } checkBox) return;
        _viewModel.SetChangeTreeNodeIncluded(node, checkBox.IsChecked == true);
        e.Handled = true;
    }

    private async void OnExpandAllChangesTreeClick(object? sender, RoutedEventArgs e)
    {
        Queue<GitChangeTreeNode> pending = new(_viewModel.ChangeTree);
        int expanded = 0;
        while (pending.TryDequeue(out GitChangeTreeNode? node))
        {
            if (!node.IsDirectory) continue;
            node.EnsureChildrenLoaded();
            node.IsExpanded = true;
            foreach (GitChangeTreeNode child in node.Children)
            {
                if (child.IsDirectory) pending.Enqueue(child);
            }

            if (++expanded % 32 == 0) await Task.Delay(1);
        }
    }

    private void OnCollapseAllChangesTreeClick(object? sender, RoutedEventArgs e) =>
        SetChangeTreeExpansion(_viewModel.ChangeTree, false);

    private static void SetChangeTreeExpansion(IEnumerable<GitChangeTreeNode> nodes, bool expanded)
    {
        foreach (GitChangeTreeNode node in nodes)
        {
            node.IsExpanded = expanded;
            SetChangeTreeExpansion(node.Children.Where(child => !child.IsPlaceholder), expanded);
        }
    }

    private static GitChangeTreeNode? FindChangeTreeParent(
        IEnumerable<GitChangeTreeNode> roots,
        GitChangeTreeNode target)
    {
        foreach (GitChangeTreeNode root in roots)
        {
            if (root.Children.Contains(target)) return root;
            GitChangeTreeNode? parent = FindChangeTreeParent(root.Children, target);
            if (parent is not null) return parent;
        }

        return null;
    }

    private void InitializeWorkspaceTabVisibilityEditor()
    {
        _workspaceTabVisibilityPresets = CreateWorkspaceTabVisibilityPresets();
        WorkspaceTabPresetSelector.ItemsSource = _workspaceTabVisibilityPresets;
        WorkspaceTabVisibilityList.ItemsSource = _workspaceTabVisibilityGroups;
        _workspaceTabVisibilityItems.Clear();
        _workspaceTabVisibilityGroups.Clear();
        Dictionary<string, WorkspaceTabVisibilityGroup> groupsByCategory = new(StringComparer.Ordinal);

        foreach (TabItem tab in AllWorkspaceTabs().Where(tab =>
                     !ReferenceEquals(tab, ProjectWorkspaceTab) &&
                     !ReferenceEquals(tab, VisibleTabsWorkspaceTab)))
        {
            if (string.IsNullOrWhiteSpace(tab.Name)) continue;
            WorkspaceCategory category = CategoryFor(tab);
            WorkspaceTabVisibilityItem item = new(
                tab.Name,
                WorkspaceTabDisplayName(tab),
                WorkspaceCategoryDisplayName(category),
                isVisible: true);
            item.VisibilityChanged += OnWorkspaceTabVisibilityItemChanged;
            _workspaceTabVisibilityItems.Add(item);
            if (!groupsByCategory.TryGetValue(item.Category, out WorkspaceTabVisibilityGroup? group))
            {
                group = new WorkspaceTabVisibilityGroup(item.Category);
                groupsByCategory.Add(item.Category, group);
                _workspaceTabVisibilityGroups.Add(group);
            }
            group.Add(item);
        }

        WorkspaceTabVisibilityPreset initialPreset = _workspaceTabVisibilityPresets.First();
        WorkspaceTabPresetSelector.SelectedItem = initialPreset;
        UpdateWorkspaceTabVisibilitySummary();
    }

    private IReadOnlyList<WorkspaceTabVisibilityPreset> CreateWorkspaceTabVisibilityPresets()
    {
        static HashSet<string> Visible(params string[] tabs) => new(tabs, StringComparer.Ordinal);
        HashSet<string> all = AllWorkspaceTabs()
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Name))
            .Select(tab => tab.Name!)
            .ToHashSet(StringComparer.Ordinal);
        return
        [
            new WorkspaceTabVisibilityPreset(
                "Full workspace",
                "Show every CyRevision project tool.",
                "Complete administration and advanced workflows",
                "Every Git, code, network, AI, Unreal, diagnostic, and extension tool",
                all),
            new WorkspaceTabVisibilityPreset(
                "Git essentials",
                "A compact revision-control workspace without Compose, CI, graphs, network, or AI tools.",
                "Daily commits and repository review",
                "Changes, history, branches, pull requests, locks, LFS, and backups",
                Visible(
                    "ProjectWorkspaceTab", "VisibleTabsWorkspaceTab", "MembersWorkspaceTab", "ChangesWorkspaceTab", "HistoryWorkspaceTab",
                    "BranchesWorkspaceTab", "PullRequestsWorkspaceTab", "LfsLocksWorkspaceTab",
                    "GitLfsWorkspaceTab", "BackupsWorkspaceTab", "HelpWorkspaceTab")),
            new WorkspaceTabVisibilityPreset(
                "Developer",
                "Git, code exploration, console, assets, AI, plugins, and diagnostics.",
                "Programming and assisted code review",
                "Git, solution explorer, code search, console, assets, AI, MCP, and plugins",
                Visible(
                    "ProjectWorkspaceTab", "VisibleTabsWorkspaceTab", "MembersWorkspaceTab", "ChangesWorkspaceTab", "HistoryWorkspaceTab",
                    "CompositionWorkspaceTab", "BranchesWorkspaceTab", "PullRequestsWorkspaceTab", "CiWorkspaceTab",
                    "GitGraphsWorkspaceTab", "LfsLocksWorkspaceTab", "GitIgnoreWorkspaceTab", "GitLfsWorkspaceTab",
                    "BackupsWorkspaceTab", "SolutionExplorerWorkspaceTab", "CodeWorkspaceTab", "ConsoleWorkspaceTab",
                    "AssetDiffWorkspaceTab", "AiWorkspaceTab", "McpWorkspaceTab", "PluginsWorkspaceTab",
                    "UnrealWorkspaceTab", "UnityWorkspaceTab", "GodotWorkspaceTab", "LoreWorkspaceTab", "PerforceWorkspaceTab", "UnrealBuildWorkspaceTab", "DiagnosticsWorkspaceTab", "HelpWorkspaceTab")),
            new WorkspaceTabVisibilityPreset(
                "Unreal production",
                "Revision, asset, build, and Unreal integration tools in one focused workspace.",
                "Unreal Engine projects and plugin validation",
                "Git, locks, LFS, assets, CI, console, Unreal connection, builds, and diagnostics",
                Visible(
                    "ProjectWorkspaceTab", "VisibleTabsWorkspaceTab", "MembersWorkspaceTab", "ChangesWorkspaceTab",
                    "HistoryWorkspaceTab", "CompositionWorkspaceTab", "BranchesWorkspaceTab", "PullRequestsWorkspaceTab",
                    "CiWorkspaceTab", "GitGraphsWorkspaceTab", "LfsLocksWorkspaceTab", "GitIgnoreWorkspaceTab",
                    "GitLfsWorkspaceTab", "BackupsWorkspaceTab", "SolutionExplorerWorkspaceTab", "CodeWorkspaceTab",
                    "ConsoleWorkspaceTab", "AssetDiffWorkspaceTab", "PluginsWorkspaceTab", "UnrealWorkspaceTab", "LoreWorkspaceTab", "PerforceWorkspaceTab",
                    "UnrealBuildWorkspaceTab", "DiagnosticsWorkspaceTab", "HelpWorkspaceTab")),
            new WorkspaceTabVisibilityPreset(
                "Game engines",
                "A focused workspace for Unreal, Unity, and Godot projects with revision and code tools.",
                "Mixed-engine game development",
                "Git, code, assets, plugins, Unreal, Unity, Godot, builds, and diagnostics",
                Visible(
                    "ProjectWorkspaceTab", "VisibleTabsWorkspaceTab", "MembersWorkspaceTab", "ChangesWorkspaceTab",
                    "HistoryWorkspaceTab", "BranchesWorkspaceTab", "PullRequestsWorkspaceTab", "CiWorkspaceTab",
                    "LfsLocksWorkspaceTab", "GitIgnoreWorkspaceTab", "GitLfsWorkspaceTab", "BackupsWorkspaceTab",
                    "SolutionExplorerWorkspaceTab", "CodeWorkspaceTab", "ConsoleWorkspaceTab", "AssetDiffWorkspaceTab",
                    "PluginsWorkspaceTab", "UnrealWorkspaceTab", "UnityWorkspaceTab", "GodotWorkspaceTab", "LoreWorkspaceTab", "PerforceWorkspaceTab",
                    "UnrealBuildWorkspaceTab", "DiagnosticsWorkspaceTab", "HelpWorkspaceTab")),
            new WorkspaceTabVisibilityPreset(
                "Team & network",
                "Collaboration, synchronization, VPN, shared files, chat, and remote execution.",
                "Distributed teams and self-hosted infrastructure",
                "Members, Sync, VPN, Swarm, shared files, team chat, Discord, WIP, and remote builds",
                Visible(
                    "ProjectWorkspaceTab", "VisibleTabsWorkspaceTab", "MembersWorkspaceTab", "ChangesWorkspaceTab",
                    "HistoryWorkspaceTab", "SynchronizationWorkspaceTab", "SyncConflictsWorkspaceTab", "SharedSyncWorkspaceTab", "VpnWorkspaceTab", "SwarmWorkspaceTab",
                    "VpnFilesWorkspaceTab", "TeamChatWorkspaceTab", "RemoteBuildsWorkspaceTab", "DiscordWorkspaceTab",
                    "WorkInProgressWorkspaceTab", "ConsoleWorkspaceTab", "DiagnosticsWorkspaceTab", "HelpWorkspaceTab")),
            new WorkspaceTabVisibilityPreset(
                "Minimal",
                "Only Project, Changes, History, Solution Explorer, Console, and Help.",
                "A clean interface for quick repository checks",
                "Project, changes, history, solution explorer, console, and help",
                Visible(
                    "ProjectWorkspaceTab", "VisibleTabsWorkspaceTab", "ChangesWorkspaceTab", "HistoryWorkspaceTab",
                    "SolutionExplorerWorkspaceTab", "ConsoleWorkspaceTab", "HelpWorkspaceTab")),
            new WorkspaceTabVisibilityPreset(
                "Custom",
                "Manual per-tab visibility for this project.",
                "A workspace tailored to this specific project",
                "Use the checklist below; changes are saved automatically",
                null,
                IsCustom: true)
        ];
    }

    private void RestoreWorkspaceTabVisibility(ProjectWorkspaceState state)
    {
        HashSet<string> knownTabs = _workspaceTabVisibilityItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        _hiddenWorkspaceTabNames.Clear();
        foreach (string hiddenTab in state.HiddenTabs ?? [])
        {
            if (knownTabs.Contains(hiddenTab)) _hiddenWorkspaceTabNames.Add(hiddenTab);
        }

        _updatingWorkspaceTabVisibility = true;
        try
        {
            foreach (WorkspaceTabVisibilityItem item in _workspaceTabVisibilityItems)
                item.IsVisible = !_hiddenWorkspaceTabNames.Contains(item.Id);

            WorkspaceTabVisibilityPreset preset = ResolveWorkspaceTabVisibilityPreset(state.TabVisibilityPreset);
            _workspaceTabVisibilityPreset = preset.Name;
            WorkspaceTabPresetSelector.SelectedItem = preset;
        }
        finally
        {
            _updatingWorkspaceTabVisibility = false;
        }

        UpdateWorkspaceCategoryToggleVisibility();
        UpdateWorkspaceTabVisibilitySummary();
    }

    private WorkspaceTabVisibilityPreset ResolveWorkspaceTabVisibilityPreset(string? preferredName = null)
    {
        WorkspaceTabVisibilityPreset? preferred = _workspaceTabVisibilityPresets.FirstOrDefault(preset =>
            !preset.IsCustom && string.Equals(preset.Name, preferredName, StringComparison.Ordinal));
        if (preferred is not null && WorkspaceTabVisibilityMatches(preferred)) return preferred;

        return _workspaceTabVisibilityPresets.FirstOrDefault(preset =>
                   !preset.IsCustom && WorkspaceTabVisibilityMatches(preset))
               ?? _workspaceTabVisibilityPresets.First(preset => preset.IsCustom);
    }

    private bool WorkspaceTabVisibilityMatches(WorkspaceTabVisibilityPreset preset)
    {
        if (preset.VisibleTabs is null) return false;
        return _workspaceTabVisibilityItems.All(item =>
            item.IsVisible == preset.VisibleTabs.Contains(item.Id));
    }

    private void OnWorkspaceTabVisibilityItemChanged(object? sender, EventArgs e)
    {
        if (_updatingWorkspaceTabVisibility) return;
        _hiddenWorkspaceTabNames.Clear();
        foreach (WorkspaceTabVisibilityItem item in _workspaceTabVisibilityItems.Where(item => !item.IsVisible))
            _hiddenWorkspaceTabNames.Add(item.Id);

        WorkspaceTabVisibilityPreset preset = ResolveWorkspaceTabVisibilityPreset();
        _workspaceTabVisibilityPreset = preset.Name;
        WorkspaceTabPresetSelector.SelectedItem = preset;
        ApplyCurrentWorkspaceTabVisibility(save: true);
    }

    private void OnApplyWorkspaceTabVisibilityPresetClick(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceTabPresetSelector.SelectedItem is not WorkspaceTabVisibilityPreset { IsCustom: false } preset ||
            preset.VisibleTabs is null)
        {
            return;
        }

        _updatingWorkspaceTabVisibility = true;
        try
        {
            _hiddenWorkspaceTabNames.Clear();
            foreach (WorkspaceTabVisibilityItem item in _workspaceTabVisibilityItems)
            {
                item.IsVisible = preset.VisibleTabs.Contains(item.Id);
                if (!item.IsVisible) _hiddenWorkspaceTabNames.Add(item.Id);
            }
            _workspaceTabVisibilityPreset = preset.Name;
        }
        finally
        {
            _updatingWorkspaceTabVisibility = false;
        }

        ApplyCurrentWorkspaceTabVisibility(save: true);
    }

    private void OnShowAllWorkspaceTabsClick(object? sender, RoutedEventArgs e)
    {
        WorkspaceTabVisibilityPreset preset = _workspaceTabVisibilityPresets.First(item => item.Name == "Full workspace");
        WorkspaceTabPresetSelector.SelectedItem = preset;
        OnApplyWorkspaceTabVisibilityPresetClick(sender, e);
    }

    private void ApplyCurrentWorkspaceTabVisibility(bool save)
    {
        UpdateWorkspaceCategoryToggleVisibility();
        ApplyWorkspaceCategory(_workspaceCategory, selectDefault: false);
        UpdateWorkspaceTabVisibilitySummary();
        if (save) SaveCurrentProjectWorkspaceState();
    }

    private void UpdateWorkspaceCategoryToggleVisibility()
    {
        OverviewCategoryToggle.IsVisible = true;
        GitCategoryToggle.IsVisible = EnabledTabsFor(WorkspaceCategory.Git).Length > 0;
        CodeCategoryToggle.IsVisible = EnabledTabsFor(WorkspaceCategory.Code).Length > 0;
        BackupCategoryToggle.IsVisible = EnabledTabsFor(WorkspaceCategory.Backup).Length > 0;
        SyncCategoryToggle.IsVisible = EnabledTabsFor(WorkspaceCategory.Sync).Length > 0;
        PluginModeCategoryToggle.IsVisible = _viewModel?.HasActivePluginOperatingMode == true &&
                                             EnabledTabsFor(WorkspaceCategory.PluginMode).Length > 0;
        NetworkCategoryToggle.IsVisible = EnabledTabsFor(WorkspaceCategory.Network).Length > 0;
        ExtensionsCategoryToggle.IsVisible = EnabledTabsFor(WorkspaceCategory.Extensions).Length > 0;
    }

    private void UpdateWorkspaceTabVisibilitySummary()
    {
        int optionalVisible = _workspaceTabVisibilityItems.Count(item => item.IsVisible && item.IsAvailableInMode);
        int unavailable = _workspaceTabVisibilityItems.Count(item => !item.IsAvailableInMode);
        int totalVisible = optionalVisible + 2;
        int total = _workspaceTabVisibilityItems.Count(item => item.IsAvailableInMode) + 2;
        string project = _viewModel?.SelectedProject?.Name ?? "this project";
        WorkspaceTabVisibilitySummary.Text = $"{totalVisible} of {total} compatible tabs visible · {unavailable} unavailable in this mode · saved for {project}";
    }

    private void ShowHiddenWorkspaceTabTemporarily(TabItem tab)
    {
        WorkspaceCategory category = CategoryFor(tab);
        _workspaceCategory = category;
        UpdateWorkspaceCategoryToggleVisibility();
        ToggleButton categoryToggle = CategoryToggleFor(category);
        categoryToggle.IsVisible = true;
        foreach (WorkspaceCategory candidate in Enum.GetValues<WorkspaceCategory>())
            CategoryToggleFor(candidate).IsChecked = candidate == category;

        TabItem[] normalTabs = EnabledTabsFor(category);
        foreach (TabItem candidate in AllWorkspaceTabs())
            candidate.IsVisible = ReferenceEquals(candidate, tab) || normalTabs.Contains(candidate);
        WorkspaceTabs.SelectedItem = tab;
    }

    private ToggleButton CategoryToggleFor(WorkspaceCategory category) => category switch
    {
        WorkspaceCategory.Overview => OverviewCategoryToggle,
        WorkspaceCategory.Git => GitCategoryToggle,
        WorkspaceCategory.Code => CodeCategoryToggle,
        WorkspaceCategory.Backup => BackupCategoryToggle,
        WorkspaceCategory.Sync => SyncCategoryToggle,
        WorkspaceCategory.PluginMode => PluginModeCategoryToggle,
        WorkspaceCategory.Network => NetworkCategoryToggle,
        _ => ExtensionsCategoryToggle
    };

    private static string WorkspaceCategoryDisplayName(WorkspaceCategory category) => category switch
    {
        WorkspaceCategory.Overview => "Overview",
        WorkspaceCategory.Git => "Git",
        WorkspaceCategory.Code => "Code & Assets",
        WorkspaceCategory.Backup => "Backup",
        WorkspaceCategory.Sync => "Sync",
        WorkspaceCategory.PluginMode => "Plugin mode",
        WorkspaceCategory.Network => "Team & Network",
        _ => "Extensions & Help"
    };

    private static string WorkspaceTabDisplayName(TabItem tab) => tab.Name switch
    {
        "MembersWorkspaceTab" => "Members",
        "NotificationsWorkspaceTab" => "Notifications",
        "VisibleTabsWorkspaceTab" => "Visible tabs",
        "LicenseWorkspaceTab" => "License",
        "ChangesWorkspaceTab" => "Changes",
        "HistoryWorkspaceTab" => "History",
        "CompositionWorkspaceTab" => "Compose",
        "BranchesWorkspaceTab" => "Branches",
        "GitAnnotationsWorkspaceTab" => "Annotations",
        "PullRequestsWorkspaceTab" => "Pull Requests",
        "CiWorkspaceTab" => "CI / Actions",
        "GitGraphsWorkspaceTab" => "Git graphs",
        "LfsLocksWorkspaceTab" => "File locks",
        "GitIgnoreWorkspaceTab" => "Ignore rules",
        "GitLfsWorkspaceTab" => "Git LFS",
        "CyStoreWorkspaceTab" => "CyStore Alpha",
        "BackupsWorkspaceTab" => "Backups",
        "SolutionExplorerWorkspaceTab" => "Solution Explorer",
        "CodeWorkspaceTab" => "Code search",
        "ConsoleWorkspaceTab" => "Console & Logs",
        "AssetDiffWorkspaceTab" => "Asset preview & diff",
        "AiWorkspaceTab" => "AI Assistant",
        "McpWorkspaceTab" => "MCP",
        "SynchronizationWorkspaceTab" => "Syncthing",
        "SyncConflictsWorkspaceTab" => "Sync conflicts",
        "SharedSyncWorkspaceTab" => "Shared Sync folders",
        "VpnWorkspaceTab" => "WireGuard VPN",
        "SwarmWorkspaceTab" => "Swarm over VPN",
        "VpnFilesWorkspaceTab" => "VPN files",
        "TeamChatWorkspaceTab" => "Team chat",
        "RemoteBuildsWorkspaceTab" => "Remote builds",
        "DiscordWorkspaceTab" => "Discord agent",
        "WorkInProgressWorkspaceTab" => "Work in progress",
        "PluginsWorkspaceTab" => "Plugins",
        "UnrealWorkspaceTab" => "Unreal",
        "UnityWorkspaceTab" => "Unity",
        "GodotWorkspaceTab" => "Godot",
        "LoreWorkspaceTab" => "Lore",
        "PerforceWorkspaceTab" => "Perforce",
        "UnrealBuildWorkspaceTab" => "Unreal builds",
        "DiagnosticsWorkspaceTab" => "Diagnostics",
        "HelpWorkspaceTab" => "Help",
        _ => tab.Header?.ToString() ?? tab.Name ?? "Tool"
    };

    private void NavigateToWorkspace(TabItem tab, Control? focusTarget = null)
    {
        if (!IsWorkspaceTabSupportedByMode(tab))
        {
            TabItem fallback = PreferredWorkspaceTabForMode();
            ApplyWorkspaceCategory(CategoryFor(fallback), selectDefault: false);
            WorkspaceTabs.SelectedItem = fallback;
            fallback.Focus();
            return;
        }

        if (!IsWorkspaceTabEnabled(tab))
        {
            ShowHiddenWorkspaceTabTemporarily(tab);
            (focusTarget ?? tab).Focus();
            return;
        }
        ApplyWorkspaceCategory(CategoryFor(tab), selectDefault: false);
        WorkspaceTabs.SelectedItem = tab;
        (focusTarget ?? tab).Focus();
    }

    private void OnOverviewCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Overview, selectDefault: true);

    private void OnGitCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Git, selectDefault: true);

    private void OnCodeCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Code, selectDefault: true);

    private void OnBackupCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Backup, selectDefault: true);

    private void OnSyncCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Sync, selectDefault: true);

    private void OnPluginModeCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.PluginMode, selectDefault: true);

    private void OnNetworkCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Network, selectDefault: true);

    private void OnExtensionsCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Extensions, selectDefault: true);

    private void ApplyWorkspaceCategory(WorkspaceCategory category, bool selectDefault)
    {
        UpdateWorkspaceCategoryToggleVisibility();
        TabItem[] visibleTabs = EnabledTabsFor(category);
        if (visibleTabs.Length == 0)
        {
            category = WorkspaceCategory.Overview;
            visibleTabs = EnabledTabsFor(category);
        }

        _workspaceCategory = category;
        OverviewCategoryToggle.IsChecked = category == WorkspaceCategory.Overview;
        GitCategoryToggle.IsChecked = category == WorkspaceCategory.Git;
        CodeCategoryToggle.IsChecked = category == WorkspaceCategory.Code;
        BackupCategoryToggle.IsChecked = category == WorkspaceCategory.Backup;
        SyncCategoryToggle.IsChecked = category == WorkspaceCategory.Sync;
        PluginModeCategoryToggle.IsChecked = category == WorkspaceCategory.PluginMode;
        NetworkCategoryToggle.IsChecked = category == WorkspaceCategory.Network;
        ExtensionsCategoryToggle.IsChecked = category == WorkspaceCategory.Extensions;

        foreach (TabItem tab in AllWorkspaceTabs())
        {
            tab.IsVisible = visibleTabs.Contains(tab);
        }

        if (selectDefault)
        {
            string key = category.ToString();
            TabItem remembered = _categoryWorkspaceTabs.TryGetValue(key, out string? rememberedName)
                ? visibleTabs.FirstOrDefault(tab => tab.Name == rememberedName) ?? visibleTabs[0]
                : visibleTabs[0];
            WorkspaceTabs.SelectedItem = remembered;
        }
        else if (WorkspaceTabs.SelectedItem is not TabItem selected || !visibleTabs.Contains(selected))
        {
            WorkspaceTabs.SelectedItem = visibleTabs[0];
        }
    }

    private TabItem[] EnabledTabsFor(WorkspaceCategory category)
    {
        return TabsFor(category).Where(IsWorkspaceTabEnabled).ToArray();
    }

    private bool IsWorkspaceTabEnabled(TabItem tab) =>
        IsWorkspaceTabSupportedByMode(tab) &&
        (ReferenceEquals(tab, ProjectWorkspaceTab) ||
         ReferenceEquals(tab, VisibleTabsWorkspaceTab) ||
         string.IsNullOrWhiteSpace(tab.Name) ||
         !_hiddenWorkspaceTabNames.Contains(tab.Name));

    private bool IsWorkspaceTabSupportedByMode(TabItem tab)
    {
        CyRevision.Core.Configuration.ProjectFeatures? features = _viewModel?.SelectedProject?.Definition.Features;
        if (features is null)
            return ReferenceEquals(tab, ProjectWorkspaceTab) || ReferenceEquals(tab, VisibleTabsWorkspaceTab);

        if (ReferenceEquals(tab, SynchronizationWorkspaceTab)) return features.PeerSyncEnabled;
        if (ReferenceEquals(tab, SyncConflictsWorkspaceTab))
            return features.PeerSyncEnabled || _viewModel?.SharedSyncFolders.Count > 0;
        if (ReferenceEquals(tab, UnrealWorkspaceTab) || ReferenceEquals(tab, UnrealBuildWorkspaceTab))
            return _viewModel is { IsUnrealIntegrationEnabled: true, IsUnrealProjectDetected: true };
        if (ReferenceEquals(tab, UnityWorkspaceTab))
            return _viewModel is { IsUnityIntegrationEnabled: true, IsUnityProjectDetected: true };
        if (ReferenceEquals(tab, GodotWorkspaceTab))
            return _viewModel is { IsGodotIntegrationEnabled: true, IsGodotProjectDetected: true };
        if (ReferenceEquals(tab, LoreWorkspaceTab))
            return _viewModel is { IsLoreIntegrationEnabled: true, IsLoreProjectDetected: true };
        if (ReferenceEquals(tab, PerforceWorkspaceTab))
            return _viewModel?.IsPerforceIntegrationEnabled == true;
        if (ReferenceEquals(tab, CyStoreWorkspaceTab))
            return _viewModel?.IsCyStorePluginEnabled == true;
        if (ReferenceEquals(tab, AiWorkspaceTab)) return _viewModel?.IsAiIntegrationEnabled == true;
        return !IsGitWorkspaceTab(tab) || features.GitEnabled;
    }

    private bool IsGitWorkspaceTab(TabItem tab) =>
        ReferenceEquals(tab, ChangesWorkspaceTab) ||
        ReferenceEquals(tab, HistoryWorkspaceTab) ||
        ReferenceEquals(tab, CompositionWorkspaceTab) ||
        ReferenceEquals(tab, BranchesWorkspaceTab) ||
        ReferenceEquals(tab, GitAnnotationsWorkspaceTab) ||
        ReferenceEquals(tab, PullRequestsWorkspaceTab) ||
        ReferenceEquals(tab, CiWorkspaceTab) ||
        ReferenceEquals(tab, GitGraphsWorkspaceTab) ||
        ReferenceEquals(tab, LfsLocksWorkspaceTab) ||
        ReferenceEquals(tab, GitIgnoreWorkspaceTab) ||
        ReferenceEquals(tab, GitLfsWorkspaceTab);

    private TabItem PreferredWorkspaceTabForMode()
    {
        CyRevision.Core.Configuration.ProjectFeatures? features = _viewModel?.SelectedProject?.Definition.Features;
        TabItem? pluginModeTab = EnabledTabsFor(WorkspaceCategory.PluginMode).FirstOrDefault();
        if (pluginModeTab is not null) return pluginModeTab;
        if (features?.GitEnabled == true && IsWorkspaceTabEnabled(ChangesWorkspaceTab)) return ChangesWorkspaceTab;
        if (features?.PeerSyncEnabled == true && IsWorkspaceTabEnabled(SynchronizationWorkspaceTab)) return SynchronizationWorkspaceTab;
        if (features?.BackupEnabled == true && IsWorkspaceTabEnabled(BackupsWorkspaceTab)) return BackupsWorkspaceTab;
        return ProjectWorkspaceTab;
    }

    private void RefreshWorkspaceModeNavigation(bool selectPrimary)
    {
        foreach (WorkspaceTabVisibilityItem item in _workspaceTabVisibilityItems)
        {
            TabItem? tab = AllWorkspaceTabs().FirstOrDefault(candidate => candidate.Name == item.Id);
            item.IsAvailableInMode = tab is not null && IsWorkspaceTabSupportedByMode(tab);
        }

        CyRevision.Core.Configuration.ProjectFeatures? features = _viewModel?.SelectedProject?.Definition.Features;
        SyncCategoryToggle.Content = _viewModel?.IsSyncCommitMode == true
            ? "Sync + Commit"
            : features switch
            {
                { GitEnabled: true, PeerSyncEnabled: true } => "Git + Sync",
                { PeerSyncEnabled: true, BackupEnabled: true } => "Sync + Versions",
                { PeerSyncEnabled: true } => "Sync",
                _ => "Sync"
            };
        FetchRemotesButton.IsVisible = features?.GitEnabled == true;
        PullButton.IsVisible = features?.GitEnabled == true;
        PushButton.IsVisible = features?.GitEnabled == true;

        UpdateWorkspaceCategoryToggleVisibility();
        UpdateWorkspaceTabVisibilitySummary();

        TabItem target = selectPrimary
            ? PreferredWorkspaceTabForMode()
            : WorkspaceTabs.SelectedItem is TabItem selected && IsWorkspaceTabEnabled(selected)
                ? selected
                : PreferredWorkspaceTabForMode();
        ApplyWorkspaceCategory(CategoryFor(target), selectDefault: false);
        WorkspaceTabs.SelectedItem = target;
    }

    private WorkspaceCategory CategoryFor(TabItem tab)
    {
        foreach (WorkspaceCategory category in Enum.GetValues<WorkspaceCategory>())
        {
            if (TabsFor(category).Contains(tab))
            {
                return category;
            }
        }

        return _workspaceCategory;
    }

    private TabItem[] TabsFor(WorkspaceCategory category) => category switch
    {
        WorkspaceCategory.Overview => [ProjectWorkspaceTab, MembersWorkspaceTab, NotificationsWorkspaceTab, VisibleTabsWorkspaceTab, LicenseWorkspaceTab],
        WorkspaceCategory.Git =>
        [
            ChangesWorkspaceTab, HistoryWorkspaceTab, CompositionWorkspaceTab, BranchesWorkspaceTab,
            GitAnnotationsWorkspaceTab, PullRequestsWorkspaceTab, CiWorkspaceTab, GitGraphsWorkspaceTab, LfsLocksWorkspaceTab, GitIgnoreWorkspaceTab, GitLfsWorkspaceTab
        ],
        WorkspaceCategory.Code => [SolutionExplorerWorkspaceTab, CodeWorkspaceTab, ConsoleWorkspaceTab, AssetDiffWorkspaceTab, AiWorkspaceTab, McpWorkspaceTab],
        WorkspaceCategory.Backup => [BackupsWorkspaceTab],
        WorkspaceCategory.Sync => [SynchronizationWorkspaceTab, SyncConflictsWorkspaceTab],
        WorkspaceCategory.PluginMode => (_viewModel?.ActivePluginOperatingModeWorkspaceTabs ?? [])
            .Select(tabId => AllWorkspaceTabs().FirstOrDefault(tab =>
                string.Equals(tab.Name, tabId, StringComparison.OrdinalIgnoreCase)))
            .Where(tab => tab is not null)
            .Cast<TabItem>()
            .ToArray(),
        WorkspaceCategory.Network =>
        [
            SharedSyncWorkspaceTab, VpnWorkspaceTab, SwarmWorkspaceTab, VpnFilesWorkspaceTab, TeamChatWorkspaceTab,
            RemoteBuildsWorkspaceTab, DiscordWorkspaceTab, WorkInProgressWorkspaceTab
        ],
        _ => [PluginsWorkspaceTab, CyStoreWorkspaceTab, UnrealWorkspaceTab, UnityWorkspaceTab, GodotWorkspaceTab, LoreWorkspaceTab, PerforceWorkspaceTab, UnrealBuildWorkspaceTab, DiagnosticsWorkspaceTab, HelpWorkspaceTab]
    };

    private TabItem[] AllWorkspaceTabs() =>
    [
        ProjectWorkspaceTab, MembersWorkspaceTab, NotificationsWorkspaceTab, VisibleTabsWorkspaceTab, LicenseWorkspaceTab,
        ChangesWorkspaceTab, SolutionExplorerWorkspaceTab, CodeWorkspaceTab,
        ConsoleWorkspaceTab, HistoryWorkspaceTab,
        CompositionWorkspaceTab, BranchesWorkspaceTab, GitAnnotationsWorkspaceTab, PullRequestsWorkspaceTab, CiWorkspaceTab, GitGraphsWorkspaceTab,
        LfsLocksWorkspaceTab, GitIgnoreWorkspaceTab, GitLfsWorkspaceTab, CyStoreWorkspaceTab, BackupsWorkspaceTab, SynchronizationWorkspaceTab, SyncConflictsWorkspaceTab, SharedSyncWorkspaceTab, VpnWorkspaceTab,
        SwarmWorkspaceTab, VpnFilesWorkspaceTab, TeamChatWorkspaceTab, RemoteBuildsWorkspaceTab, DiscordWorkspaceTab,
        WorkInProgressWorkspaceTab, AssetDiffWorkspaceTab, AiWorkspaceTab, McpWorkspaceTab,
        PluginsWorkspaceTab, UnrealWorkspaceTab, UnityWorkspaceTab, GodotWorkspaceTab, LoreWorkspaceTab, PerforceWorkspaceTab, UnrealBuildWorkspaceTab, DiagnosticsWorkspaceTab, HelpWorkspaceTab
    ];

    private void OnShowProjectWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(ProjectWorkspaceTab);

    private async void OnRefreshProjectMembersClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshProjectMembersAsync();

    private void OnShowChangesWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(ChangesWorkspaceTab);

    private void OnShowCodeWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(CodeWorkspaceTab);

    private void OnShowConsoleWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        NavigateToWorkspace(ConsoleWorkspaceTab, RepositoryConsoleInput);
        RepositoryConsoleInput.Focus();
    }

    private async void OnRunRepositoryCommandClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RunRepositoryCommandAsync();

    private void OnStopRepositoryCommandClick(object? sender, RoutedEventArgs e) =>
        _viewModel.StopRepositoryCommand();

    private void OnClearRepositoryConsoleOutputClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ClearRepositoryConsoleOutput();

    private async void OnClearRepositoryConsoleHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                "Clear repository command history",
                "Remove the saved console commands for the selected project? This does not execute or change anything in the repository.",
                "Clear history"))
        {
            _viewModel.ClearRepositoryConsoleHistory();
        }
    }

    private void OnUseRepositoryCommandClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.UseSelectedRepositoryCommand();
        RepositoryConsoleInput.Focus();
        RepositoryConsoleInput.CaretIndex = RepositoryConsoleInput.Text?.Length ?? 0;
    }

    private void OnRepositoryCommandHistoryDoubleTapped(object? sender, TappedEventArgs e) =>
        OnUseRepositoryCommandClick(sender, e);

    private async void OnRepositoryConsoleInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            await _viewModel.RunRepositoryCommandAsync();
            return;
        }
        if (e.Key is not (Key.Up or Key.Down)) return;
        e.Handled = true;
        _viewModel.NavigateRepositoryCommandHistory(e.Key == Key.Up ? -1 : 1);
        RepositoryConsoleInput.CaretIndex = RepositoryConsoleInput.Text?.Length ?? 0;
    }

    private void OnRepositoryConsoleOutputTextChanged(object? sender, TextChangedEventArgs e)
    {
        RepositoryConsoleOutputBox.CaretIndex = RepositoryConsoleOutputBox.Text?.Length ?? 0;
    }

    private void OnRefreshApplicationLogsClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RefreshApplicationLogs();

    private void OnClearApplicationLogViewClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ClearApplicationLogView();

    private void OnOpenApplicationLogFolderClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _viewModel.ApplicationLogDirectory,
            UseShellExecute = true
        });
    }

    private void OnSolutionTreeLayoutClick(object? sender, RoutedEventArgs e) => ApplySolutionExplorerLayout(tree: true);

    private void OnSolutionListLayoutClick(object? sender, RoutedEventArgs e) => ApplySolutionExplorerLayout(tree: false);

    private void ApplySolutionExplorerLayout(bool tree)
    {
        _solutionExplorerTreeLayout = tree;
        SolutionTreeLayoutToggle.IsChecked = tree;
        SolutionListLayoutToggle.IsChecked = !tree;
        bool searching = _viewModel.HasCodeFileSearchResults;
        SolutionSearchResultsPanel.IsVisible = searching;
        SolutionTreePanel.IsVisible = tree && !searching;
        SolutionListPanel.IsVisible = !tree && !searching;
    }

    private void OnShowHistoryWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(HistoryWorkspaceTab);

    private void OnShowMultiRestoreWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        NavigateToWorkspace(CompositionWorkspaceTab);
        MainRevisionCompositionView.SelectSection(RevisionCompositionSection.MultiRestore);
        _ = _viewModel.LoadMultiRestoreCommitAsync(_viewModel.SelectedExplorerRevision);
    }

    private void OnShowCherryPickWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        NavigateToWorkspace(CompositionWorkspaceTab);
        MainRevisionCompositionView.SelectSection(RevisionCompositionSection.CherryPick);
    }


    private void OnShowGitGraphsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(GitGraphsWorkspaceTab);

    private void OnShowBranchesWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(BranchesWorkspaceTab);

    private void OnShowPullRequestsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(PullRequestsWorkspaceTab);

    private void OnShowGitLfsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(GitLfsWorkspaceTab);

    private void OnShowCyStoreWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(CyStoreWorkspaceTab);

    private void OnShowLfsLocksWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(LfsLocksWorkspaceTab);

    private void OnShowBackupsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(BackupsWorkspaceTab);

    private void OnShowSynchronizationWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(SynchronizationWorkspaceTab);

    private void OnShowVpnWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(VpnWorkspaceTab);

    private void OnShowSwarmWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(SwarmWorkspaceTab);

    private void OnShowVpnFilesWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(VpnFilesWorkspaceTab);

    private void OnShowRemoteBuildsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(RemoteBuildsWorkspaceTab);

    private void OnShowDiscordWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(DiscordWorkspaceTab);

    private void OnShowAiWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(AiWorkspaceTab);

    private void OnShowMcpWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(McpWorkspaceTab);

    private void OnShowPluginsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(PluginsWorkspaceTab);

    private void OnShowHelpWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(HelpWorkspaceTab);

    private void OnOpenCurrentPageDocumentationClick(object? sender, RoutedEventArgs e)
    {
        string pageName = WorkspaceTabs.SelectedItem is TabItem tab
            ? WorkspaceTabDisplayName(tab)
            : string.Empty;
        _viewModel.DocumentationSearch = pageName;
        NavigateToWorkspace(HelpWorkspaceTab);
    }

    private void OnOpenGlobalSearchClick(object? sender, RoutedEventArgs e)
    {
        NavigateToWorkspace(CodeWorkspaceTab, GlobalCodeSearch);
        GlobalCodeSearch.SelectAll();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnToggleLaunchAtLoginClick(object? sender, RoutedEventArgs e) =>
        _desktopBehaviorToggle?.Invoke(DesktopBehaviorSetting.LaunchAtLogin);

    private void OnToggleStartHiddenAtLoginClick(object? sender, RoutedEventArgs e) =>
        _desktopBehaviorToggle?.Invoke(DesktopBehaviorSetting.StartHiddenAtLogin);

    private void OnToggleCloseToTrayClick(object? sender, RoutedEventArgs e) =>
        _desktopBehaviorToggle?.Invoke(DesktopBehaviorSetting.CloseToTray);

    private void OnToggleShowTrayIconClick(object? sender, RoutedEventArgs e) =>
        _desktopBehaviorToggle?.Invoke(DesktopBehaviorSetting.ShowTrayIcon);

    private async void OnAboutClick(object? sender, RoutedEventArgs e) => await ShowAboutAsync();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        KeyModifiers required = KeyModifiers.Control | KeyModifiers.Shift;
        if (e.Key == Key.F && (e.KeyModifiers & required) == required)
        {
            NavigateToWorkspace(CodeWorkspaceTab, GlobalCodeSearch);
            GlobalCodeSearch.SelectAll();
            e.Handled = true;
        }
    }

    private async void OnCodeSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _viewModel.SearchCodeAsync();
    }

    private async void OnSearchCodeClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SearchCodeAsync();

    private void OnCancelCodeSearchClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelCodeSearch();

    private async void OnRefreshCodeWorkspaceClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshCodeWorkspaceAsync(preserveLoadedTree: false);

    private async void OnSolutionTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem { DataContext: CodeTreeNode directory } &&
            _viewModel.SelectedProject is { } project)
        {
            await _viewModel.LoadCodeDirectoryAsync(project, directory);
        }
    }

    private async void OnCodeSelectionHistoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadCodeSelectionHistoryAsync(CodePreview.SelectionStart, CodePreview.SelectionEnd);

    private async void OnRunAiAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RunAiAgentAsync();

    private async void OnDetectCodexClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DetectCodexAsync(autoConnect: false);

    private async void OnToggleCodexConnectionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ToggleCodexChatConnectionAsync();

    private async void OnSendAiChatClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SendCodexChatMessageAsync();

    private async void OnGenerateAiCommitDescriptionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.GenerateAiCommitDescriptionAsync();

    private async void OnGenerateAiPullRequestDraftClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.GenerateAiPullRequestDraftAsync();

    private async void OnGenerateAiCodeSummaryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.GenerateAiCodeSummaryAsync();

    private void OnClearAiCodeSummaryClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ClearAiCodeSummary();

    private void OnCancelAiChatClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelAiChat();

    private async void OnCreateAiConversationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateAiConversationAsync();

    private async void OnDeleteAiConversationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DeleteSelectedAiConversationAsync();

    private async void OnSaveAiConversationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveAiConversationSettingsAsync();

    private void OnAddAiMcpStdioClick(object? sender, RoutedEventArgs e) =>
        _viewModel.AddAiMcpServer(CyRevision.Plugin.Abstractions.AiMcpTransport.Stdio);

    private void OnAddAiMcpHttpClick(object? sender, RoutedEventArgs e) =>
        _viewModel.AddAiMcpServer(CyRevision.Plugin.Abstractions.AiMcpTransport.StreamableHttp);

    private void OnRemoveAiMcpClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RemoveSelectedAiMcpServer();

    private async void OnSaveAiMcpClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveAiMcpProfileAsync();

    private async void OnEmergencyBlockAiMcpClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.EmergencyBlockAiMcpAsync();

    private async void OnUnblockAiMcpClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.UnblockAiMcpAsync();

    private async void OnAddExistingClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner un dépôt Git existant");
        if (path is not null)
        {
            await _viewModel.AddExistingRepositoryAsync(path);
        }
    }

    private async void OnCreateGitClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner le dossier du nouveau dépôt Git");
        if (path is not null)
        {
            GitInitializationOptions? options = await ShowGitInitializationWizardAsync(path);
            if (options is not null)
                await _viewModel.CreateGitRepositoryAsync(path, options);
        }
    }

    private async void OnCloneGitClick(object? sender, RoutedEventArgs e) =>
        await ShowCloneRepositoryDialogAsync();

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner un dossier à synchroniser ou sauvegarder");
        if (path is not null)
        {
            await _viewModel.AddFolderProjectAsync(path);
        }
    }

    private async void OnRemoveProjectClick(object? sender, RoutedEventArgs e)
    {
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        if (project is null) return;
        ProjectRemovalChoice? choice = await ShowRemoveProjectConfirmationAsync(project);
        if (choice?.Confirmed == true)
        {
            await _viewModel.RemoveSelectedProjectAsync(choice.RemoveGeneratedCaches);
        }
    }

    private async void OnMoveProjectUpClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.MoveSelectedProjectAsync(-1);

    private async void OnMoveProjectDownClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.MoveSelectedProjectAsync(1);

    private async void OnProjectAccentColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string accentColor } && DataContext is MainWindowViewModel)
        {
            await _viewModel.SetSelectedProjectAccentColorAsync(accentColor);
        }
    }

    private async void OnSaveProjectGroupClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SetSelectedProjectSidebarGroupAsync();

    private void OnProjectGroupExpansionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ProjectSidebarGroupViewModel group })
            _viewModel.RememberProjectGroupExpansion(group);
    }

    private void OnGroupedProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ProjectItemViewModel project })
            _viewModel.SelectedProject = project;
    }

    private async void OnProjectAutoStartSyncClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await _viewModel.SetProjectServiceAutoStartAsync(sync: checkBox.IsChecked == true);
    }

    private async void OnProjectAutoStartVpnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await _viewModel.SetProjectServiceAutoStartAsync(vpn: checkBox.IsChecked == true);
    }

    private async void OnRefreshProjectLicenseClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshProjectLicenseAsync();

    private void OnApplyProjectLicenseTemplateClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ApplySelectedProjectLicenseTemplate();

    private async void OnImportProjectLicenseClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Import a license text file");
        if (path is not null) await _viewModel.ImportProjectLicenseDraftAsync(path);
    }

    private async void OnSaveProjectLicenseClick(object? sender, RoutedEventArgs e)
    {
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        if (project is null || !_viewModel.CanSaveProjectLicense) return;

        bool overwrite = _viewModel.ProjectLicenseTargetExists;
        string fileName = _viewModel.ProjectLicenseFileName.Trim();
        string action = overwrite ? "Replace" : "Create";
        if (!await ShowConfirmationAsync(
                overwrite ? "Replace project license" : "Add project license",
                $"{action} '{fileName}' in {project.Name}? This changes a project file and may affect redistribution rights. Review the complete terms before continuing.",
                overwrite ? "Replace license" : "Create license"))
        {
            return;
        }

        await _viewModel.SaveProjectLicenseAsync(overwrite);
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();

    private void OnOpenProjectFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = _viewModel.SelectedProject?.RootPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private async void OnAnalyzeGitGraphsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeGitGraphsAsync();

    private async void OnGraphCommitSelected(object? sender, GitCommitSelectedEventArgs e) =>
        await _viewModel.SelectExplorerCommitAsync(e.Commit.Hash);

    private async void OnCompareExplorerCommitsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CompareExplorerCommitsAsync();

    private async void OnExportExplorerFileClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickSaveFileAsync(
            _viewModel.SelectedExplorerFile?.Path,
            "Exporter cette version");
        if (path is not null)
        {
            await _viewModel.ExportSelectedExplorerFileAsync(path);
        }
    }

    private async void OnStageAllClick(object? sender, RoutedEventArgs e) => await _viewModel.StageAllAsync();

    private async void OnUnstageAllClick(object? sender, RoutedEventArgs e) => await _viewModel.UnstageAllAsync();

    private async void OnScanAllUntrackedClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ScanAllUntrackedFilesAsync();

    private void OnIncludeAllChangesClick(object? sender, RoutedEventArgs e) =>
        _viewModel.IncludeAllPreparedChanges();

    private void OnKeepAllChangesClick(object? sender, RoutedEventArgs e) =>
        _viewModel.KeepAllPreparedChanges();

    private void OnDeselectVersionedChangesClick(object? sender, RoutedEventArgs e) =>
        _viewModel.DeselectVersionedChanges();

    private void OnDeselectUnversionedChangesClick(object? sender, RoutedEventArgs e) =>
        _viewModel.DeselectUnversionedChanges();

    private async void OnSetSelectedChangeLocalClick(object? sender, RoutedEventArgs e) =>
        await SetChangesLocalOnlyAsync(true);

    private async void OnRestoreSelectedLocalChangeClick(object? sender, RoutedEventArgs e) =>
        await SetChangesLocalOnlyAsync(false);

    private async void OnSetContextChangesLocalClick(object? sender, RoutedEventArgs e) =>
        await SetChangesLocalOnlyAsync(true, fallBackToChecked: false);

    private async void OnRestoreContextChangesClick(object? sender, RoutedEventArgs e) =>
        await SetChangesLocalOnlyAsync(false, fallBackToChecked: false);

    private async Task SetChangesLocalOnlyAsync(bool isLocalOnly, bool fallBackToChecked = true)
    {
        if (_viewModel.IsBusy) return;
        await _viewModel.SetChangesLocalOnlyAsync(GetChangeActionTargets(fallBackToChecked), isLocalOnly);
    }

    private async void OnRollbackSelectedChangeClick(object? sender, RoutedEventArgs e)
    {
        await ConfirmRollbackChangesAsync(GetChangeActionTargets());
    }

    private async void OnRollbackContextChangesClick(object? sender, RoutedEventArgs e) =>
        await ConfirmRollbackChangesAsync(GetChangeActionTargets(fallBackToChecked: false));

    private async Task ConfirmRollbackChangesAsync(IReadOnlyCollection<GitChangeViewModel> changes)
    {
        if (_viewModel.IsBusy || changes.Count == 0) return;
        int untracked = changes.Count(change => change.IsUntracked);
        string deletionWarning = untracked > 0
            ? $" {untracked:N0} untracked file(s) will be deleted from disk and cannot be recovered from Git."
            : string.Empty;
        if (await ShowConfirmationAsync(
                "Rollback selected changes",
                $"Discard changes in {changes.Count:N0} file(s)? Tracked files return to HEAD.{deletionWarning}",
                "Rollback selected"))
        {
            await _viewModel.RollbackChangesAsync(changes);
        }
    }

    private async void OnDeleteSelectedChangesClick(object? sender, RoutedEventArgs e) =>
        await ConfirmDeleteChangesAsync(fallBackToChecked: true);

    private async void OnDeleteContextChangesClick(object? sender, RoutedEventArgs e) =>
        await ConfirmDeleteChangesAsync(fallBackToChecked: false);

    private async Task ConfirmDeleteChangesAsync(bool fallBackToChecked)
    {
        if (_viewModel.IsBusy) return;
        GitChangeViewModel[] changes = GetChangeActionTargets(fallBackToChecked);
        GitChangeTreeNode[] treeNodes = GetSelectedChangeTreeNodes();
        GitChangeTreeNode[] directories = treeNodes
            .Where(node => node.IsDirectory && !string.IsNullOrWhiteSpace(node.RelativePath))
            .ToArray();
        string[] paths = treeNodes.Length > 0
            ? treeNodes.SelectMany(node =>
                    node.IsDirectory && !string.IsNullOrWhiteSpace(node.RelativePath)
                        ? [node.RelativePath]
                        : node.ContainedChanges.Select(change => change.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : changes.Select(change => change.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        if (paths.Length == 0) return;

        int untracked = changes.Count(change => change.IsUntracked);
        bool deleteDirectories = directories.Length > 0;
        string directoryNames = string.Join(", ", directories.Take(3).Select(node => $"'{node.RelativePath}'"));
        string effect = deleteDirectories
            ? $"Delete {directories.Length:N0} selected folder(s) recursively from disk ({directoryNames})? Every file in them will be removed, including files that are not currently shown as changed."
            : $"Delete {paths.Length:N0} selected file(s) from disk?";
        string recovery = untracked > 0
            ? $" {untracked:N0} listed untracked file(s) cannot be recovered from Git."
            : string.Empty;
        if (await ShowConfirmationAsync(
                deleteDirectories ? "Delete folders from disk" : "Delete files from disk",
                $"{effect} Tracked files remain visible as Git deletions and can be restored from HEAD.{recovery}",
                deleteDirectories ? "Delete folders" : "Delete files"))
        {
            await _viewModel.DeleteWorkingTreePathsAsync(paths);
        }
    }

    private GitChangeTreeNode[] GetSelectedChangeTreeNodes()
    {
        if (_lastChangesSelectionTree?.SelectedItems is { Count: > 0 } selectedItems)
        {
            return selectedItems.Cast<object>()
                .OfType<GitChangeTreeNode>()
                .Where(node => !node.IsPlaceholder)
                .Distinct()
                .ToArray();
        }

        return _lastChangesSelectionGrid is null &&
               _viewModel.SelectedChangeTreeNode is { IsPlaceholder: false } node
            ? [node]
            : [];
    }

    private GitChangeViewModel[] GetChangeActionTargets(bool fallBackToChecked = true)
    {
        IEnumerable<GitChangeViewModel> candidates;
        if (_lastChangesSelectionGrid is not null)
        {
            candidates = _lastChangesSelectionGrid.SelectedItems.Cast<object>().OfType<GitChangeViewModel>();
        }
        else if (_lastChangesSelectionTree is not null)
        {
            candidates = _lastChangesSelectionTree.SelectedItems.Cast<object>()
                .OfType<GitChangeTreeNode>()
                .Where(node => !node.IsPlaceholder)
                .SelectMany(node => node.ContainedChanges);
        }
        else if (_viewModel.SelectedChangeTreeNode is { } node && !node.IsPlaceholder)
        {
            candidates = node.ContainedChanges;
        }
        else if (_viewModel.SelectedChange is { } selectedChange)
        {
            candidates = [selectedChange];
        }
        else
        {
            candidates = [];
        }

        GitChangeViewModel[] result = candidates
            .DistinctBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (result.Length == 0 && fallBackToChecked)
        {
            result = _viewModel.Changes
                .Where(change => change.IsIncluded && !change.IsLocalOnly)
                .DistinctBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        return result;
    }

    private async void OnRollbackIncludedChangesClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        GitChangeViewModel[] changes = _viewModel.Changes
            .Where(change => change.IsIncluded && !change.IsLocalOnly)
            .ToArray();
        if (changes.Length == 0)
        {
            return;
        }

        int untracked = changes.Count(change => change.IsUntracked);
        string deletionWarning = untracked > 0
            ? $" {untracked} untracked file(s) will be deleted from disk and cannot be recovered from Git."
            : string.Empty;
        if (await ShowConfirmationAsync(
                "Rollback selected changes",
                $"Discard changes in {changes.Length} selected file(s)? Tracked files return to HEAD.{deletionWarning}",
                "Rollback selected"))
        {
            await _viewModel.RollbackIncludedChangesAsync();
        }
    }

    private async void OnCommitClick(object? sender, RoutedEventArgs e)
    {
        GitChangeViewModel[] foreignLocks = _viewModel.Changes
            .Where(change => change.IsIncluded && change.HasForeignLock)
            .ToArray();
        if (foreignLocks.Length > 0)
        {
            string examples = string.Join(", ", foreignLocks.Take(4).Select(change =>
                $"{change.Path} ({change.FileLock!.OwnerName})"));
            if (!await ShowConfirmationAsync(
                    "Commit files locked by teammates",
                    $"{foreignLocks.Length} selected file(s) are locked by another user: {examples}. The lock is not removed. Commit anyway?",
                    "Commit anyway"))
            {
                return;
            }
        }

        await _viewModel.CommitAsync();
    }

    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateBranchAsync(NewBranchName.Text ?? string.Empty);

    private async void OnCreateBranchFromCommitClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateBranchFromSelectedCommitAsync(NewBranchName.Text ?? string.Empty);

    private async void OnRefreshHistoricalWorktreesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshHistoricalWorktreesAsync();

    private void OnOpenHistoricalWorktreeClick(object? sender, RoutedEventArgs e)
    {
        string? path = _viewModel.SelectedHistoricalWorktree?.Path;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private async void OnRemoveHistoricalWorktreeClick(object? sender, RoutedEventArgs e)
    {
        GitHistoricalWorktree? worktree = _viewModel.SelectedHistoricalWorktree;
        if (worktree is null) return;
        if (await ShowConfirmationAsync(
                "Remove historical worktree",
                $"Remove the isolated worktree '{worktree.DisplayName}'? Git refuses removal when it contains local changes, so your work stays protected.",
                "Remove worktree"))
        {
            await _viewModel.RemoveSelectedHistoricalWorktreeAsync(force: false);
        }
    }

    private async void OnCheckoutBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckoutSelectedBranchAsync();

    private void OnBranchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        _viewModel.SetSelectedBranches(listBox.SelectedItems is { } items ? items.OfType<GitBranch>() : []);
    }

    private async void OnMergeBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.MergeSelectedBranchAsync();

    private async void OnMergeCurrentBranchIntoSelectedClick(object? sender, RoutedEventArgs e)
    {
        GitBranch? target = _viewModel.SelectedBranch;
        if (target is null || target.IsCurrent || target.IsRemote) return;
        if (await ShowConfirmationAsync(
                Translate("Merge current branch into selected branch"),
                $"{Translate("CyRevision will switch to the target branch and merge the branch that is currently checked out into it.")}\n\n{target.Name}\n\n" +
                Translate("The working tree must be clean. If conflicts occur, the three-way resolver and its safety backup will be available."),
                Translate("Merge to selected branch")))
        {
            await _viewModel.MergeCurrentBranchIntoSelectedAsync();
        }
    }

    private async void OnRemoveLocalBranchClick(object? sender, RoutedEventArgs e)
    {
        if (_branchRemovalFlowActive)
            return;
        _branchRemovalFlowActive = true;
        Button? actionButton = sender as Button;
        if (actionButton is not null)
            actionButton.IsEnabled = false;
        try
        {
            await RunRemoveLocalBranchFlowAsync();
        }
        finally
        {
            _branchRemovalFlowActive = false;
            if (actionButton is not null)
                actionButton.IsEnabled = _viewModel.CanRemoveSelectedLocalBranch;
        }
    }

    private async Task RunRemoveLocalBranchFlowAsync()
    {
        IReadOnlyList<GitLocalBranchRemovalAnalysis> analyses = await _viewModel.AnalyzeSelectedLocalBranchRemovalsAsync();
        if (analyses.Count == 0)
            return;

        GitLocalBranchRemovalAnalysis[] blocked = analyses
            .Where(analysis => !analysis.CanRemoveSafely && !analysis.CanForceRemove)
            .ToArray();
        if (blocked.Length > 0)
        {
            string details = string.Join("\n", blocked.Select(item => $"• {item.BranchName}: {Translate(item.SafetyMessage)}"));
            await ShowMessageAsync(Translate("Some local branches are protected"), details);
            return;
        }

        GitLocalBranchRemovalAnalysis[] risky = analyses.Where(analysis => !analysis.CanRemoveSafely).ToArray();
        bool forceUnretained = risky.Length > 0;
        string branchList = string.Join("\n", analyses.Select(analysis =>
            $"• {analysis.BranchName} — {Translate(analysis.SafetyMessage)}"));
        if (forceUnretained)
        {
            bool understandsRisk = await ShowConfirmationAsync(
                Translate("Force-remove unretained local branches"),
                $"{risky.Length:N0} {Translate("selected branch(es) contain commits that are not retained by a verified remote or the current branch.")}\n\n{branchList}\n\n" +
                Translate("This mode is intended for disposable test branches. CyRevision cannot restore an unretained branch unless another reference or backup contains it."),
                Translate("I understand the risk"));
            if (!understandsRisk || !await ShowConfirmationAsync(
                    Translate("Final force-removal confirmation"),
                    Translate("Remove the selected local branch references now? The working project and every remote branch remain untouched."),
                    Translate("Force-remove selected branches")))
                return;
        }
        else if (!await ShowConfirmationAsync(
                     Translate("Remove selected local branches"),
                     $"{branchList}\n\n" +
                     Translate("Only local branch references are removed. Remote branches remain untouched. CyRevision refreshes the branch index once after the batch."),
                     Translate("Remove selected branches")))
        {
            return;
        }

        int removed = await _viewModel.RemoveLocalBranchesAsync(analyses, forceUnretained);
        if (removed == 0)
            return;

        bool previewPrune = await ShowConfirmationAsync(
            Translate("Local branches removed"),
            $"{removed:N0} {Translate("local branch(es) removed. Remote references were left untouched and the branch index was refreshed once.")}\n\n" +
            Translate("Preview the native Git LFS cleanup now? The preview is read-only and normally much faster than the advanced retention analysis."),
            Translate("Preview Git LFS cleanup"),
            Translate("Later"));
        if (!previewPrune)
            return;

        await _viewModel.RunNativeLfsPruneAsync(dryRun: true);

        if (await ShowConfirmationAsync(
                Translate("Run native Git LFS prune"),
                Translate("The preview is complete. Run the verified native prune now? Objects removed from the local cache can be downloaded again from the remote when needed."),
                Translate("Run verified prune")))
        {
            await _viewModel.RunNativeLfsPruneAsync(dryRun: false);
        }
    }

    private async void OnFetchClick(object? sender, RoutedEventArgs e) => await _viewModel.FetchAsync();

    private async void OnPullClick(object? sender, RoutedEventArgs e) => await _viewModel.PullAsync();

    private async void OnPushClick(object? sender, RoutedEventArgs e) => await _viewModel.PushAsync();

    private async void OnSaveRemoteClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveRemoteAsync();

    private void OnNewGitAnnotationClick(object? sender, RoutedEventArgs e) =>
        _viewModel.NewGitAnnotation();

    private void OnUseSelectedCommitAnnotationClick(object? sender, RoutedEventArgs e) =>
        _viewModel.UseSelectedCommitForAnnotation();

    private void OnUseSelectedBranchAnnotationClick(object? sender, RoutedEventArgs e) =>
        _viewModel.UseSelectedBranchForAnnotation();

    private async void OnSaveGitAnnotationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveGitAnnotationAsync();

    private async void OnDeleteGitAnnotationClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGitAnnotation is null) return;
        if (await ShowConfirmationAsync(
                "Delete local annotation",
                "Remove this annotation from CyRevision local storage? Git history and project files are not modified.",
                "Delete annotation"))
            await _viewModel.DeleteSelectedGitAnnotationAsync();
    }

    private async void OnResolvePullRequestRepositoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ResolvePullRequestRepositoryAsync();

    private async void OnRefreshPullRequestsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshPullRequestsAsync();

    private async void OnRefreshPullRequestCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshSelectedPullRequestCiAsync();

    private async void OnDispatchPullRequestCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DispatchSelectedPullRequestCiWorkflowAsync();

    private async void OnRerunPullRequestCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RerunSelectedPullRequestCiAsync();

    private void OnOpenPullRequestCiRunClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPullRequestCiRun is not { } run) return;
        Process.Start(new ProcessStartInfo { FileName = run.WebUrl.AbsoluteUri, UseShellExecute = true });
    }

    private void OnPullRequestCiJobsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid ||
            !e.GetCurrentPoint(grid).Properties.IsRightButtonPressed ||
            e.Source is not Control source ||
            source.FindAncestorOfType<DataGridRow>() is not
                { DataContext: CyRevision.PullRequests.CiWorkflowJob job })
            return;
        grid.SelectedItem = job;
        _viewModel.SelectedPullRequestCiJob = job;
    }

    private void OnPullRequestCiJobContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu) return;
        foreach (MenuItem item in contextMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "CancelRun")
                item.IsEnabled = _viewModel.CanCancelSelectedPullRequestCiRun;
        }
    }

    private async void OnCancelPullRequestCiRunClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPullRequestCiRun is not { } run ||
            _viewModel.SelectedPullRequestCiJob is not { } job ||
            !_viewModel.CanCancelSelectedPullRequestCiRun)
            return;
        if (await ShowConfirmationAsync(
                "Cancel CI workflow run",
                $"Cancel workflow run #{run.Id} containing job '{job.Name}'? GitHub cancels the complete run, including every queued or running job. This requires Actions write permission.",
                "Cancel workflow run"))
            await _viewModel.CancelSelectedPullRequestCiRunAsync();
    }

    private async void OnRefreshCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshCiAsync();

    private async void OnDispatchCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DispatchSelectedCiWorkflowAsync();

    private async void OnDispatchReleaseCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DispatchReleaseCiWorkflowAsync();

    private async void OnRerunFailedCiClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RerunFailedCiJobsAsync();

    private async void OnCancelCiRunClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CancelSelectedCiRunAsync();

    private void OnOpenCiRunClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedCiRun is not { } run) return;
        Process.Start(new ProcessStartInfo { FileName = run.WebUrl.AbsoluteUri, UseShellExecute = true });
    }

    private void OnOpenGitHubTokenHelpClick(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/settings/personal-access-tokens/new",
            UseShellExecute = true
        });

    private async void OnCreatePullRequestClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreatePullRequestAsync();

    private async void OnAddCommitTaskLinksClick(object? sender, RoutedEventArgs e) =>
        await ShowWorkItemPickerAsync(forPullRequest: false);

    private async void OnAddPullRequestTaskLinksClick(object? sender, RoutedEventArgs e) =>
        await ShowWorkItemPickerAsync(forPullRequest: true);

    private async Task ShowWorkItemPickerAsync(bool forPullRequest)
    {
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        IReadOnlyList<IWorkItemIntegrationPlugin> plugins = _viewModel.GetActiveWorkItemPlugins();
        if (project is null || plugins.Count == 0)
        {
            await ShowMessageAsync(
                Translate("Task integration unavailable"),
                Translate("Enable a task integration plugin for this project first."));
            return;
        }

        WorkItemPickerDialog dialog = new(project.Id, plugins, forPullRequest, Translate);
        WorkItemPickerResult? result = await dialog.ShowDialog<WorkItemPickerResult?>(this);
        if (result is null || result.WorkItems.Count == 0) return;
        if (forPullRequest)
            _viewModel.AddWorkItemReferencesToPullRequest(result.WorkItems, result.PrefixPullRequestTitle);
        else
            _viewModel.AddWorkItemReferencesToCommit(result.WorkItems);
    }

    private async void OnAddPullRequestCommentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AddPullRequestCommentAsync();

    private async void OnSubmitPullRequestReviewClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SubmitPullRequestReviewAsync();

    private async void OnCheckoutPullRequestClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckoutSelectedPullRequestAsync();

    private void OnOpenPullRequestClick(object? sender, RoutedEventArgs e) =>
        _viewModel.OpenSelectedPullRequestInBrowser();

    private void OnOpenPullRequestCommitExplorerClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.PullRequestCommitRevisions.Count == 0) return;
        GitRevision selected = _viewModel.PullRequestCommitRevisions[0];
        if (_commitExplorerWindow is null)
        {
            CommitExplorerWindow window = new(_viewModel, _viewModel.PullRequestCommitRevisions, selected);
            window.Closed += (_, _) => _commitExplorerWindow = null;
            _commitExplorerWindow = window;
            window.Show();
            return;
        }
        _commitExplorerWindow.ShowRevisions(_viewModel.PullRequestCommitRevisions, selected);
        _commitExplorerWindow.Activate();
    }

    private async void OnMergePullRequestClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPullRequest is not { } pullRequest) return;
        if (!await ShowConfirmationAsync(
                "Merge pull request",
                $"Merge #{pullRequest.Number} '{pullRequest.Title}' into {pullRequest.BaseBranch} using {_viewModel.PullRequestMergeMethod}? This changes the remote repository and may trigger CI or deployments.",
                "Merge"))
            return;

        bool merged = await _viewModel.MergeSelectedPullRequestAsync();
        if (!merged) return;

        if (_viewModel.ShouldAutomaticallyUpdatePullRequestTasksAfterMerge)
        {
            await _viewModel.CompleteLinkedPullRequestTasksAsync();
        }
        else if (_viewModel.ShouldAskToUpdatePullRequestTasksAfterMerge &&
                 await ShowConfirmationAsync(
                     "Complete linked tasks",
                     "The pull request was merged. Move the detected linked tasks to their configured completion status now? Each provider validates permissions before applying the transition.",
                     "Complete tasks"))
        {
            await _viewModel.CompleteLinkedPullRequestTasksAsync();
        }
    }

    private async void OnCompletePullRequestTasksClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasPullRequestLinkedWorkItems) return;
        if (await ShowConfirmationAsync(
                "Complete linked tasks",
                "Move every resolved linked task detected in this merged pull request to its provider completion status? Unresolved references are skipped.",
                "Complete tasks"))
            await _viewModel.CompleteLinkedPullRequestTasksAsync();
    }

    private async void OnRemoveMergedPullRequestBranchClick(object? sender, RoutedEventArgs e)
    {
        GitLocalBranchRemovalAnalysis? analysis =
            await _viewModel.AnalyzeSelectedPullRequestLocalBranchRemovalAsync();
        if (analysis is null)
        {
            await ShowMessageAsync(
                "Local branch unavailable",
                "CyRevision could not analyze this local branch. Check that the pull request is merged and that its head branch still exists locally.");
            return;
        }

        string details =
            $"{analysis.SafetyMessage}\n\nUnique commits: {analysis.UniqueCommitCount:N0}\nRetained by: {analysis.RetainedBy}\n\nOnly the local branch reference will be removed. The remote branch and working files are not deleted.";
        if (analysis.CanRemoveSafely)
        {
            if (await ShowConfirmationAsync(
                    "Remove merged local branch",
                    details,
                    "Remove local branch"))
                await _viewModel.RemoveSelectedPullRequestLocalBranchAsync(force: false);
            return;
        }

        if (!analysis.CanForceRemove)
        {
            await ShowMessageAsync("Local branch protected", details);
            return;
        }

        if (await ShowConfirmationAsync(
                "Force-remove local branch",
                details + "\n\nNo verified Git reference retains every unique commit. Force removal can make unpublished commits difficult to recover.",
                "Force remove"))
            await _viewModel.RemoveSelectedPullRequestLocalBranchAsync(force: true);
    }

    private async void OnTogglePullRequestStateClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPullRequest is not { } pullRequest) return;
        bool reopen = !pullRequest.State.Equals("open", StringComparison.OrdinalIgnoreCase);
        string action = reopen ? "Reopen" : "Close";
        if (await ShowConfirmationAsync(
                $"{action} pull request",
                $"{action} pull request #{pullRequest.Number} '{pullRequest.Title}' on the remote repository?",
                action))
        {
            await _viewModel.ToggleSelectedPullRequestStateAsync();
        }
    }

    private async void OnTrackLfsClick(object? sender, RoutedEventArgs e) => await _viewModel.TrackLfsPatternAsync();

    private async void OnOpenGitLfsSetupAssistantClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProject is not { } project) return;
        GitInitializationOptions? options = await ShowGitInitializationWizardAsync(project.RootPath, lfsOnly: true);
        if (options is not null)
        {
            await _viewModel.ApplyGitLfsRecommendationsAsync(project.RootPath, options.LfsPatterns);
        }
    }

    private async void OnReloadGitIgnoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadGitIgnoreAsync();

    private async void OnSaveGitIgnoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveGitIgnoreAsync();

    private async void OnRefreshIgnoredFilesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshIgnoredFilesAsync();

    private async void OnTestGitIgnorePathClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.TestGitIgnorePathAsync();

    private async void OnGitIgnoreTestPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _viewModel.TestGitIgnorePathAsync();
    }

    private void OnApplyGitIgnoreTemplateClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ApplyGitIgnoreTemplate();

    private async void OnRefreshIgnoreSuggestionsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadIgnoreSuggestionsAsync(force: true);

    private void OnAppendGitIgnoreSuggestionsClick(object? sender, RoutedEventArgs e) =>
        _viewModel.AppendSelectedSuggestionsToGitIgnore();

    private void OnAppendSyncthingIgnoreSuggestionsClick(object? sender, RoutedEventArgs e) =>
        _viewModel.AppendSelectedSuggestionsToSyncthingIgnore();

    private void OnRefreshDiagnosticsClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RefreshPerformanceDiagnostics();

    private void OnClearDiagnosticsClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ClearPerformanceDiagnostics();

    private async void OnRefreshLfsLocksClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshLfsLocksAsync();

    private async void OnLoadLfsInventoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadLfsInventoryAsync();

    private async void OnInitializeCyStoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.InitializeCyStoreAsync();

    private async void OnRefreshCyStoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshCyStoreAsync();

    private async void OnCaptureCyStoreLfsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CaptureCyStoreLfsAsync();

    private async void OnVerifyCyStoreVersionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.VerifySelectedCyStoreVersionAsync();

    private async void OnReconstructCyStoreVersionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ReconstructSelectedCyStoreVersionAsync();

    private void OnCancelCyStoreClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelCyStoreOperation();

    private async void OnUnlockLfsLockClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LfsFileLock fileLock })
        {
            return;
        }

        bool force = !fileLock.IsOurs;
        string message = force
            ? $"'{fileLock.Path}' is locked by {fileLock.OwnerName}. Force unlock can allow concurrent edits to the same binary file. Remove this teammate's lock?"
            : $"Remove your Git LFS lock from '{fileLock.Path}'? The file itself will not be modified.";
        if (await ShowConfirmationAsync(
                force ? "Force unlock Git LFS file" : "Unlock Git LFS file",
                message,
                force ? "Force unlock" : "Unlock"))
        {
            await _viewModel.UnlockLfsLockAsync(fileLock, force);
        }
    }

    private void OnAllLfsLocksSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            _viewModel.SetSelectedLfsLocks(grid.SelectedItems.Cast<object>().OfType<LfsFileLock>(), mineList: false);
    }

    private void OnMyLfsLocksSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            _viewModel.SetSelectedLfsLocks(grid.SelectedItems.Cast<object>().OfType<LfsFileLock>(), mineList: true);
    }

    private async void OnUnlockSelectedProjectLocksClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                "Unlock selected Git LFS files",
                $"Remove {_viewModel.SelectedProjectLockCount:N0} selected lock(s)? Locks owned by teammates require force unlock.",
                "Unlock selected"))
        {
            await _viewModel.UnlockSelectedLfsLocksAsync(mineList: false);
        }
    }

    private async void OnUnlockSelectedMyLocksClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                "Unlock selected Git LFS files",
                $"Remove {_viewModel.SelectedMyLockCount:N0} selected lock(s) owned by your current identity?",
                "Unlock selected"))
        {
            await _viewModel.UnlockSelectedLfsLocksAsync(mineList: true);
        }
    }

    private async void OnUnlockAllMyLfsLocksClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                "Unlock all my Git LFS files",
                "Remove every Git LFS lock owned by your current identity? Files and Git history will not be modified.",
                "Unlock all mine"))
        {
            await _viewModel.UnlockAllLfsLocksAsync(forceEveryLock: false);
        }
    }

    private async void OnForceUnlockAllLfsLocksClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                "Force unlock every Git LFS file",
                "This removes every visible project lock, including locks owned by teammates. Use it only for stale or abandoned locks. Continue?",
                "Force unlock all"))
        {
            await _viewModel.UnlockAllLfsLocksAsync(forceEveryLock: true);
        }
    }

    private async void OnExportLfsVersionClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickSaveFileAsync(
            _viewModel.SelectedLfsVersion?.Path,
            "Exporter la version LFS");
        if (path is not null)
        {
            await _viewModel.ExportSelectedLfsVersionAsync(path);
        }
    }

    private async void OnRequestLfsVersionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RequestSelectedLfsVersionFromPeerAsync();

    private async void OnRestoreLfsVersionClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedLfsVersion is null)
        {
            return;
        }

        string title = Translate("Confirmer la restauration");
        string message = Translate(
            "Cette action remplace le fichier de travail par la version sélectionnée. Aucun commit ni indexation ne sera créé automatiquement.");
        if (await ShowConfirmationAsync(title, message))
        {
            await _viewModel.RestoreSelectedLfsVersionAsync();
        }
    }

    private async void OnPickBackupStoreClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner l'emplacement des sauvegardes");
        if (path is not null)
        {
            await _viewModel.SetBackupStoreAsync(path);
        }
    }

    private async void OnSaveBackupSettingsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveBackupSettingsAsync();

    private async void OnPickColdArchiveClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync(Translate("Sélectionner l'emplacement de l'archive froide"));
        if (path is not null)
        {
            _viewModel.SetColdArchivePath(path);
        }
    }

    private async void OnArchiveOldBackupsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.RemoveArchivedHotCopies && !await ShowConfirmationAsync(
                Translate("Remove verified hot backup copies?"),
                Translate("CyRevision will first copy and verify every eligible manifest and object in cold storage. It will then remove only verified hot manifests and objects that are no longer referenced by any project. This is optional and cannot be inferred from the selected profile."),
                Translate("Archive and reclaim")))
            return;
        await _viewModel.ArchiveOldBackupsAsync();
    }

    private async void OnAnalyzeGitArchiveClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeGitArchiveCandidatesAsync();

    private async void OnArchiveGitBranchClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.RemoveGitBranchAfterArchive && !await ShowConfirmationAsync(
                Translate("Remove the local branch after verification?"),
                Translate("CyRevision will create and verify a compressed Git bundle first. It will then remove only the selected local branch reference. The remote is not changed, the current branch is protected, and automatic Git garbage collection is never run."),
                Translate("Archive and remove local ref")))
            return;
        await _viewModel.ArchiveSelectedGitBranchAsync();
    }

    private async void OnRestoreGitArchiveClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RestoreSelectedGitArchiveAsync();

    private async void OnCreateBackupClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateBackupAsync();

    private async void OnRestoreBackupClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Restaurer le snapshot dans un dossier vide");
        if (path is not null)
        {
            await _viewModel.RestoreSelectedBackupAsync(path);
        }
    }

    private async void OnApplyPresetClick(object? sender, RoutedEventArgs e)
    {
        string? recommendedGitIgnore = null;
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        if (project is not null &&
            _viewModel.SelectedPreset?.Features.GitEnabled == true &&
            !File.Exists(Path.Combine(project.RootPath, ".gitignore")))
        {
            GitIgnorePromptChoice choice = await ShowMissingGitIgnoreRecommendationAsync(project.RootPath);
            if (choice.Action == GitIgnorePromptAction.Cancel) return;
            recommendedGitIgnore = choice.Action == GitIgnorePromptAction.CreateRecommended
                ? choice.Content
                : null;
        }

        await _viewModel.ApplySelectedPresetAsync(recommendedGitIgnore);
        RefreshWorkspaceModeNavigation(selectPrimary: true);
        ConfigureRepositoryChangeMonitor();
    }

    private async void OnPickSyncthingClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Sélectionner l'exécutable Syncthing dédié");
        if (path is not null)
        {
            await _viewModel.SetSyncthingExecutableAsync(path);
        }
    }

    private async void OnDetectSyncthingClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConfigureSyncthingAutomaticallyAsync();

    private async void OnSaveSyncthingSettingsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveSyncthingSettingsAsync();

    private async void OnPickSyncSourceFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Choose the folder synchronized by this project mode");
        if (path is not null) _viewModel.SetSyncSourceFolderPath(path);
    }

    private async void OnPickSyncVersionStoreClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Choose a separate Syncthing version store");
        if (path is not null) _viewModel.SetSyncVersionStorePath(path);
    }

    private async void OnPickSyncCompressedBackupFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Choose the compressed Sync backup destination");
        if (path is not null) _viewModel.SetSyncCompressedBackupPath(path);
    }

    private async void OnCreateCompressedSyncBackupClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateCompressedSyncBackupAsync();

    private async void OnSaveVersionedSyncSettingsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveBackupSettingsAsync();
        await _viewModel.SaveSyncthingSettingsAsync();
    }

    private async void OnPickSharedSyncFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Choose the independent folder to synchronize");
        if (path is not null) _viewModel.SetSharedSyncFolderPath(path);
    }

    private async void OnSaveSharedSyncFolderClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AddOrUpdateSharedSyncFolderAsync();

    private async void OnRemoveSharedSyncFolderClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveSelectedSharedSyncFolderAsync();

    private async void OnScanSharedSyncFolderClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ScanSelectedSharedSyncFolderAsync();

    private async void OnRefreshSyncthingClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshSyncthingWorkspaceAsync();

    private void OnOpenSyncSetupWizardClick(object? sender, RoutedEventArgs e) =>
        SynchronizationDetailTabs.SelectedItem = SyncSetupWizardTab;

    private async void OnRefreshSyncHistoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshSyncHistoryAsync();

    private async void OnClearSyncHistoryFileFilterClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearSyncHistoryFileFilter();
        await _viewModel.RefreshSyncHistoryAsync();
    }

    private async void OnRefreshSyncConflictsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshSyncConflictsAsync();

    private async void OnKeepOriginalSyncConflictClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSyncConflict is not { } conflict) return;
        if (await ShowConfirmationAsync(
                "Keep current original",
                $"Resolve the conflict for '{conflict.RelativeOriginalPath}' by keeping the current original? CyRevision will save both versions before removing the conflict copy.",
                "Keep original"))
        {
            await _viewModel.ResolveSelectedSyncConflictAsync(SyncConflictResolution.KeepOriginal);
        }
    }

    private async void OnUseSyncConflictVersionClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSyncConflict is not { } conflict) return;
        if (await ShowConfirmationAsync(
                "Use conflict version",
                $"Replace '{conflict.RelativeOriginalPath}' with the selected conflict version? CyRevision will save both versions before replacement.",
                "Use conflict version"))
        {
            await _viewModel.ResolveSelectedSyncConflictAsync(SyncConflictResolution.UseConflict);
        }
    }

    private async void OnRestoreSyncConflictClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSyncConflictBackup is not { } backup) return;
        if (await ShowConfirmationAsync(
                "Restore conflict state",
                $"Restore the original and conflict copy for '{backup.RelativeOriginalPath}' to their state before resolution? Current files at those two paths can be replaced.",
                "Restore"))
        {
            await _viewModel.RestoreSelectedSyncConflictBackupAsync();
        }
    }

    private async void OnCleanExpiredSyncConflictsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CleanExpiredSyncConflictBackupsAsync();

    private async void OnCreateSyncCommitClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateSyncCommitAsync();

    private async void OnRefreshSyncCommitsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshSyncCommitsAsync();

    private async void OnAnalyzeSyncCommitClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeSelectedSyncCommitAsync();

    private void OnKeepLocalSyncCommitConflictClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ResolveSelectedSyncCommitConflict(SyncCommitConflictChoice.KeepLocal);

    private void OnUseIncomingSyncCommitConflictClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ResolveSelectedSyncCommitConflict(SyncCommitConflictChoice.UseIncoming);

    private async void OnApplySyncCommitClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedSyncCommitAsync();

    private async void OnShowSelectedFileSyncHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedCodeNode is not { IsDirectory: false, IsPlaceholder: false } node) return;
        await _viewModel.FilterSyncHistoryForFileAsync(node.RelativePath);
        if (_viewModel.SelectedProject?.Definition.Features.PeerSyncEnabled == true)
        {
            NavigateToWorkspace(SynchronizationWorkspaceTab);
            SynchronizationDetailTabs.SelectedItem = SyncHistoryTab;
        }
        else
        {
            NavigateToWorkspace(SharedSyncWorkspaceTab);
        }
    }

    private async void OnScanSyncthingClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ScanSyncthingFolderAsync();

    private async void OnLoadSyncthingIgnoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadSyncthingIgnoreRulesAsync();

    private void OnUseSyncthingUnrealIgnoreClick(object? sender, RoutedEventArgs e) =>
        _viewModel.UseSyncthingUnrealIgnoreTemplate();

    private async void OnSaveSyncthingIgnoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveSyncthingIgnoreRulesAsync();

    private async void OnStartSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.StartSyncAsync();

    private async void OnPauseSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.PauseSyncAsync();

    private async void OnResumeSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.ResumeSyncAsync();

    private async void OnStopSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.StopSyncAsync();

    private async void OnExchangeGitClick(object? sender, RoutedEventArgs e) => await _viewModel.ExchangeGitAsync();

    private async void OnConfigureVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.ConfigureVpnAsync();

    private async void OnSaveVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveVpnSettingsAsync();

    private async void OnStartVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.StartVpnAsync();

    private async void OnStopVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.StopVpnAsync();

    private async void OnRefreshVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.RefreshVpnAsync();

    private async void OnInspectVpnSetupClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.InspectVpnSetupAsync();

    private async void OnTestVpnConnectivityClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.TestVpnConnectivityAsync();

    private async void OnApplyVpnFirewallClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Apply CyRevision firewall rules"),
                Translate("Only the generated CyRevision rules will be added. Administrator authorization may be requested. Existing unrelated firewall rules are not changed."),
                Translate("Apply rules")))
        {
            await _viewModel.ApplyVpnFirewallAsync();
        }
    }

    private async void OnRemoveVpnFirewallClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Remove CyRevision firewall rules"),
                Translate("Only firewall rules whose deterministic names belong to this CyRevision project will be removed."),
                Translate("Remove rules")))
        {
            await _viewModel.RemoveVpnFirewallAsync();
        }
    }

    private async void OnOpenVpnRouterClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.OpenVpnRouterAsync();

    private async void OnPublishVpnSyncClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.PublishVpnExchangeViaSyncAsync();

    private async void OnRefreshVpnSyncClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshVpnSyncMessagesAsync();

    private async void OnLoadVpnSyncClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadSelectedVpnSyncMessageAsync();

    private async void OnPickSwarmAgentClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Select SwarmAgent.exe");
        if (path is not null) _viewModel.SetSwarmAgentPath(path);
    }

    private async void OnPickSwarmCoordinatorClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Select SwarmCoordinator.exe");
        if (path is not null) _viewModel.SetSwarmCoordinatorPath(path);
    }

    private async void OnPickSwarmOptionsClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Select SwarmAgent.Options.xml");
        if (path is not null) _viewModel.SetSwarmOptionsPath(path);
    }

    private async void OnPickSwarmCacheClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the Swarm cache folder");
        if (path is not null) _viewModel.SetSwarmCacheFolder(path);
    }

    private async void OnSaveSwarmClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveSwarmAsync();

    private async void OnDiagnoseSwarmClick(object? sender, RoutedEventArgs e) => await _viewModel.DiagnoseSwarmAsync();

    private async void OnApplySwarmOptionsClick(object? sender, RoutedEventArgs e) => await _viewModel.ApplySwarmOptionsAsync();

    private async void OnApplySwarmDnsClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Apply local Swarm DNS alias"),
                Translate("CyRevision will replace only this project's marked block in the Windows hosts file and flush the DNS cache. Administrator authorization is required."),
                Translate("Apply alias")))
        {
            await _viewModel.ApplySwarmDnsAsync();
        }
    }

    private async void OnRemoveSwarmDnsClick(object? sender, RoutedEventArgs e) => await _viewModel.RemoveSwarmDnsAsync();

    private async void OnLaunchSwarmAgentClick(object? sender, RoutedEventArgs e) => await _viewModel.LaunchSwarmAgentAsync();

    private async void OnLaunchSwarmCoordinatorClick(object? sender, RoutedEventArgs e) => await _viewModel.LaunchSwarmCoordinatorAsync();

    private async void OnPickVpnInboxClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the private VPN inbox folder");
        if (path is not null) _viewModel.SetVpnFileInboxPath(path);
    }

    private async void OnPickVpnSharedFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the folder explicitly shared over VPN");
        if (path is not null) _viewModel.SetVpnFileSharedFolderPath(path);
    }

    private async void OnSaveVpnFilesClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveVpnFileExchangeAsync();

    private async void OnStartVpnFilesClick(object? sender, RoutedEventArgs e) => await _viewModel.StartVpnFileExchangeAsync();

    private async void OnStopVpnFilesClick(object? sender, RoutedEventArgs e) => await _viewModel.StopVpnFileExchangeAsync();

    private async void OnTestVpnFilePeerClick(object? sender, RoutedEventArgs e) => await _viewModel.TestVpnFilePeerAsync();

    private async void OnRefreshVpnSharedFilesClick(object? sender, RoutedEventArgs e) => await _viewModel.RefreshVpnSharedFilesAsync();

    private async void OnSendVpnFileClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Select a file to send to the VPN peer inbox");
        if (path is not null) await _viewModel.SendVpnFileAsync(path);
    }

    private async void OnDownloadVpnSharedFileClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickSaveFileAsync(
            _viewModel.SelectedVpnSharedFile?.RelativePath,
            "Save the verified VPN file as");
        if (path is not null) await _viewModel.DownloadVpnSharedFileAsync(path);
    }

    private async void OnRotateVpnFileTokenClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Rotate file-exchange token"),
                Translate("The endpoint will stop and every authorized peer must receive the new token through a separate trusted channel."),
                Translate("Rotate token")))
        {
            await _viewModel.RotateVpnFileTokenAsync();
        }
    }

    private async void OnSaveTeamChatClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveTeamChatAsync();

    private async void OnStartTeamChatClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StartTeamChatHostAsync();

    private async void OnStopTeamChatClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StopTeamChatHostAsync();

    private async void OnRefreshTeamChatClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshTeamChatAsync();

    private async void OnSendTeamChatClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SendTeamChatMessageAsync();

    private async void OnCreateTeamChatChannelClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateTeamChatChannelAsync();

    private async void OnPickTeamChatSyncFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the folder synchronized for team chat");
        if (path is not null) _viewModel.SetTeamChatSyncFolder(path);
    }

    private async void OnPickTeamChatAttachmentClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Select an image or file to attach");
        if (path is not null) _viewModel.SetTeamChatAttachment(path);
    }

    private async void OnOpenTeamChatAttachmentClick(object? sender, RoutedEventArgs e)
    {
        string? path = await _viewModel.PrepareSelectedTeamChatAttachmentAsync();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private async void OnRotateTeamChatTokenClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Rotate team chat token"),
                Translate("The VPN chat host will stop and authorized teammates must receive the new token through a separate trusted channel."),
                Translate("Rotate token")))
        {
            await _viewModel.RotateTeamChatTokenAsync();
        }
    }

    private async void OnPickLfsExternalStorageClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the dedicated external Git LFS storage directory");
        if (path is not null) _viewModel.SetLfsExternalStoragePath(path);
    }

    private async void OnPickLfsManagementArchiveClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the verified Git LFS retention archive");
        if (path is not null) _viewModel.SetLfsManagementArchivePath(path);
    }

    private async void OnSaveLfsManagementClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveLfsManagementAsync();

    private async void OnAnalyzeLfsStorageClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeLfsStorageAsync();

    private async void OnPreviewNativeLfsPruneClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RunNativeLfsPruneAsync(dryRun: true);

    private async void OnRunNativeLfsPruneClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Run native Git LFS prune"),
                Translate("Git LFS will verify objects against the configured remote and remove only local cache objects considered old and unreferenced by Git LFS. Current branch files remain available. Run the preview first; this cleanup cannot be undone locally without downloading the objects again."),
                Translate("Run verified prune")))
        {
            await _viewModel.RunNativeLfsPruneAsync(dryRun: false);
        }
    }

    private void OnCancelLfsStorageAnalysisClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelLfsStorageAnalysis();

    private async void OnAnalyzeRepositoryStorageClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeRepositoryStorageAsync();

    private async void OnArchiveLfsCandidatesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ArchiveLfsCandidatesAsync();

    private async void OnExecuteLfsCleanupClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Clean verified LFS objects"),
                Translate("CyRevision deletes only objects listed as eligible in the current preview. Current checkouts and worktrees stay protected. Old managed asset versions require the configured remote, peer, or archive evidence, and an audit is written before completion. Re-analyze after any branch change."),
                Translate("Clean verified objects")))
        {
            await _viewModel.ExecuteLfsCleanupAsync();
        }
    }

    private async void OnRelocateLfsStorageClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.LfsRemoveOriginalAfterRelocation || await ShowConfirmationAsync(
                Translate("Relocate Git LFS storage"),
                Translate("Every LFS object is copied and SHA-256 verified before lfs.storage is changed. After activation, the old object cache will be removed to reclaim space."),
                Translate("Relocate and remove old cache")))
        {
            await _viewModel.RelocateLfsStorageAsync();
        }
    }

    private async void OnPickRemoteBuildArtifactsClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the downloaded remote-build artifact directory");
        if (path is not null) _viewModel.SetRemoteBuildArtifactDestination(path);
    }

    private async void OnSaveRemoteBuildClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveRemoteBuildAsync();

    private async void OnTestRemoteBuildClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.TestRemoteBuildAsync();

    private async void OnStartRemoteBuildClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StartRemoteBuildAsync();

    private async void OnCancelRemoteBuildClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CancelRemoteBuildAsync();

    private async void OnSaveDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveDiscordAgentAsync();

    private async void OnConnectDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConnectDiscordAgentAsync();

    private async void OnLaunchLocalDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LaunchLocalDiscordAgentAsync();

    private async void OnStartDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StartDiscordAgentAsync();

    private async void OnStopDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StopDiscordAgentAsync();

    private async void OnCheckDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckDiscordAgentNowAsync();

    private async void OnTestDiscordWebhookClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.TestDiscordWebhookAsync();

    private async void OnRemoveDiscordWebhookClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveDiscordWebhookAsync();

    private async void OnCreateVpnInvitationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateVpnInvitationAsync();

    private async void OnJoinVpnInvitationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.JoinVpnInvitationAsync();

    private async void OnAcceptVpnResponseClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AcceptVpnResponseAsync();

    private async void OnRemoveVpnPeerClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveSelectedVpnPeerAsync();

    private async void OnCreateInvitationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreatePeerInvitationAsync();

    private async void OnPrepareJoinRequestClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.PreparePeerJoinRequestAsync();

    private async void OnApproveJoinRequestClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ApprovePeerJoinRequestAsync();

    private async void OnImportMembershipGrantClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ImportMembershipGrantAsync();

    private async void OnRevokePeerClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RevokeSelectedPeerAsync();

    private async void OnUpdatePeerRoleClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.UpdateSelectedPeerRoleAsync();

    private async void OnRefreshAdvisoryReservationsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAdvisoryReservationsAsync();

    private async void OnCleanExpiredAdvisoryReservationsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveExpiredAdvisoryReservationsAsync();

    private async void OnPickAssetBaselineClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Choisir l'asset de référence");
        if (path is not null)
        {
            _viewModel.SetAssetBaseline(path);
        }
    }

    private async void OnPickAssetCandidateClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Choisir l'asset candidat");
        if (path is not null)
        {
            _viewModel.SetAssetCandidate(path);
        }
    }

    private async void OnCompareAssetsClick(object? sender, RoutedEventArgs e) => await _viewModel.CompareAssetsAsync();

    private void OnUseSelectedAssetAsBaselineClick(object? sender, RoutedEventArgs e) =>
        _viewModel.UseSelectedAssetAsBaseline();

    private void OnUseSelectedAssetAsCandidateClick(object? sender, RoutedEventArgs e) =>
        _viewModel.UseSelectedAssetAsCandidate();

    private void OnAssetExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CodeFileEntry? file = e.AddedItems.OfType<CodeFileEntry>().LastOrDefault()
                              ?? (sender as ListBox)?.SelectedItem as CodeFileEntry;
        if (file is not null) _viewModel.SelectedAssetExplorerFile = file;
    }

    private void OnSolutionSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CodeFileEntry? file = e.AddedItems.OfType<CodeFileEntry>().LastOrDefault()
                              ?? (sender as ListBox)?.SelectedItem as CodeFileEntry;
        if (file is not null) _viewModel.SelectedCodeFileSearchResult = file;
    }

    private async void OnCompareSelectedToHeadClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CompareSelectedChangeToHeadAsync();

    private async void OnEnablePluginClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.EnableSelectedPluginAsync();

    private async void OnDisablePluginClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DisableSelectedPluginAsync();

    private async void OnPickUnrealProjectClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an Unreal Engine project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Unreal Engine project") { Patterns = ["*.uproject"] }
            ]
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.SetUnrealProjectPath(path);
        }
    }

    private async void OnInstallUnrealPluginClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.InstallUnrealEditorPluginAsync();

    private async void OnConfigureUnrealBridgeClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConfigureUnrealBridgeAsync();

    private async void OnPickUnityProjectClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select a Unity project");
        if (path is not null) _viewModel.SetUnityProjectPath(path);
    }

    private async void OnInstallUnityPluginClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.InstallUnityEditorPluginAsync();

    private async void OnConfigureUnityBridgeClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConfigureUnityBridgeAsync();

    private async void OnPickGodotProjectClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select a Godot project");
        if (path is not null) _viewModel.SetGodotProjectPath(path);
    }

    private async void OnInstallGodotPluginClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.InstallGodotEditorPluginAsync();

    private async void OnConfigureGodotBridgeClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConfigureGodotBridgeAsync();

    private async void OnPickLoreProjectClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select a Lore or Unreal project");
        if (path is not null) _viewModel.SetLoreProjectPath(path);
    }

    private async void OnDetectLoreCliClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DetectLoreCliAsync();

    private async void OnReadLoreStatusClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ReadLoreStatusAsync();

    private async void OnScanLoreStatusClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Scan Lore working tree"),
                Translate("Lore status --scan walks the working tree and persists refreshed dirty flags in Lore's local metadata. Project files are not changed. Continue?"),
                Translate("Scan working tree")))
        {
            await _viewModel.ScanLoreStatusAsync();
        }
    }

    private async void OnListLoreBranchesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ListLoreBranchesAsync();

    private async void OnStageLorePathClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StageLorePathAsync();

    private async void OnCommitLoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CommitLoreAsync();

    private async void OnPushLoreClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Push Lore commits"),
                Translate("Push the local Lore commits to the configured Lore server of record?"),
                Translate("Push")))
        {
            await _viewModel.PushLoreAsync();
        }
    }

    private async void OnSyncLoreClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SyncLoreAsync();

    private async void OnCreateLoreBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateLoreBranchAsync();

    private async void OnSwitchLoreBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SwitchLoreBranchAsync();

    private async void OnInstallLoreUnrealCompanionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.InstallLoreUnrealCompanionAsync();

    private async void OnDetectPerforceCliClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DetectPerforceCliAsync();

    private async void OnSavePerforceConfigurationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SavePerforceConfigurationAsync();

    private async void OnRefreshPerforceClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshPerforceAsync();

    private async void OnPreviewPerforceReconcileClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.PreviewPerforceReconcileAsync();

    private async void OnApplyPerforceReconcileClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Apply Perforce reconcile"),
                Translate("Open matching added, edited and deleted project files in the selected Perforce workspace? Review the preview first. File contents are not rewritten, but Perforce workspace state will change."),
                Translate("Apply reconcile")))
        {
            await _viewModel.ApplyPerforceReconcileAsync();
        }
    }

    private async void OnPreviewPerforceSyncClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.PreviewPerforceSyncAsync();

    private async void OnApplyPerforceSyncClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Sync Perforce workspace"),
                Translate("Update the selected project workspace from the Perforce server? Open or locally modified files may require manual resolution. Review the sync preview first."),
                Translate("Sync workspace")))
        {
            await _viewModel.ApplyPerforceSyncAsync();
        }
    }

    private async void OnOpenPerforcePathForEditClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.OpenPerforcePathForEditAsync();

    private async void OnLoadPerforceFileHistoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadPerforceFileHistoryAsync();

    private async void OnRevertUnchangedPerforceFileClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RevertSelectedPerforceFileAsync(unchangedOnly: true);

    private async void OnRevertPerforceFileClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Revert Perforce file"),
                Translate("Discard the selected file's opened state and local changes? This operation can destroy unsubmitted work."),
                Translate("Revert file")))
        {
            await _viewModel.RevertSelectedPerforceFileAsync(unchangedOnly: false);
        }
    }

    private async void OnSubmitPerforceClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Submit Perforce changelist"),
                Translate("Submit the selected/default changelist to the configured Perforce server?"),
                Translate("Submit")))
        {
            await _viewModel.SubmitPerforceAsync();
        }
    }

    private async void OnSaveUnrealAssetInspectionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveUnrealAssetInspectionOptionsAsync();

    private async void OnRefreshUnrealAssetInspectionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshUnrealAssetInspectionCacheAsync();

    private async void OnClearUnrealAssetInspectionClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Clear Unreal asset preview cache"),
                Translate("Delete the generated Unreal thumbnails and metadata for this project? Source assets are never modified."),
                Translate("Clear cache")))
        {
            await _viewModel.ClearUnrealAssetInspectionCacheAsync();
        }
    }

    private async void OnDiscoverUnrealBuildClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DiscoverUnrealBuildEnvironmentAsync(forceRefresh: true);

    private async void OnSaveUnrealBuildPresetClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveUnrealBuildPresetAsync();

    private void OnApplyUnrealBuildPresetClick(object? sender, RoutedEventArgs e) =>
        _viewModel.ApplySelectedUnrealBuildPreset();

    private async void OnDeleteUnrealBuildPresetClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DeleteSelectedUnrealBuildPresetAsync();

    private async void OnRunUnrealBuildClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RunSelectedUnrealBuildAsync();

    private async void OnRunUnrealBuildRangeClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RunUnrealBuildRangeAsync();

    private void OnCancelUnrealBuildClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelUnrealBuild();

    private async void OnPickUnrealLinuxToolchainClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the Unreal Linux cross-compiler root");
        if (path is not null) _viewModel.UnrealLinuxToolchainPath = path;
    }

    private async void OnPickUnrealAndroidSdkClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the Android SDK root");
        if (path is not null) _viewModel.UnrealAndroidSdkPath = path;
    }

    private async void OnPickUnrealBuildOutputClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Select the Unreal build output folder");
        if (path is not null) _viewModel.UnrealBuildOutputPath = path;
    }

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckForUpdatesAsync();

    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        string? packagePath = await _viewModel.DownloadAvailableUpdateAsync();
        if (packagePath is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = packagePath,
            UseShellExecute = true
        });
    }

    private void OnOpenUpdateReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_viewModel.UpdateReleasePageUrl, UriKind.Absolute, out Uri? releaseUri) ||
            !releaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !releaseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = releaseUri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private async Task ShowAboutAsync()
    {
        string version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "Alpha";
        Button close = new()
        {
            Content = Translate("Close"),
            Padding = new Avalonia.Thickness(18, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3574F0")),
            Foreground = Avalonia.Media.Brushes.White
        };
        Window dialog = new()
        {
            Title = Translate("About CyRevision"),
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1F22")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 13,
                Children =
                {
                    new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(
                            Avalonia.Platform.AssetLoader.Open(new Uri("avares://CyRevision.Desktop/Assets/Branding/cyrevision-icon-master.png"))),
                        Width = 62,
                        Height = 62,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
                    },
                    new TextBlock
                    {
                        Text = "CyRevision",
                        FontSize = 24,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = $"Alpha · {Translate("Version")} {version}",
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9B9DA3"))
                    },
                    new TextBlock
                    {
                        Text = Translate("Decentralized Git, LFS, synchronization, backup, VPN, and production tools."),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DFE1E5"))
                    },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    internal async Task ShowSystemIntegrationErrorAsync(string details)
    {
        await ShowMessageAsync(
            Translate("System integration error"),
            Translate("CyRevision could not update the system startup setting.") + Environment.NewLine + details);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        Button close = new()
        {
            Content = Translate("Close"),
            Padding = new Avalonia.Thickness(18, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3574F0")),
            Foreground = Avalonia.Media.Brushes.White
        };
        Window dialog = new()
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1F22")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DFE1E5"))
                    },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task ShowCloneRepositoryDialogAsync()
    {
        string defaultParent = _viewModel.SelectedProject is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetDirectoryName(_viewModel.SelectedProject.RootPath)
              ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        TextBox remoteUrl = new()
        {
            PlaceholderText = "https://server/team/repository.git",
            MinWidth = 510
        };
        TextBox parentFolder = new() { Text = defaultParent, MinWidth = 430 };
        TextBox repositoryName = new() { PlaceholderText = "repository", MinWidth = 250 };
        CheckBox submodules = new()
        {
            Content = Translate("Clone Git submodules recursively"),
            IsChecked = false
        };
        TextBlock destination = new()
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 10,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9B9DA3"))
        };
        TextBlock status = new()
        {
            Text = Translate("Enter a remote repository URL and choose where its local copy will be created."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B8C1D8"))
        };
        ProgressBar progress = new()
        {
            IsIndeterminate = true,
            IsVisible = false,
            Height = 3
        };
        Button browse = new()
        {
            Content = Translate("Browse…"),
            Padding = new Avalonia.Thickness(12, 7)
        };
        Button cancel = new()
        {
            Content = Translate("Cancel"),
            Padding = new Avalonia.Thickness(16, 8),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#393B40"))
        };
        Button clone = new()
        {
            Content = Translate("Clone repository"),
            Padding = new Avalonia.Thickness(16, 8),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3574F0")),
            Foreground = Avalonia.Media.Brushes.White,
            IsDefault = true
        };
        ToolTip.SetTip(browse, Translate("Choose the parent folder that will contain the cloned repository"));
        ToolTip.SetTip(clone, Translate("Clone the remote repository, configure local Git LFS, and add it to CyRevision"));

        bool repositoryNameEdited = false;
        void UpdateDestination()
        {
            string name = repositoryName.Text?.Trim() ?? string.Empty;
            destination.Text = string.IsNullOrWhiteSpace(name)
                ? Translate("Destination: choose a repository name")
                : $"{Translate("Destination")}: {Path.Combine(parentFolder.Text?.Trim() ?? string.Empty, name)}";
        }
        remoteUrl.TextChanged += (_, _) =>
        {
            if (!repositoryNameEdited)
            {
                repositoryName.Text = SuggestCloneFolderName(remoteUrl.Text);
            }
            UpdateDestination();
        };
        repositoryName.TextChanged += (_, _) =>
        {
            if (repositoryName.IsFocused)
            {
                repositoryNameEdited = true;
            }
            UpdateDestination();
        };
        parentFolder.TextChanged += (_, _) => UpdateDestination();

        Window dialog = new()
        {
            Title = Translate("Clone a remote repository"),
            Width = 650,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1F22"))
        };
        Grid folderRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        folderRow.Children.Add(parentFolder);
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        StackPanel buttons = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, clone }
        };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(22),
            Spacing = 9,
            Children =
            {
                new TextBlock { Text = Translate("Remote repository"), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                remoteUrl,
                new TextBlock { Text = Translate("Local parent folder"), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                folderRow,
                new TextBlock { Text = Translate("Local repository name"), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                repositoryName,
                submodules,
                destination,
                status,
                progress,
                buttons
            }
        };

        CancellationTokenSource? cloneCancellation = null;
        browse.Click += async (_, _) =>
        {
            string? selected = await PickFolderAsync(Translate("Choose the local parent folder"));
            if (selected is not null)
            {
                parentFolder.Text = selected;
            }
        };
        cancel.Click += (_, _) =>
        {
            if (cloneCancellation is not null)
            {
                cloneCancellation.Cancel();
                status.Text = Translate("Cancelling clone…");
                cancel.IsEnabled = false;
            }
            else
            {
                dialog.Close();
            }
        };
        clone.Click += async (_, _) =>
        {
            string url = remoteUrl.Text?.Trim() ?? string.Empty;
            string parent = parentFolder.Text?.Trim() ?? string.Empty;
            string name = repositoryName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            {
                status.Text = Translate("Remote URL, parent folder, and repository name are required.");
                return;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            {
                status.Text = Translate("The local repository name contains invalid characters.");
                return;
            }

            string target = Path.Combine(parent, name);
            cloneCancellation = new CancellationTokenSource();
            remoteUrl.IsEnabled = parentFolder.IsEnabled = repositoryName.IsEnabled = false;
            browse.IsEnabled = clone.IsEnabled = submodules.IsEnabled = false;
            cancel.Content = Translate("Cancel clone");
            progress.IsVisible = true;
            status.Text = Translate("Cloning and configuring local Git LFS…");
            bool succeeded = await _viewModel.CloneGitRepositoryAsync(
                url,
                target,
                submodules.IsChecked == true,
                cloneCancellation.Token);
            cloneCancellation.Dispose();
            cloneCancellation = null;
            if (succeeded)
            {
                dialog.Close();
                return;
            }

            progress.IsVisible = false;
            status.Text = _viewModel.StatusMessage;
            remoteUrl.IsEnabled = parentFolder.IsEnabled = repositoryName.IsEnabled = true;
            browse.IsEnabled = clone.IsEnabled = submodules.IsEnabled = true;
            cancel.IsEnabled = true;
            cancel.Content = Translate("Cancel");
        };
        UpdateDestination();
        await dialog.ShowDialog(this);
    }

    private static string SuggestCloneFolderName(string? remoteUrl)
    {
        string value = remoteUrl?.Trim().TrimEnd('/', '\\') ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }
        int separator = Math.Max(value.LastIndexOf('/'), value.LastIndexOf(':'));
        string name = separator >= 0 ? value[(separator + 1)..] : value;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }

    private async Task<string?> PickFileAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task<string?> PickSaveFileAsync(string? sourcePath, string title)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Translate(title),
            SuggestedFileName = string.IsNullOrWhiteSpace(sourcePath) ? "revision.bin" : Path.GetFileName(sourcePath),
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    private async Task<ProjectRemovalChoice?> ShowRemoveProjectConfirmationAsync(ProjectItemViewModel project)
    {
        CheckBox removeCaches = new()
        {
            Content = Translate("Also remove generated CyRevision caches"),
            IsChecked = false,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        TextBlock cacheDetails = new()
        {
            Text = Translate("This removes only .cyrevision/cache (generated indexes and previews). Git data, project files, AI conversations and worktrees are kept."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 500,
            FontSize = 10,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9B9DA3"))
        };
        Button cancel = new()
        {
            Content = Translate("Cancel"),
            Padding = new Avalonia.Thickness(16, 9),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#202941"))
        };
        Button confirm = new()
        {
            Content = Translate("Remove from CyRevision"),
            Padding = new Avalonia.Thickness(16, 9),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A3E51")),
            Foreground = Avalonia.Media.Brushes.White
        };
        Window dialog = new()
        {
            Title = Translate("Remove project from CyRevision"),
            Width = 570,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#10172A")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = project.Name,
                        FontSize = 18,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(project.AccentColor))
                    },
                    new TextBlock
                    {
                        Text = project.RootPath,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontSize = 10,
                        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9B9DA3"))
                    },
                    new Border
                    {
                        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#17223A")),
                        BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2D3A4E")),
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(5),
                        Padding = new Avalonia.Thickness(12),
                        Child = new TextBlock
                        {
                            Text = Translate("The project folder and repository will stay on disk and will not be modified."),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#78D7B7"))
                        }
                    },
                    removeCaches,
                    cacheDetails,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 9,
                        Children = { cancel, confirm }
                    }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(null);
        confirm.Click += (_, _) => dialog.Close(new ProjectRemovalChoice(true, removeCaches.IsChecked == true));
        return await dialog.ShowDialog<ProjectRemovalChoice?>(this);
    }

    public Task<bool> ShowExitConfirmationAsync() =>
        ShowConfirmationAsync(
            Translate("Quit CyRevision?"),
            Translate("Running operations will be cancelled and all CyRevision background services will stop. Unsaved text entered in editors may be lost. Do you want to quit?"),
            Translate("Quit CyRevision"),
            Translate("Cancel"));

    private async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string? confirmLabel = null,
        string? cancelLabel = null)
    {
        TextBlock description = new()
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 470,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DDE3F0"))
        };
        Button cancel = new()
        {
            Content = cancelLabel ?? Translate("Annuler"),
            Padding = new Avalonia.Thickness(16, 9),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#202941"))
        };
        Button confirm = new()
        {
            Content = confirmLabel ?? Translate("Restaurer"),
            Padding = new Avalonia.Thickness(16, 9),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A3E51")),
            Foreground = Avalonia.Media.Brushes.White
        };
        StackPanel buttons = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 9,
            Children = { cancel, confirm }
        };
        Window dialog = new()
        {
            Title = title,
            Width = 540,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#10172A")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 20,
                Children = { description, buttons }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private string Translate(string source) => _localization?.Translate(source) ?? source;

    private sealed record ProjectRemovalChoice(bool Confirmed, bool RemoveGeneratedCaches);
}
