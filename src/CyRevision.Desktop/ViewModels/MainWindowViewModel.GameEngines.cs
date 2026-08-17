using Avalonia.Threading;
using CyRevision.Core.Projects;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isUnityIntegrationEnabled;
    private bool _isGodotIntegrationEnabled;
    private string _unityProjectPath = string.Empty;
    private string _godotProjectPath = string.Empty;
    private string _unityPluginSummary = "Enable the Unity Integration plugin to inspect and link a Unity project.";
    private string _godotPluginSummary = "Enable the Godot Integration plugin to inspect and link a Godot project.";
    private string _unityBridgeSummary = "The optional Unity bridge is disabled.";
    private string _godotBridgeSummary = "The optional Godot bridge is disabled.";
    private GameEngineProjectInspection? _unityProjectInspection;
    private GameEngineProjectInspection? _godotProjectInspection;
    private IGameEngineIntegrationPlugin? _subscribedUnityPlugin;
    private IGameEngineIntegrationPlugin? _subscribedGodotPlugin;

    public bool IsSelectedUnityPlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.unity", StringComparison.OrdinalIgnoreCase);

    public bool IsSelectedGodotPlugin =>
        string.Equals(SelectedPlugin?.Id, "cyrevision.godot", StringComparison.OrdinalIgnoreCase);

    public bool IsUnityIntegrationEnabled
    {
        get => _isUnityIntegrationEnabled;
        private set => SetProperty(ref _isUnityIntegrationEnabled, value);
    }

    public bool IsGodotIntegrationEnabled
    {
        get => _isGodotIntegrationEnabled;
        private set => SetProperty(ref _isGodotIntegrationEnabled, value);
    }

    public string UnityProjectPath
    {
        get => _unityProjectPath;
        private set => SetProperty(ref _unityProjectPath, value);
    }

    public string GodotProjectPath
    {
        get => _godotProjectPath;
        private set => SetProperty(ref _godotProjectPath, value);
    }

    public string UnityPluginSummary
    {
        get => _unityPluginSummary;
        private set => SetProperty(ref _unityPluginSummary, value);
    }

    public string GodotPluginSummary
    {
        get => _godotPluginSummary;
        private set => SetProperty(ref _godotPluginSummary, value);
    }

    public string UnityBridgeSummary
    {
        get => _unityBridgeSummary;
        private set => SetProperty(ref _unityBridgeSummary, value);
    }

    public string GodotBridgeSummary
    {
        get => _godotBridgeSummary;
        private set => SetProperty(ref _godotBridgeSummary, value);
    }

    public string UnityEngineVersion => _unityProjectInspection?.EngineVersion ?? "Not detected";
    public string GodotEngineVersion => _godotProjectInspection?.EngineVersion ?? "Not detected";
    public string UnityInstalledPluginVersion => _unityProjectInspection?.InstalledPluginVersion ?? "Not installed";
    public string GodotInstalledPluginVersion => _godotProjectInspection?.InstalledPluginVersion ?? "Not installed";
    public string UnityBundledPluginVersion => _unityProjectInspection?.BundledPluginVersion ?? "Unavailable";
    public string GodotBundledPluginVersion => _godotProjectInspection?.BundledPluginVersion ?? "Unavailable";
    public string UnityCompatibilityStatus => _unityProjectInspection?.CompatibilityStatus ?? "Select a Unity project.";
    public string GodotCompatibilityStatus => _godotProjectInspection?.CompatibilityStatus ?? "Select a Godot project.";
    public string UnitySupportedVersions => "Validated: " + string.Join(", ", _unityProjectInspection?.SupportedVersions ?? []);
    public string GodotSupportedVersions => "Validated: " + string.Join(", ", _godotProjectInspection?.SupportedVersions ?? []);
    public bool IsUnityProjectDetected => _unityProjectInspection?.IsValid == true;
    public bool IsGodotProjectDetected => _godotProjectInspection?.IsValid == true;
    public bool IsUnityPluginCompatible => _unityProjectInspection?.IsCompatible == true;
    public bool IsGodotPluginCompatible => _godotProjectInspection?.IsCompatible == true;
    public bool CanInstallUnityEditorPlugin => IsUnityIntegrationEnabled && IsUnityPluginCompatible && _unityProjectInspection?.BundledPluginVersion is not null;
    public bool CanInstallGodotEditorPlugin => IsGodotIntegrationEnabled && IsGodotPluginCompatible && _godotProjectInspection?.BundledPluginVersion is not null;

    public void SetUnityProjectPath(string path)
    {
        UnityProjectPath = Path.GetFullPath(path);
        RefreshGameEngineInspection(GameEngineKind.Unity);
    }

    public void SetGodotProjectPath(string path)
    {
        GodotProjectPath = Path.GetFullPath(path);
        RefreshGameEngineInspection(GameEngineKind.Godot);
    }

    public Task InstallUnityEditorPluginAsync() => InstallGameEngineCompanionAsync(GameEngineKind.Unity);
    public Task InstallGodotEditorPluginAsync() => InstallGameEngineCompanionAsync(GameEngineKind.Godot);
    public Task ConfigureUnityBridgeAsync() => ConfigureGameEngineBridgeAsync(GameEngineKind.Unity);
    public Task ConfigureGodotBridgeAsync() => ConfigureGameEngineBridgeAsync(GameEngineKind.Godot);

    private void DetectSelectedGameEngineProjects(ProjectItemViewModel? project)
    {
        if (project is null)
        {
            UnityProjectPath = string.Empty;
            GodotProjectPath = string.Empty;
            RefreshGameEngineInspection(GameEngineKind.Unity);
            RefreshGameEngineInspection(GameEngineKind.Godot);
            return;
        }
        string root = project?.RootPath ?? string.Empty;
        UnityProjectPath = Directory.Exists(Path.Combine(root, "Assets")) &&
                           File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt")) ? root : string.Empty;
        GodotProjectPath = File.Exists(Path.Combine(root, "project.godot")) ? root : string.Empty;
        RefreshGameEngineInspection(GameEngineKind.Unity);
        RefreshGameEngineInspection(GameEngineKind.Godot);
    }

    private void RefreshGameEnginePluginCatalog()
    {
        IGameEngineIntegrationPlugin? unity = GetGameEnginePlugin(GameEngineKind.Unity);
        IGameEngineIntegrationPlugin? godot = GetGameEnginePlugin(GameEngineKind.Godot);
        IsUnityIntegrationEnabled = unity is not null;
        IsGodotIntegrationEnabled = godot is not null;
        AttachGameEnginePluginEvents(unity, GameEngineKind.Unity);
        AttachGameEnginePluginEvents(godot, GameEngineKind.Godot);
        UnityBridgeSummary = unity is null ? "The optional Unity bridge is disabled." : FormatGameEngineBridgeStatus(unity.BridgeStatus);
        GodotBridgeSummary = godot is null ? "The optional Godot bridge is disabled." : FormatGameEngineBridgeStatus(godot.BridgeStatus);
        RefreshGameEngineInspection(GameEngineKind.Unity);
        RefreshGameEngineInspection(GameEngineKind.Godot);
    }

    private void RefreshGameEngineInspection(GameEngineKind engine)
    {
        IGameEngineIntegrationPlugin? plugin = GetGameEnginePlugin(engine);
        string path = engine == GameEngineKind.Unity ? UnityProjectPath : GodotProjectPath;
        GameEngineProjectInspection? inspection = plugin is null || string.IsNullOrWhiteSpace(path) ? null : plugin.InspectProject(path);
        if (engine == GameEngineKind.Unity)
        {
            _unityProjectInspection = inspection;
            UnityPluginSummary = plugin is null
                ? "Enable the Unity Integration plugin to inspect and link a Unity project."
                : inspection?.Summary ?? "Select a Unity project to install the autonomous Editor companion.";
        }
        else
        {
            _godotProjectInspection = inspection;
            GodotPluginSummary = plugin is null
                ? "Enable the Godot Integration plugin to inspect and link a Godot project."
                : inspection?.Summary ?? "Select a Godot project to install the autonomous Editor dock.";
        }
        NotifyGameEngineProperties(engine);
    }

    private async Task InstallGameEngineCompanionAsync(GameEngineKind engine)
    {
        IGameEngineIntegrationPlugin? plugin = GetGameEnginePlugin(engine);
        string path = engine == GameEngineKind.Unity ? UnityProjectPath : GodotProjectPath;
        if (plugin is null || string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = $"Enable the {engine} Integration plugin and select a {engine} project.";
            return;
        }
        await RunOperationAsync($"Installing CyRevision{engine}…", async () =>
        {
            string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CyRevision.Desktop");
            GameEnginePluginInstallationResult result = await plugin.InstallOrUpdateEditorPluginAsync(path, executable);
            if (engine == GameEngineKind.Unity) UnityPluginSummary = result.Message;
            else GodotPluginSummary = result.Message;
            RefreshGameEngineInspection(engine);
            if (!result.Succeeded) throw new InvalidOperationException(result.Message);
        }, $"CyRevision{engine} installed and linked through the private loopback bridge");
    }

    private async Task ConfigureGameEngineBridgeAsync(GameEngineKind engine)
    {
        IGameEngineIntegrationPlugin? plugin = GetGameEnginePlugin(engine);
        string path = engine == GameEngineKind.Unity ? UnityProjectPath : GodotProjectPath;
        if (plugin is null || string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = $"Enable the {engine} Integration plugin and select a {engine} project.";
            return;
        }
        await RunOperationAsync($"Configuring the {engine} bridge…", async () =>
        {
            string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CyRevision.Desktop");
            GameEngineBridgeStatus status = await plugin.ConfigureProjectConnectionAsync(path, executable);
            if (engine == GameEngineKind.Unity) UnityBridgeSummary = FormatGameEngineBridgeStatus(status);
            else GodotBridgeSummary = FormatGameEngineBridgeStatus(status);
        }, $"{engine} project authorized on its private loopback bridge");
    }

    private IGameEngineIntegrationPlugin? GetGameEnginePlugin(GameEngineKind engine) =>
        _pluginManager.GetExtensions<IGameEngineIntegrationPlugin>().FirstOrDefault(plugin => plugin.Engine == engine);

    private void AttachGameEnginePluginEvents(IGameEngineIntegrationPlugin? plugin, GameEngineKind engine)
    {
        IGameEngineIntegrationPlugin? current = engine == GameEngineKind.Unity ? _subscribedUnityPlugin : _subscribedGodotPlugin;
        if (ReferenceEquals(current, plugin)) return;
        if (current is not null) current.ProjectChanged -= OnGameEngineProjectChanged;
        if (engine == GameEngineKind.Unity) _subscribedUnityPlugin = plugin;
        else _subscribedGodotPlugin = plugin;
        if (plugin is not null) plugin.ProjectChanged += OnGameEngineProjectChanged;
    }

    private void DetachGameEnginePluginEvents(string pluginId)
    {
        if (string.Equals(pluginId, "cyrevision.unity", StringComparison.OrdinalIgnoreCase)) AttachGameEnginePluginEvents(null, GameEngineKind.Unity);
        if (string.Equals(pluginId, "cyrevision.godot", StringComparison.OrdinalIgnoreCase)) AttachGameEnginePluginEvents(null, GameEngineKind.Godot);
    }

    private void DetachAllGameEnginePluginEvents()
    {
        AttachGameEnginePluginEvents(null, GameEngineKind.Unity);
        AttachGameEnginePluginEvents(null, GameEngineKind.Godot);
    }

    private void OnGameEngineProjectChanged(object? sender, GameEngineProjectChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = $"{eventArgs.Engine} reported '{eventArgs.Action}' for {eventArgs.ProjectRoot}";
            if (SelectedProject is not null && ProjectPathsEqual(SelectedProject.RootPath, eventArgs.ProjectRoot)) _ = RefreshAsync();
        });
    }

    private void NotifyGameEngineProperties(GameEngineKind engine)
    {
        if (engine == GameEngineKind.Unity)
        {
            OnPropertyChanged(nameof(UnityEngineVersion));
            OnPropertyChanged(nameof(UnityInstalledPluginVersion));
            OnPropertyChanged(nameof(UnityBundledPluginVersion));
            OnPropertyChanged(nameof(UnityCompatibilityStatus));
            OnPropertyChanged(nameof(UnitySupportedVersions));
            OnPropertyChanged(nameof(IsUnityProjectDetected));
            OnPropertyChanged(nameof(IsUnityPluginCompatible));
            OnPropertyChanged(nameof(CanInstallUnityEditorPlugin));
        }
        else
        {
            OnPropertyChanged(nameof(GodotEngineVersion));
            OnPropertyChanged(nameof(GodotInstalledPluginVersion));
            OnPropertyChanged(nameof(GodotBundledPluginVersion));
            OnPropertyChanged(nameof(GodotCompatibilityStatus));
            OnPropertyChanged(nameof(GodotSupportedVersions));
            OnPropertyChanged(nameof(IsGodotProjectDetected));
            OnPropertyChanged(nameof(IsGodotPluginCompatible));
            OnPropertyChanged(nameof(CanInstallGodotEditorPlugin));
        }
    }

    private static string FormatGameEngineBridgeStatus(GameEngineBridgeStatus status) =>
        $"{(status.IsRunning ? "Connected" : "Stopped")} · {status.Endpoint} · {status.AuthorizedProjectCount} authorized project(s) · {status.Detail}";
}
