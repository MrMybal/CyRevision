using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CyRevision.Automation;

public sealed class AutomationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record AutomationEvent(string Type, JsonElement Data);

/// <summary>Owns the source-built CyMCPSDK ABI. No provider or protocol is reimplemented here.</summary>
public sealed class CyMcpRuntime : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr HostCall(IntPtr state, IntPtr operation, IntPtr arguments, IntPtr context, IntPtr cancellation, int phase);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void HostFree(IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void HostEvent(IntPtr state, IntPtr data);
    private const string Library = "CyRevision.CyMcpNative";
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    private static extern IntPtr cyrevision_cymcp_create([MarshalAs(UnmanagedType.LPUTF8Str)]string catalogue, [MarshalAs(UnmanagedType.LPUTF8Str)]string options, IntPtr state, HostCall call, HostFree free, HostEvent events);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr cyrevision_cymcp_request(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string json);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern void cyrevision_cymcp_free(IntPtr json);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern void cyrevision_cymcp_destroy(IntPtr handle);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int cyrevision_cymcp_cancelled(IntPtr context);
    private readonly HostCall _hostCall;
    private readonly HostFree _hostFree;
    private readonly HostEvent _hostEvent;
    private readonly SemaphoreSlim _requests = new(1,1);
    private readonly RevisionAutomationEngine _engine;
    private readonly ConcurrentDictionary<string,bool> _remoteTools = new();
    private IntPtr _handle;
    public event Action<AutomationEvent>? Event;

    public CyMcpRuntime(RevisionAutomationEngine engine, object? options=null)
    {
        _engine=engine;_hostCall=CallHost;_hostFree=Marshal.FreeCoTaskMem;
        _hostEvent=(_,data)=>{try{using var value=JsonDocument.Parse(Marshal.PtrToStringUTF8(data)!);Event?.Invoke(new(value.RootElement.GetProperty("type").GetString()!,value.RootElement.GetProperty("data").Clone()));}catch{/* Never unwind a managed subscriber into native code. */}};
        var configuration=JsonSerializer.SerializeToNode(options??new{},JsonOptions)!.AsObject();
        configuration["privateDirectory"]=engine.PrivateData;
        configuration["spoolDirectory"]??=Path.Combine(engine.PrivateData,"results");
        if(configuration["performance"]?.GetValue<bool>()==true)configuration["performanceFile"]??=Path.Combine(engine.PrivateData,"performance.jsonl");
        try{_handle=cyrevision_cymcp_create(JsonSerializer.Serialize(engine.Catalogue,JsonOptions),configuration.ToJsonString(),IntPtr.Zero,_hostCall,_hostFree,_hostEvent);}
        catch(DllNotFoundException){throw new AutomationException("RuntimeMissing","Build the CyMCPSDK runtime with scripts/build-cymcp.ps1 (Windows) or scripts/build-cymcp.sh before using this feature.");}
        if(_handle==IntPtr.Zero)throw new AutomationException("RuntimeConfiguration","CyMCPSDK rejected its registry or options.");
    }

    private IntPtr CallHost(IntPtr state,IntPtr operation,IntPtr arguments,IntPtr context,IntPtr cancellation,int phase)
    {
        object response;
        try{
            string name=Marshal.PtrToStringUTF8(operation)!;
            using var args=JsonDocument.Parse(Marshal.PtrToStringUTF8(arguments)!);
            if(phase==0){bool allowed=_remoteTools.TryGetValue(name,out bool readOnly)?_engine.CanCallRemote(name,readOnly):_engine.IsAllowed(name);response=new{ok=true,result=new{allowed}};}
            else{
                using var cancel=new CancellationTokenSource();
                using var timer=new Timer(_=>{if(cyrevision_cymcp_cancelled(cancellation)!=0)cancel.Cancel();},null,10,10);
                try{
                    if(phase==2){_engine.ConfirmRemoteAsync(name,args.RootElement.Clone(),_remoteTools.GetValueOrDefault(name),cancel.Token).GetAwaiter().GetResult();response=new{ok=true,result=new{}};}
                    else{var result=_engine.ExecuteAsync(name,args.RootElement.Clone(),cancel.Token).GetAwaiter().GetResult();response=new{ok=true,result};}
                }finally{timer.DisposeAsync().AsTask().GetAwaiter().GetResult();}
            }
        }catch(Exception error){string code=error switch{AutomationException known=>known.Code,OperationCanceledException=>"Cancelled",UnauthorizedAccessException=>"AccessDenied",ArgumentException=>"InvalidArguments",_=>"BusinessError"};response=new{ok=false,error=new{code,message=error.Message}};}
        return Marshal.StringToCoTaskMemUTF8(JsonSerializer.Serialize(response,JsonOptions));
    }

    public JsonElement Request(object request)
    {
        ObjectDisposedException.ThrowIf(_handle==IntPtr.Zero,this);
        var response=cyrevision_cymcp_request(_handle,JsonSerializer.Serialize(request,JsonOptions));
        if(response==IntPtr.Zero)throw new AutomationException("MemoryLimit","Native SDK result allocation failed.");
        try{
            using var value=JsonDocument.Parse(Marshal.PtrToStringUTF8(response)!);
            if(!value.RootElement.GetProperty("ok").GetBoolean()){var error=value.RootElement.GetProperty("error");throw new AutomationException(error.GetProperty("code").GetString()!,error.GetProperty("message").GetString()!);}
            return value.RootElement.GetProperty("result").Clone();
        }finally{cyrevision_cymcp_free(response);}
    }
    public async Task<JsonElement> RequestAsync(object request,CancellationToken token=default)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try{
            using var registration=token.Register(()=>{try{Request(new{command="cancel"});}catch{}});
            return await Task.Run(()=>Request(request),token).ConfigureAwait(false);
        }finally{_requests.Release();}
    }
    public async Task<JsonElement> ConnectRemoteAsync(object config,CancellationToken token=default)
    {
        var result=await RequestAsync(config,token);
        foreach(var item in result.EnumerateArray())_remoteTools[item.GetProperty("name").GetString()!]=item.GetProperty("readOnly").GetBoolean();
        return result;
    }
    public void Dispose()
    {
        if(_handle==IntPtr.Zero)return;
        // Callers first await their running requests. Native destruction stops only owned services.
        cyrevision_cymcp_destroy(_handle);_handle=IntPtr.Zero;
        GC.KeepAlive(_hostCall);GC.KeepAlive(_hostFree);GC.KeepAlive(_hostEvent);
        _requests.Dispose();
    }
}
