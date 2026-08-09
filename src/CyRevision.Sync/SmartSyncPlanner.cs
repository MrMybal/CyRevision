using CyRevision.Core.Configuration;

namespace CyRevision.Sync;

public enum LfsHistoryTransferMode
{
    OnDemand,
    RecentVersions,
    Everything
}

public enum SmartSyncPriority
{
    Critical,
    High,
    Normal,
    Archive,
    OnDemand
}

public sealed record SmartSyncPolicy(
    LfsHistoryTransferMode LfsHistoryMode = LfsHistoryTransferMode.OnDemand,
    int RecentLfsVersionCount = 3,
    bool ReplicateBackups = false)
{
    public void Validate()
    {
        if (RecentLfsVersionCount is < 1 or > 100)
        {
            throw new InvalidOperationException("The number of recent LFS versions must be between 1 and 100.");
        }
    }
}

public sealed record SmartSyncInventory(
    int CommitCount,
    int WorkingFileCount,
    int CurrentLfsObjectCount,
    long CurrentLfsBytes,
    int MissingCurrentLfsObjectCount,
    int HistoricalLfsObjectCount,
    long HistoricalLfsBytes,
    int BackupSnapshotCount);

public sealed record SmartSyncPlanItem(
    string Content,
    string Strategy,
    SmartSyncPriority Priority,
    int ItemCount,
    long EstimatedBytes,
    string Reason)
{
    public string SizeText => FormatSize(EstimatedBytes);
    public string CountText => ItemCount.ToString("N0");

    private static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, size);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

public sealed record SmartSyncPlan(
    IReadOnlyList<SmartSyncPlanItem> Items,
    int ImmediateItemCount,
    long ImmediateBytes,
    int DeferredItemCount);

public sealed class SmartSyncPlanner
{
    public SmartSyncPlan Build(
        ProjectFeatures features,
        SmartSyncInventory inventory,
        SmartSyncPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(policy);
        features.Validate();
        policy.Validate();
        List<SmartSyncPlanItem> items = [];

        if (features.GitEnabled)
        {
            items.Add(new SmartSyncPlanItem(
                "Git revisions and refs",
                features.PeerSyncEnabled ? "Signed peer exchange" : "Local only",
                SmartSyncPriority.Critical,
                inventory.CommitCount,
                0,
                "Small metadata is exchanged before large assets so history becomes usable first."));
        }

        if (features.PeerSyncEnabled)
        {
            items.Add(new SmartSyncPlanItem(
                "Current working state",
                "Continuous current-state sync",
                SmartSyncPriority.High,
                inventory.WorkingFileCount,
                0,
                "Only the CyRevision-owned sync profile participates."));
        }

        if (features.LfsEnabled)
        {
            items.Add(new SmartSyncPlanItem(
                "Current LFS objects",
                features.PeerSyncEnabled ? "Eager peer replication" : "Local inventory",
                SmartSyncPriority.High,
                inventory.CurrentLfsObjectCount,
                inventory.CurrentLfsBytes,
                "Objects required by the current revision have priority."));
            if (inventory.MissingCurrentLfsObjectCount > 0)
            {
                items.Add(new SmartSyncPlanItem(
                    "Missing current LFS objects",
                    "Authorized peer or archive request",
                    SmartSyncPriority.Critical,
                    inventory.MissingCurrentLfsObjectCount,
                    0,
                    "CyRevision reports missing content but never downloads it silently."));
            }

            SmartSyncPriority historyPriority = policy.LfsHistoryMode switch
            {
                LfsHistoryTransferMode.Everything => SmartSyncPriority.Normal,
                LfsHistoryTransferMode.RecentVersions => SmartSyncPriority.Archive,
                _ => SmartSyncPriority.OnDemand
            };
            string historyStrategy = policy.LfsHistoryMode switch
            {
                LfsHistoryTransferMode.Everything => "Replicate every available version",
                LfsHistoryTransferMode.RecentVersions => $"Keep the {policy.RecentLfsVersionCount} most recent versions",
                _ => "Fetch only when previewed, exported, or restored"
            };
            items.Add(new SmartSyncPlanItem(
                "Historical LFS objects",
                historyStrategy,
                historyPriority,
                inventory.HistoricalLfsObjectCount,
                inventory.HistoricalLfsBytes,
                "Old heavy assets are separated from current project availability."));
        }

        if (features.BackupEnabled)
        {
            items.Add(new SmartSyncPlanItem(
                "Backup snapshots",
                policy.ReplicateBackups ? "Replicate to backup peer" : "Cold archive / local store",
                policy.ReplicateBackups ? SmartSyncPriority.Archive : SmartSyncPriority.OnDemand,
                inventory.BackupSnapshotCount,
                0,
                "Backups are not mixed with the live working-state synchronization."));
        }

        SmartSyncPlanItem[] ordered = items.OrderBy(item => item.Priority).ToArray();
        SmartSyncPlanItem[] immediate = ordered.Where(item => item.Priority <= SmartSyncPriority.Normal).ToArray();
        return new SmartSyncPlan(
            ordered,
            immediate.Sum(item => item.ItemCount),
            immediate.Sum(item => item.EstimatedBytes),
            ordered.Where(item => item.Priority > SmartSyncPriority.Normal).Sum(item => item.ItemCount));
    }
}
