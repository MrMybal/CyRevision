using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class BranchFileExplorerWindow : Window
{
    private readonly BranchFileExplorerViewModel? _viewModel;

    public BranchFileExplorerWindow()
    {
        InitializeComponent();
    }

    public BranchFileExplorerWindow(BranchFileExplorerViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync();
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) =>
        Topmost = AlwaysOnTopToggle.IsChecked == true;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnRefreshRemoteClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.RefreshRemoteAsync();
    }

    private async void OnRetrieveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.RetrieveSelectedForPreviewAsync();
    }

    private async void OnLoadDiffClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.LoadDiffAsync();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedListFile is not { } file) return;
        IStorageFile? destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {file.Name} from {_viewModel.BranchName}",
            SuggestedFileName = file.Name
        });
        if (destination is null) return;
        try
        {
            await _viewModel.ExportSelectedAsync(destination.Path.LocalPath);
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Export failed", exception.Message);
        }
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedListFile is not { } file) return;
        bool confirmed = await ShowConfirmationAsync(
            "Restore selected file",
            $"Replace only '{file.Path}' in the working tree with the version from '{_viewModel.BranchName}'?\n\n" +
            "CyRevision will back up the current file under .cyrevision/backups before replacement. " +
            "The branch, index, and staging area will not be changed.",
            "Restore file");
        if (!confirmed) return;
        try
        {
            await _viewModel.RestoreSelectedToWorkingTreeAsync();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Restore failed", exception.Message);
        }
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message, string confirmLabel)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 560,
            Height = 250,
            MinWidth = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1E1F22")
        };
        Button cancel = new() { Content = "Cancel", MinWidth = 90 };
        Button confirm = new() { Content = confirmLabel, MinWidth = 110 };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    [Grid.RowProperty] = 1,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm }
                }
            }
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 520,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1E1F22")
        };
        Button close = new() { Content = "Close", MinWidth = 90 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    [Grid.RowProperty] = 1,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close }
                }
            }
        };
        await dialog.ShowDialog(this);
    }
}
