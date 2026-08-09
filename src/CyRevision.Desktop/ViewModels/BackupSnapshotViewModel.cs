using CyRevision.Backup;

namespace CyRevision.Desktop.ViewModels;

public sealed class BackupSnapshotViewModel
{
    public BackupSnapshotViewModel(BackupSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public BackupSnapshot Snapshot { get; }

    public Guid SnapshotId => Snapshot.SnapshotId;

    public string CreatedAt => Snapshot.CreatedAt.ToLocalTime().ToString("g");

    public string LogicalSize => FormatSize(Snapshot.LogicalSizeBytes);

    public string StoredSize => FormatSize(Snapshot.StoredSizeBytes);

    public string ShortId => Snapshot.SnapshotId.ToString("N")[..8];

    private static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
