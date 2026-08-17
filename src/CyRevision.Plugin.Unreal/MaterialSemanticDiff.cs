using System.Text;
using System.Text.Json;

namespace CyRevision.Plugin.Unreal;

internal sealed record MaterialSemanticDiffResult(
    string Summary,
    string Text,
    IReadOnlyDictionary<string, string> Metadata);

internal static class MaterialSemanticDiff
{
    private const int MaximumDetailLines = 400;

    public static MaterialSemanticDiffResult? Compare(
        string baselineManifest,
        string candidateManifest,
        string relativePath)
    {
        using JsonDocument baselineDocument = JsonDocument.Parse(baselineManifest);
        using JsonDocument candidateDocument = JsonDocument.Parse(candidateManifest);
        MaterialSnapshot? baseline = ReadSnapshot(baselineDocument.RootElement);
        MaterialSnapshot? candidate = ReadSnapshot(candidateDocument.RootElement);
        if (baseline is null || candidate is null) return null;

        List<string> details = [];
        int settingChanged = 0;
        int parameterAdded = 0;
        int parameterRemoved = 0;
        int parameterChanged = 0;
        int expressionAdded = 0;
        int expressionRemoved = 0;
        int expressionChanged = 0;
        int expressionMoved = 0;

        foreach (string key in baseline.Settings.Keys.Union(candidate.Settings.Keys, StringComparer.Ordinal))
        {
            baseline.Settings.TryGetValue(key, out string? before);
            candidate.Settings.TryGetValue(key, out string? after);
            if (string.Equals(before, after, StringComparison.Ordinal)) continue;
            settingChanged++;
            AddDetail(details, $"~ Setting: {key} [{Display(before)}] -> [{Display(after)}]");
        }

        foreach (string key in candidate.Parameters.Keys.Except(baseline.Parameters.Keys, StringComparer.Ordinal))
        {
            parameterAdded++;
            MaterialParameter parameter = candidate.Parameters[key];
            AddDetail(details, $"+ Parameter: {parameter.DisplayName} = {Display(parameter.Value)}");
        }
        foreach (string key in baseline.Parameters.Keys.Except(candidate.Parameters.Keys, StringComparer.Ordinal))
        {
            parameterRemoved++;
            AddDetail(details, $"- Parameter: {baseline.Parameters[key].DisplayName}");
        }
        foreach (string key in baseline.Parameters.Keys.Intersect(candidate.Parameters.Keys, StringComparer.Ordinal))
        {
            MaterialParameter before = baseline.Parameters[key];
            MaterialParameter after = candidate.Parameters[key];
            if (before == after) continue;
            parameterChanged++;
            AddDetail(details,
                $"~ Parameter: {after.DisplayName} [{Display(before.Value)}] -> [{Display(after.Value)}]" +
                (before.Overridden == after.Overridden ? string.Empty :
                    $" (override {before.Overridden} -> {after.Overridden})"));
        }

        foreach (string key in candidate.Expressions.Keys.Except(baseline.Expressions.Keys, StringComparer.Ordinal))
        {
            expressionAdded++;
            AddDetail(details, $"+ Expression: {candidate.Expressions[key].DisplayName}");
        }
        foreach (string key in baseline.Expressions.Keys.Except(candidate.Expressions.Keys, StringComparer.Ordinal))
        {
            expressionRemoved++;
            AddDetail(details, $"- Expression: {baseline.Expressions[key].DisplayName}");
        }
        foreach (string key in baseline.Expressions.Keys.Intersect(candidate.Expressions.Keys, StringComparer.Ordinal))
        {
            MaterialExpression before = baseline.Expressions[key];
            MaterialExpression after = candidate.Expressions[key];
            if (before.X != after.X || before.Y != after.Y)
            {
                expressionMoved++;
                AddDetail(details,
                    $"~ Moved: {after.DisplayName} ({before.X}, {before.Y}) -> ({after.X}, {after.Y})");
            }
            if (!string.Equals(before.Class, after.Class, StringComparison.Ordinal) ||
                !string.Equals(before.Description, after.Description, StringComparison.Ordinal) ||
                !DictionaryEqual(before.Properties, after.Properties))
            {
                expressionChanged++;
                AddDetail(details, $"~ Expression properties: {after.DisplayName}");
            }
        }

        HashSet<string> baselineConnections = BuildConnections(baseline);
        HashSet<string> candidateConnections = BuildConnections(candidate);
        string[] connectionsAdded = candidateConnections.Except(baselineConnections, StringComparer.Ordinal).ToArray();
        string[] connectionsRemoved = baselineConnections.Except(candidateConnections, StringComparer.Ordinal).ToArray();
        foreach (string connection in connectionsAdded) AddDetail(details, $"+ Connection: {connection}");
        foreach (string connection in connectionsRemoved) AddDetail(details, $"- Connection: {connection}");

        int semanticChanges = settingChanged + parameterAdded + parameterRemoved + parameterChanged +
                              expressionAdded + expressionRemoved + expressionChanged + expressionMoved +
                              connectionsAdded.Length + connectionsRemoved.Length;
        string summary = semanticChanges == 0
            ? "Material graph is semantically equivalent"
            : $"{semanticChanges} material semantic change(s)";

        StringBuilder text = new();
        text.AppendLine("Material semantic diff");
        text.AppendLine($"File: {relativePath}");
        text.AppendLine($"Settings: ~{settingChanged}");
        text.AppendLine($"Parameters: +{parameterAdded} / -{parameterRemoved} / ~{parameterChanged}");
        text.AppendLine($"Expressions: +{expressionAdded} / -{expressionRemoved} / ~{expressionChanged} / moved {expressionMoved}");
        text.AppendLine($"Connections: +{connectionsAdded.Length} / -{connectionsRemoved.Length}");
        if (baseline.Truncated || candidate.Truncated)
            text.AppendLine("Warning: one graph was truncated by the safety limit.");
        text.AppendLine();
        if (details.Count == 0)
            text.AppendLine("No semantic Material change was detected.");
        else
            foreach (string detail in details) text.AppendLine(detail);
        if (details.Count >= MaximumDetailLines)
            text.AppendLine("… Additional details were omitted.");

        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["Semantic asset type"] = "Material",
            ["Semantic changes"] = semanticChanges.ToString(),
            ["Settings changed"] = settingChanged.ToString(),
            ["Parameters added"] = parameterAdded.ToString(),
            ["Parameters removed"] = parameterRemoved.ToString(),
            ["Parameters changed"] = parameterChanged.ToString(),
            ["Expressions added"] = expressionAdded.ToString(),
            ["Expressions removed"] = expressionRemoved.ToString(),
            ["Expressions changed"] = expressionChanged.ToString(),
            ["Expressions moved"] = expressionMoved.ToString(),
            ["Connections added"] = connectionsAdded.Length.ToString(),
            ["Connections removed"] = connectionsRemoved.Length.ToString()
        };
        return new MaterialSemanticDiffResult(summary, text.ToString(), metadata);
    }

    private static MaterialSnapshot? ReadSnapshot(JsonElement root)
    {
        if (!root.TryGetProperty("material", out JsonElement material) ||
            material.ValueKind != JsonValueKind.Object)
            return null;

        Dictionary<string, string> settings = new(StringComparer.Ordinal)
        {
            ["Blend mode"] = Text(material, "blendMode"),
            ["Shading models"] = Text(material, "shadingModels"),
            ["Two sided"] = Boolean(material, "twoSided").ToString()
        };
        if (material.TryGetProperty("settings", out JsonElement settingsJson))
        {
            foreach (JsonElement setting in settingsJson.EnumerateArray())
                settings[Text(setting, "name")] = Text(setting, "value");
        }

        Dictionary<string, MaterialParameter> parameters = new(StringComparer.Ordinal);
        if (material.TryGetProperty("parameters", out JsonElement parametersJson))
        {
            foreach (JsonElement parameter in parametersJson.EnumerateArray())
            {
                MaterialParameter value = new(
                    Text(parameter, "key"),
                    Text(parameter, "type"),
                    Text(parameter, "name"),
                    Text(parameter, "value"),
                    Boolean(parameter, "overridden"));
                parameters[value.Key] = value;
            }
        }

        Dictionary<string, MaterialExpression> expressions = new(StringComparer.Ordinal);
        if (material.TryGetProperty("expressions", out JsonElement expressionsJson))
        {
            foreach (JsonElement expression in expressionsJson.EnumerateArray())
            {
                Dictionary<string, string> properties = new(StringComparer.Ordinal);
                if (expression.TryGetProperty("properties", out JsonElement propertiesJson))
                {
                    foreach (JsonElement property in propertiesJson.EnumerateArray())
                        properties[Text(property, "name")] = Text(property, "value");
                }
                HashSet<string> references = new(StringComparer.Ordinal);
                if (expression.TryGetProperty("references", out JsonElement referencesJson))
                {
                    foreach (JsonElement reference in referencesJson.EnumerateArray())
                        references.Add(reference.GetString() ?? string.Empty);
                }
                MaterialExpression value = new(
                    Text(expression, "key"),
                    Text(expression, "name"),
                    Text(expression, "class"),
                    Text(expression, "description"),
                    Number(expression, "x"),
                    Number(expression, "y"),
                    properties,
                    references);
                expressions[value.Key] = value;
            }
        }
        return new MaterialSnapshot(settings, parameters, expressions, Boolean(material, "expressionsTruncated"));
    }

    private static HashSet<string> BuildConnections(MaterialSnapshot snapshot)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (MaterialExpression expression in snapshot.Expressions.Values)
        foreach (string reference in expression.References)
        {
            if (!snapshot.Expressions.ContainsKey(reference)) continue;
            result.Add(string.CompareOrdinal(expression.Key, reference) <= 0
                ? $"{expression.Key} <-> {reference}"
                : $"{reference} <-> {expression.Key}");
        }
        return result;
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out string? value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static void AddDetail(List<string> details, string detail)
    {
        if (details.Count < MaximumDetailLines) details.Add(detail);
    }

    private static string Display(string? value) => string.IsNullOrEmpty(value) ? "default" : value;
    private static string Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement property)) return string.Empty;
        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }
    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int value) ? value : 0;
    private static bool Boolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.True;

    private sealed record MaterialSnapshot(
        Dictionary<string, string> Settings,
        Dictionary<string, MaterialParameter> Parameters,
        Dictionary<string, MaterialExpression> Expressions,
        bool Truncated);

    private sealed record MaterialParameter(
        string Key,
        string Type,
        string Name,
        string Value,
        bool Overridden)
    {
        public string DisplayName => $"{Type}: {Name}";
    }

    private sealed record MaterialExpression(
        string Key,
        string Name,
        string Class,
        string Description,
        int X,
        int Y,
        Dictionary<string, string> Properties,
        HashSet<string> References)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Class : $"{Name} ({Class.Split('/').Last()})";
    }
}
