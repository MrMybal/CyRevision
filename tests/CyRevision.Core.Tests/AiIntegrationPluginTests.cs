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

    private static AiAgentRequest CreateRequest(AiWorkspacePermission permissions)
    {
        AiProviderDescriptor provider = new(
            "codex", "Codex", AiProviderKind.CodexCli, string.Empty, string.Empty, string.Empty,
            true, false, "test");
        return new AiAgentRequest(Environment.CurrentDirectory, "Inspect the code", string.Empty, provider,
            string.Empty, string.Empty, "codex", null, permissions);
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
