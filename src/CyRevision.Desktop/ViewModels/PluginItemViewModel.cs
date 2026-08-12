using CyRevision.Desktop.Plugins;

namespace CyRevision.Desktop.ViewModels;

public sealed class PluginItemViewModel(PluginCatalogEntry entry)
{
    public string Id => entry.Id;
    public string Name => entry.Name;
    public string Version => entry.Version;
    public string Description => entry.Description;
    public string Category => entry.Category;
    public bool IsEnabled => entry.IsEnabled;
    public string State => entry.Status;
}
