using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CyRevision.Code;
using CyRevision.Core.Configuration;
using CyRevision.Core.Projects;
using CyRevision.Core.Updates;
using CyRevision.Desktop.Diagnostics;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.Native;
using CyRevision.Desktop.Plugins;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;
using CyRevision.Diff;
using CyRevision.Discord;
using CyRevision.Discord.Control;
using CyRevision.Git;
using CyRevision.PullRequests;
using CyRevision.RemoteBuild;
using CyRevision.Sync;
using CyRevision.Vpn;

namespace CyRevision.Core.Tests;

public sealed class ApplicationUpdateStartupTests
{
    [Fact]
    public async Task StartupChecksBeforeTheProjectCatalogueCompletes_AndOffersANewerRelease()
    {
        await using Fixture fixture = new();
        int offers = 0;
        TaskCompletionSource offered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Model.UpdateAvailable += (_, _) => { offers++; offered.TrySetResult(); };
        Task startup = fixture.Model.InitializeAsync();
        try
        {
            await fixture.Catalog.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(startup.IsCompleted);
            Assert.True(fixture.Model.IsCheckingForUpdates);
            Assert.Equal(1, fixture.Handler.Requests);
            // A manual request during the automatic check must not duplicate it.
            await fixture.Adapter.ExecuteAsync(Request("updates.check"));
            Assert.Equal(1, fixture.Handler.Requests);
            fixture.Handler.Complete("0.1.33");
            await offered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Model.HasUpdateAvailable);
            Assert.True(fixture.Model.CanInstallUpdate);
            Assert.False(fixture.Model.IsCheckingForUpdates);
            Assert.Equal(1, offers);
            Assert.Null(fixture.Model.SelectedProject);
            Assert.False(startup.IsCompleted);
            Assert.Equal(1, fixture.Handler.Requests); // No installer/checksum request.
            JsonElement state = JsonSerializer.SerializeToElement(fixture.Adapter.State(Request("state")));
            Assert.True(state.GetProperty("fields").GetProperty("HasUpdateAvailable").GetProperty("value").GetBoolean());
            Assert.False(state.GetProperty("ready").GetBoolean());
        }
        finally
        {
            fixture.Catalog.Release.TrySetResult([]);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("0.1.32", false)]
    [InlineData("0.1.31", false)]
    [InlineData("0.1.33", true)]
    public async Task ManualCheckWorksBeforeInitialization_WithoutAProject(string latest, bool available)
    {
        await using Fixture fixture = new();
        int offers = 0;
        fixture.Model.UpdateAvailable += (_, _) => offers++;
        fixture.Handler.Complete(latest);
        await fixture.Adapter.ExecuteAsync(Request("updates.check"));
        Assert.Equal(available, fixture.Model.HasUpdateAvailable);
        Assert.Equal(available ? 1 : 0, offers);
        Assert.True(fixture.Model.HasCheckedForUpdates);
        Assert.False(fixture.Model.HasUpdateCheckFailed);
        Assert.True(fixture.Model.CanCheckForUpdates);
        Assert.False(fixture.Model.IsBusy);
        Assert.Equal(1, fixture.Model.UpdateCheckRevision);
        Assert.Equal(1, fixture.Handler.Requests);
    }

    [Fact]
    public async Task NetworkFailureCompletesTheCheck_WithoutOfferingOrBlockingTheApplication()
    {
        await using Fixture fixture = new();
        int offers = 0;
        fixture.Model.UpdateAvailable += (_, _) => offers++;
        fixture.Handler.Response.SetException(new HttpRequestException("Network unavailable"));
        await fixture.Adapter.ExecuteAsync(Request("updates.check"));
        Assert.True(fixture.Model.HasUpdateCheckFailed);
        Assert.True(fixture.Model.HasCheckedForUpdates);
        Assert.False(fixture.Model.HasUpdateAvailable);
        Assert.False(fixture.Model.CanInstallUpdate);
        Assert.False(fixture.Model.IsCheckingForUpdates);
        Assert.True(fixture.Model.CanCheckForUpdates);
        Assert.False(fixture.Model.IsBusy);
        Assert.Equal(0, offers);
    }

    [Theory]
    [InlineData("_hasCheckedForUpdates")]
    [InlineData("_hasUpdateCheckFailed")]
    [InlineData("_updateCheckRevision")]
    public void ApplicationUpdateStateIsNotRestoredFromAProjectSession(string fieldName)
    {
        Type model = typeof(MainWindowViewModel);
        FieldInfo field = model.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        MethodInfo predicate = model.GetMethod("ShouldCacheProjectField", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)predicate.Invoke(null, [field])!);
    }

    private static JsonElement Request(string command) => JsonSerializer.SerializeToElement(new { command });

    [Fact]
    public async Task CyDocLinksWorkWithoutAProjectAndDuringAnOperation()
    {
        await using Fixture fixture = new(Path.Combine(AppContext.BaseDirectory, "Help"));
        Assert.True(fixture.Model.IsDocumentationAvailable);
        Assert.True(NativeWorkspaceCatalog.IsAvailable(NativeWorkspaceCatalog.Find("help"), fixture.Model));
        var busy = typeof(MainWindowViewModel).GetField("_isBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        busy.SetValue(fixture.Model, true);
        var result = JsonSerializer.SerializeToElement(await fixture.Adapter.ExecuteAsync(JsonSerializer.SerializeToElement(new { id = 42, command = "docs.open", topicId = "ci-actions", projectId = "stale-context" })));
        Assert.EndsWith("#/en/ci-actions", result.GetProperty("url").GetString());
        await fixture.Adapter.ExecuteAsync(JsonSerializer.SerializeToElement(new { command = "app.language", language = "fr" }));
        var french = JsonSerializer.SerializeToElement(await fixture.Adapter.ExecuteAsync(JsonSerializer.SerializeToElement(new { id = 43, command = "docs.open", topicId = "git-history", anchor = "file-history-including-lfs", mediaId = "history-v2" })));
        Assert.EndsWith("#/fr/git-history/file-history-including-lfs?media=history-v2", french.GetProperty("url").GetString());
        var context = JsonSerializer.SerializeToElement(await fixture.Adapter.ExecuteAsync(JsonSerializer.SerializeToElement(new { command = "docs.open", topicId = "code" })));
        Assert.EndsWith("#/fr/code-explorer", context.GetProperty("url").GetString());
        busy.SetValue(fixture.Model, false);
    }

    private sealed class UpdateHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(string version) => Response.TrySetResult(new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                tag_name = "v" + version, draft = false, prerelease = false,
                html_url = "https://github.com/MrMybal/CyRevision/releases/tag/v" + version,
                body = "Improved startup updates.",
                assets = new[]
                {
                    new { name = $"CyRevision-Setup-{version}-win-x64.exe", browser_download_url = $"https://github.com/MrMybal/CyRevision/releases/download/v{version}/CyRevision-Setup-{version}-win-x64.exe", size = 10 },
                    new { name = $"CyRevision-{version}-linux-x64.deb", browser_download_url = $"https://github.com/MrMybal/CyRevision/releases/download/v{version}/CyRevision-{version}-linux-x64.deb", size = 10 },
                    new { name = $"CyRevision-{version}-osx-arm64.dmg", browser_download_url = $"https://github.com/MrMybal/CyRevision/releases/download/v{version}/CyRevision-{version}-osx-arm64.dmg", size = 10 },
                    new { name = $"CyRevision-{version}-osx-x64.dmg", browser_download_url = $"https://github.com/MrMybal/CyRevision/releases/download/v{version}/CyRevision-{version}-osx-x64.dmg", size = 10 },
                    new { name = $"CyRevision-Setup-{version}-win-arm64.exe", browser_download_url = $"https://github.com/MrMybal/CyRevision/releases/download/v{version}/CyRevision-Setup-{version}-win-arm64.exe", size = 10 },
                    new { name = $"CyRevision-{version}-linux-arm64.deb", browser_download_url = $"https://github.com/MrMybal/CyRevision/releases/download/v{version}/CyRevision-{version}-linux-arm64.deb", size = 10 }
                }
            }), Encoding.UTF8, "application/json")
        });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.EndsWith("/releases/latest", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            return Response.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingCatalog : IProjectCatalog
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<ProjectDefinition>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<ProjectDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); return Release.Task.WaitAsync(cancellationToken); }
        public Task<ProjectDefinition?> FindByIdAsync(Guid id, CancellationToken token = default) => Task.FromResult<ProjectDefinition?>(null);
        public Task UpsertAsync(ProjectDefinition project, CancellationToken token = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken token = default) => Task.CompletedTask;
    }

    // Real application services under a temporary profile. Catalogue loading
    // and release responses are controlled by the test.
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionUpdateTests", Guid.NewGuid().ToString("N"));
        private readonly ApplicationLogService _log;
        private readonly HttpClient _http;
        public BlockingCatalog Catalog { get; } = new();
        public UpdateHandler Handler { get; } = new();
        public MainWindowViewModel Model { get; }
        public NativeModelAdapter Adapter { get; }
        public Fixture(string? documentationDirectory = null)
        {
            ApplicationPaths paths = new(Path.Combine(_root, "config"), Path.Combine(_root, "data"), Path.Combine(_root, "cache"));
            Directory.CreateDirectory(paths.ConfigurationDirectory);
            _log = new(paths.DataDirectory);
            _http = new(Handler);
            ApplicationUpdateService updates = new(new Uri("https://github.com/MrMybal/CyRevision"), ReleaseVersion.Parse("0.1.32"), _http);
            GitCliRepositoryService git = new();
            JsonDiscordAgentStore discordStore = new(paths.DiscordDirectory);
            DiscordProjectAgent discord = new(new GitDiscordProjectSnapshotProvider(git), discordStore, new DiscordWebhookClient());
            WireGuardConfigService vpnConfig = new(paths.VpnDirectory);
            VpnNetworkSetupService vpnNetwork = new();
            LocalizationService localization = new();
            localization.Configure(paths.ConfigurationDirectory);
            Model = new(Catalog, git, paths,
                new JsonSyncthingProfileStore(paths.ManagedSyncthingDirectory), new SyncthingRuntimeResolver(managedRoot: paths.ManagedSyncthingDirectory), new SyncthingIgnoreFileService(),
                new GitPeerExchangeService(), new AssetDiffService(), new JsonVpnProfileStore(paths.VpnDirectory), new WireGuardKeyService(), vpnConfig,
                new ManagedWireGuardEngine(paths.VpnDirectory, vpnConfig), new WireGuardRuntimeResolver(), vpnNetwork, new VpnSyncExchangeService(),
                new JsonSwarmProfileStore(paths.VpnDirectory), new SwarmSetupService(vpnNetwork), new JsonVpnFileExchangeProfileStore(paths.VpnDirectory), new VpnFileExchangeService(),
                new JsonTeamChatProfileStore(Path.Combine(paths.ConfigurationDirectory, "team-chat")), new TeamChatService(paths.TeamChatDirectory),
                new JsonLfsManagementProfileStore(paths.LfsManagementDirectory), new LfsStorageManager(), new JsonRemoteBuildConnectionStore(paths.RemoteBuildDirectory), new RemoteBuildSnapshotBuilder(),
                localization, new OfflineDocumentationService(documentationDirectory ?? Path.Combine(_root, "docs")), updates, discordStore, discord, new DiscordControlConnectionStore(paths.DiscordControlDirectory),
                new CyRevisionPluginManager(_root, paths.ConfigurationDirectory, paths.DataDirectory, "0.1.32"), new CodeWorkspaceService(),
                new GitHubPullRequestService(), new GitHubActionsService(), _log, new RepositoryConsoleService(paths.ConfigurationDirectory));
            Adapter = new(Model, paths.ConfigurationDirectory);
        }
        public async ValueTask DisposeAsync()
        {
            await Model.DisposeAsync();
            _http.Dispose();
            _log.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
