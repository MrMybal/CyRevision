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

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner un dossier à synchroniser ou sauvegarder");
        if (path is not null)
        {
            await _viewModel.AddFolderProjectAsync(path);
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

    private async void OnMergeBranchClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.MergeSelectedBranchAsync();

    private async void OnFetchClick(object? sender, RoutedEventArgs e) => await _viewModel.FetchAsync();

    private async void OnPullClick(object? sender, RoutedEventArgs e) => await _viewModel.PullAsync();

    private async void OnPushClick(object? sender, RoutedEventArgs e) => await _viewModel.PushAsync();

    private async void OnSaveRemoteClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveRemoteAsync();

    private async void OnTrackLfsClick(object? sender, RoutedEventArgs e) => await _viewModel.TrackLfsPatternAsync();

    private async void OnPickBackupStoreClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Sélectionner l'emplacement des sauvegardes");
        if (path is not null)
        {
            await _viewModel.SetBackupStoreAsync(path);
        }
    }

    private async void OnSaveBackupSettingsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveBackupSettingsAsync();

    private async void OnCreateBackupClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateBackupAsync();

    private async void OnRestoreBackupClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync("Restaurer le snapshot dans un dossier vide");
        if (path is not null)
        {
            await _viewModel.RestoreSelectedBackupAsync(path);
        }
    }

    private async void OnApplyPresetClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedPresetAsync();

    private async void OnPickSyncthingClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Sélectionner l'exécutable Syncthing dédié");
        if (path is not null)
        {
            await _viewModel.SetSyncthingExecutableAsync(path);
        }
    }

    private async void OnStartSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.StartSyncAsync();

    private async void OnPauseSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.PauseSyncAsync();

    private async void OnResumeSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.ResumeSyncAsync();

    private async void OnStopSyncClick(object? sender, RoutedEventArgs e) => await _viewModel.StopSyncAsync();

    private async void OnExchangeGitClick(object? sender, RoutedEventArgs e) => await _viewModel.ExchangeGitAsync();

    private async void OnCreateInvitationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreatePeerInvitationAsync();

    private async void OnPrepareJoinRequestClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.PreparePeerJoinRequestAsync();

    private async void OnApproveJoinRequestClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ApprovePeerJoinRequestAsync();

    private async void OnImportMembershipGrantClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ImportMembershipGrantAsync();

    private async void OnRevokePeerClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RevokeSelectedPeerAsync();

    private async void OnPickAssetBaselineClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Choisir l'asset de référence");
        if (path is not null)
        {
            _viewModel.SetAssetBaseline(path);
        }
    }

    private async void OnPickAssetCandidateClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFileAsync("Choisir l'asset candidat");
        if (path is not null)
        {
            _viewModel.SetAssetCandidate(path);
        }
    }

    private async void OnCompareAssetsClick(object? sender, RoutedEventArgs e) => await _viewModel.CompareAssetsAsync();

    private async void OnCompareSelectedToHeadClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CompareSelectedChangeToHeadAsync();

    private async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task<string?> PickFileAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
