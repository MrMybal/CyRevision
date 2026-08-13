namespace CyRevision.Desktop.SystemIntegration;

internal sealed record DesktopBehaviorPreferences(
    bool LaunchAtLogin,
    bool StartHiddenAtLogin,
    bool CloseToTray,
    bool ShowTrayIcon)
{
    public static DesktopBehaviorPreferences Default { get; } = new(
        LaunchAtLogin: true,
        StartHiddenAtLogin: true,
        CloseToTray: true,
        ShowTrayIcon: true);
}

internal enum DesktopBehaviorSetting
{
    LaunchAtLogin,
    StartHiddenAtLogin,
    CloseToTray,
    ShowTrayIcon
}
