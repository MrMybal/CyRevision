using System.Collections.ObjectModel;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isPerforceIntegrationEnabled;
    private bool _isPerforceBusy;
    private string _perforceCliPath = "p4";
    private string _perforceServer = Environment.GetEnvironmentVariable("P4PORT") ?? string.Empty;
    private string _perforceUser = Environment.GetEnvironmentVariable("P4USER") ?? Environment.UserName;
    private string _perforceWorkspace = Environment.GetEnvironmentVariable("P4CLIENT") ?? string.Empty;
    private bool _perforceWriteOperationsEnabled;
    private bool _perforceIncludeOtherWorkspaces = true;
    private string _perforceCliSummary = "Enable the Perforce plugin to detect the official p4 CLI.";
    private string _perforceConnectionSummary = "Perforce connection has not been validated.";
    private string _perforceCommandOutput = "No Perforce command has been run.";
    private string _perforcePath = string.Empty;
    private string _perforceSubmitDescription = string.Empty;
    private string _perforceOpenedSearch = string.Empty;
    private bool _perforceSubmitDefaultChangelist = true;
    private PerforceConnectionStatus? _perforceConnection;
    private PerforceOpenedFile? _selectedPerforceOpenedFile;
    private PerforceChangelist? _selectedPerforceChangelist;
    private IReadOnlyList<PerforceOpenedFile> _allPerforceOpenedFiles = [];
    private Guid? _perforceSettingsProjectId;

    public ObservableCollection<PerforceOpenedFile> PerforceOpenedFiles { get; } = [];
    public ObservableCollection<PerforceChangelist> PerforcePendingChangelists { get; } = [];
    public ObservableCollection<PerforceChangelist> PerforceSubmittedChangelists { get; } = [];
    public ObservableCollection<PerforceFileRevision> PerforceFileHistory { get; } = [];

    public bool IsSelectedPerforcePlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.perforce", StringComparison.OrdinalIgnoreCase);

    public bool IsPerforceIntegrationEnabled
    {
        get => _isPerforceIntegrationEnabled;
        private set
        {
            if (SetProperty(ref _isPerforceIntegrationEnabled, value)) NotifyPerforceCapabilities();
        }
    }

    public bool IsPerforceBusy
    {
        get => _isPerforceBusy;
        private set
        {
            if (SetProperty(ref _isPerforceBusy, value)) NotifyPerforceCapabilities();
        }
    }

    public string PerforceCliPath
    {
        get => _perforceCliPath;
        set => SetProperty(ref _perforceCliPath, value);
    }

    public string PerforceServer
    {
        get => _perforceServer;
        set => SetProperty(ref _perforceServer, value);
    }

    public string PerforceUser
    {
        get => _perforceUser;
        set => SetProperty(ref _perforceUser, value);
    }

    public string PerforceWorkspace
    {
        get => _perforceWorkspace;
        set => SetProperty(ref _perforceWorkspace, value);
    }

    public bool PerforceWriteOperationsEnabled
    {
        get => _perforceWriteOperationsEnabled;
        set
        {
            if (SetProperty(ref _perforceWriteOperationsEnabled, value)) NotifyPerforceCapabilities();
        }
    }

    public bool PerforceIncludeOtherWorkspaces
    {
        get => _perforceIncludeOtherWorkspaces;
        set => SetProperty(ref _perforceIncludeOtherWorkspaces, value);
    }

    public string PerforceCliSummary
    {
        get => _perforceCliSummary;
        private set => SetProperty(ref _perforceCliSummary, value);
    }

    public string PerforceConnectionSummary
    {
        get => _perforceConnectionSummary;
        private set => SetProperty(ref _perforceConnectionSummary, value);
    }

    public string PerforceCommandOutput
    {
        get => _perforceCommandOutput;
        private set => SetProperty(ref _perforceCommandOutput, value);
    }

    public string PerforcePath
    {
        get => _perforcePath;
        set => SetProperty(ref _perforcePath, value);
    }

    public string PerforceSubmitDescription
    {
        get => _perforceSubmitDescription;
        set
        {
            if (SetProperty(ref _perforceSubmitDescription, value)) OnPropertyChanged(nameof(CanSubmitPerforce));
        }
    }

    public string PerforceOpenedSearch
    {
        get => _perforceOpenedSearch;
        set
        {
            if (SetProperty(ref _perforceOpenedSearch, value)) ApplyPerforceOpenedFilter();
        }
    }

    public bool PerforceSubmitDefaultChangelist
    {
        get => _perforceSubmitDefaultChangelist;
        set
        {
            if (SetProperty(ref _perforceSubmitDefaultChangelist, value)) OnPropertyChanged(nameof(CanSubmitPerforce));
        }
    }

    public PerforceOpenedFile? SelectedPerforceOpenedFile
    {
        get => _selectedPerforceOpenedFile;
        set
        {
            if (SetProperty(ref _selectedPerforceOpenedFile, value))
            {
                if (value is not null) PerforcePath = value.LocalPath;
                NotifyPerforceCapabilities();
            }
        }
    }

    public PerforceChangelist? SelectedPerforceChangelist
    {
        get => _selectedPerforceChangelist;
        set
        {
            if (SetProperty(ref _selectedPerforceChangelist, value)) OnPropertyChanged(nameof(CanSubmitPerforce));
        }
    }

    public bool IsPerforceConnectionValidated =>
        _perforceConnection is { CliAvailable: true, ServerReachable: true, Authenticated: true, WorkspaceValid: true };

    public bool CanReadPerforce => IsPerforceIntegrationEnabled && SelectedProject is not null && !IsPerforceBusy;
    public bool CanWritePerforce => CanReadPerforce && IsPerforceConnectionValidated && PerforceWriteOperationsEnabled;
    public bool CanSubmitPerforce => CanWritePerforce &&
        (PerforceSubmitDefaultChangelist
            ? !string.IsNullOrWhiteSpace(PerforceSubmitDescription)
            : SelectedPerforceChangelist is not null);

    public async Task DetectPerforceCliAsync()
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        if (plugin is null)
        {
            PerforceCliSummary = "Enable the Perforce plugin first.";
            return;
        }

        IsPerforceBusy = true;
        try
        {
            PerforceCliDetection detection = await Task.Run(() => plugin.DetectCli(PerforceCliPath));
            PerforceCliPath = detection.ExecutablePath;
            PerforceCliSummary = detection.Summary;
        }
        finally
        {
            IsPerforceBusy = false;
        }
    }

    public async Task SavePerforceConfigurationAsync()
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        if (plugin is null || SelectedProject is null) return;
        await RunOperationAsync("Saving Perforce project configuration…", async () =>
        {
            await plugin.SaveSettingsAsync(CreatePerforceSettings());
            _perforceSettingsProjectId = SelectedProject.Id;
        }, "Perforce configuration saved locally; no password or ticket was stored");
    }

    public async Task RefreshPerforceAsync()
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        if (plugin is null || SelectedProject is null) return;
        Guid projectId = SelectedProject.Id;
        PerforceProjectSettings settings = CreatePerforceSettings();
        await RunOperationAsync("Refreshing Perforce workspace…", async () =>
        {
            IsPerforceBusy = true;
            try
            {
                _perforceConnection = await plugin.InspectConnectionAsync(settings);
                if (SelectedProject?.Id != projectId) return;
                PerforceConnectionSummary = _perforceConnection.Summary;
                NotifyPerforceCapabilities();
                if (!IsPerforceConnectionValidated)
                {
                    _allPerforceOpenedFiles = [];
                    ApplyPerforceOpenedFilter();
                    ReplaceCollection(PerforcePendingChangelists, []);
                    ReplaceCollection(PerforceSubmittedChangelists, []);
                    ReplaceCollection(PerforceFileHistory, []);
                    PerforceCommandOutput = "Validate the p4 server, login ticket and workspace mapping before loading Perforce data.";
                    throw new InvalidOperationException(PerforceConnectionSummary);
                }

                Task<IReadOnlyList<PerforceOpenedFile>> openedTask = plugin.GetOpenedFilesAsync(settings, PerforceIncludeOtherWorkspaces);
                Task<IReadOnlyList<PerforceChangelist>> pendingTask = plugin.GetChangelistsAsync(settings, "pending");
                Task<IReadOnlyList<PerforceChangelist>> submittedTask = plugin.GetChangelistsAsync(settings, "submitted");
                await Task.WhenAll(openedTask, pendingTask, submittedTask);
                if (SelectedProject?.Id != projectId) return;

                _allPerforceOpenedFiles = await openedTask;
                ApplyPerforceOpenedFilter();
                ReplaceCollection(PerforcePendingChangelists, await pendingTask);
                ReplaceCollection(PerforceSubmittedChangelists, await submittedTask);
                SelectedPerforceChangelist = PerforcePendingChangelists.FirstOrDefault(
                    item => item.Number == SelectedPerforceChangelist?.Number) ?? PerforcePendingChangelists.FirstOrDefault();
                NotifyPerforceCapabilities();
            }
            finally
            {
                IsPerforceBusy = false;
            }
        }, "Perforce workspace refreshed");
    }

    public Task PreviewPerforceReconcileAsync() => RunPerforceCommandAsync(
        "Previewing Perforce reconcile…",
        (plugin, settings) => plugin.PreviewReconcileAsync(settings),
        "Perforce reconcile preview ready");

    public async Task ApplyPerforceReconcileAsync()
    {
        await RunPerforceCommandAsync(
            "Reconciling the Perforce workspace…",
            (plugin, settings) => plugin.ReconcileAsync(settings),
            "Perforce workspace reconciled");
        await RefreshPerforceAsync();
    }

    public Task PreviewPerforceSyncAsync() => RunPerforceCommandAsync(
        "Previewing Perforce sync…",
        (plugin, settings) => plugin.SyncAsync(settings, true),
        "Perforce sync preview ready");

    public async Task ApplyPerforceSyncAsync()
    {
        await RunPerforceCommandAsync(
            "Synchronizing the Perforce workspace…",
            (plugin, settings) => plugin.SyncAsync(settings, false),
            "Perforce workspace synchronized");
        await RefreshPerforceAsync();
    }

    public async Task OpenPerforcePathForEditAsync()
    {
        string path = PerforcePath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Enter or select a project file to open for edit.";
            return;
        }
        await RunPerforceCommandAsync(
            "Opening the selected file for edit…",
            (plugin, settings) => plugin.OpenForEditAsync(settings, [path], SelectedPerforceChangelist?.Number),
            "File opened for edit in Perforce");
        await RefreshPerforceAsync();
    }

    public async Task RevertSelectedPerforceFileAsync(bool unchangedOnly)
    {
        string? path = SelectedPerforceOpenedFile?.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        await RunPerforceCommandAsync(
            unchangedOnly ? "Reverting unchanged Perforce file…" : "Reverting selected Perforce file…",
            (plugin, settings) => plugin.RevertAsync(settings, [path], unchangedOnly),
            unchangedOnly ? "Unchanged file reverted" : "Selected file reverted");
        await RefreshPerforceAsync();
    }

    public async Task SubmitPerforceAsync()
    {
        int? changelist = PerforceSubmitDefaultChangelist ? null : SelectedPerforceChangelist?.Number;
        await RunPerforceCommandAsync(
            "Submitting Perforce changelist…",
            (plugin, settings) => plugin.SubmitAsync(settings, changelist, PerforceSubmitDescription),
            "Perforce changelist submitted");
        PerforceSubmitDescription = string.Empty;
        await RefreshPerforceAsync();
    }

    public async Task LoadPerforceFileHistoryAsync()
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        if (plugin is null || SelectedProject is null || SelectedPerforceOpenedFile is null) return;
        string relative = Path.GetRelativePath(SelectedProject.RootPath, SelectedPerforceOpenedFile.LocalPath);
        await RunOperationAsync("Loading Perforce file history…", async () =>
        {
            IReadOnlyList<PerforceFileRevision> revisions = await plugin.GetFileHistoryAsync(CreatePerforceSettings(), relative);
            ReplaceCollection(PerforceFileHistory, revisions);
        }, "Perforce file history loaded");
    }

    private async Task RunPerforceCommandAsync(
        string progress,
        Func<IPerforceIntegrationPlugin, PerforceProjectSettings, Task<PerforceCommandResult>> operation,
        string success)
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        if (plugin is null || SelectedProject is null) return;
        await RunOperationAsync(progress, async () =>
        {
            IsPerforceBusy = true;
            try
            {
                PerforceCommandResult result = await operation(plugin, CreatePerforceSettings());
                PerforceCommandOutput = FormatPerforceCommandOutput(result);
                if (!result.Succeeded) throw new InvalidOperationException(PerforceCommandOutput);
            }
            finally
            {
                IsPerforceBusy = false;
            }
        }, success);
    }

    private void RefreshPerforcePluginCatalog()
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        IsPerforceIntegrationEnabled = plugin is not null;
        if (plugin is null)
        {
            _perforceConnection = null;
            _perforceSettingsProjectId = null;
            _allPerforceOpenedFiles = [];
            ReplaceCollection(PerforceOpenedFiles, []);
            ReplaceCollection(PerforcePendingChangelists, []);
            ReplaceCollection(PerforceSubmittedChangelists, []);
            ReplaceCollection(PerforceFileHistory, []);
            PerforceCliSummary = "Enable the Perforce plugin to detect the official p4 CLI.";
            PerforceConnectionSummary = "Perforce integration is disabled for this project.";
        }
        else if (SelectedProject is not null)
        {
            _ = LoadPerforceSettingsAsync(SelectedProject.Id);
        }
        NotifyPerforceCapabilities();
    }

    private async Task LoadPerforceSettingsAsync(Guid projectId)
    {
        IPerforceIntegrationPlugin? plugin = GetPerforcePlugin();
        ProjectItemViewModel? project = SelectedProject;
        if (plugin is null || project?.Id != projectId) return;
        PerforceProjectSettings? settings = await plugin.LoadSettingsAsync(projectId);
        if (SelectedProject?.Id != projectId) return;

        PerforceCliPath = settings?.ExecutablePath ?? "p4";
        PerforceServer = settings?.Server ?? Environment.GetEnvironmentVariable("P4PORT") ?? string.Empty;
        PerforceUser = settings?.User ?? Environment.GetEnvironmentVariable("P4USER") ?? Environment.UserName;
        PerforceWorkspace = settings?.Workspace ?? Environment.GetEnvironmentVariable("P4CLIENT") ?? string.Empty;
        PerforceWriteOperationsEnabled = settings?.WriteOperationsEnabled ?? false;
        _perforceSettingsProjectId = projectId;
        PerforceConnectionSummary = settings is null
            ? "Configure P4PORT, P4USER and P4CLIENT, then validate the connection."
            : "Saved Perforce coordinates loaded. Validate the connection before workspace writes.";
        await DetectPerforceCliAsync();
    }

    private PerforceProjectSettings CreatePerforceSettings()
    {
        ProjectItemViewModel project = SelectedProject ?? throw new InvalidOperationException("Select a project first.");
        return new PerforceProjectSettings(
            project.Id,
            project.RootPath,
            string.IsNullOrWhiteSpace(PerforceCliPath) ? "p4" : PerforceCliPath.Trim(),
            PerforceServer.Trim(),
            PerforceUser.Trim(),
            PerforceWorkspace.Trim(),
            PerforceWriteOperationsEnabled);
    }

    private void ApplyPerforceOpenedFilter()
    {
        string search = PerforceOpenedSearch.Trim();
        IEnumerable<PerforceOpenedFile> filtered = _allPerforceOpenedFiles;
        if (search.Length > 0)
        {
            filtered = filtered.Where(file =>
                file.DepotPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                file.LocalPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                file.Action.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                file.User.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                file.Workspace.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                file.Change.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        ReplaceCollection(PerforceOpenedFiles, filtered);
    }

    private IPerforceIntegrationPlugin? GetPerforcePlugin() => _pluginManager.GetPlugin<IPerforceIntegrationPlugin>();

    private void NotifyPerforceCapabilities()
    {
        OnPropertyChanged(nameof(IsPerforceConnectionValidated));
        OnPropertyChanged(nameof(CanReadPerforce));
        OnPropertyChanged(nameof(CanWritePerforce));
        OnPropertyChanged(nameof(CanSubmitPerforce));
    }

    private static string FormatPerforceCommandOutput(PerforceCommandResult result)
    {
        string output = result.StandardOutput.Trim();
        string error = result.StandardError.Trim();
        if (output.Length == 0 && error.Length == 0) return result.Summary;
        if (error.Length == 0) return output;
        if (output.Length == 0) return error;
        return output + Environment.NewLine + Environment.NewLine + "stderr:" + Environment.NewLine + error;
    }
}
