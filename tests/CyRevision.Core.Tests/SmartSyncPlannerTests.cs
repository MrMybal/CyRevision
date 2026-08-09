using CyRevision.Core.Configuration;
using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SmartSyncPlannerTests
{
    [Fact]
    public void PlannerPrioritizesCurrentHistoryAndMissingObjectsWithoutStartingAnEngine()
    {
        ProjectFeatures features = new(
            GitEnabled: true,
            LfsEnabled: true,
            PeerSyncEnabled: true,
            BackupEnabled: true,
            StandardGitRemoteEnabled: false);
        SmartSyncInventory inventory = new(
            CommitCount: 120,
            WorkingFileCount: 7,
            CurrentLfsObjectCount: 14,
            CurrentLfsBytes: 8L * 1024 * 1024 * 1024,
            MissingCurrentLfsObjectCount: 2,
            HistoricalLfsObjectCount: 80,
            HistoricalLfsBytes: 90L * 1024 * 1024 * 1024,
            BackupSnapshotCount: 12);

        SmartSyncPlan plan = new SmartSyncPlanner().Build(
            features,
            inventory,
            new SmartSyncPolicy(LfsHistoryTransferMode.OnDemand, 3, ReplicateBackups: false));

        Assert.Equal(SmartSyncPriority.Critical, plan.Items[0].Priority);
        Assert.Contains(plan.Items, item => item.Content == "Current LFS objects" && item.Priority == SmartSyncPriority.High);
        Assert.Contains(plan.Items, item => item.Content == "Missing current LFS objects" && item.Priority == SmartSyncPriority.Critical);
        Assert.Contains(plan.Items, item => item.Content == "Historical LFS objects" && item.Priority == SmartSyncPriority.OnDemand);
        Assert.Contains(plan.Items, item => item.Content == "Backup snapshots" && item.Priority == SmartSyncPriority.OnDemand);
        Assert.True(plan.DeferredItemCount >= 92);
    }
}
