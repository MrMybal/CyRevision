using System.Text;
using System.Text.Json;

namespace CyRevision.Plugin.Unreal;

internal sealed record BlueprintSemanticDiffResult(
    string Summary,
    string Text,
    IReadOnlyDictionary<string, string> Metadata);

internal static class BlueprintSemanticDiff
{
    private const int MaximumDetailLines = 400;

    public static BlueprintSemanticDiffResult? Compare(
        string baselineManifest,
        string candidateManifest,
        string relativePath)
    {
        using JsonDocument baselineDocument = JsonDocument.Parse(baselineManifest);
        using JsonDocument candidateDocument = JsonDocument.Parse(candidateManifest);
        BlueprintSnapshot? baseline = ReadSnapshot(baselineDocument.RootElement);
        BlueprintSnapshot? candidate = ReadSnapshot(candidateDocument.RootElement);
        if (baseline is null || candidate is null) return null;

        List<string> details = [];
        int graphAdded = 0;
        int graphRemoved = 0;
        int nodeAdded = 0;
        int nodeRemoved = 0;
        int nodeChanged = 0;
        int nodeMoved = 0;
        int pinChanged = 0;
        int variableAdded = 0;
        int variableRemoved = 0;
        int variableChanged = 0;

        foreach (string key in candidate.Graphs.Keys.Except(baseline.Graphs.Keys, StringComparer.Ordinal))
        {
            graphAdded++;
            AddDetail(details, $"+ Graph: {candidate.Graphs[key].DisplayName}");
        }
        foreach (string key in baseline.Graphs.Keys.Except(candidate.Graphs.Keys, StringComparer.Ordinal))
        {
            graphRemoved++;
            AddDetail(details, $"- Graph: {baseline.Graphs[key].DisplayName}");
        }

        foreach (string graphKey in baseline.Graphs.Keys.Intersect(candidate.Graphs.Keys, StringComparer.Ordinal))
        {
            BlueprintGraph beforeGraph = baseline.Graphs[graphKey];
            BlueprintGraph afterGraph = candidate.Graphs[graphKey];
            foreach (string nodeKey in afterGraph.Nodes.Keys.Except(beforeGraph.Nodes.Keys, StringComparer.Ordinal))
            {
                nodeAdded++;
                AddDetail(details, $"+ Node: {afterGraph.DisplayName} / {afterGraph.Nodes[nodeKey].DisplayName}");
            }
            foreach (string nodeKey in beforeGraph.Nodes.Keys.Except(afterGraph.Nodes.Keys, StringComparer.Ordinal))
            {
                nodeRemoved++;
                AddDetail(details, $"- Node: {beforeGraph.DisplayName} / {beforeGraph.Nodes[nodeKey].DisplayName}");
            }
            foreach (string nodeKey in beforeGraph.Nodes.Keys.Intersect(afterGraph.Nodes.Keys, StringComparer.Ordinal))
            {
                BlueprintNode beforeNode = beforeGraph.Nodes[nodeKey];
                BlueprintNode afterNode = afterGraph.Nodes[nodeKey];
                if (!string.Equals(beforeNode.Class, afterNode.Class, StringComparison.Ordinal) ||
                    !string.Equals(beforeNode.Title, afterNode.Title, StringComparison.Ordinal))
                {
                    nodeChanged++;
                    AddDetail(details,
                        $"~ Node: {afterGraph.DisplayName} / {beforeNode.DisplayName} -> {afterNode.DisplayName}");
                }
                if (beforeNode.X != afterNode.X || beforeNode.Y != afterNode.Y)
                {
                    nodeMoved++;
                    AddDetail(details,
                        $"~ Moved: {afterGraph.DisplayName} / {afterNode.DisplayName} " +
                        $"({beforeNode.X}, {beforeNode.Y}) -> ({afterNode.X}, {afterNode.Y})");
                }

                foreach (string pinKey in beforeNode.Pins.Keys.Union(afterNode.Pins.Keys, StringComparer.Ordinal))
                {
                    beforeNode.Pins.TryGetValue(pinKey, out BlueprintPin? beforePin);
                    afterNode.Pins.TryGetValue(pinKey, out BlueprintPin? afterPin);
                    if (beforePin is null || afterPin is null)
                    {
                        pinChanged++;
                        AddDetail(details,
                            $"{(afterPin is null ? '-' : '+')} Pin: {afterGraph.DisplayName} / " +
                            $"{afterNode.DisplayName} / {(afterPin ?? beforePin)!.DisplayName}");
                        continue;
                    }
                    if (!string.Equals(beforePin.Type, afterPin.Type, StringComparison.Ordinal) ||
                        !string.Equals(beforePin.Default, afterPin.Default, StringComparison.Ordinal))
                    {
                        pinChanged++;
                        AddDetail(details,
                            $"~ Pin: {afterGraph.DisplayName} / {afterNode.DisplayName} / {afterPin.DisplayName} " +
                            $"[{beforePin.Type}; {EmptyLabel(beforePin.Default)}] -> " +
                            $"[{afterPin.Type}; {EmptyLabel(afterPin.Default)}]");
                    }
                }
            }
        }

        foreach (string key in candidate.Variables.Keys.Except(baseline.Variables.Keys, StringComparer.Ordinal))
        {
            variableAdded++;
            AddDetail(details, $"+ Variable: {candidate.Variables[key].DisplayName}");
        }
        foreach (string key in baseline.Variables.Keys.Except(candidate.Variables.Keys, StringComparer.Ordinal))
        {
            variableRemoved++;
            AddDetail(details, $"- Variable: {baseline.Variables[key].DisplayName}");
        }
        foreach (string key in baseline.Variables.Keys.Intersect(candidate.Variables.Keys, StringComparer.Ordinal))
        {
            BlueprintVariable before = baseline.Variables[key];
            BlueprintVariable after = candidate.Variables[key];
            if (before != after)
            {
                variableChanged++;
                AddDetail(details,
                    $"~ Variable: {before.DisplayName} [{before.Type}; {EmptyLabel(before.Default)}] -> " +
                    $"{after.DisplayName} [{after.Type}; {EmptyLabel(after.Default)}]");
            }
        }

        HashSet<string> baselineConnections = BuildConnections(baseline);
        HashSet<string> candidateConnections = BuildConnections(candidate);
        string[] connectionsAdded = candidateConnections.Except(baselineConnections, StringComparer.Ordinal).ToArray();
        string[] connectionsRemoved = baselineConnections.Except(candidateConnections, StringComparer.Ordinal).ToArray();
        foreach (string connection in connectionsAdded)
            AddDetail(details, $"+ Connection: {connection}");
        foreach (string connection in connectionsRemoved)
            AddDetail(details, $"- Connection: {connection}");

        bool parentChanged = !string.Equals(baseline.ParentClass, candidate.ParentClass, StringComparison.Ordinal);
        if (parentChanged)
            details.Insert(0, $"~ Parent class: {baseline.ParentClass} -> {candidate.ParentClass}");

        int totalChanges = graphAdded + graphRemoved + nodeAdded + nodeRemoved + nodeChanged + nodeMoved +
                           pinChanged + variableAdded + variableRemoved + variableChanged +
                           connectionsAdded.Length + connectionsRemoved.Length + (parentChanged ? 1 : 0);
        bool truncated = baseline.Truncated || candidate.Truncated;
        string summary = totalChanges == 0
            ? "Blueprint graphs are semantically equivalent"
            : $"Blueprint semantic diff · {totalChanges:N0} change(s)";

        StringBuilder text = new();
        text.AppendLine("Blueprint semantic diff");
        text.AppendLine($"File: {relativePath}");
        text.AppendLine($"Parent class: {baseline.ParentClass} -> {candidate.ParentClass}");
        text.AppendLine($"Graphs: +{graphAdded} / -{graphRemoved}");
        text.AppendLine($"Nodes: +{nodeAdded} / -{nodeRemoved} / ~{nodeChanged} / moved {nodeMoved}");
        text.AppendLine($"Pins changed: {pinChanged}");
        text.AppendLine($"Connections: +{connectionsAdded.Length} / -{connectionsRemoved.Length}");
        text.AppendLine($"Variables: +{variableAdded} / -{variableRemoved} / ~{variableChanged}");
        if (truncated)
            text.AppendLine("Warning: at least one Blueprint manifest reached its safety limit; results may be partial.");
        text.AppendLine();
        if (details.Count == 0)
        {
            text.AppendLine("No semantic Blueprint change was detected.");
        }
        else
        {
            foreach (string detail in details) text.AppendLine(detail);
            if (details.Count >= MaximumDetailLines)
                text.AppendLine("… Additional details were omitted from this view.");
        }

        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Asset kind"] = "Blueprint",
            ["Semantic changes"] = totalChanges.ToString(),
            ["Graphs added"] = graphAdded.ToString(),
            ["Graphs removed"] = graphRemoved.ToString(),
            ["Nodes added"] = nodeAdded.ToString(),
            ["Nodes removed"] = nodeRemoved.ToString(),
            ["Nodes changed"] = nodeChanged.ToString(),
            ["Nodes moved"] = nodeMoved.ToString(),
            ["Pins changed"] = pinChanged.ToString(),
            ["Connections added"] = connectionsAdded.Length.ToString(),
            ["Connections removed"] = connectionsRemoved.Length.ToString(),
            ["Variables changed"] = (variableAdded + variableRemoved + variableChanged).ToString(),
            ["Manifest truncated"] = truncated ? "Yes" : "No"
        };
        return new BlueprintSemanticDiffResult(summary, text.ToString().TrimEnd(), metadata);
    }

    private static BlueprintSnapshot? ReadSnapshot(JsonElement root)
    {
        if (!root.TryGetProperty("blueprint", out JsonElement blueprint) ||
            blueprint.ValueKind != JsonValueKind.Object)
            return null;

        Dictionary<string, BlueprintVariable> variables = new(StringComparer.Ordinal);
        if (blueprint.TryGetProperty("variables", out JsonElement variablesJson))
        {
            foreach (JsonElement item in variablesJson.EnumerateArray())
            {
                BlueprintVariable variable = new(
                    Text(item, "key"),
                    Text(item, "name"),
                    Text(item, "friendlyName"),
                    Text(item, "type"),
                    Text(item, "default"),
                    Text(item, "category"),
                    Text(item, "repNotify"));
                variables[variable.Key] = variable;
            }
        }

        Dictionary<string, BlueprintGraph> graphs = new(StringComparer.Ordinal);
        if (blueprint.TryGetProperty("graphs", out JsonElement graphsJson))
        {
            foreach (JsonElement graphJson in graphsJson.EnumerateArray())
            {
                Dictionary<string, BlueprintNode> nodes = new(StringComparer.Ordinal);
                if (graphJson.TryGetProperty("nodes", out JsonElement nodesJson))
                {
                    foreach (JsonElement nodeJson in nodesJson.EnumerateArray())
                    {
                        Dictionary<string, BlueprintPin> pins = new(StringComparer.Ordinal);
                        if (nodeJson.TryGetProperty("pins", out JsonElement pinsJson))
                        {
                            foreach (JsonElement pinJson in pinsJson.EnumerateArray())
                            {
                                HashSet<string> links = new(StringComparer.Ordinal);
                                if (pinJson.TryGetProperty("links", out JsonElement linksJson))
                                {
                                    foreach (JsonElement link in linksJson.EnumerateArray())
                                        links.Add(link.GetString() ?? string.Empty);
                                }
                                BlueprintPin pin = new(
                                    Text(pinJson, "key"),
                                    Text(pinJson, "name"),
                                    Text(pinJson, "direction"),
                                    Text(pinJson, "type"),
                                    Text(pinJson, "default"),
                                    links);
                                pins[pin.Key] = pin;
                            }
                        }
                        BlueprintNode node = new(
                            Text(nodeJson, "key"),
                            Text(nodeJson, "name"),
                            Text(nodeJson, "title"),
                            Text(nodeJson, "class"),
                            Number(nodeJson, "x"),
                            Number(nodeJson, "y"),
                            pins);
                        nodes[node.Key] = node;
                    }
                }
                BlueprintGraph graph = new(
                    Text(graphJson, "key"),
                    Text(graphJson, "name"),
                    Text(graphJson, "kind"),
                    nodes);
                graphs[graph.Key] = graph;
            }
        }
        return new BlueprintSnapshot(
            Text(blueprint, "parentClass"),
            variables,
            graphs,
            Boolean(blueprint, "nodesTruncated") || Boolean(blueprint, "pinsTruncated"));
    }

    private static HashSet<string> BuildConnections(BlueprintSnapshot snapshot)
    {
        HashSet<string> connections = new(StringComparer.Ordinal);
        foreach (BlueprintGraph graph in snapshot.Graphs.Values)
        foreach (BlueprintNode node in graph.Nodes.Values)
        foreach (BlueprintPin pin in node.Pins.Values)
        foreach (string link in pin.Links)
        {
            string left = $"{graph.Key}/{node.Key}:{pin.Direction}:{pin.Name}";
            string right = $"{graph.Key}/{link}";
            connections.Add(string.CompareOrdinal(left, right) <= 0 ? $"{left} <-> {right}" : $"{right} <-> {left}");
        }
        return connections;
    }

    private static void AddDetail(List<string> details, string detail)
    {
        if (details.Count < MaximumDetailLines) details.Add(detail);
    }

    private static string EmptyLabel(string value) => string.IsNullOrEmpty(value) ? "default" : value;
    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) ? property.GetString() ?? string.Empty : string.Empty;
    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int value) ? value : 0;
    private static bool Boolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.True;

    private sealed record BlueprintSnapshot(
        string ParentClass,
        Dictionary<string, BlueprintVariable> Variables,
        Dictionary<string, BlueprintGraph> Graphs,
        bool Truncated);

    private sealed record BlueprintVariable(
        string Key,
        string Name,
        string FriendlyName,
        string Type,
        string Default,
        string Category,
        string RepNotify)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Name : $"{FriendlyName} ({Name})";
    }

    private sealed record BlueprintGraph(
        string Key,
        string Name,
        string Kind,
        Dictionary<string, BlueprintNode> Nodes)
    {
        public string DisplayName => $"{Kind}: {Name}";
    }

    private sealed record BlueprintNode(
        string Key,
        string Name,
        string Title,
        string Class,
        int X,
        int Y,
        Dictionary<string, BlueprintPin> Pins)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Name : Title;
    }

    private sealed record BlueprintPin(
        string Key,
        string Name,
        string Direction,
        string Type,
        string Default,
        HashSet<string> Links)
    {
        public string DisplayName => $"{Direction} {Name}";
    }
}
