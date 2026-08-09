using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

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
            JsonProjectCatalog catalog = new(paths.ProjectCatalogPath);
            GitCliRepositoryService gitService = new();
            MainWindowViewModel viewModel = new(catalog, gitService);

            desktop.MainWindow = new MainWindow(viewModel);
            desktop.Exit += (_, _) => catalog.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

