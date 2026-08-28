using System.Diagnostics;
using Avalonia.Interactivity;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private void OnNewGitRemoteClick(object? sender, RoutedEventArgs e) =>
        _viewModel.BeginNewGitRemote();

    private async void OnRefreshGitRemotesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshGitOverviewRemotesAsync();

    private async void OnSaveGitOverviewRemoteClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveGitRemoteAsync();

    private void OnOpenGitRemotePageClick(object? sender, RoutedEventArgs e)
    {
        string? webUrl = _viewModel.SelectedGitRemote?.WebUrl;
        if (string.IsNullOrWhiteSpace(webUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = webUrl,
            UseShellExecute = true
        });
    }

    private async void OnRemoveGitRemoteClick(object? sender, RoutedEventArgs e)
    {
        GitRemoteInfo? remote = _viewModel.SelectedGitRemote;
        if (remote is null)
        {
            return;
        }

        if (!await ShowConfirmationAsync(
                Translate("Remove Git remote"),
                Translate($"Remove remote '{remote.Name}' from this local repository? This removes only the local Git configuration and remote-tracking references. The server repository and its branches are not deleted."),
                Translate("Remove remote")))
        {
            return;
        }

        await _viewModel.RemoveSelectedGitRemoteAsync();
    }
}
