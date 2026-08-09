using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CyRevision.Desktop.Controls;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = null!;
    private UiLocalizer? _uiLocalizer;
    private LocalizationService? _localization;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, LocalizationService localization) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _localization = localization;
        _uiLocalizer = new UiLocalizer(this, localization);
        Opened += OnOpened;
        Closed += (_, _) => _uiLocalizer?.Dispose();
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

    private async void OnAnalyzeGitGraphsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeGitGraphsAsync();

    private async void OnGraphCommitSelected(object? sender, GitCommitSelectedEventArgs e) =>
        await _viewModel.SelectExplorerCommitAsync(e.Commit.Hash);

    private async void OnCompareExplorerCommitsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CompareExplorerCommitsAsync();

    private async void OnExportExplorerFileClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickSaveFileAsync(
            _viewModel.SelectedExplorerFile?.Path,
            "Exporter cette version");
        if (path is not null)
        {
            await _viewModel.ExportSelectedExplorerFileAsync(path);
        }
    }

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

    private async void OnExportLfsVersionClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickSaveFileAsync(
            _viewModel.SelectedLfsVersion?.Path,
            "Exporter la version LFS");
        if (path is not null)
        {
            await _viewModel.ExportSelectedLfsVersionAsync(path);
        }
    }

    private async void OnRestoreLfsVersionClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedLfsVersion is null)
        {
            return;
        }

        string title = Translate("Confirmer la restauration");
        string message = Translate(
            "Cette action remplace le fichier de travail par la version sélectionnée. Aucun commit ni indexation ne sera créé automatiquement.");
        if (await ShowConfirmationAsync(title, message))
        {
            await _viewModel.RestoreSelectedLfsVersionAsync();
        }
    }

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

    private async void OnPickColdArchiveClick(object? sender, RoutedEventArgs e)
    {
        string? path = await PickFolderAsync(Translate("Sélectionner l'emplacement de l'archive froide"));
        if (path is not null)
        {
            _viewModel.SetColdArchivePath(path);
        }
    }

    private async void OnArchiveOldBackupsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ArchiveOldBackupsAsync();

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

    private async void OnConfigureVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.ConfigureVpnAsync();

    private async void OnSaveVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.SaveVpnSettingsAsync();

    private async void OnStartVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.StartVpnAsync();

    private async void OnStopVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.StopVpnAsync();

    private async void OnRefreshVpnClick(object? sender, RoutedEventArgs e) => await _viewModel.RefreshVpnAsync();

    private async void OnCreateVpnInvitationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CreateVpnInvitationAsync();

    private async void OnJoinVpnInvitationClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.JoinVpnInvitationAsync();

    private async void OnAcceptVpnResponseClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AcceptVpnResponseAsync();

    private async void OnRemoveVpnPeerClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveSelectedVpnPeerAsync();

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

    private async void OnRefreshAdvisoryReservationsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAdvisoryReservationsAsync();

    private async void OnCleanExpiredAdvisoryReservationsClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveExpiredAdvisoryReservationsAsync();

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

    private async Task<string?> PickSaveFileAsync(string? sourcePath, string title)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Translate(title),
            SuggestedFileName = string.IsNullOrWhiteSpace(sourcePath) ? "revision.bin" : Path.GetFileName(sourcePath),
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        TextBlock description = new()
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 470,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DDE3F0"))
        };
        Button cancel = new()
        {
            Content = Translate("Annuler"),
            Padding = new Avalonia.Thickness(16, 9),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#202941"))
        };
        Button confirm = new()
        {
            Content = Translate("Restaurer"),
            Padding = new Avalonia.Thickness(16, 9),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A3E51")),
            Foreground = Avalonia.Media.Brushes.White
        };
        StackPanel buttons = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 9,
            Children = { cancel, confirm }
        };
        Window dialog = new()
        {
            Title = title,
            Width = 540,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#10172A")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 20,
                Children = { description, buttons }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private string Translate(string source) => _localization?.Translate(source) ?? source;
}
