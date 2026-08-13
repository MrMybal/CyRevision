using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CyRevision.Desktop.ViewModels;

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
    public RevisionCompositionView()
    {
        InitializeComponent();
        ApplyMultiRestoreLayout(MultiRestoreLayoutMode.Columns);
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
        MultiRestoreColumnsLayoutToggle.IsChecked = layout == MultiRestoreLayoutMode.Columns;
        MultiRestoreFilesLayoutToggle.IsChecked = layout == MultiRestoreLayoutMode.FilesFocus;
        MultiRestoreWorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(
            layout == MultiRestoreLayoutMode.Columns ? 0.72 : 0.48,
            GridUnitType.Star);
        MultiRestoreWorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(
            layout == MultiRestoreLayoutMode.Columns ? 1.18 : 1.55,
            GridUnitType.Star);
        MultiRestoreWorkspaceGrid.ColumnDefinitions[4].Width = new GridLength(
            layout == MultiRestoreLayoutMode.Columns ? 1 : 0.82,
            GridUnitType.Star);
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
