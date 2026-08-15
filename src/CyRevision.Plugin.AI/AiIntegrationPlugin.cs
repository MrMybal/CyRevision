using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.AI;

public sealed class AiIntegrationPlugin : IAiIntegrationPlugin
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly SemaphoreSlim _codexConnectionGate = new(1, 1);
    private CyRevisionPluginContext? _context;
    private JsonAiMcpProfileStore? _mcpProfileStore;
    private CodexAppServerSession? _codexChatSession;

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        "cyrevision.ai",
        "AI Workspace",
        "0.3.0",
        "Optional Codex CLI, API, local-model, and project-scoped MCP integration.",
        "AI");

    public IReadOnlyList<AiProviderDescriptor> Providers { get; } =
    [
        new("codex-cli", "Codex CLI", AiProviderKind.CodexCli, string.Empty, string.Empty, string.Empty,
            true, false, "Runs the installed Codex CLI inside the selected repository sandbox."),
        new("openai-api", "OpenAI API", AiProviderKind.OpenAiApi, "gpt-5.2-codex", "https://api.openai.com/v1/responses", string.Empty,
            false, true, "Uses the Responses API. The API key is kept for this session only."),
        new("compatible-api", "OpenAI-compatible API", AiProviderKind.OpenAiCompatibleApi, string.Empty, "http://127.0.0.1:1234/v1/responses", string.Empty,
            false, false, "Connects to a configurable Responses-compatible endpoint."),
        new("codex-ollama", "Codex + Ollama", AiProviderKind.CodexLocalProvider, string.Empty, string.Empty, "ollama",
            true, false, "Runs Codex against a local Ollama provider."),
        new("codex-lmstudio", "Codex + LM Studio", AiProviderKind.CodexLocalProvider, string.Empty, string.Empty, "lmstudio",
            true, false, "Runs Codex against a local LM Studio provider.")
    ];

    public Task InitializeAsync(CyRevisionPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _mcpProfileStore = new JsonAiMcpProfileStore(context.ConfigurationDirectory);
        return Task.CompletedTask;
    }

    public Task<AiAgentResult> RunAsync(AiAgentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureRequest(request);
        return request.Provider.Kind is AiProviderKind.CodexCli or AiProviderKind.CodexLocalProvider
            ? RunCodexAsync(request, cancellationToken)
            : RunApiAsync(request, cancellationToken);
    }

    public bool IsCodexChatConnected => _codexChatSession?.IsConnected == true;

    public async ValueTask DisposeAsync()
    {
        await DisconnectCodexChatAsync().ConfigureAwait(false);
        _codexConnectionGate.Dispose();
        _httpClient.Dispose();
    }

    public Task<AiCodexDetectionResult> DetectCodexAsync(
        string executablePath = "codex",
        CancellationToken cancellationToken = default) =>
        CodexLocalDetector.DetectAsync(executablePath, cancellationToken);

    public async Task<AiChatConnectionResult> ConnectCodexChatAsync(
        AiChatConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        await _codexConnectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCodexChatCoreAsync().ConfigureAwait(false);
            try
            {
                _codexChatSession = await CodexAppServerSession.ConnectAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                return new AiChatConnectionResult(
                    true,
                    _codexChatSession.ThreadId,
                    $"Connected to Codex for {request.ProjectName}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _codexChatSession = null;
                return new AiChatConnectionResult(false, string.Empty, exception.Message);
            }
        }
        finally
        {
            _codexConnectionGate.Release();
        }
    }

    public Task<AiChatTurnResult> SendCodexChatAsync(
        string message,
        IProgress<AiChatProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CodexAppServerSession? session = _codexChatSession;
        return session is null || !session.IsConnected
            ? Task.FromResult(new AiChatTurnResult(
                false,
                string.Empty,
                "Codex is not connected to the selected project.",
                string.Empty,
                TimeSpan.Zero))
            : session.SendAsync(message, progress, cancellationToken);
    }

    public async Task DisconnectCodexChatAsync(CancellationToken cancellationToken = default)
    {
        await _codexConnectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCodexChatCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _codexConnectionGate.Release();
        }
    }

    private async Task DisconnectCodexChatCoreAsync()
    {
        CodexAppServerSession? session = Interlocked.Exchange(ref _codexChatSession, null);
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<AiMcpProjectProfile> GetMcpProfileAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        (_mcpProfileStore ?? throw new InvalidOperationException("The AI plugin is not initialized."))
        .GetAsync(projectId, cancellationToken);

    public Task SaveMcpProfileAsync(
        AiMcpProjectProfile profile,
        CancellationToken cancellationToken = default) =>
        (_mcpProfileStore ?? throw new InvalidOperationException("The AI plugin is not initialized."))
        .SaveAsync(profile with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);

    public static IReadOnlyList<string> BuildCodexArguments(AiAgentRequest request)
    {
        string sandbox = request.Permissions.HasFlag(AiWorkspacePermission.ModifyFiles)
            ? "workspace-write"
            : "read-only";
        List<string> arguments = ["--ask-for-approval", "never"];
        if (request.Permissions.HasFlag(AiWorkspacePermission.NetworkAccess))
        {
            arguments.Add("--search");
        }
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments.Add("--model");
            arguments.Add(request.Model.Trim());
        }
        if (request.Provider.Kind == AiProviderKind.CodexLocalProvider)
        {
            arguments.Add("--oss");
            arguments.Add("--local-provider");
            arguments.Add(request.Provider.LocalProvider);
        }
        foreach (string configurationOverride in McpCodexConfigurationBuilder.BuildOverrides(
                     request.McpProfile,
                     request.Permissions))
        {
            arguments.Add("--config");
            arguments.Add(configurationOverride);
        }
        arguments.Add("exec");
        arguments.Add("--json");
        arguments.Add("--cd");
        arguments.Add(Path.GetFullPath(request.RepositoryPath));
        arguments.Add("--sandbox");
        arguments.Add(sandbox);
        arguments.Add(BuildGuardedPrompt(request));
        return arguments;
    }

    private async Task<AiAgentResult> RunCodexAsync(AiAgentRequest request, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProcessStartInfo startInfo = new()
        {
            FileName = string.IsNullOrWhiteSpace(request.ExecutablePath) ? "codex" : request.ExecutablePath.Trim(),
            WorkingDirectory = Path.GetFullPath(request.RepositoryPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in BuildCodexArguments(request)) startInfo.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Codex CLI could not start.");
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            return new AiAgentResult(false, string.Empty,
                "Codex CLI was not found. Install it or configure another provider. " + exception.Message,
                -1, stopwatch.Elapsed);
        }

        List<string> messages = [];
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                string? message = ExtractCodexMessage(line);
                if (!string.IsNullOrWhiteSpace(message)) messages.Add(message);
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        string error = await errorTask;
        stopwatch.Stop();
        return new AiAgentResult(
            process.ExitCode == 0,
            string.Join(Environment.NewLine + Environment.NewLine, messages.Distinct()),
            error.Trim(),
            process.ExitCode,
            stopwatch.Elapsed);
    }

    private async Task<AiAgentResult> RunApiAsync(AiAgentRequest request, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        McpResponsesApiPlan mcpPlan = McpResponsesApiConfigurationBuilder.Build(
            request.McpProfile,
            request.Permissions);
        using HttpRequestMessage message = new(HttpMethod.Post, NormalizeResponsesEndpoint(request.Endpoint));
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }
        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["model"] = request.Model,
            ["instructions"] = "You are connected to CyRevision. You cannot change repository files directly. Use only the MCP servers and tools supplied in this request, within their project policy.",
            ["input"] = BuildGuardedPrompt(request, mcpPlan)
        };
        if (mcpPlan.Tools.Count > 0) payload["tools"] = mcpPlan.Tools;
        message.Content = JsonContent.Create(payload);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        string responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();
        if (!response.IsSuccessStatusCode)
        {
            return new AiAgentResult(false, string.Empty,
                $"API returned {(int)response.StatusCode}: {TrimDiagnostic(responsePayload)}", (int)response.StatusCode, stopwatch.Elapsed);
        }
        IReadOnlyList<string> approvals = ExtractMcpApprovalRequests(responsePayload);
        string notices = string.Join(" ", mcpPlan.Notices);
        if (approvals.Count > 0)
        {
            string diagnostic = $"MCP approval required for {string.Join(", ", approvals)}. " +
                                "CyRevision did not approve the tool call automatically. Set the trusted server/tool policy to Approve to allow immediate API execution.";
            if (notices.Length > 0) diagnostic += " " + notices;
            return new AiAgentResult(false, string.Empty, diagnostic, 409, stopwatch.Elapsed);
        }
        return new AiAgentResult(true, ExtractResponsesApiText(responsePayload), notices, (int)response.StatusCode, stopwatch.Elapsed);
    }

    private static string BuildGuardedPrompt(AiAgentRequest request, McpResponsesApiPlan? apiPlan = null)
    {
        StringBuilder guard = new();
        guard.AppendLine("CyRevision workspace authorization:");
        guard.AppendLine($"- Repository: {Path.GetFullPath(request.RepositoryPath)}");
        guard.AppendLine(request.Permissions.HasFlag(AiWorkspacePermission.ModifyFiles)
            ? "- File modifications inside the repository are authorized."
            : "- Read-only: do not modify, create, rename, or delete files.");
        guard.AppendLine(request.Permissions.HasFlag(AiWorkspacePermission.NetworkAccess)
            ? "- Network access is authorized when needed."
            : "- Do not use the network or contact external services.");
        guard.AppendLine("- Never run git add, git commit, git push, or rewrite Git history. CyRevision brokers authorized Git operations after the run.");
        guard.AppendLine("- Stay inside the selected repository and preserve unrelated user changes.");
        IReadOnlyList<AiMcpServerConfiguration> effectiveMcpServers = apiPlan?.Servers ??
            McpCodexConfigurationBuilder.GetEffectiveServers(request.McpProfile, request.Permissions);
        if (request.McpProfile is null || !request.McpProfile.Enabled || request.McpProfile.EmergencyBlocked)
        {
            guard.AppendLine("- MCP is blocked for this run. Do not attempt to use MCP tools or servers.");
        }
        else if (effectiveMcpServers.Count == 0)
        {
            guard.AppendLine("- No MCP server satisfies the current workspace/network permission policy.");
        }
        else
        {
            guard.AppendLine($"- MCP is limited to these effective servers: {string.Join(", ", effectiveMcpServers.Select(server => server.Name))}.");
            foreach (AiMcpServerConfiguration server in effectiveMcpServers)
            {
                guard.AppendLine($"  - {server.Name}: {server.Capability}; allowed tools [{server.EnabledTools}]; blocked tools [{server.DisabledTools}].");
            }
        }
        guard.AppendLine();
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            guard.AppendLine("Selected read-only context:");
            guard.AppendLine(request.Context);
            guard.AppendLine();
        }
        guard.AppendLine("User task:");
        guard.Append(request.Prompt.Trim());
        return guard.ToString();
    }

    private static string? ExtractCodexMessage(string jsonLine)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonLine);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("item", out JsonElement item) &&
                item.TryGetProperty("type", out JsonElement itemType) &&
                itemType.GetString() == "agent_message" &&
                item.TryGetProperty("text", out JsonElement itemText))
                return itemText.GetString();
            if (root.TryGetProperty("message", out JsonElement directMessage) && directMessage.ValueKind == JsonValueKind.String)
                return directMessage.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractResponsesApiText(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("output_text", out JsonElement direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString() ?? string.Empty;
        if (!root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
            return payload;
        List<string> texts = [];
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                    texts.Add(text.GetString() ?? string.Empty);
            }
        }
        return texts.Count == 0 ? payload : string.Join(Environment.NewLine, texts);
    }

    private static IReadOnlyList<string> ExtractMcpApprovalRequests(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("output", out JsonElement output) ||
                output.ValueKind != JsonValueKind.Array) return [];
            List<string> approvals = [];
            foreach (JsonElement item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out JsonElement type) ||
                    type.GetString() != "mcp_approval_request") continue;
                string tool = item.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "tool" : "tool";
                string server = item.TryGetProperty("server_label", out JsonElement label) ? label.GetString() ?? "MCP" : "MCP";
                approvals.Add($"{server}/{tool}");
            }
            return approvals;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Uri NormalizeResponsesEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        Uri uri = new(endpoint.Trim(), UriKind.Absolute);
        return uri;
    }

    private static string TrimDiagnostic(string value) => value.Length <= 2_000 ? value : value[..2_000] + "…";

    private static void EnsureRequest(AiAgentRequest request)
    {
        if (!Directory.Exists(request.RepositoryPath)) throw new DirectoryNotFoundException(request.RepositoryPath);
        if (!request.Permissions.HasFlag(AiWorkspacePermission.ReadRepository))
            throw new InvalidOperationException("Repository read permission is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        if (request.Provider.RequiresApiKey && string.IsNullOrWhiteSpace(request.ApiKey))
            throw new InvalidOperationException("This provider requires an API key.");
        if (request.Provider.Kind is AiProviderKind.OpenAiApi or AiProviderKind.OpenAiCompatibleApi)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Endpoint);
        }
    }
}
