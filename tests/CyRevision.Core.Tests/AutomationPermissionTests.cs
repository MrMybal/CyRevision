using System.Text.Json;
using System.Text.Json.Nodes;
using CyRevision.Automation;

namespace CyRevision.Core.Tests;

public sealed class AutomationPermissionTests
{
    [Fact]
    public async Task ReadOnlyUiStateChangesStillRequireTheHostGrantAndConfirmation()
    {
        string root=Path.Combine(Path.GetTempPath(),"cyrevision-automation-permissions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            int calls=0,confirmations=0;var permissions=new AutomationPermissions();var engine=new RevisionAutomationEngine(root,Path.Combine(root,"private"),permissions);
            engine.Add(new("test.navigate","Test navigation",new JsonObject(),new JsonObject(),true,Handler:(_,_)=>{calls++;return Task.FromResult<object>(new{});},RequiredCapabilities:AutomationCapability.Navigation,ConfirmationRequired:true));
            var args=JsonSerializer.SerializeToElement(new{});
            Assert.Equal("AccessDenied",(await Assert.ThrowsAsync<AutomationException>(()=>engine.ExecuteAsync("test.navigate",args,default))).Code);
            permissions.Capabilities=AutomationCapability.Navigation;
            Assert.Equal("ConfirmationRequired",(await Assert.ThrowsAsync<AutomationException>(()=>engine.ExecuteAsync("test.navigate",args,default))).Code);
            permissions.ConfirmAsync=(_,_,_)=>{confirmations++;permissions.Capabilities=0;return Task.FromResult(true);};
            Assert.Equal("AccessDenied",(await Assert.ThrowsAsync<AutomationException>(()=>engine.ExecuteAsync("test.navigate",args,default))).Code);
            Assert.Equal(0,calls);Assert.Equal(1,confirmations);
            permissions.Capabilities=AutomationCapability.Navigation;permissions.ConfirmAsync=(_,_,_)=>Task.FromResult(true);
            await engine.ExecuteAsync("test.navigate",args,default);Assert.Equal(1,calls);
        }finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task DisabledModeOrPluginCannotBeBypassedWithWriteGrants()
    {
        string root=Path.Combine(Path.GetTempPath(),"cyrevision-automation-permissions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            var permissions=new AutomationPermissions{Write=true,Network=true,Capabilities=(AutomationCapability)63,ConfirmAsync=(_,_,_)=>Task.FromResult(true)};
            var engine=new RevisionAutomationEngine(root,Path.Combine(root,"private"),permissions);
            engine.Add(new("test.disabled","Disabled workflow",new JsonObject(),new JsonObject(),false,Handler:(_,_)=>throw new InvalidOperationException("Must not run"),Available:()=>false));
            Assert.False(engine.IsAllowed("test.disabled"));
            Assert.Equal("AccessDenied",(await Assert.ThrowsAsync<AutomationException>(()=>engine.ExecuteAsync("test.disabled",JsonSerializer.SerializeToElement(new{}),default))).Code);
        }finally{Directory.Delete(root,true);}
    }
}
