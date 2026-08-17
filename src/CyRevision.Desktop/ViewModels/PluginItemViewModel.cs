using CyRevision.Desktop.Plugins;

namespace CyRevision.Desktop.ViewModels;

public sealed class PluginItemViewModel(PluginCatalogEntry entry, bool isEnabledForProject)
{
    public string Id => entry.Id;
    public string Name => entry.Name;
    public string Version => entry.Version;
    public string Description => entry.Description;
    public string Category => entry.Category;
    public bool IsEnabled => isEnabledForProject;
    public string State => isEnabledForProject
        ? entry.InstanceLoaded ? "Enabled" : entry.Status
        : "Disabled";
}
