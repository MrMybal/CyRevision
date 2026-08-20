using Avalonia.Controls;
using Avalonia.Interactivity;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class GitConflictResolverWindow : Window
{
    private UiLocalizer? _uiLocalizer;
    private LocalizationService? _localization;
    private GitConflictResolverViewModel? _viewModel;

    public GitConflictResolverWindow()
    {
        InitializeComponent();
    }

    public GitConflictResolverWindow(
        GitConflictResolverViewModel viewModel,
        LocalizationService? localization = null) : this()
    {
        _viewModel = viewModel;
        _localization = localization;
        DataContext = viewModel;
        if (localization is not null) _uiLocalizer = new UiLocalizer(this, localization);
        viewModel.OperationCompleted += OnOperationCompleted;
        Closed += OnWindowClosed;
        Opened += OnWindowOpened;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_viewModel is not null) await _viewModel.InitializeAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null) _viewModel.OperationCompleted -= OnOperationCompleted;
        _uiLocalizer?.Dispose();
        _uiLocalizer = null;
        _localization = null;
        _viewModel = null;
    }

    private void OnOperationCompleted(object? sender, EventArgs e) => Close(true);

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) =>
        Topmost = AlwaysOnTopToggle.IsChecked == true;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);
    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await _viewModel!.RefreshAsync();
    private void OnUseBaseClick(object? sender, RoutedEventArgs e) => _viewModel?.UseBase();
    private void OnUseOursClick(object? sender, RoutedEventArgs e) => _viewModel?.UseOurs();
    private void OnUseTheirsClick(object? sender, RoutedEventArgs e) => _viewModel?.UseTheirs();
    private void OnAcceptBlockOursClick(object? sender, RoutedEventArgs e) => _viewModel?.AcceptCurrentBlockOurs();
    private void OnAcceptBlockIncomingClick(object? sender, RoutedEventArgs e) => _viewModel?.AcceptCurrentBlockIncoming();
    private void OnPreviousConflictBlockClick(object? sender, RoutedEventArgs e) => _viewModel?.SelectPreviousConflictBlock();
    private void OnNextConflictBlockClick(object? sender, RoutedEventArgs e) => _viewModel?.SelectNextConflictBlock();
    private async void OnAskAiClick(object? sender, RoutedEventArgs e) => await _viewModel!.AskAiAsync(false);
    private async void OnProposeAiClick(object? sender, RoutedEventArgs e) => await _viewModel!.AskAiAsync(true);
    private void OnPreviewAiClick(object? sender, RoutedEventArgs e) => _viewModel?.PreviewAiProposal();
    private async void OnResolveClick(object? sender, RoutedEventArgs e) => await _viewModel!.ResolveSelectedAsync();
    private async void OnContinueClick(object? sender, RoutedEventArgs e) => await _viewModel!.ContinueAsync();

    private async void OnAbortClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        Window confirmation = new()
        {
            Title = "Abort Git operation",
            Width = 470,
            Height = 190,
            MinWidth = 470,
            MinHeight = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1E1F22")
        };
        Grid grid = new() { RowDefinitions = RowDefinitions.Parse("*,Auto"), Margin = new Avalonia.Thickness(16) };
        grid.Children.Add(new TextBlock
        {
            Text = "Abort the current Git operation and restore its pre-operation state? Resolutions made during this operation will be discarded.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        StackPanel actions = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        Grid.SetRow(actions, 1);
        Button cancel = new() { Content = "Cancel", Classes = { "secondary" } };
        Button abort = new() { Content = "Abort operation", Classes = { "danger" } };
        cancel.Click += (_, _) => confirmation.Close(false);
        abort.Click += (_, _) => confirmation.Close(true);
        actions.Children.Add(cancel);
        actions.Children.Add(abort);
        grid.Children.Add(actions);
        confirmation.Content = grid;
        UiLocalizer? confirmationLocalizer = _localization is null
            ? null
            : new UiLocalizer(confirmation, _localization);
        try
        {
            if (await confirmation.ShowDialog<bool>(this)) await _viewModel.AbortAsync();
        }
        finally
        {
            confirmationLocalizer?.Dispose();
        }
    }
}
