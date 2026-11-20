using System.Text.Json;
using System.Text.Json.Nodes;
using CyRevision.Automation;

namespace CyRevision.Core.Tests;

public sealed class LocalMcpAccessTests
{
    [Theory]
    [InlineData(LocalMcpAccessProfile.Free,false,false)]
    [InlineData(LocalMcpAccessProfile.LocalControlled,false,true)]
    [InlineData(LocalMcpAccessProfile.TokenOnly,true,false)]
    [InlineData(LocalMcpAccessProfile.Protected,true,true)]
    public void ProfilesKeepAuthenticationIndependentOfOperationControls(LocalMcpAccessProfile profile,bool token,bool controls)
    {
        Assert.Equal(token,profile.RequiresToken()); Assert.Equal(controls,profile.ControlsOperations());
        Assert.Equal(profile,LocalMcpAccess.Parse(profile.Id()));
    }

    [Fact]
    public void MissingChoiceMigratesToFreeWithoutLosingOtherSettings()
    {
        string root=Temporary();
        try {
            string path=Path.Combine(root,"access.json");
            Assert.Equal(LocalMcpAccessProfile.Free,LocalMcpAccessStore.LoadOrMigrate(path));
            File.WriteAllText(path,"{\"otherSetting\":42}");
            Assert.Equal(LocalMcpAccessProfile.Free,LocalMcpAccessStore.LoadOrMigrate(path));
            var document=JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal(42,document["otherSetting"]!.GetValue<int>());
            Assert.Equal("free",document["accessProfile"]!.GetValue<string>());
        } finally {Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData("local_controlled",LocalMcpAccessProfile.LocalControlled)]
    [InlineData("token_only",LocalMcpAccessProfile.TokenOnly)]
    [InlineData("protected_access",LocalMcpAccessProfile.Protected)]
    public void ExplicitProfilesSurviveMigrationAndRestart(string choice,LocalMcpAccessProfile expected)
    {
        string root=Temporary();
        try {
            string path=Path.Combine(root,"access.json");
            string original="{\"localAccessProfile\":\""+choice+"\",\"otherSetting\":42}";
            File.WriteAllText(path,original);
            Assert.Equal(expected,LocalMcpAccessStore.LoadOrMigrate(path));
            Assert.Equal(original,File.ReadAllText(path));
            LocalMcpAccessStore.Save(path,expected);
            Assert.Equal(expected,LocalMcpAccessStore.LoadOrMigrate(path));
        } finally {Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData("{\"accessProfile\":\"unknown\"}")]
    [InlineData("{invalid}")]
    [InlineData("{\"accessProfile\":42}")]
    public void InvalidExplicitChoiceNeverSilentlyBecomesFree(string contents)
    {
        string root=Temporary();
        try {
            string path=Path.Combine(root,"access.json");File.WriteAllText(path,contents);
            Assert.ThrowsAny<Exception>(()=>LocalMcpAccessStore.LoadOrMigrate(path));
            Assert.Equal(contents,File.ReadAllText(path));
        } finally {Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData(LocalMcpAccessProfile.Free)]
    [InlineData(LocalMcpAccessProfile.TokenOnly)]
    public async Task UncontrolledProfilesNeedNeitherGrantsNorConfirmationButRetainScope(LocalMcpAccessProfile profile)
    {
        string root=Temporary();
        try {
            var permissions=new AutomationPermissions {AccessProfile=profile,Read=false,ConfirmAsync=(_,_,_)=>throw new InvalidOperationException("Must not confirm")};
            var engine=new RevisionAutomationEngine(root,Path.Combine(root,"private"),permissions);int calls=0;
            engine.Add(new("test.change","Change",new JsonObject(),new JsonObject(),false,Network:true,Stage:true,Commit:true,
                Handler:(_,_)=>{calls++;return Task.FromResult<object>(new{});},RequiredCapabilities:(AutomationCapability)63));
            await engine.ExecuteAsync("test.change",JsonSerializer.SerializeToElement(new{}),default);
            Assert.Equal(1,calls);
            permissions.ScopeValid=()=>false; Assert.False(engine.IsAllowed("test.change"));
            permissions.ScopeValid=()=>true;permissions.Blocked=true;Assert.False(engine.IsAllowed("test.change"));
            permissions.Blocked=false;permissions.GitEnabled=()=>false;Assert.False(engine.IsAllowed("git.status"));
        } finally {Directory.Delete(root,true);}
    }
    private static string Temporary(){string root=Path.Combine(Path.GetTempPath(),"cyrevision-local-access",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);return root;}
}
