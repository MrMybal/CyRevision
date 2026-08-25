using CyRevision.Desktop.ViewModels;

namespace CyRevision.Core.Tests;

public sealed class OperationTaskViewModelTests
{
    [Fact]
    public void Complete_PreservesFailureForTheActivityHistory()
    {
        OperationTaskViewModel task = new("Refresh repository", "Demo", "Loading");

        task.Complete("Failed", "Git returned exit code 1");

        Assert.Equal("Failed", task.State);
        Assert.Equal("Git returned exit code 1", task.Detail);
        Assert.True(task.IsAttention);
        Assert.False(task.IsRunning);
        Assert.NotNull(task.CompletedAt);
        Assert.NotEmpty(task.DurationText);
    }

    [Fact]
    public void Complete_MarksCancellationAsNonAttention()
    {
        OperationTaskViewModel task = new("Refresh repository", "Demo");

        task.Complete("Cancelled", "Superseded by a newer request");

        Assert.False(task.IsAttention);
        Assert.Equal("Cancelled", task.State);
    }

    [Fact]
    public void Complete_MarksSuccessfulTaskAsNonAttention()
    {
        OperationTaskViewModel task = new("Refresh repository", "Demo");

        task.Complete("Completed", "Repository refreshed");

        Assert.False(task.IsAttention);
        Assert.Equal("Completed", task.State);
    }
}
