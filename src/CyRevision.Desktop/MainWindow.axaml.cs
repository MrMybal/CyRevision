using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using CyRevision.Desktop.Controls;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;

namespace CyRevision.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = null!;
    private UiLocalizer? _uiLocalizer;
    private LocalizationService? _localization;
    private FocusedDiffWindow? _focusedDiffWindow;
    private WorkspaceLayoutPreferencesStore? _workspaceLayoutStore;
    private WorkspaceLayoutPreferences _layoutPreferences = WorkspaceLayoutPreferences.Default;
    private HistoryLayoutMode _historyLayout = HistoryLayoutMode.Columns;
    private ChangesLayoutMode _changesLayout = ChangesLayoutMode.Balanced;
    private CodeLayoutMode _codeLayout = CodeLayoutMode.Balanced;

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
        _localization = localization;
        _workspaceLayoutStore = new WorkspaceLayoutPreferencesStore(configurationDirectory);
        _layoutPreferences = _workspaceLayoutStore.Load();
        HistoryTimelineToggle.IsChecked = _layoutPreferences.ShowTimeline;
        HistoryFilesToggle.IsChecked = _layoutPreferences.ShowFiles;
        HistoryDiffToggle.IsChecked = _layoutPreferences.ShowDiff;
        CodeExplorerPanelToggle.IsChecked = _layoutPreferences.ShowCodeExplorer;
        CodeSymbolsPanelToggle.IsChecked = _layoutPreferences.ShowCodeSymbols;
        CodeResultsPanelToggle.IsChecked = _layoutPreferences.ShowCodeResults;
        ApplyHistoryLayout(_layoutPreferences.HistoryLayout, false);
        ApplyChangesLayout(_layoutPreferences.ChangesLayout, false, true);
        ApplyCodeLayout(_layoutPreferences.CodeLayout, false, true);
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
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _focusedDiffWindow?.Close();
        _focusedDiffWindow = null;
        _uiLocalizer?.Dispose();
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
        ChangesWorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(listWeight, GridUnitType.Star);
        ChangesWorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(8);
        ChangesWorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(diffWeight, GridUnitType.Star);
        if (persist) SaveWorkspaceLayoutPreferences();
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
            CodeLayoutMode.EditorFocus => (0.56, 1.95, 0.64, 1.0, 0.24),
            CodeLayoutMode.SearchFocus => (0.68, 1.0, 1.42, 1.0, 0.30),
            _ => (0.8, 1.45, 1.0, 1.0, 0.32)
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

    private void OnSaveWorkspaceLayoutClick(object? sender, RoutedEventArgs e) =>
        SaveWorkspaceLayoutPreferences();

    private void OnResetWorkspaceLayoutClick(object? sender, RoutedEventArgs e)
    {
        _layoutPreferences = WorkspaceLayoutPreferences.Default;
        HistoryTimelineToggle.IsChecked = true;
        HistoryFilesToggle.IsChecked = true;
        HistoryDiffToggle.IsChecked = true;
        CodeExplorerPanelToggle.IsChecked = true;
        CodeSymbolsPanelToggle.IsChecked = true;
        CodeResultsPanelToggle.IsChecked = true;
        ApplyHistoryLayout(HistoryLayoutMode.Columns, false);
        ApplyChangesLayout(ChangesLayoutMode.Balanced, false);
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
            GetGridWeight(HistoryWorkspaceGrid.RowDefinitions[2], _layoutPreferences.HistoryBottomWeight));
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

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        KeyModifiers required = KeyModifiers.Control | KeyModifiers.Shift;
        if (e.Key == Key.F && (e.KeyModifiers & required) == required)
        {
            WorkspaceTabs.SelectedItem = CodeWorkspaceTab;
            GlobalCodeSearch.Focus();
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
        await _viewModel.RefreshCodeWorkspaceAsync();

    private async void OnCodeSelectionHistoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.LoadCodeSelectionHistoryAsync(CodePreview.SelectionStart, CodePreview.SelectionEnd);

    private async void OnRunAiAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RunAiAgentAsync();

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

    private async void OnCommitClick(object? sender, RoutedEventArgs e) => await _viewModel.CommitAsync();

    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateBranchAsync(NewBranchName.Text ?? string.Empty);

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
