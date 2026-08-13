using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class CommitExplorerWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private FocusedDiffWindow? _diffWindow;
    private Func<GitRevision, IReadOnlyList<GitCommitFileChange>, Task>? _composeSelectionHandler;

    public CommitExplorerWindow()
    {
        InitializeComponent();
    }

    public CommitExplorerWindow(MainWindowViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        ShowRevisions(viewModel.History, viewModel.SelectedExplorerRevision);
    }

    public CommitExplorerWindow(
        MainWindowViewModel viewModel,
        IEnumerable<GitRevision> revisions,
        GitRevision? selectedRevision) : this(viewModel)
    {
        ShowRevisions(revisions, selectedRevision);
    }

    public void ShowRevisions(IEnumerable<GitRevision> revisions, GitRevision? selectedRevision)
    {
        _viewModel?.ShowCommitExplorerRevisions(revisions, selectedRevision);
    }

    public void EnableComposeSelection(Func<GitRevision, IReadOnlyList<GitCommitFileChange>, Task> handler)
    {
        _composeSelectionHandler = handler;
        AddToComposeButton.IsVisible = true;
    }

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) =>
        Topmost = AlwaysOnTopToggle.IsChecked == true;

    private void OnColumnsLayoutClick(object? sender, RoutedEventArgs e) => ApplyLayout(columns: true);

    private void OnReviewLayoutClick(object? sender, RoutedEventArgs e) => ApplyLayout(columns: false);

    private void ApplyLayout(bool columns)
    {
        ColumnsLayoutToggle.IsChecked = columns;
        ReviewLayoutToggle.IsChecked = !columns;
        ResetWorkspaceGrid();
        if (columns)
        {
            SetColumn(0, 0.9, GridUnitType.Star);
            SetColumn(1, 7);
            SetColumn(2, 1.05, GridUnitType.Star);
            SetColumn(3, 7);
            SetColumn(4, 1.35, GridUnitType.Star);
            SetRow(0, 1, GridUnitType.Star);
            Place(CommitsPanel, 0, 0);
            Place(FilesPanel, 0, 2);
            Place(DiffPanel, 0, 4);
            ConfigureVertical(FirstSplitter, 1);
            ConfigureVertical(SecondSplitter, 3);
        }
        else
        {
            SetColumn(0, 0.8, GridUnitType.Star);
            SetColumn(1, 7);
            SetColumn(2, 1.7, GridUnitType.Star);
            SetRow(0, 1, GridUnitType.Star);
            SetRow(1, 7);
            SetRow(2, 1, GridUnitType.Star);
            Place(CommitsPanel, 0, 0, 3);
            Place(FilesPanel, 0, 2);
            Place(DiffPanel, 2, 2);
            ConfigureVertical(FirstSplitter, 1, 3);
            ConfigureHorizontal(SecondSplitter, 1, 2);
        }
        ApplyDiffVisibility();
    }

    private void OnDiffToggleClick(object? sender, RoutedEventArgs e) => ApplyDiffVisibility();

    private void ApplyDiffVisibility()
    {
        bool visible = DiffToggle.IsChecked == true;
        DiffPanel.IsVisible = visible;
        SecondSplitter.IsVisible = visible;
    }

    private void OnOpenDiffClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedExplorerFile is null) return;
        if (_diffWindow is not null)
        {
            _diffWindow.Activate();
            return;
        }
        _diffWindow = new FocusedDiffWindow(_viewModel, DiffWindowSource.History);
        _diffWindow.Closed += (_, _) => _diffWindow = null;
        _diffWindow.Show(this);
    }

    private async void OnAddToComposeClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedCommitExplorerRevision is not { } revision || _composeSelectionHandler is null)
            return;

        GitCommitFileChange[] selected = FilesList.SelectedItems
            .Cast<object>()
            .OfType<GitCommitFileChange>()
            .ToArray();
        if (selected.Length == 0 && _viewModel.SelectedExplorerFile is { } current)
            selected = [current];
        if (selected.Length == 0) return;

        await _composeSelectionHandler(revision, selected);
    }

    private void ResetWorkspaceGrid()
    {
        foreach (ColumnDefinition column in ExplorerWorkspaceGrid.ColumnDefinitions) column.Width = new GridLength(0);
        foreach (RowDefinition row in ExplorerWorkspaceGrid.RowDefinitions) row.Height = new GridLength(0);
        FirstSplitter.IsVisible = false;
        SecondSplitter.IsVisible = false;
    }

    private void SetColumn(int index, double value, GridUnitType unit = GridUnitType.Pixel) =>
        ExplorerWorkspaceGrid.ColumnDefinitions[index].Width = new GridLength(value, unit);

    private void SetRow(int index, double value, GridUnitType unit = GridUnitType.Pixel) =>
        ExplorerWorkspaceGrid.RowDefinitions[index].Height = new GridLength(value, unit);

    private static void Place(Control control, int row, int column, int rowSpan = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetRowSpan(control, rowSpan);
        Grid.SetColumn(control, column);
    }

    private static void ConfigureVertical(GridSplitter splitter, int column, int rowSpan = 1)
    {
        splitter.IsVisible = true;
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        splitter.Width = 7;
        splitter.Height = double.NaN;
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 0);
        Grid.SetRowSpan(splitter, rowSpan);
    }

    private static void ConfigureHorizontal(GridSplitter splitter, int row, int column)
    {
        splitter.IsVisible = true;
        splitter.ResizeDirection = GridResizeDirection.Rows;
        splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        splitter.Width = double.NaN;
        splitter.Height = 7;
        Grid.SetRow(splitter, row);
        Grid.SetColumn(splitter, column);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _diffWindow?.Close();
        Close();
    }
}
