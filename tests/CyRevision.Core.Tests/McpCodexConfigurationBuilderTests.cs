using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.AI;

namespace CyRevision.Core.Tests;

public sealed class McpCodexConfigurationBuilderTests
{
    [Fact]
    public void EmergencyBlockRemovesEveryConfiguredServer()
    {
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, true, false, [CreateServer()], DateTimeOffset.UtcNow);

        IReadOnlyList<string> overrides = McpCodexConfigurationBuilder.BuildOverrides(
            profile, AiWorkspacePermission.ReadRepository | AiWorkspacePermission.NetworkAccess);

        Assert.Equal(["mcp_servers={}"], overrides);
        Assert.DoesNotContain(overrides, value => value.Contains("command", StringComparison.Ordinal));
    }

    [Fact]
    public void PolicyBuildsAllowDenyAndPerToolApprovalOverrides()
    {
        AiMcpServerConfiguration server = CreateServer() with
        {
            EnabledTools = "read\nsearch",
            DisabledTools = "delete",
            ToolApprovalOverrides = "search=approve\ndelete=prompt"
        };
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, false, true, [server], DateTimeOffset.UtcNow);

        IReadOnlyList<string> overrides = McpCodexConfigurationBuilder.BuildOverrides(
            profile, AiWorkspacePermission.ReadRepository);

        Assert.Contains("mcp_servers={}", overrides);
        Assert.Contains("mcp_servers.docs.enabled_tools=[\"read\",\"search\"]", overrides);
        Assert.Contains("mcp_servers.docs.disabled_tools=[\"delete\"]", overrides);
        Assert.Contains("mcp_servers.docs.tools.search.approval_mode=\"approve\"", overrides);
        Assert.Contains("mcp_servers.docs.tools.delete.approval_mode=\"prompt\"", overrides);
    }

    [Fact]
    public void NetworkAndWriteServersNeedMatchingWorkspacePermissions()
    {
        AiMcpServerConfiguration http = CreateServer() with
        {
            Id = "remote",
            Transport = AiMcpTransport.StreamableHttp,
            Command = string.Empty,
            Url = "https://example.test/mcp",
            RequiresNetwork = true
        };
        AiMcpServerConfiguration writer = CreateServer() with
        {
            Id = "writer",
            Capability = AiMcpCapability.ReadWrite
        };
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, false, true, [http, writer], DateTimeOffset.UtcNow);

        IReadOnlyList<string> readOnly = McpCodexConfigurationBuilder.BuildOverrides(
            profile, AiWorkspacePermission.ReadRepository);
        IReadOnlyList<string> authorized = McpCodexConfigurationBuilder.BuildOverrides(
            profile,
            AiWorkspacePermission.ReadRepository |
            AiWorkspacePermission.ModifyFiles |
            AiWorkspacePermission.NetworkAccess);

        Assert.DoesNotContain(readOnly, value => value.Contains("remote", StringComparison.Ordinal));
        Assert.DoesNotContain(readOnly, value => value.Contains("writer", StringComparison.Ordinal));
        Assert.Contains(authorized, value => value == "mcp_servers.remote.url=\"https://example.test/mcp\"");
        Assert.Contains(authorized, value => value == "mcp_servers.writer.command=\"docs-mcp\"");
    }

    [Fact]
    public async Task PluginStoresProfileOutsideTheRepository()
    {
        using TemporaryDirectory root = new();
        string configuration = Path.Combine(root.Path, "configuration");
        string repository = Path.Combine(root.Path, "repository");
        Directory.CreateDirectory(repository);
        AiIntegrationPlugin plugin = new();
        await plugin.InitializeAsync(new CyRevisionPluginContext(
            root.Path, root.Path, configuration, Path.Combine(root.Path, "data"), "test"));
        Guid projectId = Guid.NewGuid();
        AiMcpProjectProfile profile = new(projectId, true, false, true, [CreateServer()], DateTimeOffset.UtcNow);

        await plugin.SaveMcpProfileAsync(profile);
        AiMcpProjectProfile loaded = await plugin.GetMcpProfileAsync(projectId);

        Assert.True(loaded.Enabled);
        Assert.Single(loaded.Servers);
        Assert.Empty(Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(configuration, "*.json", SearchOption.AllDirectories));
        await plugin.DisposeAsync();
    }

    [Fact]
    public void ResponsesApiUsesOnlyExplicitlyAllowedHttpTools()
    {
        AiMcpServerConfiguration server = CreateServer() with
        {
            Id = "remote docs",
            Transport = AiMcpTransport.StreamableHttp,
            Command = string.Empty,
            Url = "https://example.test/mcp",
            RequiresNetwork = true,
            EnabledTools = "search, read, delete",
            DisabledTools = "delete",
            ApprovalMode = AiMcpApprovalMode.Approve
        };
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, false, true, [server], DateTimeOffset.UtcNow);

        McpResponsesApiPlan plan = McpResponsesApiConfigurationBuilder.Build(
            profile,
            AiWorkspacePermission.ReadRepository | AiWorkspacePermission.NetworkAccess);

        IReadOnlyDictionary<string, object?> tool = Assert.Single(plan.Tools);
        Assert.Equal("remote_docs", tool["server_label"]);
        Assert.Equal("never", tool["require_approval"]);
        Assert.Equal(["search", "read"], Assert.IsType<string[]>(tool["allowed_tools"]));
        Assert.Single(plan.Servers);
    }

    [Fact]
    public void ResponsesApiBlocksStdioAndDenyOnlyConfigurations()
    {
        AiMcpServerConfiguration denyOnly = CreateServer() with
        {
            Id = "remote",
            Transport = AiMcpTransport.StreamableHttp,
            Command = string.Empty,
            Url = "https://example.test/mcp",
            RequiresNetwork = true,
            DisabledTools = "delete"
        };
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, false, true, [CreateServer(), denyOnly], DateTimeOffset.UtcNow);

        McpResponsesApiPlan plan = McpResponsesApiConfigurationBuilder.Build(
            profile,
            AiWorkspacePermission.ReadRepository | AiWorkspacePermission.NetworkAccess);

        Assert.Empty(plan.Tools);
        Assert.Contains(plan.Notices, notice => notice.Contains("STDIO", StringComparison.Ordinal));
        Assert.Contains(plan.Notices, notice => notice.Contains("allowlist", StringComparison.Ordinal));
    }

    [Fact]
    public void ResponsesApiRequiresPresentAuthorizationEnvironmentVariable()
    {
        AiMcpServerConfiguration server = CreateServer() with
        {
            Transport = AiMcpTransport.StreamableHttp,
            Command = string.Empty,
            Url = "https://example.test/mcp",
            RequiresNetwork = true,
            EnabledTools = "read",
            BearerTokenEnvironmentVariable = "CYREVISION_TEST_MISSING_MCP_TOKEN"
        };
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, false, true, [server], DateTimeOffset.UtcNow);

        McpResponsesApiPlan plan = McpResponsesApiConfigurationBuilder.Build(
            profile,
            AiWorkspacePermission.ReadRepository | AiWorkspacePermission.NetworkAccess,
            _ => null);

        Assert.Empty(plan.Tools);
        Assert.Contains(plan.Notices, notice => notice.Contains("is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void ResponsesApiKeepsNonApprovedPoliciesBehindApproval()
    {
        AiMcpServerConfiguration server = CreateServer() with
        {
            Transport = AiMcpTransport.StreamableHttp,
            Command = string.Empty,
            Url = "https://example.test/mcp",
            RequiresNetwork = true,
            EnabledTools = "read",
            ApprovalMode = AiMcpApprovalMode.Writes
        };
        AiMcpProjectProfile profile = new(
            Guid.NewGuid(), true, false, true, [server], DateTimeOffset.UtcNow);

        McpResponsesApiPlan plan = McpResponsesApiConfigurationBuilder.Build(
            profile,
            AiWorkspacePermission.ReadRepository | AiWorkspacePermission.NetworkAccess);

        Assert.Equal("always", Assert.Single(plan.Tools)["require_approval"]);
    }

    private static AiMcpServerConfiguration CreateServer() => new(
        Id: "docs",
        Name: "Documentation",
        Transport: AiMcpTransport.Stdio,
        Enabled: true,
        Required: false,
        Capability: AiMcpCapability.ReadOnly,
        RequiresNetwork: false,
        Command: "docs-mcp",
        Arguments: "--stdio",
        WorkingDirectory: string.Empty,
        EnvironmentVariables: string.Empty,
        ForwardEnvironmentVariables: string.Empty,
        Url: string.Empty,
        BearerTokenEnvironmentVariable: string.Empty,
        HttpHeaders: string.Empty,
        EnvironmentHttpHeaders: string.Empty,
        HttpAuth: AiMcpHttpAuth.None,
        OAuthScopes: string.Empty,
        OAuthResource: string.Empty,
        EnabledTools: string.Empty,
        DisabledTools: string.Empty,
        ToolApprovalOverrides: string.Empty,
        ApprovalMode: AiMcpApprovalMode.Writes,
        StartupTimeoutSeconds: 10,
        ToolTimeoutSeconds: 60);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrevision-mcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
