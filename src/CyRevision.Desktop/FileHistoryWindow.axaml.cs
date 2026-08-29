using Avalonia.Controls;
using Avalonia.Interactivity;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class FileHistoryWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private ProjectItemViewModel? _project;
    private string _relativePath = string.Empty;
    private IReadOnlyList<FileHistoryRow> _rows = [];
    private CancellationTokenSource? _loadCancellation;
    private CommitExplorerWindow? _commitExplorerWindow;
    private FileHistoryColumnPreferencesStore? _columnPreferencesStore;
    private ContextMenu? _columnsMenu;
    private ContextMenu? _gridColumnsMenu;
    private readonly Dictionary<string, DataGridLength> _defaultColumnWidths = new(StringComparer.Ordinal);
    private bool _restoringColumnPreferences;

    public FileHistoryWindow()
    {
        InitializeComponent();
    }

    public FileHistoryWindow(
        MainWindowViewModel viewModel,
        ProjectItemViewModel project,
        string relativePath,
        string configurationDirectory) : this()
    {
        _viewModel = viewModel;
        _project = project;
        _relativePath = relativePath.Replace('\\', '/');
        _columnPreferencesStore = new FileHistoryColumnPreferencesStore(configurationDirectory);
        Title = $"CyRevision — {System.IO.Path.GetFileName(_relativePath)} history";
        WindowTitleBlock.Text = $"{project.Name} · {System.IO.Path.GetFileName(_relativePath)}";
        PathBlock.Text = _relativePath;
        StatusBlock.Text = "Loading Git history and LFS pointer metadata…";
        ConfigureColumnPreferences();
        RestoreColumnPreferences();
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
    }

    public bool IsShowing(ProjectItemViewModel project, string relativePath) =>
        _project?.Id == project.Id &&
        string.Equals(
            _relativePath,
            relativePath.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private void ConfigureColumnPreferences()
    {
        _defaultColumnWidths.Clear();
        for (int index = 0; index < HistoryGrid.Columns.Count; index++)
        {
            DataGridColumn column = HistoryGrid.Columns[index];
            _defaultColumnWidths[ColumnKey(column, index)] = column.Width;
        }

        _columnsMenu = CreateColumnsMenu();
        _gridColumnsMenu = CreateColumnsMenu();
        ColumnsButton.ContextMenu = _columnsMenu;
        HistoryGrid.ContextMenu = _gridColumnsMenu;
    }

    private ContextMenu CreateColumnsMenu()
    {
        MenuItem visibleColumns = new() { Header = "Visible columns" };
        for (int index = 0; index < HistoryGrid.Columns.Count; index++)
        {
            DataGridColumn column = HistoryGrid.Columns[index];
            string key = ColumnKey(column, index);
            MenuItem item = new()
            {
                Header = key == "Commit" ? "Commit (always visible)" : key,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = column.IsVisible,
                IsEnabled = key != "Commit",
                Tag = column
            };
            item.Click += OnColumnVisibilityClick;
            visibleColumns.Items.Add(item);
        }

        MenuItem reset = new() { Header = "Reset columns" };
        reset.Click += OnResetColumnsClick;
        ContextMenu menu = new()
        {
            ItemsSource = new object[] { visibleColumns, new Separator(), reset }
        };
        menu.Opened += (_, _) => SyncColumnMenuChecks();
        return menu;
    }
    private void RestoreColumnPreferences()
    {
        if (_columnPreferencesStore is null || _project is null)
        {
            return;
        }

        Dictionary<string, FileHistoryColumnPreference> saved = _columnPreferencesStore.Load(_project.Id)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        if (saved.Count == 0)
        {
            return;
        }

        _restoringColumnPreferences = true;
        try
        {
            DataGridColumn[] declarationOrder = HistoryGrid.Columns.ToArray();
            for (int index = 0; index < declarationOrder.Length; index++)
            {
                DataGridColumn column = declarationOrder[index];
                string key = ColumnKey(column, index);
                if (!saved.TryGetValue(key, out FileHistoryColumnPreference? preference))
                {
                    continue;
                }

                column.IsVisible = key == "Commit" || preference.IsVisible;
                if (double.IsFinite(preference.Width) && preference.Width >= column.MinWidth)
                {
                    column.Width = new DataGridLength(preference.Width);
                }
            }

            DataGridColumn[] displayOrder = declarationOrder
                .Select((column, index) => new
                {
                    Column = column,
                    Index = index,
                    Preference = saved.GetValueOrDefault(ColumnKey(column, index))
                })
                .OrderBy(item => item.Preference?.DisplayIndex ?? item.Index)
                .ThenBy(item => item.Index)
                .Select(item => item.Column)
                .ToArray();
            for (int displayIndex = 0; displayIndex < displayOrder.Length; displayIndex++)
            {
                displayOrder[displayIndex].DisplayIndex = displayIndex;
            }
        }
        finally
        {
            _restoringColumnPreferences = false;
        }

        SyncColumnMenuChecks();
    }

    private void SaveColumnPreferences()
    {
        if (_restoringColumnPreferences || _columnPreferencesStore is null || _project is null)
        {
            return;
        }

        Dictionary<string, FileHistoryColumnPreference> previous = _columnPreferencesStore.Load(_project.Id)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        FileHistoryColumnPreference[] preferences = HistoryGrid.Columns
            .Select((column, index) =>
            {
                string key = ColumnKey(column, index);
                double width = column.ActualWidth;
                if (!double.IsFinite(width) || width < column.MinWidth)
                {
                    width = previous.GetValueOrDefault(key)?.Width ?? Math.Max(column.MinWidth, 80);
                }

                return new FileHistoryColumnPreference(
                    key,
                    column.DisplayIndex,
                    width,
                    key == "Commit" || column.IsVisible);
            })
            .OrderBy(item => item.DisplayIndex)
            .ToArray();
        _columnPreferencesStore.Save(_project.Id, preferences);
    }

    private void OnColumnsClick(object? sender, RoutedEventArgs e)
    {
        SyncColumnMenuChecks();
        _columnsMenu?.Open(ColumnsButton);
    }

    private void OnColumnVisibilityClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DataGridColumn column } item)
        {
            return;
        }

        string key = ColumnKey(column, HistoryGrid.Columns.IndexOf(column));
        column.IsVisible = key == "Commit" || item.IsChecked == true;
        SyncColumnMenuChecks();
        SaveColumnPreferences();
    }

    private void OnHistoryColumnReordered(object? sender, DataGridColumnEventArgs e) =>
        SaveColumnPreferences();

    private void OnResetColumnsClick(object? sender, RoutedEventArgs e)
    {
        _restoringColumnPreferences = true;
        try
        {
            for (int index = 0; index < HistoryGrid.Columns.Count; index++)
            {
                DataGridColumn column = HistoryGrid.Columns[index];
                string key = ColumnKey(column, index);
                column.IsVisible = true;
                column.DisplayIndex = index;
                if (_defaultColumnWidths.TryGetValue(key, out DataGridLength width))
                {
                    column.Width = width;
                }
            }
        }
        finally
        {
            _restoringColumnPreferences = false;
        }

        SyncColumnMenuChecks();
        SaveColumnPreferences();
    }

    private void SyncColumnMenuChecks()
    {
        foreach (ContextMenu menu in new[] { _columnsMenu, _gridColumnsMenu }.OfType<ContextMenu>())
        {
            if (menu.Items.OfType<MenuItem>().FirstOrDefault() is not { } root)
            {
                continue;
            }

            foreach (MenuItem item in root.Items.OfType<MenuItem>())
            {
                if (item.Tag is DataGridColumn column)
                {
                    item.IsChecked = column.IsVisible;
                }
            }
        }
    }

    private static string ColumnKey(DataGridColumn column, int index) =>
        column.Tag as string ?? column.Header?.ToString() ?? $"Column {index + 1}";

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened;
        await LoadHistoryAsync(forceRefresh: false);
    }

    private async Task LoadHistoryAsync(bool forceRefresh)
    {
        if (_viewModel is null || _project is null)
        {
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _loadCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        LoadingProgress.IsVisible = true;
        StatusBlock.Text = "Loading commits, authors, renames, and LFS pointer metadata…";

        try
        {
            IReadOnlyList<GitFileRevision> history = await _viewModel.LoadProjectFileHistoryAsync(
                _project,
                _relativePath,
                maximumCount: 500,
                forceRefresh: forceRefresh,
                cancellationToken: cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _rows = history.Select(item => new FileHistoryRow(item)).ToArray();
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            StatusBlock.Text = "History loading cancelled.";
        }
        catch (Exception exception)
        {
            _rows = [];
            HistoryGrid.ItemsSource = _rows;
            StatusBlock.Text = exception.Message;
            ClearDetails();
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                cancellation.Dispose();
                RefreshButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                LoadingProgress.IsVisible = false;
            }
        }
    }

    private void ApplyFilter()
    {
        string query = HistorySearch.Text?.Trim() ?? string.Empty;
        FileHistoryRow[] visible = query.Length == 0
            ? _rows.ToArray()
            : _rows.Where(row => row.Matches(query)).ToArray();
        HistoryGrid.ItemsSource = visible;
        HistoryGrid.SelectedItem = visible.FirstOrDefault();

        int authorCount = _rows.Select(row => row.Author).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int lfsCount = _rows.Count(row => row.IsLfs);
        string visibleSummary = visible.Length == _rows.Count ? string.Empty : $" · {visible.Length:N0} visible";
        StatusBlock.Text = _rows.Count == 0
            ? "No commit found for this file."
            : $"{_rows.Count:N0} commit(s) · {authorCount:N0} author(s) · {lfsCount:N0} LFS version(s){visibleSummary}";
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) =>
        await LoadHistoryAsync(forceRefresh: true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _loadCancellation?.Cancel();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not FileHistoryRow row)
        {
            ClearDetails();
            return;
        }

        OpenCommitButton.IsEnabled = true;
        CommitDetailBlock.Text = row.Revision.Hash;
        AuthorDetailBlock.Text = $"{row.Revision.AuthorName} <{row.Revision.AuthorEmail}>";
        ChangeDetailBlock.Text = $"{row.Date} · {row.Change} · {row.ChangeSummary}{Environment.NewLine}{row.Entry.Path}";
        LfsDetailBlock.Text = row.Entry.LfsPointer is { } pointer
            ? $"OID sha256:{pointer.OidSha256}{Environment.NewLine}Size: {FormatSize(pointer.Size)}"
            : row.Entry.IsBinary
                ? "Binary file stored directly in Git."
                : "Not stored through Git LFS in this revision.";
        MessageDetailBlock.Text = row.Message;
    }

    private void ClearDetails()
    {
        OpenCommitButton.IsEnabled = false;
        CommitDetailBlock.Text = "Select a revision.";
        AuthorDetailBlock.Text = "—";
        ChangeDetailBlock.Text = "—";
        LfsDetailBlock.Text = "—";
        MessageDetailBlock.Text = "—";
    }

    private void OnOpenCommitClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || HistoryGrid.SelectedItem is not FileHistoryRow row)
        {
            return;
        }

        GitRevision[] revisions = _rows.Select(item => item.Revision).ToArray();
        if (_commitExplorerWindow is null)
        {
            CommitExplorerWindow window = new(_viewModel, revisions, row.Revision);
            window.Closed += (_, _) => _commitExplorerWindow = null;
            _commitExplorerWindow = window;
            window.Show(this);
            return;
        }

        _commitExplorerWindow.ShowRevisions(revisions, row.Revision);
        _commitExplorerWindow.Activate();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        SaveColumnPreferences();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _viewModel = null;
        _project = null;
    }

    private static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private sealed class FileHistoryRow
    {
        public FileHistoryRow(GitFileRevision entry) => Entry = entry;

        public GitFileRevision Entry { get; }
        public GitRevision Revision => Entry.Revision;
        public string Commit => Revision.ShortHash;
        public string Author => Revision.AuthorName;
        public string Date => Revision.AuthoredAt.ToLocalTime().ToString("g");
        public string Change => Entry.Kind.ToString();
        public string Message => Revision.Subject;
        public bool IsLfs => Entry.LfsPointer is not null;
        public string Storage => IsLfs ? "Git LFS" : Entry.IsBinary ? "Git binary" : "Git";
        public string Size => Entry.LfsPointer is { } pointer ? FormatSize(pointer.Size) : "—";
        public string ChangeSummary => Entry.IsBinary
            ? "Binary"
            : $"+{Entry.AddedLines ?? 0} / -{Entry.DeletedLines ?? 0}";
        public string ChangeColor => Entry.Kind switch
        {
            GitChangeKind.Added => "#78D7B7",
            GitChangeKind.Deleted => "#FF6B7A",
            GitChangeKind.Renamed => "#C792EA",
            _ => "#61AFEF"
        };
        public string StorageColor => IsLfs ? "#E5C07B" : "#9B9DA3";

        public bool Matches(string query) =>
            Commit.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Revision.Hash.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Revision.AuthorEmail.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (Entry.LfsPointer?.OidSha256.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}