using CyRevision.Desktop.Workspace;

namespace CyRevision.Core.Tests;

public sealed class WorkspaceLoadCoordinatorTests
{
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
