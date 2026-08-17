using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isLoreIntegrationEnabled;
    private bool _isLoreBusy;
    private string _loreProjectPath = string.Empty;
    private string _loreCliPath = "lore";
    private string _loreCliSummary = "Enable the Lore Project Management plugin to detect the Lore CLI.";
    private string _loreProjectSummary = "Select a project to inspect its Lore workspace.";
    private string _loreRepositorySummary = "No Lore workspace selected.";
    private string _loreStatusOutput = "Status has not been loaded.";
    private string _loreBranchesOutput = "Branches have not been loaded.";
    private string _loreStagePath = string.Empty;
    private string _loreCommitMessage = string.Empty;
    private string _loreNewBranchName = string.Empty;
    private LoreProjectInspection? _loreProjectInspection;

    public bool IsSelectedLorePlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.lore", StringComparison.OrdinalIgnoreCase);

    public bool IsLoreIntegrationEnabled
    {
        get => _isLoreIntegrationEnabled;
        private set => SetProperty(ref _isLoreIntegrationEnabled, value);
    }

    public bool IsLoreBusy
    {
        get => _isLoreBusy;
        private set
        {
            if (SetProperty(ref _isLoreBusy, value)) NotifyLoreCapabilities();
        }
    }

    public string LoreProjectPath
    {
        get => _loreProjectPath;
        private set => SetProperty(ref _loreProjectPath, value);
    }

    public string LoreCliPath
    {
        get => _loreCliPath;
        set => SetProperty(ref _loreCliPath, value);
    }

    public string LoreCliSummary
    {
        get => _loreCliSummary;
        private set => SetProperty(ref _loreCliSummary, value);
    }

    public string LoreProjectSummary
    {
        get => _loreProjectSummary;
        private set => SetProperty(ref _loreProjectSummary, value);
    }

    public string LoreRepositorySummary
    {
        get => _loreRepositorySummary;
        private set => SetProperty(ref _loreRepositorySummary, value);
    }

    public string LoreStatusOutput
    {
        get => _loreStatusOutput;
        private set => SetProperty(ref _loreStatusOutput, value);
    }

    public string LoreBranchesOutput
    {
        get => _loreBranchesOutput;
        private set => SetProperty(ref _loreBranchesOutput, value);
    }

    public string LoreStagePath
    {
        get => _loreStagePath;
        set => SetProperty(ref _loreStagePath, value);
    }

    public string LoreCommitMessage
    {
        get => _loreCommitMessage;
        set => SetProperty(ref _loreCommitMessage, value);
    }

    public string LoreNewBranchName
    {
        get => _loreNewBranchName;
        set => SetProperty(ref _loreNewBranchName, value);
    }

    public bool IsLoreProjectDetected => _loreProjectInspection?.IsProject == true;
    public bool IsLoreUnrealProjectDetected => _loreProjectInspection?.UnrealProjectDetected == true;
    public bool IsLoreUnrealCompanionInstalled => _loreProjectInspection?.UnrealCompanionInstalled == true;
    public string LoreUnrealCompanionVersion => _loreProjectInspection?.UnrealCompanionVersion ?? "Not installed";
    public bool CanReadLore => IsLoreIntegrationEnabled && IsLoreProjectDetected && !IsLoreBusy;
    public bool CanInstallLoreUnrealCompanion => IsLoreIntegrationEnabled && IsLoreUnrealProjectDetected && !IsLoreBusy;

    public void SetLoreProjectPath(string path)
    {
        LoreProjectPath = Path.GetFullPath(path);
        RefreshLoreInspection();
    }

    public async Task DetectLoreCliAsync()
    {
        ILoreIntegrationPlugin? plugin = GetLorePlugin();
        if (plugin is null)
        {
            LoreCliSummary = "Enable the Lore Project Management plugin first.";
            return;
        }

        IsLoreBusy = true;
        try
        {
            LoreCliDetection detection = await Task.Run(() => plugin.DetectCli(LoreCliPath));
            LoreCliPath = detection.ExecutablePath;
            LoreCliSummary = detection.Summary;
            if (detection.IsAvailable) await plugin.SaveCliPathAsync(detection.ExecutablePath);
        }
        finally
        {
            IsLoreBusy = false;
        }
    }

    public Task ReadLoreStatusAsync() => RunLoreOperationAsync(
        "Reading Lore status…",
        (plugin, path) => plugin.ReadStatusAsync(path),
        output => LoreStatusOutput = output,
        "Lore status loaded without scanning the working tree");

    public Task ScanLoreStatusAsync() => RunLoreOperationAsync(
        "Scanning the Lore working tree…",
        (plugin, path) => plugin.ScanStatusAsync(path),
        output => LoreStatusOutput = output,
        "Lore working tree scanned and dirty flags updated");

    public Task ListLoreBranchesAsync() => RunLoreOperationAsync(
        "Reading Lore branches…",
        (plugin, path) => plugin.ListBranchesAsync(path),
        output => LoreBranchesOutput = output,
        "Lore branches loaded");

    public Task StageLorePathAsync()
    {
        string path = LoreStagePath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Enter a project-relative path or glob to stage in Lore.";
            return Task.CompletedTask;
        }
        return RunLoreCommandAsync("Staging Lore path…", ["stage", path], "Lore path staged");
    }

    public async Task CommitLoreAsync()
    {
        string message = LoreCommitMessage.Trim();
        if (message.Length == 0)
        {
            StatusMessage = "Enter a Lore commit message.";
            return;
        }
        await RunLoreCommandAsync("Creating Lore commit…", ["commit", message], "Lore commit created");
        if (StatusMessage == "Lore commit created") LoreCommitMessage = string.Empty;
    }

    public Task PushLoreAsync() => RunLoreCommandAsync("Pushing Lore commits…", ["push"], "Lore commits pushed");

    public Task SyncLoreAsync() => RunLoreCommandAsync("Synchronizing Lore workspace…", ["sync"], "Lore workspace synchronized");

    public async Task CreateLoreBranchAsync()
    {
        string name = LoreNewBranchName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Enter a Lore branch name.";
            return;
        }
        await RunLoreCommandAsync("Creating Lore branch…", ["branch", "create", name], "Lore branch created");
        await ListLoreBranchesAsync();
    }

    public async Task SwitchLoreBranchAsync()
    {
        string name = LoreNewBranchName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Enter the Lore branch name to switch to.";
            return;
        }
        await RunLoreCommandAsync("Switching Lore branch…", ["branch", "switch", name], "Lore branch switched");
        await ListLoreBranchesAsync();
    }

    public async Task InstallLoreUnrealCompanionAsync()
    {
        ILoreIntegrationPlugin? plugin = GetLorePlugin();
        if (plugin is null || string.IsNullOrWhiteSpace(LoreProjectPath))
        {
            StatusMessage = "Enable Lore Project Management and select an Unreal project first.";
            return;
        }

        await RunOperationAsync("Installing CyRevision Lore Companion…", async () =>
        {
            IsLoreBusy = true;
            try
            {
                LoreUnrealCompanionInstallationResult result = await plugin.InstallOrUpdateUnrealCompanionAsync(LoreProjectPath);
                LoreProjectSummary = result.Message;
                RefreshLoreInspection();
                if (!result.Succeeded) throw new InvalidOperationException(result.Message);
            }
            finally
            {
                IsLoreBusy = false;
            }
        }, "CyRevision Lore Companion installed in the Unreal project");
    }

    private async Task RunLoreOperationAsync(
        string progress,
        Func<ILoreIntegrationPlugin, string, Task<LoreCommandResult>> operation,
        Action<string> assignOutput,
        string success)
    {
        ILoreIntegrationPlugin? plugin = GetLorePlugin();
        if (plugin is null || !IsLoreProjectDetected)
        {
            StatusMessage = "Enable Lore Project Management and select a Lore workspace first.";
            return;
        }

        await RunOperationAsync(progress, async () =>
        {
            IsLoreBusy = true;
            try
            {
                LoreCommandResult result = await operation(plugin, LoreProjectPath);
                assignOutput(FormatLoreCommandOutput(result));
                if (!result.Succeeded) throw new InvalidOperationException(result.Summary + " " + result.StandardError);
                RefreshLoreInspection();
            }
            finally
            {
                IsLoreBusy = false;
            }
        }, success);
    }

    private Task RunLoreCommandAsync(string progress, IReadOnlyList<string> arguments, string success) =>
        RunLoreOperationAsync(
            progress,
            (plugin, path) => plugin.RunProjectCommandAsync(path, arguments),
            output => LoreStatusOutput = output,
            success);

    private void DetectSelectedLoreProject(ProjectItemViewModel? project)
    {
        LoreProjectPath = project?.RootPath ?? string.Empty;
        RefreshLoreInspection();
    }

    private void RefreshLorePluginCatalog()
    {
        ILoreIntegrationPlugin? plugin = GetLorePlugin();
        IsLoreIntegrationEnabled = plugin is not null;
        if (plugin is null)
        {
            _loreProjectInspection = null;
            LoreCliSummary = "Enable the Lore Project Management plugin to detect the Lore CLI.";
            LoreProjectSummary = "Lore integration is disabled.";
        }
        else
        {
            RefreshLoreInspection();
            _ = DetectLoreCliAsync();
        }
        NotifyLoreCapabilities();
    }

    private void RefreshLoreInspection()
    {
        ILoreIntegrationPlugin? plugin = GetLorePlugin();
        _loreProjectInspection = plugin is null || string.IsNullOrWhiteSpace(LoreProjectPath)
            ? null
            : plugin.InspectProject(LoreProjectPath);
        LoreProjectSummary = _loreProjectInspection?.Summary ??
                             (plugin is null ? "Lore integration is disabled." : "Select a project to inspect its Lore workspace.");
        LoreRepositorySummary = _loreProjectInspection is null
            ? "No Lore workspace selected."
            : string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(_loreProjectInspection.RepositoryName) ? "Repository not reported" : _loreProjectInspection.RepositoryName,
                string.IsNullOrWhiteSpace(_loreProjectInspection.CurrentBranch) ? "Branch not reported" : _loreProjectInspection.CurrentBranch,
                string.IsNullOrWhiteSpace(_loreProjectInspection.ServerUrl) ? "Server not reported" : _loreProjectInspection.ServerUrl
            });
        NotifyLoreCapabilities();
    }

    private ILoreIntegrationPlugin? GetLorePlugin() => _pluginManager.GetPlugin<ILoreIntegrationPlugin>();

    private void NotifyLoreCapabilities()
    {
        OnPropertyChanged(nameof(IsLoreProjectDetected));
        OnPropertyChanged(nameof(IsLoreUnrealProjectDetected));
        OnPropertyChanged(nameof(IsLoreUnrealCompanionInstalled));
        OnPropertyChanged(nameof(LoreUnrealCompanionVersion));
        OnPropertyChanged(nameof(CanReadLore));
        OnPropertyChanged(nameof(CanInstallLoreUnrealCompanion));
    }

    private static string FormatLoreCommandOutput(LoreCommandResult result)
    {
        string output = result.StandardOutput.Trim();
        string error = result.StandardError.Trim();
        if (output.Length == 0 && error.Length == 0) return result.Summary;
        if (error.Length == 0) return output;
        if (output.Length == 0) return error;
        return output + Environment.NewLine + Environment.NewLine + "stderr:" + Environment.NewLine + error;
    }
}
