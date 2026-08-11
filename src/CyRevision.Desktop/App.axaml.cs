using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Core.Updates;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.ViewModels;
using CyRevision.Diff;
using CyRevision.Git;
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
            JsonProjectCatalog catalog = new(paths.ProjectCatalogPath);
            GitCliRepositoryService gitService = new();
            GitPeerExchangeService gitExchange = new();
            AssetDiffService assetDiff = new();
            JsonSyncthingProfileStore syncProfiles = new(paths.ManagedSyncthingDirectory);
            JsonVpnProfileStore vpnProfiles = new(paths.VpnDirectory);
            WireGuardConfigService vpnConfiguration = new(paths.VpnDirectory);
            ManagedWireGuardEngine vpnEngine = new(paths.VpnDirectory, vpnConfiguration);
            string? initialProjectPath = ReadProjectArgument(desktop.Args);
            MainWindowViewModel viewModel = new(
                catalog, gitService, paths, syncProfiles, gitExchange, assetDiff,
                vpnProfiles, new WireGuardKeyService(), vpnConfiguration, vpnEngine,
                localization, documentation, updates, initialProjectPath);

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
