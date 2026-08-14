namespace CyRevision.Desktop.ViewModels;

public sealed record PerformanceMetricViewModel(
    DateTimeOffset Timestamp,
    string Area,
    string Operation,
    string Duration,
    string Detail)
{
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}
