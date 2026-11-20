using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyRevision.Automation;
using CyRevision.Core.Configuration;

namespace CyRevision.Cli;

internal static class Program
{
    private static readonly HashSet<string> Switches=["confirm","allow-write","allow-network","allow-stage","allow-commit","allow-navigation","allow-publication","allow-destructive","allow-services","allow-administration","allow-builds","full-discovery","disk-spool","metrics","notifications","stream","automatic-model"];
    private static readonly HashSet<string> Values=["project","instance","profile-dir","access-profile","token","approval-file","arguments","arguments-file","backup-store","inline-bytes","result-ttl-ms","cache-ttl-ms","context-bytes","history-id","endpoint","api-key-env","executable","attachment","endpoint-file","provider","model","text","remote-config-file","memory-bytes","disk-bytes","chunk-bytes","timeout-ms"];
    private static void Print(object value)=>Console.WriteLine(JsonSerializer.Serialize(value,CyMcpRuntime.JsonOptions));
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
        string command=args.FirstOrDefault()??"help";
        try{
            if(args.Length==0||args.Any(arg=>arg is "--help" or "-h")||command=="help"){
                Print(new{application="CyRevision CLI",sdkVersion="0.7.1",commands=new[]{"list","schema NAME","invoke NAME --arguments JSON","mcp","serve --endpoint-file PATH","providers","discover","chat --provider ID --model ID --text TEXT"},options=new[]{"--project PATH (required for headless operations)","--instance URL_OR_PRIVATE_ENDPOINT_FILE (list/schema/invoke; URL alone with Free)","--access-profile free|local-controlled|token-only|protected (default Free, persisted per project)","--token TOKEN (attached token profiles only)","--profile-dir PRIVATE_FOLDER","--allow-write --allow-stage --allow-commit --allow-network (controlled profiles only, default off)","--allow-navigation --allow-publication --allow-destructive --allow-services --allow-administration --allow-builds (controlled profiles only, default off)","--confirm (exact direct invoke only)","--approval-file JSON_FILE (one exact operation/arguments grant for mcp/serve)","--full-discovery --disk-spool --metrics --notifications","--inline-bytes N --result-ttl-ms N --cache-ttl-ms N","--context-bytes N --history-id ID --restore-history","--endpoint URL --api-key-env NAME --executable PATH --attachment PATH"},exitCodes=new{success=0,arguments=2,denied=3,confirmation=4,unavailable=5,business=6,cancelled=130}});return 0;
            }
            var options=new Dictionary<string,string>(StringComparer.Ordinal);var positional=new List<string>();
            for(int i=1;i<args.Length;i++){
                if(!args[i].StartsWith("--",StringComparison.Ordinal)){positional.Add(args[i]);continue;}
                string key=args[i][2..];if(options.ContainsKey(key))throw new ArgumentException("Duplicate option: "+key);
                if(!Switches.Contains(key)&&!Values.Contains(key)&&key!="restore-history")throw new ArgumentException("Unknown option: --"+key);
                if(Switches.Contains(key)||key=="restore-history")options[key]="true";
                else{if(i+1>=args.Length||args[i+1].StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("A value is required for --"+key);options[key]=args[++i];}
            }
            string Option(string key,string fallback="")=>options.GetValueOrDefault(key,fallback);
            bool Has(string key)=>options.ContainsKey(key);
            int Integer(string key,int fallback){int value=Has(key)?int.Parse(Option(key),System.Globalization.CultureInfo.InvariantCulture):fallback;if(value<0)throw new ArgumentException("--"+key+" must be non-negative.");return value;}
            string Name()=>positional.Count==1?positional[0]:throw new ArgumentException("One operation name is required.");
            JsonElement Arguments()=>JsonSerializer.SerializeToElement(JsonNode.Parse(Has("arguments-file")?File.ReadAllText(Option("arguments-file")):Option("arguments","{}")));
            using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
            if(Has("timeout-ms"))cancellation.CancelAfter(Integer("timeout-ms",0));
            if(Has("instance")){
                if(command is not ("list" or "schema" or "invoke"))throw new ArgumentException("Attached CLI supports list, schema and invoke. External MCP clients connect directly to the instance HTTP endpoint.");
                var result=await AttachedAsync(Option("instance"),Option("token"),command=="invoke"?new{method="invoke",name=Name(),arguments=Arguments()}:(object)new{method="describe"},cancellation.Token);
                if(command=="schema")result=result.GetProperty("operations").EnumerateArray().First(item=>item.GetProperty("name").GetString()==Name()).Clone();
                Print(new{ok=true,result});return 0;
            }
            if(!Has("project"))throw new ArgumentException("Select an explicit --project folder, or use --instance.");
            string data=Path.Combine(Has("profile-dir")?Path.GetFullPath(Option("profile-dir")):ApplicationPaths.CreateDefault().DataDirectory,"automation");
            string projectRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Option("project")));
            string projectKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows()?projectRoot.ToUpperInvariant():projectRoot))).ToLowerInvariant();
            string accessFile=Path.Combine(data,"profiles",projectKey,"local-access.json");
            var accessProfile=Has("access-profile")?LocalMcpAccess.Parse(Option("access-profile")):LocalMcpAccessStore.LoadOrMigrate(accessFile);
            if(Has("access-profile"))LocalMcpAccessStore.Save(accessFile,accessProfile);
            JsonNode? consent=Has("approval-file")?JsonNode.Parse(await File.ReadAllTextAsync(Option("approval-file"),cancellation.Token)):null;
            int consentUsed=0;
            var permissions=new AutomationPermissions{AccessProfile=accessProfile,Write=Has("allow-write"),Stage=Has("allow-stage"),Commit=Has("allow-commit"),Network=Has("allow-network")};
            permissions.Capabilities=(Has("allow-navigation")?AutomationCapability.Navigation:0)|(Has("allow-publication")?AutomationCapability.Publication:0)|(Has("allow-destructive")?AutomationCapability.Destructive:0)|(Has("allow-services")?AutomationCapability.Services:0)|(Has("allow-administration")?AutomationCapability.Administration:0)|(Has("allow-builds")?AutomationCapability.Builds:0);
            permissions.ConfirmAsync=(name,arguments,_)=>{
                if(command=="invoke"&&Has("confirm")&&name==Name())return Task.FromResult(true);
                bool approved=consent?["operation"]?.GetValue<string>()==name&&JsonNode.DeepEquals(consent?["arguments"],JsonNode.Parse(arguments.GetRawText()));
                return Task.FromResult(approved&&Interlocked.CompareExchange(ref consentUsed,1,0)==0);
            };
            if(Has("confirm")&&command!="invoke")throw new ArgumentException("--confirm applies only to the exact direct invoke command; use a scoped --approval-file for a server.");
            var engine=new RevisionAutomationEngine(Option("project"),data,permissions,backupStore:Has("backup-store")?Option("backup-store"):null);
            using var runtime=new CyMcpRuntime(engine,new{compactDiscovery=!Has("full-discovery"),diskSpool=Has("disk-spool"),performance=Has("metrics"),notifications=Has("notifications"),inlineBytes=Integer("inline-bytes",16384),resultTtlMs=Integer("result-ttl-ms",300000),cacheTtlMs=Integer("cache-ttl-ms",30000),memoryBytes=Integer("memory-bytes",67108864),diskBytes=Integer("disk-bytes",1073741824),chunkBytes=Integer("chunk-bytes",32768)});
            if(Has("remote-config-file")){
                permissions.RemoteTools=true;var remote=JsonNode.Parse(await File.ReadAllTextAsync(Option("remote-config-file"),cancellation.Token))!.AsObject();
                remote["command"]="remote-connect";
                if(permissions.ControlOperations&&remote.ContainsKey("url")&&!permissions.Network)throw new AutomationException("AccessDenied","External HTTP tools require --allow-network.");
                if(remote.Remove("tokenEnvironment",out var env)&&env is not null)remote["token"]=Environment.GetEnvironmentVariable(env.GetValue<string>())??"";
                await runtime.ConnectRemoteAsync(remote,cancellation.Token);
            }
            if(Has("stream"))runtime.Event+=value=>Print(new{type="event",@event=value});
            object request=command switch{
                "list"=>new{command="describe"},"schema"=>new{command="describe",name=Name()},
                "invoke"=>new{command="invoke",name=Name(),arguments=Arguments()},
                "providers"=>new{command="providers"},"discover"=>new{command="discover"},
                "mcp"=>new{command="mcp-stdio"},_=>new{command="describe"}
            };
            if(command=="serve"){
                string token=accessProfile.RequiresToken()?Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant():"";
                var server=await runtime.RequestAsync(new{command="serve",token},cancellation.Token);
                string endpointFile=Path.GetFullPath(Option("endpoint-file",Path.Combine(data,"instance.json")));
                string url="http://127.0.0.1:"+server.GetProperty("port").GetInt32();
                await runtime.RequestAsync(new{command="private-write",path=endpointFile,text=JsonSerializer.Serialize(new{url,token,accessProfile=accessProfile.Id(),pid=Environment.ProcessId,project=engine.Root},CyMcpRuntime.JsonOptions)},cancellation.Token);
                Print(new{ok=true,url,endpointFile,accessProfile=accessProfile.Id(),requiresToken=accessProfile.RequiresToken()});
                try{await Task.Delay(Timeout.Infinite,cancellation.Token);}catch(OperationCanceledException){}
                finally{await runtime.RequestAsync(new{command="stop-server"});if(File.Exists(endpointFile))File.Delete(endpointFile);}
                return 0;
            }
            if(command=="chat"){
                string provider=Option("provider");if(provider.Length==0)throw new ArgumentException("--provider is required; list the SDK providers first.");
                string historyId=Option("history-id",Guid.NewGuid().ToString("N")),historyDirectory=Path.Combine(data,"conversations");
                if(!Has("restore-history")&&File.Exists(Path.Combine(historyDirectory,historyId+".json")))throw new AutomationException("ConfirmationRequired","History exists; use --restore-history or a new --history-id.");
                JsonElement? checkpoint=Has("restore-history")?await runtime.RequestAsync(new{command="chat-load",directory=historyDirectory,id=historyId},cancellation.Token):null;
                var connect=new JsonObject{["command"]="chat-connect",["provider"]=provider,["model"]=Option("model"),["apiKey"]=Has("api-key-env")?Environment.GetEnvironmentVariable(Option("api-key-env")):"",["executable"]=Option("executable"),["cwd"]=engine.Root,["automaticModel"]=Has("automatic-model"),["contextBytes"]=Integer("context-bytes",0),["attachTools"]=provider is "codex" or "openai" or "anthropic" or "openrouter" or "ollama" or "openai-compatible" or "llama.cpp" or "lmstudio",["settings"]=new JsonObject{["base_url"]=Option("endpoint")}};
                if(!Has("endpoint"))connect["settings"]!.AsObject().Remove("base_url");
                if(checkpoint.HasValue)connect["checkpoint"]=JsonNode.Parse(checkpoint.Value.GetRawText());
                await runtime.RequestAsync(connect,cancellation.Token);
                var content=new JsonArray(new JsonObject{["type"]="text",["text"]=Option("text")});
                if(Has("attachment"))content.Add(JsonNode.Parse((await runtime.RequestAsync(new{command="attachment",path=Path.GetFullPath(Option("attachment"))},cancellation.Token)).GetRawText()));
                JsonElement result;
                try{result=await runtime.RequestAsync(new{command="chat-send",content},cancellation.Token);}
                finally{try{await runtime.RequestAsync(new{command="chat-save",directory=historyDirectory,id=historyId});}catch(AutomationException){Console.Error.WriteLine("Conversation history could not be saved; no generation was retried.");}}
                Print(new{ok=true,result});return 0;
            }
            if(command is not ("list" or "schema" or "invoke" or "providers" or "discover" or "mcp"))throw new ArgumentException("Unknown command. Use --help.");
            var value=await runtime.RequestAsync(request,cancellation.Token);if(command!="mcp")Print(new{ok=true,result=value});return 0;
        }catch(Exception error){
            string code=error is AutomationException known?known.Code:error is OperationCanceledException?"Cancelled":error is HttpRequestException?"ConnectionFailed":"InvalidArguments";
            var message=new{ok=false,error=new{code,message=error is HttpRequestException?error.GetBaseException().Message:error.Message}};
            Console.Error.WriteLine(JsonSerializer.Serialize(message,CyMcpRuntime.JsonOptions));if(command!="mcp")Print(message);
            return code switch{"AccessDenied" or "Unauthorized"=>3,"ConfirmationRequired"=>4,"RuntimeMissing" or "RuntimeConfiguration"=>5,"Cancelled"=>130,"InvalidArguments" or "InvalidParameters" or "UnknownOperation" or "UnknownCommand"=>2,_=>6};
        }
    }

    private static async Task<JsonElement> AttachedAsync(string endpointFile,string bearer,object request,CancellationToken token)
    {
        Uri endpoint;
        if(Uri.TryCreate(endpointFile,UriKind.Absolute,out var address)&&address.Scheme is "http" or "https") endpoint=address;
        else {
            var file=new FileInfo(endpointFile);if(file.Length>65536||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Invalid instance descriptor.");
            using var descriptor=JsonDocument.Parse(await File.ReadAllTextAsync(endpointFile,token));
            endpoint=new Uri(descriptor.RootElement.GetProperty("url").GetString()!);
            if(bearer.Length==0&&descriptor.RootElement.TryGetProperty("token",out var secret))bearer=secret.GetString()??"";
        }
        if(!endpoint.IsLoopback||endpoint.Scheme!="http"||endpoint.UserInfo.Length>0||endpoint.Query.Length>0||endpoint.Fragment.Length>0||endpoint.AbsolutePath is not ("/" or "/mcp" or "/v1"))throw new ArgumentException("Instance connections are restricted to loopback HTTP.");
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});
        if(bearer.Length>0)client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        using var response=await client.PostAsync(new Uri(endpoint,"/v1"),new StringContent(JsonSerializer.Serialize(request,CyMcpRuntime.JsonOptions),Encoding.UTF8,"application/json"),token);
        if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)throw new AutomationException("Unauthorized","This instance requires a valid token.");
        if(response.StatusCode==System.Net.HttpStatusCode.Forbidden)throw new AutomationException("AccessDenied","The instance rejected this connection.");
        using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if(!response.IsSuccessStatusCode){var error=result.RootElement.GetProperty("error");throw new AutomationException(error.GetProperty("code").GetString()!,error.GetProperty("message").GetString()!);}
        return result.RootElement.Clone();
    }
}
