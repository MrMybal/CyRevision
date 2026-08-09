using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        await _viewModel.InitializeAsync();
    }

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

    private async void OnRemoveProjectClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveSelectedProjectAsync();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();

    private async void OnStageAllClick(object? sender, RoutedEventArgs e) => await _viewModel.StageAllAsync();

    private async void OnUnstageAllClick(object? sender, RoutedEventArgs e) => await _viewModel.UnstageAllAsync();

    private async void OnCommitClick(object? sender, RoutedEventArgs e) => await _viewModel.CommitAsync();

    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateBranchAsync(NewBranchName.Text ?? string.Empty);

    private async void OnCheckoutBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckoutSelectedBranchAsync();

    private async void OnFetchClick(object? sender, RoutedEventArgs e) => await _viewModel.FetchAsync();

    private async void OnPullClick(object? sender, RoutedEventArgs e) => await _viewModel.PullAsync();

    private async void OnPushClick(object? sender, RoutedEventArgs e) => await _viewModel.PushAsync();

    private async void OnSaveRemoteClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveRemoteAsync();

    private async void OnTrackLfsClick(object? sender, RoutedEventArgs e) => await _viewModel.TrackLfsPatternAsync();

    private async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
