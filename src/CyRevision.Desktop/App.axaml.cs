using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using System.Security;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Core.Updates;
using CyRevision.Code;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Plugins;
using CyRevision.Desktop.SystemIntegration;
using CyRevision.Desktop.ViewModels;
using CyRevision.Diff;
using CyRevision.Discord;
using CyRevision.Discord.Control;
using CyRevision.Git;
using CyRevision.PullRequests;
using CyRevision.RemoteBuild;
using CyRevision.Sync;
using CyRevision.Vpn;

namespace CyRevision.Desktop;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _viewModel;
    private LocalizationService? _localization;
    private DesktopBehaviorPreferencesStore? _desktopPreferencesStore;
    private StartupRegistrationService? _startupRegistration;
    private DesktopBehaviorPreferences _desktopPreferences = DesktopBehaviorPreferences.Default;
    private bool _explicitExit;
    private TrayIcon _mainTrayIcon = null!;
    private NativeMenuItem _trayOpenItem = null!;
    private NativeMenuItem _trayHideItem = null!;
    private NativeMenuItem _trayRefreshItem = null!;
    private NativeMenuItem _trayStatusItem = null!;
    private NativeMenuItem _trayLaunchAtLoginItem = null!;
    private NativeMenuItem _trayStartHiddenItem = null!;
    private NativeMenuItem _trayCloseToTrayItem = null!;
    private NativeMenuItem _trayQuitItem = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ResolveTrayControls();
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ApplicationPaths paths = ApplicationPaths.CreateDefault();
            LocalizationService localization = new();
            localization.Configure(paths.ConfigurationDirectory);
            _localization = localization;
            _desktopPreferencesStore = new DesktopBehaviorPreferencesStore(paths.ConfigurationDirectory);
            _desktopPreferences = _desktopPreferencesStore.Load();
            if (!_desktopPreferences.ShowTrayIcon)
            {
                _desktopPreferences = _desktopPreferences with
                {
                    StartHiddenAtLogin = false,
                    CloseToTray = false
                };
            }
            _startupRegistration = new StartupRegistrationService();
            ApplyStartupRegistrationAtLaunch();
            OfflineDocumentationService documentation = new(
                Path.Combine(AppContext.BaseDirectory, "Documentation"));
            ApplicationUpdateService updates = new(
                new Uri("https://github.com/MrMybal/CyRevision"),
                ApplicationUpdateService.ReadCurrentVersion(typeof(App).Assembly));
            CyRevisionPluginManager pluginManager = new(
                AppContext.BaseDirectory,
                paths.ConfigurationDirectory,
                paths.DataDirectory,
                ApplicationUpdateService.ReadCurrentVersion(typeof(App).Assembly).ToString());
            JsonProjectCatalog catalog = new(paths.ProjectCatalogPath);
            GitCliRepositoryService gitService = new();
            GitHubPullRequestService pullRequests = new();
            JsonDiscordAgentStore discordStore = new(paths.DiscordDirectory);
            DiscordControlConnectionStore discordConnections = new(paths.DiscordControlDirectory);
            DiscordProjectAgent discordAgent = new(
                new GitDiscordProjectSnapshotProvider(gitService),
                discordStore,
                new DiscordWebhookClient());
            GitPeerExchangeService gitExchange = new();
            AssetDiffService assetDiff = new();
            JsonSyncthingProfileStore syncProfiles = new(paths.ManagedSyncthingDirectory);
            JsonVpnProfileStore vpnProfiles = new(paths.VpnDirectory);
            WireGuardConfigService vpnConfiguration = new(paths.VpnDirectory);
            ManagedWireGuardEngine vpnEngine = new(paths.VpnDirectory, vpnConfiguration);
            WireGuardRuntimeResolver vpnRuntimeResolver = new();
            VpnNetworkSetupService vpnNetworkSetup = new();
            VpnSyncExchangeService vpnSyncExchange = new();
            JsonSwarmProfileStore swarmProfiles = new(paths.VpnDirectory);
            SwarmSetupService swarmSetup = new(vpnNetworkSetup);
            JsonVpnFileExchangeProfileStore vpnFileProfiles = new(paths.VpnDirectory);
            VpnFileExchangeService vpnFileExchange = new();
            JsonLfsManagementProfileStore lfsManagementProfiles = new(paths.LfsManagementDirectory);
            LfsStorageManager lfsStorageManager = new();
            JsonRemoteBuildConnectionStore remoteBuildConnections = new(paths.RemoteBuildDirectory);
            RemoteBuildSnapshotBuilder remoteBuildSnapshots = new();
            string? initialProjectPath = ReadProjectArgument(desktop.Args);
            MainWindowViewModel viewModel = new(
                catalog, gitService, paths, syncProfiles, gitExchange, assetDiff,
                vpnProfiles, new WireGuardKeyService(), vpnConfiguration, vpnEngine, vpnRuntimeResolver,
                vpnNetworkSetup, vpnSyncExchange, swarmProfiles, swarmSetup, vpnFileProfiles, vpnFileExchange,
                lfsManagementProfiles, lfsStorageManager, remoteBuildConnections, remoteBuildSnapshots,
                localization, documentation, updates, discordStore, discordAgent, discordConnections,
                pluginManager, new CodeWorkspaceService(), pullRequests, initialProjectPath);
            _viewModel = viewModel;

            MainWindow mainWindow = new(viewModel, localization, paths.ConfigurationDirectory)
            {
                StartHidden = IsBackgroundStart(desktop.Args) && _desktopPreferences.StartHiddenAtLogin
            };
            _mainWindow = mainWindow;
            mainWindow.ExitRequested += OnExitRequested;
            mainWindow.Closing += OnMainWindowClosing;
            mainWindow.ConfigureDesktopBehavior(_desktopPreferences, ToggleDesktopBehaviorSetting);
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            localization.LanguageChanged += OnLanguageChanged;
            desktop.MainWindow = mainWindow;
            ApplyDesktopPreferencesToUi();
            desktop.Exit += (_, _) =>
            {
                _explicitExit = true;
                _mainTrayIcon.IsVisible = false;
                _mainTrayIcon.Dispose();
                localization.LanguageChanged -= OnLanguageChanged;
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                Task.Run(async () => await viewModel.DisposeAsync()).GetAwaiter().GetResult();
                catalog.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyStartupRegistrationAtLaunch()
    {
        if (_startupRegistration is null || _desktopPreferencesStore is null)
        {
            return;
        }

        try
        {
            _startupRegistration.SetEnabled(
                _desktopPreferences.LaunchAtLogin,
                _desktopPreferences.StartHiddenAtLogin);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
        {
            _desktopPreferences = _desktopPreferences with { LaunchAtLogin = false };
            try
            {
                _desktopPreferencesStore.Save(_desktopPreferences);
            }
            catch (Exception saveException) when (saveException is IOException or UnauthorizedAccessException)
            {
                // The UI remains usable even if system integration settings are read-only.
            }
        }
    }

    private async void ToggleDesktopBehaviorSetting(DesktopBehaviorSetting setting)
    {
        if (_desktopPreferencesStore is null || _startupRegistration is null)
        {
            return;
        }

        DesktopBehaviorPreferences previous = _desktopPreferences;
        DesktopBehaviorPreferences updated = setting switch
        {
            DesktopBehaviorSetting.LaunchAtLogin => previous with { LaunchAtLogin = !previous.LaunchAtLogin },
            DesktopBehaviorSetting.StartHiddenAtLogin => previous with { StartHiddenAtLogin = !previous.StartHiddenAtLogin },
            DesktopBehaviorSetting.CloseToTray => previous with { CloseToTray = !previous.CloseToTray },
            DesktopBehaviorSetting.ShowTrayIcon => previous with { ShowTrayIcon = !previous.ShowTrayIcon },
            _ => previous
        };

        if (!updated.ShowTrayIcon)
        {
            updated = updated with { CloseToTray = false, StartHiddenAtLogin = false };
        }

        try
        {
            if (updated.LaunchAtLogin || previous.LaunchAtLogin)
            {
                _startupRegistration.SetEnabled(updated.LaunchAtLogin, updated.StartHiddenAtLogin);
            }

            _desktopPreferencesStore.Save(updated);
            _desktopPreferences = updated;
            ApplyDesktopPreferencesToUi();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
        {
            _desktopPreferences = previous;
            ApplyDesktopPreferencesToUi();
            if (_mainWindow is not null)
            {
                await _mainWindow.ShowSystemIntegrationErrorAsync(exception.Message);
            }
        }
    }

    private void ApplyDesktopPreferencesToUi()
    {
        _mainTrayIcon.IsVisible = _desktopPreferences.ShowTrayIcon;
        _trayLaunchAtLoginItem.IsChecked = _desktopPreferences.LaunchAtLogin;
        _trayStartHiddenItem.IsChecked = _desktopPreferences.StartHiddenAtLogin;
        _trayCloseToTrayItem.IsChecked = _desktopPreferences.CloseToTray;
        _trayStartHiddenItem.IsEnabled = _desktopPreferences.LaunchAtLogin && _desktopPreferences.ShowTrayIcon;
        _mainWindow?.ConfigureDesktopBehavior(_desktopPreferences, ToggleDesktopBehaviorSetting);
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        if (_localization is null)
        {
            return;
        }

        _trayOpenItem.Header = _localization.Translate("Open CyRevision");
        _trayHideItem.Header = _localization.Translate("Hide window");
        _trayRefreshItem.Header = _localization.Translate("Refresh project");
        _trayLaunchAtLoginItem.Header = _localization.Translate("Launch at system login");
        _trayStartHiddenItem.Header = _localization.Translate("Start hidden in tray");
        _trayCloseToTrayItem.Header = _localization.Translate("Close window to tray");
        _trayQuitItem.Header = _localization.Translate("Quit CyRevision");
        UpdateTrayStatus();
    }

    private void UpdateTrayStatus()
    {
        if (_viewModel is null || _localization is null)
        {
            return;
        }

        bool hasProject = _viewModel.SelectedProject is not null;
        string status = hasProject
            ? $"{_viewModel.SelectedProject!.Name} · {_viewModel.CurrentBranch} · {_viewModel.ChangeSummary}"
            : _localization.Translate("No project selected");
        _trayStatusItem.Header = status.Length <= 96 ? status : status[..93] + "...";
        _trayRefreshItem.IsEnabled = hasProject;
        _mainTrayIcon.ToolTipText = hasProject
            ? $"CyRevision · {_viewModel.SelectedProject!.Name}"
            : "CyRevision Alpha";
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedProject) or
            nameof(MainWindowViewModel.CurrentBranch) or
            nameof(MainWindowViewModel.ChangeSummary))
        {
            Dispatcher.UIThread.Post(UpdateTrayStatus);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateTrayText();

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_explicitExit)
        {
            return;
        }

        e.Cancel = true;
        if (_desktopPreferences.CloseToTray && _desktopPreferences.ShowTrayIcon)
        {
            HideMainWindow();
            return;
        }

        RequestExit();
    }

    private void OnExitRequested(object? sender, EventArgs e) => RequestExit();

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        if (_mainWindow?.IsVisible == true)
        {
            HideMainWindow();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void OnTrayOpenClick(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayHideClick(object? sender, EventArgs e) => HideMainWindow();

    private async void OnTrayRefreshClick(object? sender, EventArgs e)
    {
        if (_viewModel?.SelectedProject is not null)
        {
            await _viewModel.RefreshAsync();
        }
    }

    private void OnTrayLaunchAtLoginClick(object? sender, EventArgs e) =>
        ToggleDesktopBehaviorSetting(DesktopBehaviorSetting.LaunchAtLogin);

    private void OnTrayStartHiddenClick(object? sender, EventArgs e) =>
        ToggleDesktopBehaviorSetting(DesktopBehaviorSetting.StartHiddenAtLogin);

    private void OnTrayCloseToTrayClick(object? sender, EventArgs e) =>
        ToggleDesktopBehaviorSetting(DesktopBehaviorSetting.CloseToTray);

    private void OnTrayQuitClick(object? sender, EventArgs e) => RequestExit();

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = true;
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void HideMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Hide();
    }

    private void RequestExit()
    {
        if (_explicitExit)
        {
            return;
        }

        _explicitExit = true;
        _mainTrayIcon.IsVisible = false;
        _desktop?.Shutdown();
    }

    private void ResolveTrayControls()
    {
        TrayIcons? trayIcons = TrayIcon.GetIcons(this);
        _mainTrayIcon = trayIcons?.SingleOrDefault()
                        ?? throw new InvalidOperationException("The CyRevision tray icon is missing from App.axaml.");
        NativeMenu menu = _mainTrayIcon.Menu
                          ?? throw new InvalidOperationException("The CyRevision tray menu is missing from App.axaml.");
        _trayOpenItem = GetTrayItem(menu, 0);
        _trayHideItem = GetTrayItem(menu, 1);
        _trayRefreshItem = GetTrayItem(menu, 2);
        _trayStatusItem = GetTrayItem(menu, 4);
        _trayLaunchAtLoginItem = GetTrayItem(menu, 6);
        _trayStartHiddenItem = GetTrayItem(menu, 7);
        _trayCloseToTrayItem = GetTrayItem(menu, 8);
        _trayQuitItem = GetTrayItem(menu, 10);
    }

    private static NativeMenuItem GetTrayItem(NativeMenu menu, int index) =>
        menu.Items[index] as NativeMenuItem
        ?? throw new InvalidOperationException($"Tray menu item {index} has an unexpected type.");

    private static string? ReadProjectArgument(string[]? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument.StartsWith("--project=", StringComparison.OrdinalIgnoreCase))
            {
                return argument["--project=".Length..].Trim('"');
            }

            if (string.Equals(argument, "--project", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Length)
            {
                return arguments[index + 1].Trim('"');
            }
        }

        return null;
    }

    private static bool IsBackgroundStart(string[]? arguments) =>
        arguments?.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase)) == true;
}
