namespace CyRevision.Sync;

public enum SyncthingFolderMode
{
    SendReceive,
    SendOnly,
    ReceiveOnly
}

public static class SyncthingFolderModeExtensions
{
    public static string ToApiValue(this SyncthingFolderMode mode) => mode switch
    {
        SyncthingFolderMode.SendOnly => "sendonly",
        SyncthingFolderMode.ReceiveOnly => "receiveonly",
        _ => "sendreceive"
    };

    public static SyncthingFolderMode ParseApiValue(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "sendonly" => SyncthingFolderMode.SendOnly,
        "receiveonly" => SyncthingFolderMode.ReceiveOnly,
        _ => SyncthingFolderMode.SendReceive
    };

    public static string ToDisplayName(this SyncthingFolderMode mode) => mode switch
    {
        SyncthingFolderMode.SendOnly => "Send only",
        SyncthingFolderMode.ReceiveOnly => "Receive only",
        _ => "Send & receive"
    };
}
