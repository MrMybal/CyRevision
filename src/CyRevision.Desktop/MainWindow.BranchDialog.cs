using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e)
    {
        CreateBranchDialogResult? options = await ShowCreateBranchDialogAsync();
        if (options is null)
        {
            return;
        }

        _viewModel.CreateHistoricalBranchInWorktree = options.UseIsolatedWorktree;
        if (options.FromSelectedCommit)
        {
            await _viewModel.CreateBranchFromSelectedCommitAsync(options.BranchName);
        }
        else
        {
            await _viewModel.CreateBranchAsync(options.BranchName);
        }
    }

    private async Task<CreateBranchDialogResult?> ShowCreateBranchDialogAsync()
    {
        GitRevision? selectedRevision = _viewModel.SelectedBranchRevision;
        TextBox branchName = new()
        {
            PlaceholderText = "feature/name",
            MinWidth = 420
        };
        CheckBox fromSelectedCommit = new()
        {
            Content = selectedRevision is null
                ? "Create from selected commit (select a commit first)"
                : $"Create from selected commit {selectedRevision.ShortHash}",
            IsEnabled = selectedRevision is not null,
            IsChecked = false
        };
        CheckBox isolatedWorktree = new()
        {
            Content = "Use an isolated worktree (recommended for historical commits)",
            IsChecked = _viewModel.CreateHistoricalBranchInWorktree,
            IsEnabled = false
        };
        TextBlock baseSummary = new()
        {
            Text = $"Base: current branch {_viewModel.CurrentBranch}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#9B9DA3"))
        };
        Button cancel = new()
        {
            Content = "Cancel",
            Padding = new Thickness(16, 8)
        };
        Button create = new()
        {
            Content = "Create branch",
            Padding = new Thickness(16, 8),
            IsEnabled = false,
            Background = new SolidColorBrush(Color.Parse("#3574F0")),
            Foreground = Brushes.White
        };
        Window dialog = new()
        {
            Title = "Create Git branch",
            Width = 540,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1E1F22"))
        };

        branchName.TextChanged += (_, _) => create.IsEnabled = !string.IsNullOrWhiteSpace(branchName.Text);
        fromSelectedCommit.IsCheckedChanged += (_, _) =>
        {
            bool historical = fromSelectedCommit.IsChecked == true && selectedRevision is not null;
            isolatedWorktree.IsEnabled = historical;
            baseSummary.Text = historical
                ? $"Base: {selectedRevision!.ShortHash} · {selectedRevision.Subject}"
                : $"Base: current branch {_viewModel.CurrentBranch}";
        };
        cancel.Click += (_, _) => dialog.Close(null);
        create.Click += (_, _) =>
        {
            string name = branchName.Text?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                return;
            }

            dialog.Close(new CreateBranchDialogResult(
                name,
                fromSelectedCommit.IsChecked == true && selectedRevision is not null,
                isolatedWorktree.IsChecked == true));
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Create a local branch",
                    FontSize = 19,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Choose the branch name and its starting point. Remote configuration remains available in Overview > Git.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#B8C1D8"))
                },
                branchName,
                baseSummary,
                fromSelectedCommit,
                isolatedWorktree,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, create }
                }
            }
        };

        dialog.Opened += (_, _) => branchName.Focus();
        return await dialog.ShowDialog<CreateBranchDialogResult?>(this);
    }

    private sealed record CreateBranchDialogResult(
        string BranchName,
        bool FromSelectedCommit,
        bool UseIsolatedWorktree);
}
