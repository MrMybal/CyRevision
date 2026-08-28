using System.Text.Json;
using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.AI;

namespace CyRevision.Core.Tests;

public sealed class AiIntegrationPluginTests
{
    [Fact]
    public void CodexIsReadOnlyWithoutModifyPermission()
    {
        AiAgentRequest request = CreateRequest(AiWorkspacePermission.ReadRepository);

        IReadOnlyList<string> arguments = AiIntegrationPlugin.BuildCodexArguments(request);

        AssertArgumentValue(arguments, "--sandbox", "read-only");
        Assert.DoesNotContain("--search", arguments);
        Assert.DoesNotContain("danger-full-access", arguments);
        Assert.DoesNotContain("--yolo", arguments);
    }

    [Fact]
    public void CodexWorkspaceAndNetworkRequireExplicitPermissions()
    {
        AiAgentRequest request = CreateRequest(
            AiWorkspacePermission.ReadRepository |
            AiWorkspacePermission.ModifyFiles |
            AiWorkspacePermission.NetworkAccess);

        IReadOnlyList<string> arguments = AiIntegrationPlugin.BuildCodexArguments(request);

        AssertArgumentValue(arguments, "--sandbox", "workspace-write");
        Assert.Contains("--search", arguments);
        Assert.Contains("Never run git add, git commit, git push", arguments[^1]);
    }

    [Fact]
    public void OpenCodeProviderIsExposed()
    {
        AiIntegrationPlugin plugin = new();

        AiProviderDescriptor provider = Assert.Single(
            plugin.Providers,
            value => value.Kind == AiProviderKind.OpenCodeCli);

        Assert.Equal("opencode-cli", provider.Id);
        Assert.True(provider.SupportsWorkspaceEdits);
        Assert.False(provider.RequiresApiKey);
    }

    [Fact]
    public void OpenCodeUsesRepositoryAndOptionalModel()
    {
        AiAgentRequest request = CreateOpenCodeRequest(
            AiWorkspacePermission.ReadRepository,
            "anthropic/claude-sonnet-4-5");

        IReadOnlyList<string> arguments = AiIntegrationPlugin.BuildOpenCodeArguments(request);

        Assert.Equal("run", arguments[0]);
        AssertArgumentValue(arguments, "--format", "json");
        AssertArgumentValue(arguments, "--dir", Path.GetFullPath(Environment.CurrentDirectory));
        AssertArgumentValue(arguments, "--model", "anthropic/claude-sonnet-4-5");
        Assert.Contains("Never run git add, git commit, git push", arguments[^1]);
    }

    [Fact]
    public void OpenCodeIsReadOnlyAndOfflineByDefault()
    {
        AiAgentRequest request = CreateOpenCodeRequest(AiWorkspacePermission.ReadRepository);

        using JsonDocument document = JsonDocument.Parse(AiIntegrationPlugin.BuildOpenCodeConfiguration(request));
        JsonElement permission = document.RootElement.GetProperty("permission");

        Assert.Equal("allow", permission.GetProperty("read").GetString());
        Assert.Equal("deny", permission.GetProperty("edit").GetString());
        Assert.Equal("deny", permission.GetProperty("bash").GetString());
        Assert.Equal("deny", permission.GetProperty("external_directory").GetString());
        Assert.Equal("deny", permission.GetProperty("webfetch").GetString());
        Assert.Equal("deny", permission.GetProperty("websearch").GetString());
    }

    [Fact]
    public void OpenCodeModifyAndNetworkRequireExplicitPermissions()
    {
        AiAgentRequest request = CreateOpenCodeRequest(
            AiWorkspacePermission.ReadRepository |
            AiWorkspacePermission.ModifyFiles |
            AiWorkspacePermission.NetworkAccess);

        using JsonDocument document = JsonDocument.Parse(AiIntegrationPlugin.BuildOpenCodeConfiguration(request));
        JsonElement permission = document.RootElement.GetProperty("permission");

        Assert.Equal("allow", permission.GetProperty("edit").GetString());
        Assert.Equal("allow", permission.GetProperty("webfetch").GetString());
        Assert.Equal("allow", permission.GetProperty("websearch").GetString());
        Assert.Equal("deny", permission.GetProperty("bash").GetString());
    }

    [Fact]
    public void OpenCodeJsonTextEventIsExtracted()
    {
        const string line = "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"OpenCode response\"}}";

        Assert.Equal("OpenCode response", AiIntegrationPlugin.ExtractOpenCodeMessage(line));
    }

    private static AiAgentRequest CreateRequest(AiWorkspacePermission permissions)
    {
        AiProviderDescriptor provider = new(
            "codex", "Codex", AiProviderKind.CodexCli, string.Empty, string.Empty, string.Empty,
            true, false, "test");
        return new AiAgentRequest(Environment.CurrentDirectory, "Inspect the code", string.Empty, provider,
            string.Empty, string.Empty, "codex", null, permissions);
    }

    private static AiAgentRequest CreateOpenCodeRequest(
        AiWorkspacePermission permissions,
        string model = "")
    {
        AiProviderDescriptor provider = new(
            "opencode-cli", "OpenCode CLI", AiProviderKind.OpenCodeCli, string.Empty, string.Empty, string.Empty,
            true, false, "test");
        return new AiAgentRequest(Environment.CurrentDirectory, "Inspect the code", string.Empty, provider,
            model, string.Empty, "opencode", null, permissions);
    }

    private static void AssertArgumentValue(IReadOnlyList<string> arguments, string key, string expected)
    {
        int index = arguments.IndexOf(key);
        Assert.True(index >= 0 && index + 1 < arguments.Count);
        Assert.Equal(expected, arguments[index + 1]);
    }
}

internal static class ReadOnlyListTestExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        }
        return -1;
    }
}
