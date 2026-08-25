using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CyRevision.Desktop.ViewModels;
using CyRevision.PullRequests;

namespace CyRevision.Desktop;

public enum CiLogWindowSource
{
    ContinuousIntegration,
    PullRequest
}

public partial class CiLogWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private MainWindowViewModel? _viewModel;
    private ObservableCollection<CiLogLine>? _logLines;
    private CiLogWindowSource _source;
    private int _visibleLineCount;

    public CiLogWindow()
    {
        InitializeComponent();
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    public CiLogWindow(MainWindowViewModel viewModel, CiLogWindowSource source) : this()
    {
        _viewModel = viewModel;
        _source = source;
        _logLines = source == CiLogWindowSource.PullRequest
            ? viewModel.PullRequestCiLogLines
            : viewModel.CiLogLines;
        _logLines.CollectionChanged += OnLogLinesChanged;
        LogFilter.ItemsSource = viewModel.CiLogFilterModes;
        SynchronizeFilter();
        ApplyFilters();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnWindowClosed;
    }

    private void OnFilterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || LogFilter.SelectedItem is not CiLogFilterMode mode) return;
        if (_source == CiLogWindowSource.PullRequest)
            _viewModel.PullRequestCiLogFilterMode = mode;
        else
            _viewModel.CiLogFilterMode = mode;
        ApplyFilters();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ScheduleRefresh();

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (_logLines is null) return;
        CiLogFilterMode mode = LogFilter.SelectedItem is CiLogFilterMode selected
            ? selected
            : CiLogFilterMode.All;
        string query = LogSearch.Text?.Trim() ?? string.Empty;
        CiLogLine[] visible = _logLines
            .Where(line => line.Matches(mode))
            .Where(line => query.Length == 0 || MatchesSearch(line, query))
            .ToArray();
        _visibleLineCount = visible.Length;
        LogLinesGrid.ItemsSource = visible;
        UpdateHeader();
    }

    private static bool MatchesSearch(CiLogLine line, string query) =>
        line.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        line.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        line.NumberText.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.PropertyName is nameof(MainWindowViewModel.CiLogFilterMode) or
            nameof(MainWindowViewModel.PullRequestCiLogFilterMode))
        {
            SynchronizeFilter();
            ApplyFilters();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.CiStatus) or
            nameof(MainWindowViewModel.PullRequestCiStatus) or
            nameof(MainWindowViewModel.SelectedCiRun) or
            nameof(MainWindowViewModel.SelectedPullRequestCiRun) or
            nameof(MainWindowViewModel.SelectedPullRequest))
            UpdateHeader();
    }

    private void SynchronizeFilter()
    {
        if (_viewModel is null) return;
        CiLogFilterMode mode = _source == CiLogWindowSource.PullRequest
            ? _viewModel.PullRequestCiLogFilterMode
            : _viewModel.CiLogFilterMode;
        if (!Equals(LogFilter.SelectedItem, mode)) LogFilter.SelectedItem = mode;
    }

    private void UpdateHeader()
    {
        if (_viewModel is null) return;
        CiLogFilterMode mode = LogFilter.SelectedItem is CiLogFilterMode selected
            ? selected
            : CiLogFilterMode.All;
        bool narrowed = !string.IsNullOrWhiteSpace(LogSearch.Text) || mode != CiLogFilterMode.All;
        string visibleSummary = narrowed && _logLines is not null
            ? $" · {_visibleLineCount:N0} visible / {_logLines.Count:N0}"
            : string.Empty;

        if (_source == CiLogWindowSource.PullRequest)
        {
            string pullRequest = _viewModel.SelectedPullRequest is { } selectedPullRequest
                ? $"PR #{selectedPullRequest.Number}"
                : "Pull request";
            string run = _viewModel.SelectedPullRequestCiRun?.Name ?? "No run selected";
            Title = $"CyRevision — {pullRequest} CI log";
            WindowTitleBlock.Text = $"{pullRequest} · {run} · full log";
            StatusBlock.Text = _viewModel.PullRequestCiStatus + visibleSummary;
            return;
        }

        string ciRun = _viewModel.SelectedCiRun?.Name ?? "No run selected";
        Title = "CyRevision — CI full log";
        WindowTitleBlock.Text = $"CI / Actions · {ciRun} · full log";
        StatusBlock.Text = _viewModel.CiStatus + visibleSummary;
    }

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) =>
        Topmost = AlwaysOnTopToggle.IsChecked == true;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        if (_logLines is not null) _logLines.CollectionChanged -= OnLogLinesChanged;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _logLines = null;
        _viewModel = null;
    }
}
