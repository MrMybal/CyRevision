using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Desktop.Controls;

public enum RevisionCompositionSection
{
    MultiRestore,
    CherryPick
}

public enum MultiRestoreLayoutMode
{
    Columns,
    FilesFocus
}

public partial class RevisionCompositionView : UserControl
{
    private MultiRestoreLayoutMode _multiRestoreLayout = MultiRestoreLayoutMode.Columns;
    private FocusedDiffWindow? _multiRestoreDiffWindow;
    private FocusedDiffWindow? _cherryPickDiffWindow;
    private CommitExplorerWindow? _commitExplorerWindow;

    public event EventHandler? DiffVisibilityChanged;

    public bool IsMultiRestoreDiffVisible => MultiRestoreDiffToggle.IsChecked == true;
    public bool IsCherryPickDiffVisible => CherryPickDiffToggle.IsChecked == true;

    public RevisionCompositionView()
    {
        InitializeComponent();
        MultiRestoreDiffToggle.IsChecked = true;
        CherryPickDiffToggle.IsChecked = true;
        ApplyMultiRestoreLayout(MultiRestoreLayoutMode.Columns);
        ApplyCherryPickDiffVisibility();
    }

    public void SetDiffVisibility(bool multiRestore, bool cherryPick)
    {
        MultiRestoreDiffToggle.IsChecked = multiRestore;
        CherryPickDiffToggle.IsChecked = cherryPick;
        ApplyMultiRestoreLayout(_multiRestoreLayout);
        ApplyCherryPickDiffVisibility();
    }

    public void SelectSection(RevisionCompositionSection section) =>
        CompositionTabs.SelectedItem = section == RevisionCompositionSection.MultiRestore ? MultiRestoreTab : CherryPickTab;

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnMultiRestoreColumnsLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyMultiRestoreLayout(MultiRestoreLayoutMode.Columns);

    private void OnMultiRestoreFilesLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyMultiRestoreLayout(MultiRestoreLayoutMode.FilesFocus);

    private void ApplyMultiRestoreLayout(MultiRestoreLayoutMode layout)
    {
        _multiRestoreLayout = layout;
        MultiRestoreColumnsLayoutToggle.IsChecked = layout == MultiRestoreLayoutMode.Columns;
        MultiRestoreFilesLayoutToggle.IsChecked = layout == MultiRestoreLayoutMode.FilesFocus;
        bool showDiff = MultiRestoreDiffToggle.IsChecked == true;
        MultiRestoreDiffPanel.IsVisible = showDiff;
        MultiRestoreFilesSplitter.IsVisible = showDiff;
        MultiRestoreWorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(
            layout == MultiRestoreLayoutMode.Columns ? 0.72 : 0.48,
            GridUnitType.Star);
        MultiRestoreWorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(
            layout == MultiRestoreLayoutMode.Columns ? 1.18 : 1.55,
            GridUnitType.Star);
        MultiRestoreWorkspaceGrid.ColumnDefinitions[3].Width = new GridLength(showDiff ? 7 : 0);
        MultiRestoreWorkspaceGrid.ColumnDefinitions[4].Width = showDiff
            ? new GridLength(layout == MultiRestoreLayoutMode.Columns ? 1 : 0.82, GridUnitType.Star)
            : new GridLength(0);
    }

    private void OnMultiRestoreDiffToggleClick(object? sender, RoutedEventArgs e)
    {
        ApplyMultiRestoreLayout(_multiRestoreLayout);
        DiffVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCherryPickDiffToggleClick(object? sender, RoutedEventArgs e)
    {
        ApplyCherryPickDiffVisibility();
        DiffVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyCherryPickDiffVisibility()
    {
        bool showDiff = CherryPickDiffToggle.IsChecked == true;
        CherryPickDiffPanel.IsVisible = showDiff;
        CherryPickDiffSplitter.IsVisible = showDiff;
        if (CherryPickDiffPanel.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(1.28, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(showDiff ? 7 : 0);
            grid.ColumnDefinitions[2].Width = showDiff
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private void OnOpenMultiRestoreDiffClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedMultiRestoreFile is not { } file || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_multiRestoreDiffWindow is not null)
        {
            _multiRestoreDiffWindow.Activate();
            return;
        }
        _multiRestoreDiffWindow = new FocusedDiffWindow(ViewModel, DiffWindowSource.MultiRestore);
        _multiRestoreDiffWindow.Closed += (_, _) => _multiRestoreDiffWindow = null;
        _multiRestoreDiffWindow.Show(owner);
    }

    private void OnOpenCherryPickDiffClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedCherryPickCommit is not { } commit || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_cherryPickDiffWindow is not null)
        {
            _cherryPickDiffWindow.Activate();
            return;
        }
        _cherryPickDiffWindow = new FocusedDiffWindow(ViewModel, DiffWindowSource.CherryPick);
        _cherryPickDiffWindow.Closed += (_, _) => _cherryPickDiffWindow = null;
        _cherryPickDiffWindow.Show(owner);
    }

    private void OnOpenMultiRestoreCommitExplorerClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not Window owner) return;
        GitRevision? selected = viewModel.MultiRestoreCommit ?? viewModel.History.FirstOrDefault();
        if (selected is null) return;

        if (_commitExplorerWindow is null)
        {
            _commitExplorerWindow = new CommitExplorerWindow(viewModel, viewModel.History, selected);
            _commitExplorerWindow.EnableComposeSelection(async (revision, files) =>
            {
                await viewModel.AddCommitExplorerFilesToMultiRestoreAsync(revision, files);
                SelectSection(RevisionCompositionSection.MultiRestore);
            });
            _commitExplorerWindow.Closed += (_, _) => _commitExplorerWindow = null;
            _commitExplorerWindow.Show(owner);
            return;
        }

        _commitExplorerWindow.ShowRevisions(viewModel.History, selected);
        _commitExplorerWindow.Activate();
    }

    private async void OnLoadMultiRestoreClick(object? sender, RoutedEventArgs e) =>
        await (ViewModel?.LoadMultiRestoreCommitAsync() ?? Task.CompletedTask);

    private void OnSelectAllRestoreClick(object? sender, RoutedEventArgs e) => ViewModel?.SelectAllMultiRestoreFiles(true);
    private void OnClearRestoreClick(object? sender, RoutedEventArgs e) => ViewModel?.SelectAllMultiRestoreFiles(false);

    private async void OnPreviewMultiRestoreClick(object? sender, RoutedEventArgs e) =>
        await (ViewModel?.PreviewMultiRestoreAsync() ?? Task.CompletedTask);

    private async void OnApplyMultiRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !await ConfirmAsync(
                "Apply multi restore",
                "The selected versions will be written to the working tree. The Git index and commit history stay unchanged. A safety backup is created first.",
                "Apply versions"))
        {
            return;
        }
        await ViewModel.ApplyMultiRestoreAsync();
    }

    private async void OnCompareBranchesClick(object? sender, RoutedEventArgs e) =>
        await (ViewModel?.CompareBranchesForCompositionAsync() ?? Task.CompletedTask);

    private void OnSelectSourceOnlyClick(object? sender, RoutedEventArgs e) => ViewModel?.SelectAllSourceOnlyCommits(true);
    private void OnClearCherryPickClick(object? sender, RoutedEventArgs e) => ViewModel?.SelectAllSourceOnlyCommits(false);
    private void OnMoveCherryPickUpClick(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelectedCherryPickCommit(-1);
    private void OnMoveCherryPickDownClick(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelectedCherryPickCommit(1);

    private async void OnPreviewCherryPickClick(object? sender, RoutedEventArgs e) =>
        await (ViewModel?.PreviewCherryPickAsync() ?? Task.CompletedTask);

    private async void OnApplyCherryPickClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !await ConfirmAsync(
                "Apply cherry-pick composition",
                "The selected commits will update the target branch locally in the displayed order. CyRevision will not push the branch.",
                "Apply commits"))
        {
            return;
        }
        await ViewModel.ApplyCherryPickAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return false;
        }

        Button cancel = new() { Content = "Cancel", Padding = new Avalonia.Thickness(14, 7) };
        Button confirm = new()
        {
            Content = confirmLabel,
            Padding = new Avalonia.Thickness(14, 7),
            Background = new SolidColorBrush(Color.Parse("#3574F0")),
            Foreground = Brushes.White
        };
        Window dialog = new()
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1E1F22")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(22),
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm }
                    }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(owner);
    }
}
