using System.Collections.ObjectModel;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isCyStorePluginEnabled;
    private bool _isCyStoreBusy;
    private double _cyStoreProgress;
    private string _cyStoreProgressText = "CyStore is idle.";
    private string _cyStoreStatusSummary = "Enable the CyStore Alpha plugin for this project to use segmented storage.";
    private string _cyStoreStorePath = string.Empty;
    private string _cyStoreLogicalSize = "0 B";
    private string _cyStoreStoredSize = "0 B";
    private string _cyStoreDeduplicationSummary = "No CyStore statistics available.";
    private string _cyStoreWarnings = string.Empty;
    private string _cyStoreLastResult = "No CyStore operation has been run.";
    private bool _isCyStoreInitialized;
    private CyStoreVersion? _selectedCyStoreVersion;
    private CancellationTokenSource? _cyStoreCancellation;

    public ObservableCollection<CyStoreVersion> CyStoreVersions { get; } = [];

    public bool IsSelectedCyStorePlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.cystore", StringComparison.OrdinalIgnoreCase);

    public bool IsCyStorePluginEnabled
    {
        get => _isCyStorePluginEnabled;
        private set
        {
            if (SetProperty(ref _isCyStorePluginEnabled, value)) NotifyCyStoreCapabilities();
        }
    }

    public bool IsCyStoreBusy
    {
        get => _isCyStoreBusy;
        private set
        {
            if (SetProperty(ref _isCyStoreBusy, value)) NotifyCyStoreCapabilities();
        }
    }

    public bool IsCyStoreInitialized
    {
        get => _isCyStoreInitialized;
        private set
        {
            if (SetProperty(ref _isCyStoreInitialized, value)) NotifyCyStoreCapabilities();
        }
    }

    public double CyStoreProgress
    {
        get => _cyStoreProgress;
        private set => SetProperty(ref _cyStoreProgress, value);
    }

    public string CyStoreProgressText
    {
        get => _cyStoreProgressText;
        private set => SetProperty(ref _cyStoreProgressText, value);
    }

    public string CyStoreStatusSummary
    {
        get => _cyStoreStatusSummary;
        private set => SetProperty(ref _cyStoreStatusSummary, value);
    }

    public string CyStoreStorePath
    {
        get => _cyStoreStorePath;
        private set => SetProperty(ref _cyStoreStorePath, value);
    }

    public string CyStoreLogicalSize
    {
        get => _cyStoreLogicalSize;
        private set => SetProperty(ref _cyStoreLogicalSize, value);
    }

    public string CyStoreStoredSize
    {
        get => _cyStoreStoredSize;
        private set => SetProperty(ref _cyStoreStoredSize, value);
    }

    public string CyStoreDeduplicationSummary
    {
        get => _cyStoreDeduplicationSummary;
        private set => SetProperty(ref _cyStoreDeduplicationSummary, value);
    }

    public string CyStoreWarnings
    {
        get => _cyStoreWarnings;
        private set => SetProperty(ref _cyStoreWarnings, value);
    }

    public string CyStoreLastResult
    {
        get => _cyStoreLastResult;
        private set => SetProperty(ref _cyStoreLastResult, value);
    }

    public CyStoreVersion? SelectedCyStoreVersion
    {
        get => _selectedCyStoreVersion;
        set
        {
            if (SetProperty(ref _selectedCyStoreVersion, value))
                NotifyCyStoreCapabilities();
        }
    }

    public bool CanInitializeCyStore =>
        IsCyStorePluginEnabled && SelectedProject is not null && !IsCyStoreBusy && !IsCyStoreInitialized;

    public bool CanCaptureCyStoreLfs =>
        IsCyStorePluginEnabled && SelectedProject is not null && !IsCyStoreBusy && IsCyStoreInitialized;

    public bool CanOperateOnSelectedCyStoreVersion =>
        IsCyStorePluginEnabled && !IsCyStoreBusy && SelectedCyStoreVersion is not null;

    public bool CanCancelCyStoreOperation => IsCyStoreBusy;

    public async Task RefreshCyStoreAsync()
    {
        ICyStorePlugin? plugin = GetCyStorePlugin();
        ProjectItemViewModel? project = SelectedProject;
        if (plugin is null || project is null)
        {
            ClearCyStoreView();
            return;
        }

        await RunCyStoreOperationAsync(
            "Reading CyStore index",
            async cancellationToken =>
            {
                CyStoreStatus status = await Task.Run(
                    () => plugin.InspectStore(project.RootPath),
                    cancellationToken);
                ApplyCyStoreStatus(status);
                IReadOnlyList<CyStoreVersion> versions = status.IsInitialized
                    ? await plugin.ListVersionsAsync(project.RootPath, cancellationToken)
                    : [];
                ReplaceCollection(CyStoreVersions, versions);
                SelectedCyStoreVersion = CyStoreVersions.FirstOrDefault();
                CyStoreLastResult = status.IsInitialized
                    ? $"Loaded {versions.Count} captured version(s)."
                    : "Initialize CyStore explicitly to create its project-local chunk store.";
            });
    }

    public async Task InitializeCyStoreAsync()
    {
        ICyStorePlugin? plugin = GetCyStorePlugin();
        ProjectItemViewModel? project = SelectedProject;
        if (plugin is null || project is null || IsCyStoreBusy) return;

        await RunCyStoreOperationAsync(
            "Initializing CyStore",
            async cancellationToken =>
            {
                CyStoreStatus status = await plugin.InitializeStoreAsync(project.RootPath, cancellationToken);
                ApplyCyStoreStatus(status);
                CyStoreLastResult =
                    "CyStore initialized under .cyrevision/cystore and excluded locally from Git. No project file, commit, LFS pointer, or remote was changed.";
            });
    }

    public async Task CaptureCyStoreLfsAsync()
    {
        ICyStorePlugin? plugin = GetCyStorePlugin();
        ProjectItemViewModel? project = SelectedProject;
        if (plugin is null || project is null || IsCyStoreBusy || !IsCyStoreInitialized) return;

        await RunCyStoreOperationAsync(
            "Capturing hydrated Git LFS files",
            async cancellationToken =>
            {
                CyStoreBatchCaptureResult result = await plugin.CaptureTrackedGitLfsFilesAsync(
                    project.RootPath,
                    CreateCyStoreProgress(),
                    cancellationToken);
                CyStoreWarnings = string.Join(Environment.NewLine, result.Warnings.Take(100));
                CyStoreLastResult = result.Summary;
                ApplyCyStoreStatus(plugin.InspectStore(project.RootPath));
                ReplaceCollection(
                    CyStoreVersions,
                    await plugin.ListVersionsAsync(project.RootPath, cancellationToken));
                SelectedCyStoreVersion = CyStoreVersions.FirstOrDefault();
            });
    }

    public async Task VerifySelectedCyStoreVersionAsync()
    {
        ICyStorePlugin? plugin = GetCyStorePlugin();
        ProjectItemViewModel? project = SelectedProject;
        CyStoreVersion? version = SelectedCyStoreVersion;
        if (plugin is null || project is null || version is null || IsCyStoreBusy) return;

        await RunCyStoreOperationAsync(
            $"Verifying {version.RelativePath}",
            async cancellationToken =>
            {
                CyStoreVerificationResult result = await plugin.VerifyVersionAsync(
                    project.RootPath,
                    version.Id,
                    CreateCyStoreProgress(),
                    cancellationToken);
                CyStoreLastResult = result.Summary;
            });
    }

    public async Task ReconstructSelectedCyStoreVersionAsync()
    {
        ICyStorePlugin? plugin = GetCyStorePlugin();
        ProjectItemViewModel? project = SelectedProject;
        CyStoreVersion? version = SelectedCyStoreVersion;
        if (plugin is null || project is null || version is null || IsCyStoreBusy) return;

        await RunCyStoreOperationAsync(
            $"Reconstructing {version.RelativePath}",
            async cancellationToken =>
            {
                CyStoreReconstructionResult result = await plugin.ReconstructVersionAsync(
                    project.RootPath,
                    version.Id,
                    CreateCyStoreProgress(),
                    cancellationToken);
                CyStoreLastResult = $"{result.Summary}{Environment.NewLine}{result.DestinationPath}";
            });
    }

    public void CancelCyStoreOperation() => _cyStoreCancellation?.Cancel();

    private async Task RunCyStoreOperationAsync(
        string label,
        Func<CancellationToken, Task> operation)
    {
        if (IsCyStoreBusy) return;
        _cyStoreCancellation?.Dispose();
        _cyStoreCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _cyStoreCancellation.Token;
        IsCyStoreBusy = true;
        CyStoreProgress = 0;
        CyStoreProgressText = label;

        try
        {
            await operation(cancellationToken);
            CyStoreProgress = 100;
            CyStoreProgressText = $"{label} complete.";
        }
        catch (OperationCanceledException)
        {
            CyStoreProgressText = $"{label} cancelled.";
            CyStoreLastResult = "The CyStore operation was cancelled. Completed chunks remain valid and can be reused later.";
        }
        catch (Exception exception)
        {
            CyStoreProgressText = $"{label} failed.";
            CyStoreLastResult = exception.Message;
            StatusMessage = $"CyStore: {exception.Message}";
        }
        finally
        {
            IsCyStoreBusy = false;
            _cyStoreCancellation?.Dispose();
            _cyStoreCancellation = null;
        }
    }

    private IProgress<CyStoreProgress> CreateCyStoreProgress() =>
        new Progress<CyStoreProgress>(progress =>
        {
            CyStoreProgress = progress.Percentage;
            string itemProgress = progress.TotalItems > 0
                ? $" · {progress.CompletedItems}/{progress.TotalItems}"
                : "";
            CyStoreProgressText =
                $"{progress.Stage}{(string.IsNullOrWhiteSpace(progress.Path) ? "" : $" · {progress.Path}")}{itemProgress}";
        });

    private void RefreshCyStorePluginCatalog()
    {
        ICyStorePlugin? plugin = GetCyStorePlugin();
        IsCyStorePluginEnabled = plugin is not null;
        OnPropertyChanged(nameof(IsSelectedCyStorePlugin));

        if (plugin is null || SelectedProject is null)
        {
            ClearCyStoreView();
            return;
        }

        ApplyCyStoreStatus(plugin.InspectStore(SelectedProject.RootPath));
        if (!IsCyStoreInitialized)
        {
            CyStoreVersions.Clear();
            SelectedCyStoreVersion = null;
        }
    }

    private ICyStorePlugin? GetCyStorePlugin() => _pluginManager.GetPlugin<ICyStorePlugin>();

    private void ApplyCyStoreStatus(CyStoreStatus status)
    {
        IsCyStoreInitialized = status.IsInitialized;
        CyStoreStatusSummary = status.Summary;
        CyStoreStorePath = status.StoreRoot;
        CyStoreLogicalSize = FormatByteSize(status.LogicalBytes);
        CyStoreStoredSize = FormatByteSize(status.StoredBytes);
        long saved = Math.Max(0, status.LogicalBytes - status.StoredBytes);
        CyStoreDeduplicationSummary = status.VersionCount == 0
            ? "No captured versions yet."
            : $"{status.UniqueChunkCount} unique chunks · {FormatByteSize(saved)} avoided across captured versions.";
    }

    private void ClearCyStoreView()
    {
        IsCyStoreInitialized = false;
        CyStoreVersions.Clear();
        SelectedCyStoreVersion = null;
        CyStoreStorePath = string.Empty;
        CyStoreLogicalSize = "0 B";
        CyStoreStoredSize = "0 B";
        CyStoreWarnings = string.Empty;
        CyStoreDeduplicationSummary = "No CyStore statistics available.";
        CyStoreStatusSummary = IsCyStorePluginEnabled
            ? "Select a Git project to inspect CyStore."
            : "Enable the CyStore Alpha plugin for this project to use segmented storage.";
        NotifyCyStoreCapabilities();
    }

    private void NotifyCyStoreCapabilities()
    {
        OnPropertyChanged(nameof(CanInitializeCyStore));
        OnPropertyChanged(nameof(CanCaptureCyStoreLfs));
        OnPropertyChanged(nameof(CanOperateOnSelectedCyStoreVersion));
        OnPropertyChanged(nameof(CanCancelCyStoreOperation));
    }
}
