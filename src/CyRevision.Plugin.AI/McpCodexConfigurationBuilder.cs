using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.AI;

public static class McpCodexConfigurationBuilder
{
    public static IReadOnlyList<string> BuildOverrides(
        AiMcpProjectProfile? profile,
        AiWorkspacePermission permissions)
    {
        List<string> overrides = [];
        if (profile is null || !profile.Enabled || profile.EmergencyBlocked)
        {
            overrides.Add("mcp_servers={}");
            return overrides;
        }

        if (profile.BlockUnmanagedServers) overrides.Add("mcp_servers={}");
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (AiMcpServerConfiguration server in profile.Servers.Where(server => server.Enabled))
        {
            if (server.RequiresNetwork && !permissions.HasFlag(AiWorkspacePermission.NetworkAccess)) continue;
            if (server.Transport == AiMcpTransport.StreamableHttp &&
                !permissions.HasFlag(AiWorkspacePermission.NetworkAccess)) continue;
            if (server.Capability == AiMcpCapability.ReadWrite &&
                !permissions.HasFlag(AiWorkspacePermission.ModifyFiles)) continue;

            string id = MakeUniqueId(NormalizeId(server.Id), usedIds);
            if (!HasValidEndpoint(server)) continue;
            string prefix = $"mcp_servers.{id}";
            overrides.Add($"{prefix}.enabled=true");
            overrides.Add($"{prefix}.required={TomlBool(server.Required)}");
            overrides.Add($"{prefix}.startup_timeout_sec={Math.Clamp(server.StartupTimeoutSeconds, 1, 300)}");
            overrides.Add($"{prefix}.tool_timeout_sec={Math.Clamp(server.ToolTimeoutSeconds, 1, 3_600)}");
            overrides.Add($"{prefix}.default_tools_approval_mode={TomlString(ToToml(server.ApprovalMode))}");

            if (server.Transport == AiMcpTransport.Stdio)
            {
                overrides.Add($"{prefix}.command={TomlString(server.Command.Trim())}");
                AddArray(overrides, $"{prefix}.args", SplitLines(server.Arguments));
                AddValue(overrides, $"{prefix}.cwd", server.WorkingDirectory);
                AddInlineMap(overrides, $"{prefix}.env", ParseMap(server.EnvironmentVariables));
                AddArray(overrides, $"{prefix}.env_vars", SplitNames(server.ForwardEnvironmentVariables));
            }
            else
            {
                overrides.Add($"{prefix}.url={TomlString(server.Url.Trim())}");
                AddValue(overrides, $"{prefix}.bearer_token_env_var", server.BearerTokenEnvironmentVariable);
                AddInlineMap(overrides, $"{prefix}.http_headers", ParseMap(server.HttpHeaders));
                AddInlineMap(overrides, $"{prefix}.env_http_headers", ParseMap(server.EnvironmentHttpHeaders));
                if (server.HttpAuth != AiMcpHttpAuth.None)
                {
                    overrides.Add($"{prefix}.auth={TomlString(server.HttpAuth == AiMcpHttpAuth.ChatGpt ? "chatgpt" : "oauth")}");
                }
                AddArray(overrides, $"{prefix}.scopes", SplitNames(server.OAuthScopes));
                AddValue(overrides, $"{prefix}.oauth_resource", server.OAuthResource);
            }

            AddArray(overrides, $"{prefix}.enabled_tools", SplitNames(server.EnabledTools));
            AddArray(overrides, $"{prefix}.disabled_tools", SplitNames(server.DisabledTools));
            foreach ((string tool, string mode) in ParseMap(server.ToolApprovalOverrides))
            {
                if (!TryNormalizeApprovalMode(mode, out string normalizedMode)) continue;
                overrides.Add($"{prefix}.tools.{TomlKeySegment(tool)}.approval_mode={TomlString(normalizedMode)}");
            }
        }
        return overrides;
    }

    public static IReadOnlyList<AiMcpServerConfiguration> GetEffectiveServers(
        AiMcpProjectProfile? profile,
        AiWorkspacePermission permissions)
    {
        if (profile is null || !profile.Enabled || profile.EmergencyBlocked) return [];
        return profile.Servers.Where(server =>
            server.Enabled &&
            (!server.RequiresNetwork || permissions.HasFlag(AiWorkspacePermission.NetworkAccess)) &&
            (server.Transport != AiMcpTransport.StreamableHttp || permissions.HasFlag(AiWorkspacePermission.NetworkAccess)) &&
            (server.Capability != AiMcpCapability.ReadWrite || permissions.HasFlag(AiWorkspacePermission.ModifyFiles)) &&
            HasValidEndpoint(server)).ToArray();
    }

    private static bool HasValidEndpoint(AiMcpServerConfiguration server) => server.Transport switch
    {
        AiMcpTransport.Stdio => !string.IsNullOrWhiteSpace(server.Command),
        AiMcpTransport.StreamableHttp => Uri.TryCreate(server.Url, UriKind.Absolute, out Uri? uri) &&
                                         uri.Scheme is "http" or "https",
        _ => false
    };

    private static string NormalizeId(string id)
    {
        StringBuilder result = new();
        foreach (char character in id.Trim())
        {
            result.Append(char.IsLetterOrDigit(character) || character is '_' or '-'
                ? char.ToLowerInvariant(character)
                : '_');
        }
        return result.Length == 0 ? "server" : result.ToString();
    }

    private static string MakeUniqueId(string candidate, ISet<string> used)
    {
        string value = candidate;
        int suffix = 2;
        while (!used.Add(value)) value = candidate + "_" + suffix++;
        return value;
    }

    private static void AddValue(ICollection<string> values, string key, string raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)) values.Add($"{key}={TomlString(raw.Trim())}");
    }

    private static void AddArray(ICollection<string> values, string key, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        values.Add($"{key}=[{string.Join(",", items.Select(TomlString))}]");
    }

    private static void AddInlineMap(ICollection<string> values, string key, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return;
        string body = string.Join(",", map.Select(pair => $"{TomlString(pair.Key)}={TomlString(pair.Value)}"));
        values.Add($"{key}={{ {body} }}");
    }

    private static IReadOnlyDictionary<string, string> ParseMap(string value)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in SplitLines(value))
        {
            int separator = line.IndexOf('=');
            if (separator <= 0) continue;
            string key = line[..separator].Trim();
            string itemValue = line[(separator + 1)..].Trim();
            if (key.Length > 0) result[key] = itemValue;
        }
        return result;
    }

    private static IReadOnlyList<string> SplitLines(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(item => !item.StartsWith('#'))
        .ToArray();

    private static IReadOnlyList<string> SplitNames(string value) => value
        .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static string ToToml(AiMcpApprovalMode mode) => mode switch
    {
        AiMcpApprovalMode.Prompt => "prompt",
        AiMcpApprovalMode.Writes => "writes",
        AiMcpApprovalMode.Approve => "approve",
        _ => "auto"
    };

    private static bool TryNormalizeApprovalMode(string value, out string normalized)
    {
        normalized = value.Trim().ToLowerInvariant();
        return normalized is "auto" or "prompt" or "writes" or "approve";
    }

    private static string TomlKeySegment(string value) => value.All(character =>
        char.IsLetterOrDigit(character) || character is '_' or '-')
        ? value
        : TomlString(value);

    private static string TomlString(string value) => JsonSerializer.Serialize(value);

    private static string TomlBool(bool value) => value ? "true" : "false";
}
