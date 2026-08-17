using System.Text;
using System.Text.Json;

namespace CyRevision.Plugin.Unreal;

internal sealed record UnrealMetadataSemanticDiffResult(
    string Summary,
    string Text,
    IReadOnlyDictionary<string, string> Metadata);

internal static class UnrealMetadataSemanticDiff
{
    private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "asset",
        "name",
        "class",
        "package",
        "previewResolution",
        "thumbnailWritten",
        "renderAttempted",
        "blueprint",
        "material"
    };

    public static UnrealMetadataSemanticDiffResult? Compare(
        string baselineManifest,
        string candidateManifest,
        string relativePath)
    {
        using JsonDocument baselineDocument = JsonDocument.Parse(baselineManifest);
        using JsonDocument candidateDocument = JsonDocument.Parse(candidateManifest);
        JsonElement baseline = baselineDocument.RootElement;
        JsonElement candidate = candidateDocument.RootElement;
        string kind = Text(baseline, "assetKind");
        if (string.IsNullOrWhiteSpace(kind) ||
            kind is "Blueprint" or "Material" or "Unreal asset" ||
            !string.Equals(kind, Text(candidate, "assetKind"), StringComparison.Ordinal))
            return null;

        Dictionary<string, string> before = ReadFields(baseline);
        Dictionary<string, string> after = ReadFields(candidate);
        List<string> details = [];
        foreach (string key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(key, out string? oldValue);
            after.TryGetValue(key, out string? newValue);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;
            details.Add($"~ {Readable(key)}: [{Display(oldValue)}] -> [{Display(newValue)}]");
        }

        string summary = details.Count == 0
            ? $"{kind} metadata is semantically equivalent"
            : $"{kind} semantic diff · {details.Count} change(s)";
        StringBuilder text = new();
        text.AppendLine($"{kind} semantic diff");
        text.AppendLine($"File: {relativePath}");
        text.AppendLine($"Metadata changes: {details.Count}");
        text.AppendLine();
        if (details.Count == 0)
            text.AppendLine($"No semantic {kind} metadata change was detected.");
        else
            foreach (string detail in details) text.AppendLine(detail);

        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["Semantic asset type"] = kind,
            ["Semantic changes"] = details.Count.ToString(),
            ["Metadata fields compared"] = before.Keys.Union(after.Keys, StringComparer.Ordinal).Count().ToString()
        };
        return new UnrealMetadataSemanticDiffResult(summary, text.ToString().TrimEnd(), metadata);
    }

    private static Dictionary<string, string> ReadFields(JsonElement root)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (IgnoredFields.Contains(property.Name) ||
                property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                continue;
            result[property.Name] = property.Value.ToString();
        }
        return result;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) ? property.GetString() ?? string.Empty : string.Empty;
    private static string Display(string? value) => string.IsNullOrEmpty(value) ? "missing" : value;
    private static string Readable(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : character.ToString()));
}
