using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

namespace CyRevision.Desktop;

internal enum WorkspaceCategory
{
    Overview,
    Git,
    Code,
    Network,
    Extensions
}

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = null!;
    private UiLocalizer? _uiLocalizer;
    private LocalizationService? _localization;
    private FocusedDiffWindow? _focusedDiffWindow;
    private FocusedDiffWindow? _changesDiffWindow;
    private FocusedDiffWindow? _pullRequestDiffWindow;
    private CommitExplorerWindow? _commitExplorerWindow;
    private readonly List<DetachedWorkspaceWindow> _detachedWorkspaceWindows = [];
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
    private bool _pullRequestDiffFocused;
    private bool _solutionExplorerTreeLayout = true;
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
        foreach (AiChatMessageViewModel message in _viewModel.AiChatMessages)
            message.PropertyChanged -= OnAiChatMessagePropertyChanged;
        _viewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        _focusedDiffWindow?.Close();
        _focusedDiffWindow = null;
        _changesDiffWindow?.Close();
        _changesDiffWindow = null;
        _pullRequestDiffWindow?.Close();
        _pullRequestDiffWindow = null;
        _commitExplorerWindow?.Close();
        _commitExplorerWindow = null;
        foreach (DetachedWorkspaceWindow window in _detachedWorkspaceWindows.ToArray())
        {
            window.Close();
        }
        _detachedWorkspaceWindows.Clear();
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
            ConfigureRepositoryChangeMonitor();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.CodeAutoRefreshFrequency))
        {
            SaveCurrentProjectWorkspaceState();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.HasCodeFileSearchResults))
        {
            ApplySolutionExplorerLayout(_solutionExplorerTreeLayout);
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
            batch.Paths.Count,
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
        _categoryWorkspaceTabs.Clear();
        if (state.CategoryTabs is not null)
        {
            foreach ((string category, string tabName) in state.CategoryTabs)
                _categoryWorkspaceTabs[category] = tabName;
        }
        _restoringProjectWorkspace = true;
        try
        {
            _viewModel.CodeAutoRefreshFrequency = state.CodeRefreshFrequency;
            ConsoleAndLogsTabs.SelectedIndex = Math.Clamp(state.ConsoleSection, 0, 1);
            TabItem tab = AllWorkspaceTabs().FirstOrDefault(item => item.Name == state.ActiveTab) ?? ProjectWorkspaceTab;
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
            new Dictionary<string, string>(_categoryWorkspaceTabs, StringComparer.Ordinal)));
    }

    private void OnWorkspaceTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_restoringProjectWorkspace) return;
        SaveCurrentProjectWorkspaceState();
        EnsureCodeWorkspaceForVisibleTab();
        EnsureSelectedWorkspaceData();
    }

    private void OnConsoleAndLogsTabSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveCurrentProjectWorkspaceState();

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

    private void ApplyChangesLayout(ChangesLayoutMode layout, bool persist = true, bool restoreSavedSize = false)
    {
        _changesLayout = layout;
        ChangesBalancedLayoutToggle.IsChecked = layout == ChangesLayoutMode.Balanced;
        ChangesDiffFocusLayoutToggle.IsChecked = layout == ChangesLayoutMode.DiffFocus;
        bool restore = restoreSavedSize && _layoutPreferences.ChangesLayout == layout;
        double listWeight = restore
            ? _layoutPreferences.ChangesListWeight
            : layout == ChangesLayoutMode.DiffFocus ? 0.68 : 1.05;
        double diffWeight = restore
            ? _layoutPreferences.ChangesDiffWeight
            : layout == ChangesLayoutMode.DiffFocus ? 1.72 : 1.35;
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

    private void OnOpenFocusedDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_focusedDiffWindow is not null)
        {
            if (_focusedDiffWindow.WindowState == WindowState.Minimized)
            {
                _focusedDiffWindow.WindowState = WindowState.Normal;
            }

            _focusedDiffWindow.Activate();
            return;
        }

        if (_localization is null)
        {
            return;
        }

        FocusedDiffWindow window = new(_viewModel, _localization);
        window.Closed += (_, _) => _focusedDiffWindow = null;
        _focusedDiffWindow = window;
        window.Show(this);
    }

    private void OnOpenChangesDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_localization is null || _viewModel.SelectedChange is null) return;
        if (_changesDiffWindow is not null)
        {
            _changesDiffWindow.Activate();
            return;
        }

        FocusedDiffWindow window = new(_viewModel, DiffWindowSource.WorkingTree, _localization);
        window.Closed += (_, _) => _changesDiffWindow = null;
        _changesDiffWindow = window;
        window.Show(this);
    }

    private void OnOpenPullRequestDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_localization is null || _viewModel.SelectedPullRequestFile is not { } file) return;
        if (_pullRequestDiffWindow is not null)
        {
            _pullRequestDiffWindow.Activate();
            return;
        }

        FocusedDiffWindow window = new(_viewModel, DiffWindowSource.PullRequest, _localization);
        window.Closed += (_, _) => _pullRequestDiffWindow = null;
        _pullRequestDiffWindow = window;
        window.Show(this);
    }

    private void OnOpenCommitExplorerWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_commitExplorerWindow is not null)
        {
            _commitExplorerWindow.ShowRevisions(_viewModel.History, _viewModel.SelectedExplorerRevision);
            if (_commitExplorerWindow.WindowState == WindowState.Minimized)
                _commitExplorerWindow.WindowState = WindowState.Normal;
            _commitExplorerWindow.Activate();
            return;
        }

        CommitExplorerWindow window = new(_viewModel);
        window.Closed += (_, _) => _commitExplorerWindow = null;
        _commitExplorerWindow = window;
        window.Show(this);
    }

    private void OnOpenSelectedBranchCommitClick(object? sender, RoutedEventArgs e) =>
        OpenSelectedBranchCommitExplorer();

    private void OnSelectedBranchCommitDoubleTapped(object? sender, TappedEventArgs e) =>
        OpenSelectedBranchCommitExplorer();

    private void OpenSelectedBranchCommitExplorer()
    {
        GitRevision? selectedRevision = _viewModel.SelectedBranchRevision;
        if (selectedRevision is null)
        {
            return;
        }

        if (_commitExplorerWindow is null)
        {
            CommitExplorerWindow window = new(
                _viewModel,
                _viewModel.SelectedBranchHistory,
                selectedRevision);
            window.Closed += (_, _) => _commitExplorerWindow = null;
            _commitExplorerWindow = window;
            window.Show(this);
            return;
        }

        _commitExplorerWindow.ShowRevisions(_viewModel.SelectedBranchHistory, selectedRevision);
        if (_commitExplorerWindow.WindowState == WindowState.Minimized)
        {
            _commitExplorerWindow.WindowState = WindowState.Normal;
        }
        _commitExplorerWindow.Activate();
    }

    private void OnChangeTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TreeView tree || tree.SelectedItem is not GitChangeTreeNode node) return;
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
            _viewModel.SelectedChangeTreeNode = node;
            _viewModel.SelectedChange = change;
        }
    }

    private void OnChangesTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem { DataContext: GitChangeTreeNode node })
        {
            node.EnsureChildrenLoaded();
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

    private void NavigateToWorkspace(TabItem tab, Control? focusTarget = null)
    {
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

    private void OnNetworkCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Network, selectDefault: true);

    private void OnExtensionsCategoryClick(object? sender, RoutedEventArgs e) =>
        ApplyWorkspaceCategory(WorkspaceCategory.Extensions, selectDefault: true);

    private void ApplyWorkspaceCategory(WorkspaceCategory category, bool selectDefault)
    {
        _workspaceCategory = category;
        OverviewCategoryToggle.IsChecked = category == WorkspaceCategory.Overview;
        GitCategoryToggle.IsChecked = category == WorkspaceCategory.Git;
        CodeCategoryToggle.IsChecked = category == WorkspaceCategory.Code;
        NetworkCategoryToggle.IsChecked = category == WorkspaceCategory.Network;
        ExtensionsCategoryToggle.IsChecked = category == WorkspaceCategory.Extensions;

        TabItem[] visibleTabs = TabsFor(category);
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
        WorkspaceCategory.Overview => [ProjectWorkspaceTab, MembersWorkspaceTab],
        WorkspaceCategory.Git =>
        [
            ChangesWorkspaceTab, HistoryWorkspaceTab, CompositionWorkspaceTab, BranchesWorkspaceTab,
            PullRequestsWorkspaceTab, CiWorkspaceTab, GitGraphsWorkspaceTab, LfsLocksWorkspaceTab, GitIgnoreWorkspaceTab, GitLfsWorkspaceTab,
            BackupsWorkspaceTab
        ],
        WorkspaceCategory.Code => [SolutionExplorerWorkspaceTab, CodeWorkspaceTab, ConsoleWorkspaceTab, AssetDiffWorkspaceTab, AiWorkspaceTab, McpWorkspaceTab],
        WorkspaceCategory.Network =>
        [
            SynchronizationWorkspaceTab, VpnWorkspaceTab, SwarmWorkspaceTab, VpnFilesWorkspaceTab, TeamChatWorkspaceTab,
            RemoteBuildsWorkspaceTab, DiscordWorkspaceTab, WorkInProgressWorkspaceTab
        ],
        _ => [PluginsWorkspaceTab, UnrealWorkspaceTab, UnrealBuildWorkspaceTab, DiagnosticsWorkspaceTab, HelpWorkspaceTab]
    };

    private TabItem[] AllWorkspaceTabs() =>
    [
        ProjectWorkspaceTab, MembersWorkspaceTab, ChangesWorkspaceTab, SolutionExplorerWorkspaceTab, CodeWorkspaceTab,
        ConsoleWorkspaceTab, HistoryWorkspaceTab,
        CompositionWorkspaceTab, BranchesWorkspaceTab, PullRequestsWorkspaceTab, CiWorkspaceTab, GitGraphsWorkspaceTab,
        LfsLocksWorkspaceTab, GitIgnoreWorkspaceTab, GitLfsWorkspaceTab, BackupsWorkspaceTab, SynchronizationWorkspaceTab, VpnWorkspaceTab,
        SwarmWorkspaceTab, VpnFilesWorkspaceTab, TeamChatWorkspaceTab, RemoteBuildsWorkspaceTab, DiscordWorkspaceTab,
        WorkInProgressWorkspaceTab, AssetDiffWorkspaceTab, AiWorkspaceTab, McpWorkspaceTab,
        PluginsWorkspaceTab, UnrealWorkspaceTab, UnrealBuildWorkspaceTab, DiagnosticsWorkspaceTab, HelpWorkspaceTab
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

    private void OnOpenDetachedHistoryClick(object? sender, RoutedEventArgs e) =>
        OpenDetachedWorkspace(DetachedWorkspaceSection.History);

    private void OnOpenDetachedCodeClick(object? sender, RoutedEventArgs e) =>
        OpenDetachedWorkspace(DetachedWorkspaceSection.Code);

    private void OnOpenDetachedMultiRestoreClick(object? sender, RoutedEventArgs e)
    {
        _ = _viewModel.LoadMultiRestoreCommitAsync(_viewModel.SelectedExplorerRevision);
        OpenDetachedWorkspace(DetachedWorkspaceSection.MultiRestore);
    }

    private void OnOpenDetachedCherryPickClick(object? sender, RoutedEventArgs e) =>
        OpenDetachedWorkspace(DetachedWorkspaceSection.CherryPick);

    private void OpenDetachedWorkspace(DetachedWorkspaceSection section)
    {
        if (_localization is null)
        {
            return;
        }

        DetachedWorkspaceWindow window = new(_viewModel, _localization, section);
        window.Closed += (_, _) => _detachedWorkspaceWindows.Remove(window);
        _detachedWorkspaceWindows.Add(window);
        window.Show(this);
    }

    private void OnShowGitGraphsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(GitGraphsWorkspaceTab);

    private void OnShowBranchesWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(BranchesWorkspaceTab);

    private void OnShowPullRequestsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(PullRequestsWorkspaceTab);

    private void OnShowGitLfsWorkspaceClick(object? sender, RoutedEventArgs e) =>
        NavigateToWorkspace(GitLfsWorkspaceTab);

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

    private void OnCancelAiChatClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelAiChat();

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
            await _viewModel.CreateGitRepositoryAsync(path);
        }
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner un dossier à synchroniser ou sauvegarder");
        if (path is not null)
        {
            await _viewModel.AddFolderProjectAsync(path);
        }
    }

    private async void OnRemoveProjectClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveSelectedProjectAsync();

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

    private async void OnSetSelectedChangeLocalClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SetSelectedChangeLocalOnlyAsync(true);

    private async void OnRestoreSelectedLocalChangeClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SetSelectedChangeLocalOnlyAsync(false);

    private async void OnRollbackSelectedChangeClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedChange is not { } change)
        {
            return;
        }

        string effect = change.IsUntracked
            ? "The untracked file will be deleted from disk. This cannot be restored from Git."
            : "The file will be restored to HEAD and its staged and working-tree changes will be discarded.";
        if (await ShowConfirmationAsync(
                "Rollback file change",
                $"Rollback '{change.Path}'? {effect}",
                "Rollback file"))
        {
            await _viewModel.RollbackSelectedChangeAsync();
        }
    }

    private async void OnRollbackIncludedChangesClick(object? sender, RoutedEventArgs e)
    {
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

    private async void OnMergeBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.MergeSelectedBranchAsync();

    private async void OnFetchClick(object? sender, RoutedEventArgs e) => await _viewModel.FetchAsync();

    private async void OnPullClick(object? sender, RoutedEventArgs e) => await _viewModel.PullAsync();

    private async void OnPushClick(object? sender, RoutedEventArgs e) => await _viewModel.PushAsync();

    private async void OnSaveRemoteClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveRemoteAsync();

    private async void OnResolvePullRequestRepositoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ResolvePullRequestRepositoryAsync();

    private async void OnRefreshPullRequestsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshPullRequestsAsync();

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
            window.Show(this);
            return;
        }
        _commitExplorerWindow.ShowRevisions(_viewModel.PullRequestCommitRevisions, selected);
        _commitExplorerWindow.Activate();
    }

    private async void OnMergePullRequestClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPullRequest is not { } pullRequest) return;
        if (await ShowConfirmationAsync(
                "Merge pull request",
                $"Merge #{pullRequest.Number} '{pullRequest.Title}' into {pullRequest.BaseBranch} using {_viewModel.PullRequestMergeMethod}? This changes the remote repository and may trigger CI or deployments.",
                "Merge"))
        {
            await _viewModel.MergeSelectedPullRequestAsync();
        }
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

    private async void OnArchiveOldBackupsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ArchiveOldBackupsAsync();

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

    private async void OnApplyPresetClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedPresetAsync();

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

    private async void OnArchiveLfsCandidatesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ArchiveLfsCandidatesAsync();

    private async void OnExecuteLfsCleanupClick(object? sender, RoutedEventArgs e)
    {
        if (await ShowConfirmationAsync(
                Translate("Clean verified LFS objects"),
                Translate("CyRevision will delete only objects from the current LFS cache that are no longer referenced locally and have the required fresh remote, peer, or archive evidence. Re-analyze after any branch change."),
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

    private async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string? confirmLabel = null)
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
            Content = Translate("Annuler"),
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
}
