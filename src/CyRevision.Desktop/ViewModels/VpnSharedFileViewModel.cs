using CyRevision.Vpn;

namespace CyRevision.Desktop.ViewModels;

public sealed class VpnSharedFileViewModel
{
    public VpnSharedFileViewModel(VpnSharedFile file) => File = file;

    public VpnSharedFile File { get; }

    public string RelativePath => File.RelativePath;

    public string Size => FormatSize(File.Size);

    public string Modified => File.ModifiedAt.ToLocalTime().ToString("g");

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
