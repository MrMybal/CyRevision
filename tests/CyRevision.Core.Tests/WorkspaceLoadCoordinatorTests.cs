using CyRevision.Desktop.Workspace;

namespace CyRevision.Core.Tests;

public sealed class WorkspaceLoadCoordinatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidatedLeaseCannotCacheStaleDataOrReleaseNewLoad(bool wholeProject)
    {
        WorkspaceLoadCoordinator coordinator = new();
        Guid project = Guid.NewGuid();
        using var old = coordinator.TryBegin(project, "History")!;
        if (wholeProject) coordinator.InvalidateProject(project);
        else coordinator.Invalidate(project, "History");
        using var current = coordinator.TryBegin(project, "History")!;
        Assert.NotNull(current);
        old.Complete();
        old.Dispose();
        Assert.False(coordinator.IsLoaded(project, "History"));
        Assert.Null(coordinator.TryBegin(project, "History"));
        current.Complete();
        current.Dispose();
        Assert.True(coordinator.IsLoaded(project, "History"));
    }

    [Fact]
    public void CompletedWorkspaceIsCachedPerProject()
    {
        WorkspaceLoadCoordinator coordinator = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        using (WorkspaceLoadCoordinator.WorkspaceLoadLease lease = coordinator.TryBegin(first, "Branches")!)
            lease.Complete();

        Assert.True(coordinator.IsLoaded(first, "Branches"));
        Assert.Null(coordinator.TryBegin(first, "Branches"));
        Assert.NotNull(coordinator.TryBegin(second, "Branches"));
    }

    [Fact]
    public void FailedOrCancelledWorkspaceCanBeRetried()
    {
        WorkspaceLoadCoordinator coordinator = new();
        Guid project = Guid.NewGuid();

        using (coordinator.TryBegin(project, "UnrealBuild")) { }

        Assert.False(coordinator.IsLoaded(project, "UnrealBuild"));
        Assert.NotNull(coordinator.TryBegin(project, "UnrealBuild"));
    }

    [Fact]
    public void InvalidateProjectDoesNotEvictOtherProjects()
    {
        WorkspaceLoadCoordinator coordinator = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        using (WorkspaceLoadCoordinator.WorkspaceLoadLease lease = coordinator.TryBegin(first, "Chat")!) lease.Complete();
        using (WorkspaceLoadCoordinator.WorkspaceLoadLease lease = coordinator.TryBegin(second, "Chat")!) lease.Complete();

        coordinator.InvalidateProject(first);

        Assert.False(coordinator.IsLoaded(first, "Chat"));
        Assert.True(coordinator.IsLoaded(second, "Chat"));
    }
}
