using System.Text.Json;
using CyRevision.Desktop.Native;
using CyRevision.PullRequests;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Core.Tests;

public sealed class NativeReviewActionTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"confirmed\":false}")]
    [InlineData("{\"confirmed\":\"true\"}")]
    public void MutationsRequireAnActualConfirmationBoolean(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() => NativePullRequestActions.ValidateConfirmation(document.RootElement));
    }

    [Fact]
    public void ConfirmedMutationsPassTheirGuard()
    {
        using JsonDocument document = JsonDocument.Parse("{\"confirmed\":true}");
        NativePullRequestActions.ValidateConfirmation(document.RootElement);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"runId\":41}")]
    [InlineData("{\"runId\":\"42\"}")]
    [InlineData("{\"runId\":null}")]
    public void RunActionsRejectStaleOrMissingIdentity(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() => NativePullRequestActions.RequireRun(Run(), document.RootElement));
    }

    [Fact]
    public void RunActionsAcceptTheSelectedRunIdentity()
    {
        using JsonDocument document = JsonDocument.Parse("{\"runId\":42}");
        CiWorkflowRun run = Run();
        Assert.Same(run, NativePullRequestActions.RequireRun(run,document.RootElement));
        Assert.Throws<InvalidOperationException>(() => NativePullRequestActions.RequireRun(null,document.RootElement));
    }

    [Fact]
    public void CommandRoutingIsAnExplicitAllowlist()
    {
        Assert.True(NativePullRequestActions.Handles("prMerge"));
        Assert.True(NativePullRequestActions.Handles("prCiCancel"));
        Assert.True(NativePullRequestActions.Handles("taskSearch"));
        Assert.False(NativePullRequestActions.Handles("DeleteEverything"));
        Assert.False(NativePullRequestActions.Handles(""));
    }

    [Fact]
    public void ActiveJobStateOverridesAnObsoleteSuccessfulRun()
    {
        CiWorkflowJob[] jobs = [new(3,"Compile","in_progress","",DateTimeOffset.UtcNow,null,null,[])];
        CiWorkflowRun result = CiStatePresentation.ApplyJobState(Run(),jobs);
        Assert.True(result.IsRunning);
        Assert.Equal("in_progress",result.Status);
        Assert.Empty(result.Conclusion);
    }

    [Fact]
    public void TaskConnectionSnapshotsUseOnlyTheirExplicitConfigurationFields()
    {
        WorkItemConnectionSettings settings = new(Guid.NewGuid(),"https://example.invalid","sample-list","sample-account","EXAMPLE_TOKEN");
        string json = JsonSerializer.Serialize(NativePullRequestActions.DescribeTaskSettings(settings));
        using JsonDocument document = JsonDocument.Parse(json);
        string[] names = document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "AccountName","BaseUrl","ScopeId","TokenEnvironmentVariable" },names);
        Assert.Null(NativePullRequestActions.DescribeTaskSettings(null));
    }

    private static CiWorkflowRun Run() => new(42,"Compile","pull_request","completed","success","feature/test","abcdef",
        DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,new Uri("https://example.invalid/actions/runs/42"));
}
