using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyRevision.Desktop.Documentation;

/// <summary>Host adapter for the packaged CyDoc manifest; CyDoc owns rendering and reading state.</summary>
public sealed class OfflineDocumentationService
{
    private readonly string _directory;
    private readonly IReadOnlyList<CyDocPage> _pages;
    private readonly Dictionary<string, string> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    public bool IsAvailable => _pages.Count > 0 && File.Exists(IndexFile);
    public string IndexFile => Path.Combine(_directory, "Documentation.html");
    public int PageCount => _pages.Select(page => page.Id).Distinct().Count();
    public IReadOnlyList<string> Modules { get; }

    public OfflineDocumentationService(string documentationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationDirectory);
        _directory = Path.GetFullPath(documentationDirectory);
        List<CyDocPage> pages = [];
        try
        {
            string manifest = Path.Combine(_directory, "DocData", "manifest.json");
            if (File.Exists(manifest) && new FileInfo(manifest).Length <= 32 * 1024 * 1024)
            {
                using var data = JsonDocument.Parse(File.ReadAllText(manifest));
                if (data.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new JsonException("Unsupported CyDoc schema.");
                foreach (var page in data.RootElement.GetProperty("pages").EnumerateArray())
                {
                    string Get(string key) => page.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                    var anchors = new HashSet<string>(StringComparer.Ordinal);
                    if (page.TryGetProperty("headings", out var headings))
                        foreach (var heading in headings.EnumerateArray()) anchors.Add(heading.GetProperty("id").GetString()!);
                    if (page.TryGetProperty("anchors", out var additional))
                        foreach (var anchor in additional.EnumerateArray()) anchors.Add(anchor.GetString()!);
                    pages.Add(new(Get("id"), Get("locale"), Get("section"), Get("title"), Get("description"), Get("text"), Get("html"), anchors));
                }
                if (data.RootElement.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Object)
                    foreach (var alias in aliases.EnumerateObject()) _aliases[alias.Name] = alias.Value.GetString()!;
            }
            string routeFile = Path.Combine(_directory, "DocData", "routes.json");
            if (File.Exists(routeFile))
                foreach (var route in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(routeFile)) ?? [])
                    _routes[route.Key] = route.Value;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        { pages.Clear(); }
        _pages = pages;
        Modules = pages.Count == 0 ? [] : new[] { "image", "gallery", "video", "gif", "mesh", "graph" }
            .Where(module => File.Exists(Path.Combine(_directory, "DocData", "media-" + module + ".js"))).ToArray();
    }

    public IReadOnlyList<DocumentationTopic> Load(string languageCode)
    {
        string language = Language(languageCode);
        var topics = _pages.GroupBy(page => page.Id).Select(group =>
        {
            var page = group.FirstOrDefault(page => page.Locale == language)
                ?? group.FirstOrDefault(page => page.Locale == "en") ?? group.First();
            return new DocumentationTopic(page.Id, page.Section, page.Title, page.Description, page.Text, [],
                Workspaces: _routes.Where(route => route.Value == page.Id).Select(route => route.Key).ToArray())
                { Language = page.Locale };
        }).ToArray();
        return topics.Length > 0 ? topics : [new("documentation-unavailable", "Help", "CyDoc documentation", "The local reader is unavailable.",
            "Reinstall the application to restore its bundled Documentation directory.", [])];
    }

    public string CreateLink(string reference, string languageCode, string? anchor = null, string? mediaId = null)
    {
        if (!IsAvailable) throw new InvalidOperationException("The local CyDoc reader is unavailable. Reinstall the application.");
        string id = string.IsNullOrWhiteSpace(reference) ? "getting-started" : reference;
        if (id.StartsWith("doc:", StringComparison.Ordinal)) id = id[4..];
        var split = id.Split('#', 2); id = split[0];
        if (split.Length == 2 && string.IsNullOrEmpty(anchor)) anchor = split[1];
        id = _routes.GetValueOrDefault(id) ?? id;
        HashSet<string> visited = new(StringComparer.Ordinal);
        while (_aliases.TryGetValue(id, out var destination))
        {
            if (!visited.Add(id)) throw new InvalidOperationException("Cyclic documentation alias.");
            var target = destination.Split('#', 2); id = target[0];
            if (target.Length == 2 && string.IsNullOrEmpty(anchor)) anchor = target[1];
        }
        string locale = Language(languageCode);
        var page = _pages.FirstOrDefault(page => page.Id == id && page.Locale == locale)
            ?? _pages.FirstOrDefault(page => page.Id == id && page.Locale == "en")
            ?? _pages.FirstOrDefault(page => page.Id == id)
            ?? throw new ArgumentException("Unknown documentation page.");
        if (!string.IsNullOrEmpty(anchor) && !page.Anchors.Contains(anchor)) throw new ArgumentException("Unknown documentation section.");
        if (!string.IsNullOrEmpty(mediaId) && !Regex.Matches(page.Html, "data-cydoc-id=\"([^\"]+)\"").Any(match => match.Groups[1].Value == mediaId))
            throw new ArgumentException("Unknown documentation media.");
        string url = new Uri(IndexFile).AbsoluteUri + "#/" + Uri.EscapeDataString(page.Locale) + "/" + Uri.EscapeDataString(page.Id);
        if (!string.IsNullOrEmpty(anchor)) url += "/" + Uri.EscapeDataString(anchor);
        if (!string.IsNullOrEmpty(mediaId)) url += "?media=" + Uri.EscapeDataString(mediaId);
        return url;
    }

    private static string Language(string? code) => code?.Split('-', '_')[0].Equals("fr", StringComparison.OrdinalIgnoreCase) == true ? "fr" : "en";
    private sealed record CyDocPage(string Id, string Locale, string Section, string Title, string Description, string Text, string Html, HashSet<string> Anchors);
}
