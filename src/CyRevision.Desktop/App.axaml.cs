using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Core.Updates;
using CyRevision.Code;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Plugins;
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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ApplicationPaths paths = ApplicationPaths.CreateDefault();
            LocalizationService localization = new();
            localization.Configure(paths.ConfigurationDirectory);
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

            desktop.MainWindow = new MainWindow(viewModel, localization, paths.ConfigurationDirectory);
            desktop.Exit += (_, _) =>
            {
                Task.Run(async () => await viewModel.DisposeAsync()).GetAwaiter().GetResult();
                catalog.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

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
}
