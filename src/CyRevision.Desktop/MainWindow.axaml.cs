using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using CyRevision.Desktop.Controls;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;

namespace CyRevision.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = null!;
    private UiLocalizer? _uiLocalizer;
    private LocalizationService? _localization;
    private FocusedDiffWindow? _focusedDiffWindow;
    private WorkspaceLayoutPreferencesStore? _workspaceLayoutStore;
    private HistoryLayoutMode _historyLayout = HistoryLayoutMode.Columns;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        LocalizationService localization,
        string configurationDirectory) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _localization = localization;
        _workspaceLayoutStore = new WorkspaceLayoutPreferencesStore(configurationDirectory);
        WorkspaceLayoutPreferences preferences = _workspaceLayoutStore.Load();
        HistoryTimelineToggle.IsChecked = preferences.ShowTimeline;
        HistoryFilesToggle.IsChecked = preferences.ShowFiles;
        HistoryDiffToggle.IsChecked = preferences.ShowDiff;
        ApplyHistoryLayout(preferences.HistoryLayout, false);
        _uiLocalizer = new UiLocalizer(this, localization);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _focusedDiffWindow?.Close();
        _focusedDiffWindow = null;
        _uiLocalizer?.Dispose();
    }

    private void OnColumnsHistoryLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyHistoryLayout(HistoryLayoutMode.Columns);

    private void OnReviewHistoryLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyHistoryLayout(HistoryLayoutMode.Review);

    private void OnDiffFocusedHistoryLayoutClick(object? sender, RoutedEventArgs e) =>
        ApplyHistoryLayout(HistoryLayoutMode.DiffFocus);

    private void ApplyHistoryLayout(HistoryLayoutMode layout, bool persist = true)
    {
        _historyLayout = layout;
        HistoryColumnsLayoutToggle.IsChecked = layout == HistoryLayoutMode.Columns;
        HistoryReviewLayoutToggle.IsChecked = layout == HistoryLayoutMode.Review;
        HistoryDiffFocusLayoutToggle.IsChecked = layout == HistoryLayoutMode.DiffFocus;
        RefreshHistoryWorkspaceLayout();
        if (persist)
        {
            SaveHistoryLayoutPreferences();
        }
    }

    private void OnHistoryPanelToggleClick(object? sender, RoutedEventArgs e)
    {
        if (HistoryTimelineToggle.IsChecked != true &&
            HistoryFilesToggle.IsChecked != true &&
            HistoryDiffToggle.IsChecked != true && sender is ToggleButton toggle)
        {
            toggle.IsChecked = true;
        }

        RefreshHistoryWorkspaceLayout();
        SaveHistoryLayoutPreferences();
    }

    private void RefreshHistoryWorkspaceLayout()
    {
        Control[] panels = [HistoryTimelinePanel, HistoryFilesPanel, HistoryDiffPanel];
        bool[] visible =
        [
            HistoryTimelineToggle.IsChecked == true,
            HistoryFilesToggle.IsChecked == true,
            HistoryDiffToggle.IsChecked == true
        ];

        for (int index = 0; index < panels.Length; index++)
        {
            panels[index].IsVisible = visible[index];
        }

        ResetHistoryWorkspaceGrid();
        int[] visibleIndices = Enumerable.Range(0, visible.Length).Where(index => visible[index]).ToArray();
        if (visibleIndices.Length < 3)
        {
            ApplyFlowHistoryLayout(panels, visibleIndices);
            return;
        }

        switch (_historyLayout)
        {
            case HistoryLayoutMode.Review:
                ApplyReviewHistoryLayout();
                break;
            case HistoryLayoutMode.DiffFocus:
                ApplyDiffFocusedHistoryLayout();
                break;
            default:
                ApplyColumnsHistoryLayout();
                break;
        }
    }

    private void ResetHistoryWorkspaceGrid()
    {
        foreach (ColumnDefinition column in HistoryWorkspaceGrid.ColumnDefinitions)
        {
            column.Width = new GridLength(0);
        }

        foreach (RowDefinition row in HistoryWorkspaceGrid.RowDefinitions)
        {
            row.Height = new GridLength(0);
        }

        HistorySplitterOne.IsVisible = false;
        HistorySplitterTwo.IsVisible = false;
    }

    private void ApplyColumnsHistoryLayout()
    {
        SetHistoryColumn(0, 0.9, GridUnitType.Star);
        SetHistoryColumn(1, 6);
        SetHistoryColumn(2, 1.25, GridUnitType.Star);
        SetHistoryColumn(3, 6);
        SetHistoryColumn(4, 1.15, GridUnitType.Star);
        SetHistoryRow(0, 1, GridUnitType.Star);
        PlaceHistoryPanel(HistoryTimelinePanel, 0, 0);
        PlaceHistoryPanel(HistoryFilesPanel, 0, 2);
        PlaceHistoryPanel(HistoryDiffPanel, 0, 4);
        ConfigureVerticalHistorySplitter(HistorySplitterOne, 1);
        ConfigureVerticalHistorySplitter(HistorySplitterTwo, 3);
    }

    private void ApplyReviewHistoryLayout()
    {
        SetHistoryColumn(0, 0.78, GridUnitType.Star);
        SetHistoryColumn(1, 6);
        SetHistoryColumn(2, 1.72, GridUnitType.Star);
        SetHistoryRow(0, 0.82, GridUnitType.Star);
        SetHistoryRow(1, 6);
        SetHistoryRow(2, 1.18, GridUnitType.Star);
        PlaceHistoryPanel(HistoryTimelinePanel, 0, 0, 3);
        PlaceHistoryPanel(HistoryFilesPanel, 0, 2);
        PlaceHistoryPanel(HistoryDiffPanel, 2, 2);
        ConfigureVerticalHistorySplitter(HistorySplitterOne, 1, 3);
        ConfigureHorizontalHistorySplitter(HistorySplitterTwo, 1, 2);
    }

    private void ApplyDiffFocusedHistoryLayout()
    {
        SetHistoryColumn(0, 0.72, GridUnitType.Star);
        SetHistoryColumn(1, 6);
        SetHistoryColumn(2, 1.78, GridUnitType.Star);
        SetHistoryRow(0, 1, GridUnitType.Star);
        SetHistoryRow(1, 6);
        SetHistoryRow(2, 1, GridUnitType.Star);
        PlaceHistoryPanel(HistoryFilesPanel, 0, 0);
        PlaceHistoryPanel(HistoryTimelinePanel, 2, 0);
        PlaceHistoryPanel(HistoryDiffPanel, 0, 2, 3);
        ConfigureVerticalHistorySplitter(HistorySplitterOne, 1, 3);
        ConfigureHorizontalHistorySplitter(HistorySplitterTwo, 1, 0);
    }

    private void ApplyFlowHistoryLayout(IReadOnlyList<Control> panels, IReadOnlyList<int> visibleIndices)
    {
        SetHistoryRow(0, 1, GridUnitType.Star);
        for (int visibleIndex = 0; visibleIndex < visibleIndices.Count; visibleIndex++)
        {
            int column = visibleIndex * 2;
            int panelIndex = visibleIndices[visibleIndex];
            double weight = panelIndex == 2 ? 1.35 : 1;
            SetHistoryColumn(column, weight, GridUnitType.Star);
            PlaceHistoryPanel(panels[panelIndex], 0, column);
        }

        if (visibleIndices.Count >= 2)
        {
            SetHistoryColumn(1, 6);
            ConfigureVerticalHistorySplitter(HistorySplitterOne, 1);
        }
    }

    private void ConfigureVerticalHistorySplitter(GridSplitter splitter, int column, int rowSpan = 1)
    {
        splitter.IsVisible = true;
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        splitter.Width = 6;
        splitter.Height = double.NaN;
        splitter.Margin = new Thickness(1, 10);
        splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Grid.SetRow(splitter, 0);
        Grid.SetRowSpan(splitter, rowSpan);
        Grid.SetColumn(splitter, column);
        Grid.SetColumnSpan(splitter, 1);
    }

    private void ConfigureHorizontalHistorySplitter(GridSplitter splitter, int row, int column)
    {
        splitter.IsVisible = true;
        splitter.ResizeDirection = GridResizeDirection.Rows;
        splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        splitter.Width = double.NaN;
        splitter.Height = 6;
        splitter.Margin = new Thickness(10, 1);
        splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Grid.SetRow(splitter, row);
        Grid.SetRowSpan(splitter, 1);
        Grid.SetColumn(splitter, column);
        Grid.SetColumnSpan(splitter, 1);
    }

    private static void PlaceHistoryPanel(Control panel, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(panel, row);
        Grid.SetRowSpan(panel, rowSpan);
        Grid.SetColumn(panel, column);
        Grid.SetColumnSpan(panel, columnSpan);
    }

    private void SetHistoryColumn(int index, double value, GridUnitType unit = GridUnitType.Pixel) =>
        HistoryWorkspaceGrid.ColumnDefinitions[index].Width = new GridLength(value, unit);

    private void SetHistoryRow(int index, double value, GridUnitType unit = GridUnitType.Pixel) =>
        HistoryWorkspaceGrid.RowDefinitions[index].Height = new GridLength(value, unit);

    private void SaveHistoryLayoutPreferences()
    {
        _workspaceLayoutStore?.Save(new WorkspaceLayoutPreferences(
            _historyLayout,
            HistoryTimelineToggle.IsChecked == true,
            HistoryFilesToggle.IsChecked == true,
            HistoryDiffToggle.IsChecked == true));
    }

    private void OnOpenFocusedDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_focusedDiffWindow is not null)
        {
            if (_focusedDiffWindow.WindowState == WindowState.Minimized)
            {
                _focusedDiffWindow.WindowState = WindowState.Normal;
            }

            _focusedDiffWindow.Activate();
            return;
        }

        if (_localization is null)
        {
            return;
        }

        FocusedDiffWindow window = new(_viewModel, _localization);
        window.Closed += (_, _) => _focusedDiffWindow = null;
        _focusedDiffWindow = window;
        window.Show(this);
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

    private async void OnRequestLfsVersionClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RequestSelectedLfsVersionFromPeerAsync();

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

    private async void OnSaveDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SaveDiscordAgentAsync();

    private async void OnStartDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StartDiscordAgentAsync();

    private async void OnStopDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.StopDiscordAgentAsync();

    private async void OnCheckDiscordAgentClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckDiscordAgentNowAsync();

    private async void OnTestDiscordWebhookClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.TestDiscordWebhookAsync();

    private async void OnRemoveDiscordWebhookClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RemoveDiscordWebhookAsync();

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

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.CheckForUpdatesAsync();

    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        string? packagePath = await _viewModel.DownloadAvailableUpdateAsync();
        if (packagePath is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = packagePath,
            UseShellExecute = true
        });
    }

    private void OnOpenUpdateReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_viewModel.UpdateReleasePageUrl, UriKind.Absolute, out Uri? releaseUri) ||
            !releaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !releaseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = releaseUri.AbsoluteUri,
            UseShellExecute = true
        });
    }

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
