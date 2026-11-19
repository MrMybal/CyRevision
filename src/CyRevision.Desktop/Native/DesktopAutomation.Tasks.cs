using System.Text.Json;
using CyRevision.Automation;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.Native;

internal sealed partial class DesktopAutomation
{
    private void RegisterTasks()
    {
        engine.Add(new("app.tasks.providers","List enabled task provider IDs and labels, without returning connection settings or credentials.",Schema(),Output(),true,
            Handler:async(_,token)=>await Ui(()=>new{providers=model.GetActiveWorkItemPlugins().Select(plugin=>new{plugin.Provider.Id,plugin.Provider.Name,plugin.Provider.Description}).ToArray()},token)));
        var search=GuardFields();search["provider"]=Text(128);search["query"]=Text();
        engine.Add(new("app.tasks.search","Search an enabled task provider using its host-configured project connection. Read NativeWorkItemSearch with app.table.read for results; connection trust/credentials cannot be supplied by the model.",Schema(search,"provider","query","stateToken","target"),Output(),true,Network:true,
            Handler:(args,token)=>Mutate(args,"pullrequests",async()=>{
                using var cancellation=token.Register(()=>NativePullRequestActions.Cancel(model));
                await NativePullRequestActions.ExecuteAsync(model,JsonSerializer.SerializeToElement(new{command="taskSearch",provider=String(args,"provider"),query=String(args,"query")},Json),adapter.AutomationList,token);
                token.ThrowIfCancellationRequested();return new{completed=true,count=adapter.AutomationList("NativeWorkItemSearch")?.Count??0};
            },token),RequiredCapabilities:AutomationCapability.Navigation,ConfirmationRequired:true,Available:()=>model.HasWorkItemPlugins));
        var link=GuardFields();link["keys"]=new(){["type"]="array",["items"]=Text(),["minItems"]=1,["maxItems"]=100};link["destination"]=Choice(["pullrequest","commit"]);link["prefixTitle"]=new(){["type"]="boolean"};
        engine.Add(new("app.tasks.link","Add exact task-search results to the existing PR or commit draft. Does not publish or change tracker status. Prepare context for changes when destination=commit, otherwise pullrequests; keys must come from NativeWorkItemSearch.",Schema(link,"keys","destination","stateToken","target"),Output(),false,
            Handler:(args,token)=>Mutate(args,String(args,"destination")=="commit"?"changes":"pullrequests",()=>{
                var results=(adapter.AutomationList("NativeWorkItemSearch")??Array.Empty<WorkItemReference>()).Cast<WorkItemReference>().ToArray();
                var tasks=args.GetProperty("keys").EnumerateArray().Select(key=>results.SingleOrDefault(item=>StableKey(item)==key.GetString())??throw new AutomationException("StaleState","Read task results again before linking.")).Distinct().ToArray();
                if(String(args,"destination")=="commit")model.AddWorkItemReferencesToCommit(tasks);
                else model.AddWorkItemReferencesToPullRequest(tasks,args.TryGetProperty("prefixTitle",out var prefix)&&prefix.GetBoolean());
                return Task.FromResult<object>(new{linked=tasks.Length,status=Redact(model.StatusMessage)});
            },token),RequiredCapabilities:AutomationCapability.Navigation|AutomationCapability.Administration,Available:()=>model.HasWorkItemPlugins));
    }
}
