using System.Text.Json;
using CyRevision.Core.Configuration;
using CyRevision.Desktop.Native;
using CyRevision.Desktop.SystemIntegration;

namespace CyRevision.Core.Tests;

public sealed class NativePreferencesActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrevision-native-preferences", Guid.NewGuid().ToString("N"));
    private ApplicationPreferences _saved = ApplicationPreferences.Default;
    private int _applicationWrites;
    private readonly ApplicationPaths _paths;

    public NativePreferencesActionsTests()
    {
        _paths = new(Path.Combine(_root, "config"), Path.Combine(_root, "data"), Path.Combine(_root, "cache"));
    }

    private NativePreferencesActions Create() => new(null, _paths, _paths, _saved, new(false, false, false, false),
        application => { _saved = application; _applicationWrites++; return Task.CompletedTask; }, _ => Task.CompletedTask);

    [Fact]
    public async Task SaveUsesTheExistingPreferenceTypesAndCallbacks()
    {
        NativePreferencesActions service = Create();
        await Execute(service, new { command = "prefs.save", application = new { confirmBeforeExit = false, automaticRepositoryRefresh = false,
            notifyOnWarnings = false, keyboardShortcuts = new Dictionary<string, string> { ["refresh"] = "Ctrl+R" } } });
        Assert.Equal(1, _applicationWrites);
        Assert.False(_saved.ConfirmBeforeExit);
        Assert.False(_saved.AutomaticRepositoryRefresh);
        Assert.False(service.AutomaticRepositoryRefresh);
        Assert.False(_saved.NotifyOnWarnings);
        Assert.Equal("Ctrl+R", _saved.KeyboardShortcuts["refresh"]);
    }

    [Fact]
    public async Task DuplicateShortcutsAreRejectedBeforePersistence()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Execute(Create(), new { command = "prefs.save", application = new {
            keyboardShortcuts = new Dictionary<string, string> { ["refresh"] = "Ctrl+O" } } }));
        Assert.Equal(0, _applicationWrites);
    }

    [Fact]
    public async Task UnknownModesAndPresetsAreRejectedInsteadOfSilentlyChangingDefaults()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Execute(Create(), new { command = "prefs.save", application = new {
            defaultWorkspacePresetByMode = new Dictionary<string, string> { ["GitOnly"] = "Missing preset" } } }));
        Assert.Equal(0, _applicationWrites);
    }

    [Fact]
    public async Task UnsupportedNativeTrayResidenceCannotBeEnabled()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Create(), new { command = "prefs.save", desktop = new {
            showTrayIcon = true, closeToTray = true, startHiddenAtLogin = true } }));
        Assert.Equal(0, _applicationWrites);
    }

    [Fact]
    public async Task ChangingCacheLocationDoesNotMoveOrDeleteTheCurrentCache()
    {
        string oldFile = CacheFile();
        string destination = Path.Combine(_root, "new-cache");
        await Execute(Create(), new { command = "prefs.save", application = new { cacheDirectory = destination } });
        Assert.Equal(destination, _saved.CacheDirectory);
        Assert.Empty(_saved.PendingCacheMoveSource);
        Assert.True(File.Exists(oldFile));
        Assert.False(File.Exists(Path.Combine(destination, "preview.tmp")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"confirmed\":false}")]
    [InlineData("{\"confirmed\":\"true\"}")]
    public async Task PurgeRequiresAnActualConfirmationAndKeepsFilesOtherwise(string fragment)
    {
        string file = CacheFile();
        using JsonDocument parsed = JsonDocument.Parse(fragment);
        Dictionary<string, object?> request = parsed.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => (object?)item.Value.Clone());
        request["command"] = "prefs.purgeCache";
        request["expectedRoot"] = _paths.CacheDirectory;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Create(), request));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task PurgeRejectsAChangedCacheTarget()
    {
        string file = CacheFile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Create(), new {
            command = "prefs.purgeCache", confirmed = true, expectedRoot = Path.Combine(_root, "other") }));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task ConfirmedPurgeTouchesOnlyTheExactActiveCache()
    {
        string file = CacheFile();
        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        string config = Path.Combine(_paths.ConfigurationDirectory, "fixture.txt");
        File.WriteAllText(config, "retain fixture");
        await Execute(Create(), new { command = "prefs.purgeCache", confirmed = true, expectedRoot = _paths.CacheDirectory });
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(config));
    }

    [Fact]
    public async Task SchedulingAMovePersistsThePlanButDoesNotMoveFilesYet()
    {
        string file = CacheFile();
        string destination = Path.Combine(_root, "future-cache");
        await Execute(Create(), new { command = "prefs.scheduleCacheMove", confirmed = true, expectedRoot = _paths.CacheDirectory, destination });
        Assert.Equal(destination, _saved.CacheDirectory);
        Assert.Equal(_paths.CacheDirectory, _saved.PendingCacheMoveSource);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(destination, "preview.tmp")));
    }

    [Fact]
    public async Task AFolderContainingUnrelatedFilesCannotBecomeAPurgeableCache()
    {
        string destination = Path.Combine(_root, "ordinary-folder");
        Directory.CreateDirectory(destination);
        string file = Path.Combine(destination, "retain.txt");File.WriteAllText(file, "fixture");
        await Assert.ThrowsAsync<IOException>(() => Execute(Create(), new { command = "prefs.save", application = new { cacheDirectory = destination } }));
        Assert.True(File.Exists(file));
        Assert.Equal(0, _applicationWrites);
    }

    [Fact]
    public async Task CacheRootCannotOverlapPersistentConfigurationOrData()
    {
        await Assert.ThrowsAsync<IOException>(() => Execute(Create(), new { command = "prefs.save", application = new { cacheDirectory = _paths.ConfigurationDirectory } }));
        await Assert.ThrowsAsync<IOException>(() => Execute(Create(), new { command = "prefs.save", application = new { cacheDirectory = _paths.DataDirectory } }));
        await Assert.ThrowsAsync<IOException>(() => Execute(Create(), new { command = "prefs.save", application = new { cacheDirectory = _root } }));
        Assert.Equal(0, _applicationWrites);
    }

    [Fact]
    public void RoutingUsesOnlyKnownPreferenceCommands()
    {
        Assert.True(NativePreferencesActions.Handles("prefs.get"));
        Assert.True(NativePreferencesActions.Handles("prefs.cancel"));
        Assert.False(NativePreferencesActions.Handles("prefs.deleteFolder"));
        Assert.False(NativePreferencesActions.Handles("prefs.exec"));
    }

    private string CacheFile()
    {
        Directory.CreateDirectory(_paths.CacheDirectory);
        string path = Path.Combine(_paths.CacheDirectory, "preview.tmp");File.WriteAllText(path, "generated fixture");return path;
    }
    private static Task<object?> Execute(NativePreferencesActions service, object request)
    {
        using JsonDocument parsed = JsonDocument.Parse(JsonSerializer.Serialize(request));
        return service.ExecuteAsync(parsed.RootElement.Clone());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
