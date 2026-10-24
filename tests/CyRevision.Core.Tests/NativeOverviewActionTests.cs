using System.Text.Json;
using CyRevision.Desktop.Native;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Core.Tests;

public sealed class NativeOverviewActionTests
{
    [Fact]
    public void InitializationPreservesAnExistingGitRepository()
    {
        using TemporaryFolder folder=new();
        Directory.CreateDirectory(Path.Combine(folder.Path,".git"));
        string source=Path.Combine(folder.Path,"source.cpp");File.WriteAllText(source,"existing content");
        Assert.Throws<InvalidOperationException>(()=>NativeOverviewActions.ValidateInitialization(folder.Path,GitInitializationOptions.Default,false));
        Assert.Equal("existing content",File.ReadAllText(source));
        NativeOverviewActions.ValidateInitialization(folder.Path,GitInitializationOptions.Default,true);
    }

    [Fact]
    public void InitializationNeverReplacesAnExistingGitIgnore()
    {
        using TemporaryFolder folder=new();
        string ignore=Path.Combine(folder.Path,".gitignore");File.WriteAllText(ignore,"custom-generated/\n");
        GitInitializationOptions options=GitInitializationOptions.Default with{CreateGitIgnore=true,GitIgnoreContent="different/\n"};
        Assert.Throws<InvalidOperationException>(()=>NativeOverviewActions.ValidateInitialization(folder.Path,options,false));
        Assert.Equal("custom-generated/\n",File.ReadAllText(ignore));
    }

    [Theory]
    [InlineData("Sample","")]
    [InlineData("","sample@example.invalid")]
    public void ALocalIdentityRequiresBothFields(string name,string email)
    {
        using TemporaryFolder folder=new();
        Assert.Throws<ArgumentException>(()=>NativeOverviewActions.ValidateInitialization(folder.Path,
            GitInitializationOptions.Default with{UserName=name,UserEmail=email},false));
    }

    [Fact]
    public void InitialCommitRequiresItsMessage()
    {
        using TemporaryFolder folder=new();
        Assert.Throws<ArgumentException>(()=>NativeOverviewActions.ValidateInitialization(folder.Path,
            GitInitializationOptions.Default with{CreateInitialCommit=true,InitialCommitMessage="  "},false));
    }

    [Fact]
    public void OptionsNormalizeAndDeduplicatePatterns()
    {
        using JsonDocument request=JsonDocument.Parse("{\"lfsPatterns\":[\"*.uasset\",\"*.UASSET\",\"Assets\\\\*.png\"],\"createGitIgnore\":true,\"gitIgnoreContent\":\"cache/\",\"initialCommitMessage\":\"Initial\"}");
        GitInitializationOptions options=NativeOverviewActions.ReadInitializationOptions(request.RootElement);
        Assert.Equal(new[]{"*.uasset","Assets/*.png"},options.LfsPatterns);
        Assert.True(options.CreateGitIgnore);
        Assert.False(options.CreateInitialCommit);
    }

    [Theory]
    [InlineData("{\"lfsPatterns\":[\"*.asset injected\"]}")]
    [InlineData("{\"lfsPatterns\":[\"# comment\"]}")]
    [InlineData("{\"lfsPatterns\":[\"*.asset\\n*.bin\"]}")]
    public void OptionsRejectInvalidLfsPatterns(string json)
    {
        using JsonDocument request=JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(()=>NativeOverviewActions.ReadInitializationOptions(request.RootElement));
    }

    [Fact]
    public void LfsOnlyRequiresAnExistingRepository()
    {
        using TemporaryFolder folder=new();
        Assert.Throws<InvalidOperationException>(()=>NativeOverviewActions.ValidateInitialization(folder.Path,GitInitializationOptions.Default,true));
    }

    private sealed class TemporaryFolder:IDisposable
    {
        public string Path {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CyRevision-native-init-tests-"+Guid.NewGuid().ToString("N"));
        public TemporaryFolder()=>Directory.CreateDirectory(Path);
        public void Dispose()=>Directory.Delete(Path,recursive:true);
    }
}
