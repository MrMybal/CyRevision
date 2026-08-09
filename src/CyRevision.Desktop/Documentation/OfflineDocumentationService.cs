using System.Text.Json;

namespace CyRevision.Desktop.Documentation;

public sealed class OfflineDocumentationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _documentationDirectory;

    public OfflineDocumentationService(string documentationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationDirectory);
        _documentationDirectory = Path.GetFullPath(documentationDirectory);
    }

    public IReadOnlyList<DocumentationTopic> Load(string languageCode)
    {
        string normalizedLanguage = string.IsNullOrWhiteSpace(languageCode)
            ? "en"
            : languageCode.Trim().ToLowerInvariant();
        IReadOnlyList<DocumentationTopic> topics = ReadCatalog(normalizedLanguage);
        if (topics.Count == 0 && normalizedLanguage != "en")
        {
            topics = ReadCatalog("en");
        }

        return topics.Count == 0 ? BuiltInFallback : topics;
    }

    private IReadOnlyList<DocumentationTopic> ReadCatalog(string languageCode)
    {
        string path = Path.Combine(_documentationDirectory, languageCode + ".json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            DocumentationTopic[]? topics = JsonSerializer.Deserialize<DocumentationTopic[]>(
                File.ReadAllText(path),
                JsonOptions);
            if (topics is null)
            {
                return [];
            }

            return topics
                .Where(IsValid)
                .GroupBy(topic => topic.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsValid(DocumentationTopic topic) =>
        !string.IsNullOrWhiteSpace(topic.Id) &&
        !string.IsNullOrWhiteSpace(topic.Title) &&
        !string.IsNullOrWhiteSpace(topic.Body);

    private static IReadOnlyList<DocumentationTopic> BuiltInFallback { get; } =
    [
        new(
            "documentation-unavailable",
            "Help",
            "Offline documentation",
            "The external documentation catalog could not be loaded.",
            "CyRevision remains fully usable. Reinstall or republish the application to restore the local Documentation folder.",
            ["help", "documentation", "offline"])
    ];
}
