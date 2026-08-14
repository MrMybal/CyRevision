using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.Plugins;

public sealed class CyRevisionPluginManager : IAsyncDisposable
{
    private readonly string _pluginsDirectory;
    private readonly string _preferencesPath;
    private readonly CyRevisionPluginContext _baseContext;
    private readonly List<PluginCatalogEntry> _entries = [];
    private HashSet<string> _enabledPluginIds = new(StringComparer.OrdinalIgnoreCase);

    public CyRevisionPluginManager(
        string applicationDirectory,
        string configurationDirectory,
        string dataDirectory,
        string applicationVersion)
    {
        _pluginsDirectory = Path.Combine(applicationDirectory, "Plugins");
        _preferencesPath = Path.Combine(configurationDirectory, "plugins.json");
        _baseContext = new CyRevisionPluginContext(
            applicationDirectory,
            string.Empty,
            configurationDirectory,
            dataDirectory,
            applicationVersion);
    }

    public IReadOnlyList<PluginCatalogEntry> Entries => _entries;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _enabledPluginIds = await LoadPreferencesAsync(cancellationToken);
        _entries.Clear();
        if (!Directory.Exists(_pluginsDirectory))
        {
            return;
        }

        foreach (string manifestPath in Directory.EnumerateFiles(
                     _pluginsDirectory,
                     "cyrevision-plugin.json",
                     SearchOption.AllDirectories))
        {
            try
            {
                PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (manifest is null || manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    continue;
                }

                bool enabled = _enabledPluginIds.Contains(manifest.Id) ||
                               (!_preferencesPathExists && manifest.EnabledByDefault);
                PluginCatalogEntry entry = new(manifestPath, manifest, enabled);
                _entries.Add(entry);
                if (enabled)
                {
                    await LoadEntryAsync(entry, cancellationToken);
                }
            }
            catch (Exception exception)
            {
                _entries.Add(PluginCatalogEntry.Invalid(manifestPath, exception.Message));
            }
        }

        _entries.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    }

    public TPlugin? GetPlugin<TPlugin>() where TPlugin : class, ICyRevisionPlugin =>
        _entries.Select(entry => entry.Instance).OfType<TPlugin>().FirstOrDefault();

    public IReadOnlyList<TExtension> GetExtensions<TExtension>() where TExtension : class =>
        _entries
            .Where(entry => entry.IsEnabled)
            .Select(entry => entry.Instance)
            .OfType<TExtension>()
            .ToArray();

    public async Task EnableAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        PluginCatalogEntry entry = GetEntry(pluginId);
        if (entry.Instance is null)
        {
            await LoadEntryAsync(entry, cancellationToken);
        }

        entry.IsEnabled = entry.Instance is not null;
        if (entry.IsEnabled)
        {
            _enabledPluginIds.Add(pluginId);
            await SavePreferencesAsync(cancellationToken);
        }
    }

    public async Task DisableAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        PluginCatalogEntry entry = GetEntry(pluginId);
        if (entry.Instance is not null)
        {
            await entry.Instance.DisposeAsync();
            entry.Instance = null;
        }

        entry.LoadContext?.Unload();
        entry.LoadContext = null;
        entry.IsEnabled = false;
        entry.Status = "Disabled";
        _enabledPluginIds.Remove(pluginId);
        await SavePreferencesAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (PluginCatalogEntry entry in _entries)
        {
            if (entry.Instance is not null)
            {
                await entry.Instance.DisposeAsync();
            }
            entry.LoadContext?.Unload();
        }
    }

    private bool _preferencesPathExists => File.Exists(_preferencesPath);

    private async Task LoadEntryAsync(PluginCatalogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            string packageDirectory = Path.GetDirectoryName(entry.ManifestPath)!;
            string assemblyPath = Path.Combine(packageDirectory, entry.EntryAssembly);
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("Plugin entry assembly was not found.", assemblyPath);
            }

            PluginLoadContext loadContext = new(assemblyPath);
            Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            Type type = assembly.GetType(entry.EntryType, true, false)!;
            if (Activator.CreateInstance(type) is not ICyRevisionPlugin plugin)
            {
                loadContext.Unload();
                throw new InvalidCastException($"{entry.EntryType} does not implement ICyRevisionPlugin.");
            }

            CyRevisionPluginContext context = _baseContext with { PackageDirectory = packageDirectory };
            await plugin.InitializeAsync(context, cancellationToken);
            entry.LoadContext = loadContext;
            entry.Instance = plugin;
            entry.IsEnabled = true;
            entry.Status = "Enabled";
        }
        catch (Exception exception)
        {
            entry.IsEnabled = false;
            entry.Status = $"Load failed: {exception.Message}";
        }
    }

    private PluginCatalogEntry GetEntry(string pluginId) =>
        _entries.FirstOrDefault(entry => string.Equals(entry.Id, pluginId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Plugin '{pluginId}' was not found.");

    private async Task<HashSet<string>> LoadPreferencesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_preferencesPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        await using FileStream stream = File.OpenRead(_preferencesPath);
        PluginPreferences? preferences = await JsonSerializer.DeserializeAsync<PluginPreferences>(
            stream,
            cancellationToken: cancellationToken);
        return new HashSet<string>(preferences?.EnabledPluginIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private async Task SavePreferencesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
        string temporary = _preferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new PluginPreferences(_enabledPluginIds.OrderBy(id => id).ToArray()),
                cancellationToken: cancellationToken);
        }
        File.Move(temporary, _preferencesPath, true);
    }

    private sealed record PluginPreferences(string[] EnabledPluginIds);

    internal sealed record PluginManifest(
        int SchemaVersion,
        string Id,
        string Name,
        string Version,
        string Description,
        string Category,
        string EntryAssembly,
        string EntryType,
        bool EnabledByDefault);
}

public sealed class PluginCatalogEntry
{
    internal PluginCatalogEntry(
        string manifestPath,
        CyRevisionPluginManager.PluginManifest manifest,
        bool enabled)
    {
        ManifestPath = manifestPath;
        Id = manifest.Id;
        Name = manifest.Name;
        Version = manifest.Version;
        Description = manifest.Description;
        Category = manifest.Category;
        EntryAssembly = manifest.EntryAssembly;
        EntryType = manifest.EntryType;
        IsEnabled = enabled;
        Status = enabled ? "Pending" : "Disabled";
    }

    private PluginCatalogEntry(string manifestPath, string error)
    {
        ManifestPath = manifestPath;
        Id = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "invalid";
        Name = "Invalid plugin";
        Version = "—";
        Description = error;
        Category = "Invalid";
        EntryAssembly = string.Empty;
        EntryType = string.Empty;
        Status = error;
    }

    public string ManifestPath { get; }
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
    public string Category { get; }
    public string EntryAssembly { get; }
    public string EntryType { get; }
    public bool IsEnabled { get; internal set; }
    public string Status { get; internal set; }
    internal ICyRevisionPlugin? Instance { get; set; }
    internal PluginLoadContext? LoadContext { get; set; }

    internal static PluginCatalogEntry Invalid(string manifestPath, string error) => new(manifestPath, error);
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string assemblyPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(
                assemblyName.Name,
                typeof(ICyRevisionPlugin).Assembly.GetName().Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
