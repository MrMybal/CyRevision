using System.Reflection;
using System.Xml.Linq;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Git;
using CyRevision.Server;
using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class ProjectModeRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionModeRuntimeTests", Guid.NewGuid().ToString("N"));

    public static IEnumerable<object[]> Modes => ProjectPresets.All.Select(preset => new object[] { preset.Kind });

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task EveryPresetUsesTheCorrectExchangeAndServerContract(ProjectPresetKind kind)
    {
        ProjectPreset preset = ProjectPresets.All.Single(item => item.Kind == kind);
        string source = Path.Combine(_root, "source");
        string data = Path.Combine(_root, "data");
        ProjectDefinition project = new(Guid.NewGuid(), "Test", source, preset.Features, preset.Retention, OperatingMode: kind);
        string exchange = ProjectSyncPolicy.ResolveExchangeDirectory(project, data);
        bool packaged = preset.Features.GitEnabled || kind == ProjectPresetKind.SyncWithCommits;
        Assert.Equal(!packaged, exchange == source);
        if (packaged) Assert.StartsWith(data + Path.DirectorySeparatorChar, exchange);

        JsonProjectCatalog catalog = new(Path.Combine(data, "projects.json"));
        ServerOptions options = new(data, source, [source], "synthetic-test-token", TimeSpan.FromDays(1));
        await using ServerRuntime server = new(options, catalog, new GitCliRepositoryService(),
            new JsonSyncthingProfileStore(Path.Combine(data, "profiles")), new GitPeerExchangeService(), null!, null!, null!);
        if (kind == ProjectPresetKind.SyncWithCommits)
        {
            Assert.False(ServerRuntime.SupportsPreset(kind));
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.CreateProjectAsync(new("Test", kind)));
            Assert.Empty(await catalog.GetAllAsync());
            await catalog.UpsertAsync(project);
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartSyncAsync(project.Id));
        }
        else
        {
            Assert.True(ServerRuntime.SupportsPreset(kind));
            ProjectDefinition created = await server.CreateProjectAsync(new("Test", kind));
            Assert.Equal(kind, created.OperatingMode);
            Assert.Equal(preset.Features, created.Features);
        }
    }

    [Fact]
    public void CachedProjectCannotUseAnotherProjectsRuntimeOrProfile()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Assert.True(ProjectSyncPolicy.OwnsRuntime(a, a, a));
        Assert.False(ProjectSyncPolicy.OwnsRuntime(b, b, a));
        Assert.False(ProjectSyncPolicy.OwnsRuntime(b, a, b));
        Assert.False(ProjectSyncPolicy.OwnsRuntime(null, a, a));
        Assert.False(ProjectSyncPolicy.OwnsRuntime(a, a, null));
    }

    [Fact]
    public void JoiningAnotherSharedIdentityPreservesButDoesNotReuseLocalHeadsAndPeerCache()
    {
        ProjectPreset preset = ProjectPresets.All.First();
        ProjectDefinition local = new(Guid.NewGuid(), "Local", _root, preset.Features, preset.Retention);
        ProjectDefinition joined = local with { SharedSyncProjectId = Guid.NewGuid() };
        foreach (string category in new[] { "sync-commit-state", "git-exchange-state" })
        {
            string oldState = ProjectSyncPolicy.ResolveStateDirectory(local, _root, category);
            string newState = ProjectSyncPolicy.ResolveStateDirectory(joined, _root, category);
            Assert.NotEqual(oldState, newState);
            Assert.StartsWith(oldState + Path.DirectorySeparatorChar, newState);
            Assert.Contains(joined.SyncProjectId.ToString("N"), newState);
        }
    }

    [Fact]
    public async Task StartupRemovesOldSharesBeforeTheProcessCanSynchronize()
    {
        string config = Path.Combine(_root, "config");
        Directory.CreateDirectory(config);
        string path = Path.Combine(config, "config.xml");
        await File.WriteAllTextAsync(path, "<configuration><folder id=\"old-working-tree\" path=\"old-source\"/><gui><address>127.0.0.1:8384</address></gui><options><listenAddress>default</listenAddress></options></configuration>");
        SyncthingIsolationOptions options = new("unused", config, Path.Combine(_root, "data"), Path.Combine(_root, "exchange"),
            new Uri("http://127.0.0.1:18384"), "test-key", 22091);
        await using ManagedSyncthingEngine engine = new(options);
        MethodInfo prepare = typeof(ManagedSyncthingEngine).GetMethod("EnsureDedicatedConfigurationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)prepare.Invoke(engine, [CancellationToken.None])!;
        XDocument result = XDocument.Load(path);
        Assert.Empty(result.Root!.Elements("folder"));
        Assert.Equal("127.0.0.1:18384", result.Root.Element("gui")!.Element("address")!.Value);
        Assert.Equal(SyncEngineState.Stopped, engine.Status.State);
    }

    [BundledSyncthingFact]
    public async Task BundledWindowsRuntimeCanGenerateFreshIsolatedConfiguration()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "SyncthingRuntime", "win-x64", "syncthing.exe");
        Assert.True(File.Exists(executable), "The Windows test output must contain the bundled runtime.");
        string config = Path.Combine(_root, "fresh-config");
        string data = Path.Combine(_root, "fresh-data");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);
        SyncthingIsolationOptions options = new(executable, config, data, Path.Combine(_root, "fresh-exchange"),
            new Uri("http://127.0.0.1:18384"), "test-key", 22091);
        await using ManagedSyncthingEngine engine = new(options);
        MethodInfo prepare = typeof(ManagedSyncthingEngine).GetMethod("EnsureDedicatedConfigurationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        await (Task)prepare.Invoke(engine, [deadline.Token])!;
        Assert.True(File.Exists(Path.Combine(config, "config.xml")));
        Assert.True(File.Exists(Path.Combine(config, "cert.pem")));
        Assert.Empty(XDocument.Load(Path.Combine(config, "config.xml")).Root!.Elements("folder"));
        Assert.Equal(SyncEngineState.Stopped, engine.Status.State); // Only generated; no network listener.
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}

public sealed class BundledSyncthingFactAttribute : FactAttribute
{
    public BundledSyncthingFactAttribute()
    {
        if (!OperatingSystem.IsWindows() ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "SyncthingRuntime", "win-x64", "syncthing.exe")))
            Skip = "Requires the bundled Windows Syncthing executable; no runtime is downloaded by tests.";
    }
}
