using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyRevision.Automation;

[Flags]
public enum AutomationCapability
{
    None=0, Navigation=1, Publication=2, Destructive=4, Services=8, Administration=16, Builds=32
}

public sealed record AutomationOperation(string Name,string Description,JsonObject InputSchema,JsonObject OutputSchema,bool ReadOnly,
    bool Network=false,bool Stage=false,bool Commit=false,
    Func<JsonElement,CancellationToken,Task<object>>? Handler=null,
    AutomationCapability RequiredCapabilities=AutomationCapability.None,bool ConfirmationRequired=false,Func<bool>? Available=null);

public sealed class AutomationPermissions
{
    public LocalMcpAccessProfile AccessProfile { get; set; } = LocalMcpAccessProfile.Free;
    public bool ControlOperations => AccessProfile.ControlsOperations();
    public bool Read {get;set;}=true;
    public bool Write {get;set;}
    public bool Network {get;set;}
    public bool Stage {get;set;}
    public bool Commit {get;set;}
    public bool Blocked {get;set;}
    public bool RemoteTools {get;set;}
    public AutomationCapability Capabilities {get;set;}
    public Func<bool> GitEnabled {get;set;}=()=>true;
    public Func<bool> BackupEnabled {get;set;}=()=>true;
    public Func<bool> ScopeValid {get;set;}=()=>true;
    public Func<string,bool> RemoteAllowed {get;set;}=_=>true;
    public Func<string,JsonElement,CancellationToken,Task<bool>> ConfirmAsync {get;set;}=(_,_,_)=>Task.FromResult(false);
}

internal static class AutomationSchema
{
    public static JsonObject String(int max=4096)=>new(){["type"]="string",["maxLength"]=max};
    public static JsonObject Integer(int minimum=0,int maximum=1000)=>new(){["type"]="integer",["minimum"]=minimum,["maximum"]=maximum};
    public static JsonObject Boolean()=>new(){["type"]="boolean"};
    public static JsonObject Paths()=>new(){["type"]="array",["items"]=String(),["minItems"]=1,["maxItems"]=2000};
    public static JsonObject Object(Dictionary<string,JsonObject>? fields=null,string[]? required=null)=>new(){["type"]="object",["properties"]=fields is null?new JsonObject():new JsonObject(fields.Select(pair=>KeyValuePair.Create<string,JsonNode?>(pair.Key,pair.Value))),["required"]=new JsonArray((required??[]).Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray()),["additionalProperties"]=false};
    public static JsonObject Output()=>new(){["type"]="object",["properties"]=new JsonObject{["value"]=new JsonObject()},["required"]=new JsonArray("value"),["additionalProperties"]=false};
}
