using System.Text;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CyRevision.Core.Configuration;
using CyRevision.Desktop.Diagnostics;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;
using System.ComponentModel;

namespace CyRevision.Desktop.Native;

public sealed class NativeBackendHost
{
    private readonly MainWindowViewModel _model;
    private readonly NativeModelAdapter _adapter;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly ApplicationLogService _log;
    private readonly SemaphoreSlim _writeGate = new(1,1);
    private readonly StreamWriter _output = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush=true };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    private bool _closing;
    private readonly RepositoryChangeMonitor _monitor;
    private string _watchedRoot="";
    public NativePreferencesActions? Preferences {get=>_adapter.Preferences;set{_adapter.Preferences=value;ConfigureMonitor();}}

    public NativeBackendHost(MainWindowViewModel model, IClassicDesktopStyleApplicationLifetime desktop, ApplicationLogService log,string? configurationDirectory=null)
    {
        _model=model;
        _adapter=new(model,configurationDirectory);
        _desktop=desktop;
        _log=log;
        _monitor=new RepositoryChangeMonitor(batch=>Dispatcher.UIThread.Post(()=>_ = RefreshDetectedAsync(batch)));
        _model.PropertyChanged+=ModelChanged;
    }

    public static bool IsRequested(string[]? args) => args?.Contains("--native-backend",StringComparer.Ordinal) == true;
    public static bool IsIsolatedProfile(string[]? args) => IsRequested(args) && args?.Contains("--profile-dir",StringComparer.Ordinal)==true;

    public static ApplicationPaths ResolvePaths(string[]? args)
    {
        if (IsRequested(args) && args is not null)
        {
            int index=Array.IndexOf(args,"--profile-dir");
            if (index >= 0 && index+1 < args.Length)
            {
                string root=Path.GetFullPath(args[index+1]);
                return new(Path.Combine(root,"config"),Path.Combine(root,"data"),Path.Combine(root,"cache"));
            }
        }
        return ApplicationPaths.CreateDefault();
    }

    public void Start()
    {
        _ = InitializeAsync();
        _ = Task.Run(ReadLoopAsync);
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _model.InitializeAsync();
            _adapter.MarkInitialized();
            ConfigureMonitor();
        }
        catch(Exception error)
        {
            _log.Error("Native","Engine initialization failed.",error);
            await SendAsync(new { id=0, ok=false, error=error.Message });
        }
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(MainWindowViewModel.SelectedProject) or nameof(MainWindowViewModel.IsProjectLoading))ConfigureMonitor();
    }
    private void ConfigureMonitor()
    {
        string root=Preferences?.AutomaticRepositoryRefresh!=false && _model.SelectedProject?.Definition.Features.GitEnabled==true?_model.SelectedProject.RootPath:"";
        if(root==_watchedRoot)return;
        _watchedRoot=root;
        if(root.Length>0 && Directory.Exists(root))_monitor.Start(root);else _monitor.Stop();
    }
    private async Task RefreshDetectedAsync(RepositoryChangeBatch batch)
    {
        if(_closing)return;
        try{await _model.RefreshDetectedChangesAsync(batch.Paths,batch.RequiresUntrackedScan,batch.GitMetadataChanged,batch.WatcherOverflowed);}
        catch(Exception error){_log.Warning("Native",$"Automatic refresh: {error.GetType().Name}.");}
    }

    private async Task ReadLoopAsync()
    {
        using StreamReader input=new(Console.OpenStandardInput(),new UTF8Encoding(false,true));
        try
        {
            while(!_closing && await input.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                if(line.Length>1024*1024)
                {
                    await SendAsync(new {id=0,ok=false,error="Request too large."}).ConfigureAwait(false);
                    continue;
                }
                JsonElement request;
                try
                {
                    using JsonDocument document=JsonDocument.Parse(line);
                    if(document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new JsonException("A request must be an object.");
                    request=document.RootElement.Clone();
                }
                catch(JsonException)
                {
                    await SendAsync(new {id=0,ok=false,error="Invalid request."}).ConfigureAwait(false);
                    continue;
                }
                Dispatcher.UIThread.Post(()=>_ = HandleAsync(request));
            }
        }
        catch(Exception error) when(error is IOException or DecoderFallbackException)
        {
            _log.Warning("Native","The interface connection closed.");
        }
        finally
        {
            Dispatcher.UIThread.Post(()=>_ = CloseAsync());
        }
    }

    private async Task HandleAsync(JsonElement request)
    {
        long id=request.TryGetProperty("id",out JsonElement value) && value.TryGetInt64(out long number) ? number : 0;
        try
        {
            if(_closing) return;
            if(request.TryGetProperty("command",out var command) && command.GetString()=="shutdown")
            {
                await SendAsync(new { id,ok=true,result=new {message="Closing"} });
                await CloseAsync();
                return;
            }
            object? result=await _adapter.ExecuteAsync(request);
            ConfigureMonitor();
            await SendAsync(new {id,ok=true,result});
        }
        catch(Exception error)
        {
            _log.Warning("Native",$"Request failed: {error.GetType().Name}: {error.Message}");
            try { await SendAsync(new {id,ok=false,error=error.Message}); }
            catch(IOException) { await CloseAsync(); }
        }
    }

    private async Task SendAsync(object response)
    {
        string json=JsonSerializer.Serialize(response,JsonOptions);
        if(Encoding.UTF8.GetByteCount(json)>24*1024*1024)
        {
            object? id=response.GetType().GetProperty("id")?.GetValue(response);
            json=JsonSerializer.Serialize(new{id=id??0,ok=false,error="This view exceeds the display budget. Narrow the filter or reduce the requested lists; the engine remains connected."},JsonOptions);
        }
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try { await _output.WriteLineAsync(json).ConfigureAwait(false); }
        finally { _writeGate.Release(); }
    }

    private async Task CloseAsync()
    {
        if(_closing) return;
        _closing=true;
        _model.PropertyChanged-=ModelChanged;_monitor.Dispose();Preferences?.Cancel();
        _model.CancelNativeOperation();
        await NativeGitActions.DisposeAsync(_model);
        try { await _model.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch(Exception error) { _log.Warning("Native",$"Service shutdown: {error.GetType().Name}."); }
        _desktop.Shutdown();
    }
}
