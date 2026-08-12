using System.Text;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.AI;

public sealed record McpResponsesApiPlan(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Tools,
    IReadOnlyList<AiMcpServerConfiguration> Servers,
    IReadOnlyList<string> Notices);

public static class McpResponsesApiConfigurationBuilder
{
    public static McpResponsesApiPlan Build(
        AiMcpProjectProfile? profile,
        AiWorkspacePermission permissions,
        Func<string, string?>? environmentResolver = null)
    {
        environmentResolver ??= Environment.GetEnvironmentVariable;
        List<IReadOnlyDictionary<string, object?>> tools = [];
        List<AiMcpServerConfiguration> servers = [];
        List<string> notices = [];

        if (profile is null || !profile.Enabled)
        {
            notices.Add("MCP is disabled for this project.");
            return new McpResponsesApiPlan(tools, servers, notices);
        }
        if (profile.EmergencyBlocked)
        {
            notices.Add("The project MCP emergency block is active.");
            return new McpResponsesApiPlan(tools, servers, notices);
        }

        HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
        foreach (AiMcpServerConfiguration server in
                 McpCodexConfigurationBuilder.GetEffectiveServers(profile, permissions))
        {
            if (server.Transport != AiMcpTransport.StreamableHttp)
            {
                notices.Add($"{server.Name}: STDIO is available only to local Codex providers.");
                continue;
            }

            string[] allowed = SplitNames(server.EnabledTools)
                .Except(SplitNames(server.DisabledTools), StringComparer.Ordinal)
                .ToArray();
            if (allowed.Length == 0)
            {
                notices.Add($"{server.Name}: the Responses API requires a non-empty effective tool allowlist.");
                continue;
            }

            string? authorization = null;
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvironmentVariable))
            {
                authorization = environmentResolver(server.BearerTokenEnvironmentVariable.Trim());
                if (string.IsNullOrWhiteSpace(authorization))
                {
                    notices.Add($"{server.Name}: environment variable '{server.BearerTokenEnvironmentVariable.Trim()}' is missing.");
                    continue;
                }
            }
            else if (server.HttpAuth != AiMcpHttpAuth.None)
            {
                notices.Add($"{server.Name}: API providers need an authorization token environment variable; Codex OAuth sessions are not reused.");
                continue;
            }

            string approval = RequiresApiApproval(server, allowed) ? "always" : "never";
            Dictionary<string, object?> tool = new(StringComparer.Ordinal)
            {
                ["type"] = "mcp",
                ["server_label"] = MakeUniqueLabel(NormalizeLabel(server.Id), labels),
                ["server_description"] = $"CyRevision project MCP server: {server.Name} ({server.Capability}).",
                ["server_url"] = server.Url.Trim(),
                ["require_approval"] = approval,
                ["allowed_tools"] = allowed
            };
            if (!string.IsNullOrWhiteSpace(authorization)) tool["authorization"] = authorization.Trim();
            if (!string.IsNullOrWhiteSpace(server.HttpHeaders) ||
                !string.IsNullOrWhiteSpace(server.EnvironmentHttpHeaders))
            {
                notices.Add($"{server.Name}: custom HTTP headers apply to local Codex only; the Responses API entry uses its authorization field.");
            }
            if (approval == "always")
            {
                notices.Add($"{server.Name}: tool calls require approval and remain blocked until the project policy is explicitly set to Approve.");
            }
            tools.Add(tool);
            servers.Add(server);
        }

        return new McpResponsesApiPlan(tools, servers, notices);
    }

    private static bool RequiresApiApproval(AiMcpServerConfiguration server, IReadOnlyCollection<string> allowedTools)
    {
        if (server.ApprovalMode != AiMcpApprovalMode.Approve) return true;
        IReadOnlyDictionary<string, string> overrides = ParseMap(server.ToolApprovalOverrides);
        return allowedTools.Any(tool => overrides.TryGetValue(tool, out string? mode) &&
                                        !string.Equals(mode, "approve", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, string> ParseMap(string value)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string line in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.IndexOf('=');
            if (separator <= 0) continue;
            string key = line[..separator].Trim();
            string itemValue = line[(separator + 1)..].Trim();
            if (key.Length > 0) result[key] = itemValue;
        }
        return result;
    }

    private static IReadOnlyList<string> SplitNames(string value) => value
        .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static string NormalizeLabel(string value)
    {
        StringBuilder result = new();
        foreach (char character in value.Trim())
        {
            result.Append(char.IsLetterOrDigit(character) || character is '_' or '-'
                ? char.ToLowerInvariant(character)
                : '_');
        }
        return result.Length == 0 ? "server" : result.ToString();
    }

    private static string MakeUniqueLabel(string candidate, ISet<string> used)
    {
        string value = candidate;
        int suffix = 2;
        while (!used.Add(value)) value = candidate + "_" + suffix++;
        return value;
    }
}
