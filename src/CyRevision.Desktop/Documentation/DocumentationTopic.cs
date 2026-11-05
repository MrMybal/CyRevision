namespace CyRevision.Desktop.Documentation;

public sealed record DocumentationTopic(
    string Id,
    string Category,
    string Title,
    string Summary,
    string Body,
    IReadOnlyList<string>? Keywords,
    IReadOnlyList<string>? Workspaces = null)
{
    public string Language { get; init; } = "en";
}
