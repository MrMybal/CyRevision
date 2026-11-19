using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CyRevision.Automation;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CyMcpRuntime? _aiSdkRuntime;
    private RevisionAutomationEngine? _aiSdkEngine;
    private Task _aiSdkRunningTask = Task.CompletedTask;
    private CancellationTokenSource? _aiSdkCancellation;
    private TaskCompletionSource<bool>? _aiSdkApproval;
    private readonly SemaphoreSlim _aiSdkApprovalGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<bool>> _aiSdkRemotePolicies = new();
    private Guid? _aiSdkProjectId;
    private CyRevision.Desktop.Native.NativePreferencesActions? _aiSdkPreferences;
    private CyRevision.Desktop.Native.NativeModelAdapter? _aiSdkUiAdapter;
    private Action<string>? _aiSdkClassicNavigate;
    internal void ConfigureAutomationPreferences(CyRevision.Desktop.Native.NativePreferencesActions preferences)=>_aiSdkPreferences=preferences;
    internal void ConfigureAutomationAdapter(CyRevision.Desktop.Native.NativeModelAdapter adapter,Action<string>? navigate=null){_aiSdkUiAdapter=adapter;_aiSdkClassicNavigate=navigate;}
    private System.ComponentModel.PropertyChangedEventHandler? _aiSdkUiChanges;
    private bool _aiSdkBusy, _aiSdkAllowWrite, _aiSdkAllowStage, _aiSdkAllowCommit, _aiSdkAllowNetwork, _aiSdkCompact = true, _aiSdkDiskSpool, _aiSdkMetrics, _aiSdkNotifications, _aiSdkRestore, _aiSdkRemoteEnabled;
    private string _aiSdkProvider = "ollama", _aiSdkModel = "", _aiSdkEndpoint = "", _aiSdkExecutable = "", _aiSdkKeyEnv = "", _aiSdkPrompt = "", _aiSdkStatus = "CyMCPSDK 0.7.0 · Not connected", _aiSdkCatalogue = "", _aiSdkPending = "", _aiSdkInstanceFile = "", _aiSdkAttachment = "", _aiSdkHistoryId = "main", _aiSdkContextBytes = "0", _aiSdkResultTtl = "300000", _aiSdkCacheTtl = "30000";
    private bool _aiSdkConnected;
    private bool _aiSdkNavigation,_aiSdkPublication,_aiSdkDestructive,_aiSdkServices,_aiSdkAdministration,_aiSdkBuilds;
    public bool AiSdkAllowNavigation {get=>_aiSdkNavigation;set{SetProperty(ref _aiSdkNavigation,value);UpdateAiSdkPermissions();}}
    public bool AiSdkAllowPublication {get=>_aiSdkPublication;set{SetProperty(ref _aiSdkPublication,value);UpdateAiSdkPermissions();}}
    public bool AiSdkAllowDestructive {get=>_aiSdkDestructive;set{SetProperty(ref _aiSdkDestructive,value);UpdateAiSdkPermissions();}}
    public bool AiSdkAllowServices {get=>_aiSdkServices;set{SetProperty(ref _aiSdkServices,value);UpdateAiSdkPermissions();}}
    public bool AiSdkAllowAdministration {get=>_aiSdkAdministration;set{SetProperty(ref _aiSdkAdministration,value);UpdateAiSdkPermissions();}}
    public bool AiSdkAllowBuilds {get=>_aiSdkBuilds;set{SetProperty(ref _aiSdkBuilds,value);UpdateAiSdkPermissions();}}
    private string _aiSdkMemoryBytes = "67108864", _aiSdkDiskBytes = "1073741824", _aiSdkChunkBytes = "32768";
    public string AiSdkMemoryBytes { get => _aiSdkMemoryBytes; set => SetProperty(ref _aiSdkMemoryBytes, value); }
    public string AiSdkDiskBytes { get => _aiSdkDiskBytes; set => SetProperty(ref _aiSdkDiskBytes, value); }
    public string AiSdkChunkBytes { get => _aiSdkChunkBytes; set => SetProperty(ref _aiSdkChunkBytes, value); }
    public ObservableCollection<AiChatMessageViewModel> AiSdkMessages { get; } = [];
    public bool IsAiSdkBusy { get => _aiSdkBusy; private set { if (SetProperty(ref _aiSdkBusy, value)) OnPropertyChanged(nameof(CanSendAiSdk)); } }
    public bool CanSendAiSdk => !IsAiSdkBusy && SelectedProject is not null;
    public bool AiSdkConnected { get => _aiSdkConnected; private set => SetProperty(ref _aiSdkConnected, value); }
    public bool AiSdkAllowWrite { get => _aiSdkAllowWrite; set { SetProperty(ref _aiSdkAllowWrite, value); UpdateAiSdkPermissions(); } }
    public bool AiSdkAllowStage { get => _aiSdkAllowStage; set { SetProperty(ref _aiSdkAllowStage, value); UpdateAiSdkPermissions(); } }
    public bool AiSdkAllowCommit { get => _aiSdkAllowCommit; set { SetProperty(ref _aiSdkAllowCommit, value); UpdateAiSdkPermissions(); } }
    public bool AiSdkAllowNetwork { get => _aiSdkAllowNetwork; set { SetProperty(ref _aiSdkAllowNetwork, value); UpdateAiSdkPermissions(); } }
    public bool AiSdkRemoteEnabled { get => _aiSdkRemoteEnabled; set { SetProperty(ref _aiSdkRemoteEnabled, value); UpdateAiSdkPermissions(); } }
    public bool AiSdkCompactDiscovery { get => _aiSdkCompact; set => SetProperty(ref _aiSdkCompact, value); }
    public bool AiSdkDiskSpool { get => _aiSdkDiskSpool; set => SetProperty(ref _aiSdkDiskSpool, value); }
    public bool AiSdkMetrics { get => _aiSdkMetrics; set => SetProperty(ref _aiSdkMetrics, value); }
    public bool AiSdkNotifications { get => _aiSdkNotifications; set => SetProperty(ref _aiSdkNotifications, value); }
    public bool AiSdkRestoreHistory { get => _aiSdkRestore; set => SetProperty(ref _aiSdkRestore, value); }
    public string AiSdkProviderId { get => _aiSdkProvider; set => SetProperty(ref _aiSdkProvider, value); }
    public string AiSdkModel { get => _aiSdkModel; set => SetProperty(ref _aiSdkModel, value); }
    public string AiSdkEndpoint { get => _aiSdkEndpoint; set => SetProperty(ref _aiSdkEndpoint, value); }
    public string AiSdkExecutable { get => _aiSdkExecutable; set => SetProperty(ref _aiSdkExecutable, value); }
    public string AiSdkApiKeyEnvironment { get => _aiSdkKeyEnv; set => SetProperty(ref _aiSdkKeyEnv, value); }
    public string AiSdkPrompt { get => _aiSdkPrompt; set => SetProperty(ref _aiSdkPrompt, value); }
    public string AiSdkStatus { get => _aiSdkStatus; private set => SetProperty(ref _aiSdkStatus, value); }
    public string AiSdkCatalogue { get => _aiSdkCatalogue; private set => SetProperty(ref _aiSdkCatalogue, value); }
    public string AiSdkPendingApproval { get => _aiSdkPending; private set { SetProperty(ref _aiSdkPending, value); OnPropertyChanged(nameof(AiSdkHasApproval)); } }
    public bool AiSdkHasApproval => _aiSdkApproval is not null;
    public string AiSdkInstanceFile { get => _aiSdkInstanceFile; private set => SetProperty(ref _aiSdkInstanceFile, value); }
    public string AiSdkAttachmentPath { get => _aiSdkAttachment; set => SetProperty(ref _aiSdkAttachment, value); }
    public string AiSdkHistoryId { get => _aiSdkHistoryId; set => SetProperty(ref _aiSdkHistoryId, value); }
    public string AiSdkContextBytes { get => _aiSdkContextBytes; set => SetProperty(ref _aiSdkContextBytes, value); }
    public string AiSdkResultTtlMs { get => _aiSdkResultTtl; set => SetProperty(ref _aiSdkResultTtl, value); }
    public string AiSdkCacheTtlMs { get => _aiSdkCacheTtl; set => SetProperty(ref _aiSdkCacheTtl, value); }
    private string AiSdkDataDirectory => Path.Combine(_applicationPaths.DataDirectory, "automation", (_aiSdkProjectId ?? SelectedProject?.Id ?? Guid.Empty).ToString("N"));
    private static int SdkInteger(string value) => int.TryParse(value, out int number) && number >= 0 ? number : throw new ArgumentException("Use a non-negative integer.");

    private async Task<CyMcpRuntime> EnsureAiSdkAsync()
    {
        var project = SelectedProject ?? throw new InvalidOperationException(_localization.Translate("Select a project first."));
        if (_aiSdkRuntime is not null && _aiSdkProjectId == project.Id) { UpdateAiSdkPermissions(); return _aiSdkRuntime; }
        await ReleaseAiSdkRuntimeAsync(); _aiSdkProjectId = project.Id;
        var permissions = new AutomationPermissions { ScopeValid = () => SelectedProject?.Id == project.Id && !IsProjectLoading && !AiMcpEmergencyBlocked && IsAiIntegrationEnabled, ConfirmAsync = ConfirmAiSdkAsync };
        permissions.RemoteAllowed = name => _aiSdkRemotePolicies.TryGetValue(name, out var check) && check();
        permissions.GitEnabled = () => SelectedProject?.Definition.Features.GitEnabled == true;
        permissions.BackupEnabled = () => SelectedProject?.Definition.Features.BackupEnabled == true;
        _aiSdkEngine = new RevisionAutomationEngine(project.RootPath, AiSdkDataDirectory, permissions, _gitService, string.IsNullOrWhiteSpace(BackupStorePath) ? null : BackupStorePath, project.Id);
        _aiSdkEngine.Add(new("app.state", "Read the current live CyRevision selection and operation state.", AutomationObjectSchema(), AutomationOutputSchema(), true, Handler: async (_, token) => await Dispatcher.UIThread.InvokeAsync<object>(() => new { projectId = SelectedProject?.Id, project = SelectedProject?.Name, CurrentBranch, IsBusy, IsProjectLoading }, DispatcherPriority.Normal, token)));
        _aiSdkEngine.Add(new("app.refresh", "Refresh the selected live repository through the desktop's actual workflow.", AutomationObjectSchema(), AutomationOutputSchema(), true, Handler: async (_, token) => { await Dispatcher.UIThread.InvokeAsync(() => RefreshAsync()); return new { refreshed = true }; }));
        new CyRevision.Desktop.Native.DesktopAutomation(this, _aiSdkEngine, _aiSdkPreferences, _aiSdkUiAdapter??=new(this,_applicationPaths.ConfigurationDirectory),_aiSdkClassicNavigate).Register();
        _aiSdkRuntime = await Task.Run(() => new CyMcpRuntime(_aiSdkEngine, new { compactDiscovery = AiSdkCompactDiscovery, diskSpool = AiSdkDiskSpool, performance = AiSdkMetrics, notifications = AiSdkNotifications, resultTtlMs = SdkInteger(AiSdkResultTtlMs), cacheTtlMs = SdkInteger(AiSdkCacheTtlMs), memoryBytes = SdkInteger(AiSdkMemoryBytes), diskBytes = SdkInteger(AiSdkDiskBytes), chunkBytes = SdkInteger(AiSdkChunkBytes) }));
        _aiSdkEngine.Changed += name => { if(!name.StartsWith("app.",StringComparison.Ordinal))Dispatcher.UIThread.Post(() => { if (SelectedProject?.Id == project.Id) _ = RefreshAsync(); }); };
        _aiSdkUiChanges = (_, change) =>
        {
            if (change.PropertyName is not (nameof(CurrentBranch) or nameof(ChangeSummaryBase)) || _aiSdkRuntime is null) return;
            try { _aiSdkRuntime.Request(new { command = "invalidate" }); } catch (AutomationException) { /* A concurrent connection failure is shown by its owning operation. */ }
        };
        PropertyChanged += _aiSdkUiChanges;
        UpdateAiSdkPermissions(); return _aiSdkRuntime;
    }
    private static JsonObject AutomationObjectSchema() => new() { ["type"] = "object", ["additionalProperties"] = false };
    private static JsonObject AutomationOutputSchema() => new() { ["type"] = "object", ["properties"] = new JsonObject { ["value"] = new JsonObject() }, ["required"] = new JsonArray("value") };
    private void UpdateAiSdkPermissions()
    {
        if (_aiSdkEngine is null) return; var p = _aiSdkEngine.Permissions;
        p.Write = AiSdkAllowWrite; p.Stage = AiSdkAllowStage; p.Commit = AiSdkAllowCommit; p.Network = AiSdkAllowNetwork; p.RemoteTools = AiSdkRemoteEnabled;
        p.Blocked = AiMcpEmergencyBlocked;
        p.Capabilities=(AiSdkAllowNavigation?AutomationCapability.Navigation:0)|(AiSdkAllowPublication?AutomationCapability.Publication:0)|
            (AiSdkAllowDestructive?AutomationCapability.Destructive:0)|(AiSdkAllowServices?AutomationCapability.Services:0)|
            (AiSdkAllowAdministration?AutomationCapability.Administration:0)|(AiSdkAllowBuilds?AutomationCapability.Builds:0);
    }
    private async Task<bool> ConfirmAiSdkAsync(string name, JsonElement args, CancellationToken token)
    {
        if (!await _aiSdkApprovalGate.WaitAsync(0, token)) return false;
        try
        {
            if (args.GetRawText().Length > 16384) throw new AutomationException("ReviewLimit", "This operation is too large to review here; use the CLI with an explicit confirmation.");
            var promise = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.UIThread.InvokeAsync(() => { _aiSdkApproval = promise; AiSdkPendingApproval = name + "\n" + JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = true }); OnPropertyChanged(nameof(AiSdkHasApproval)); });
            using var registration = token.Register(() => promise.TrySetCanceled(token));
            return await promise.Task.ConfigureAwait(false);
        }
        finally { await Dispatcher.UIThread.InvokeAsync(() => { _aiSdkApproval = null; AiSdkPendingApproval = ""; OnPropertyChanged(nameof(AiSdkHasApproval)); }); _aiSdkApprovalGate.Release(); }
    }
    public void ApproveAiSdkOperation() => _aiSdkApproval?.TrySetResult(true);
    public void RejectAiSdkOperation() => _aiSdkApproval?.TrySetResult(false);
    public void CancelAiSdkTurn() { _aiSdkApproval?.TrySetResult(false); _aiSdkCancellation?.Cancel(); }
    public async Task ListAiSdkProvidersAsync() => await SdkAction(async runtime => AiSdkCatalogue = (await runtime.RequestAsync(new { command = "providers" })).GetRawText());
    public async Task DiscoverAiSdkProvidersAsync() => await SdkAction(async runtime => AiSdkCatalogue = (await runtime.RequestAsync(new { command = "discover" })).GetRawText());
    public async Task ListAiSdkModelsAsync() => await SdkAction(async runtime => AiSdkCatalogue = (await runtime.RequestAsync(AiSdkConnectionRequest("models"))).GetRawText());
    public async Task ListAiSdkOperationsAsync() => await SdkAction(async runtime => AiSdkCatalogue = (await runtime.RequestAsync(new { command = "describe" })).GetRawText());
    public async Task ShowAiSdkMetricsAsync() => await SdkAction(async runtime => AiSdkCatalogue = (await runtime.RequestAsync(new { command = "metrics" })).GetRawText());
    private JsonObject AiSdkConnectionRequest(string command)
    {
        var settings = new JsonObject(); if (!string.IsNullOrWhiteSpace(AiSdkEndpoint)) settings["base_url"] = AiSdkEndpoint;
        bool directTools = AiSdkProviderId is "codex" or "openai" or "anthropic" or "openrouter" or "ollama" or "openai-compatible" or "llama.cpp" or "lmstudio";
        if (!directTools && AiSdkInstanceFile.Length > 0)
        {
            using var descriptor = JsonDocument.Parse(File.ReadAllText(AiSdkInstanceFile)); string url = descriptor.RootElement.GetProperty("url").GetString()!; string token = descriptor.RootElement.GetProperty("token").GetString()!;
            if (AiSdkProviderId == "claude-code") settings["mcp_servers"] = new JsonObject { ["cyrevision"] = new JsonObject { ["type"] = "http", ["url"] = url + "/mcp", ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + token } } };
            else settings["mcp_servers"] = new JsonArray(new JsonObject { ["type"] = "http", ["name"] = "cyrevision", ["url"] = url + "/mcp", ["headers"] = new JsonArray(new JsonObject { ["name"] = "Authorization", ["value"] = "Bearer " + token }) });
        }
        return new JsonObject { ["command"] = command, ["provider"] = AiSdkProviderId, ["model"] = AiSdkModel, ["executable"] = AiSdkExecutable, ["cwd"] = _aiSdkEngine!.Root, ["apiKey"] = string.IsNullOrWhiteSpace(AiSdkApiKeyEnvironment) ? "" : Environment.GetEnvironmentVariable(AiSdkApiKeyEnvironment) ?? "", ["attachTools"] = directTools, ["contextBytes"] = SdkInteger(AiSdkContextBytes), ["settings"] = settings };
    }
    private async Task ConnectAiSdkCoreAsync(CyMcpRuntime runtime)
    {
        if (!AiSdkConnected)
        {
            var request = AiSdkConnectionRequest("chat-connect");
            string directory = Path.Combine(AiSdkDataDirectory, "conversations");
            if (AiSdkRestoreHistory) request["checkpoint"] = JsonNode.Parse((await runtime.RequestAsync(new { command = "chat-load", directory, id = AiSdkHistoryId })).GetRawText());
            else if (File.Exists(Path.Combine(directory, AiSdkHistoryId + ".json"))) throw new AutomationException("ConfirmationRequired", "Saved history exists. Enable Restore saved session, or choose a new History ID.");
            await runtime.RequestAsync(request); AiSdkConnected = true;
            if (AiSdkRestoreHistory) await LoadAiSdkTranscriptAsync(runtime);
        }
    }
    public async Task ConnectAiSdkAsync() => await SdkAction(async runtime => { await ConnectAiSdkCoreAsync(runtime); AiSdkStatus = _localization.Translate("CyMCPSDK connected. No message was sent."); });
    public async Task SendAiSdkMessageAsync()
    {
        if (IsAiSdkBusy || string.IsNullOrWhiteSpace(AiSdkPrompt)) return;
        _aiSdkRunningTask = SendAiSdkCoreAsync(); await _aiSdkRunningTask;
    }
    private async Task SendAiSdkCoreAsync()
    {
        IsAiSdkBusy = true; CyMcpRuntime? runtime = null;
        AiChatMessageViewModel? assistant = null; Action<AutomationEvent>? sink = null;
        try
        {
            runtime = await EnsureAiSdkAsync(); await ConnectAiSdkCoreAsync(runtime);
            _aiSdkCancellation = new(); var token = _aiSdkCancellation.Token;
            var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = AiSdkPrompt });
            if (!string.IsNullOrWhiteSpace(AiSdkAttachmentPath)) content.Add(JsonNode.Parse((await runtime.RequestAsync(new { command = "attachment", path = Path.GetFullPath(AiSdkAttachmentPath) }, token)).GetRawText()));
            AiSdkMessages.Add(new("user", AiSdkPrompt)); assistant = new("assistant", "", displayName: AiSdkProviderId); AiSdkMessages.Add(assistant); AiSdkPrompt = ""; AiSdkStatus = _localization.Translate("CyMCPSDK is working…");
            sink = value => Dispatcher.UIThread.Post(() => { if (value.Type == "TextDelta" && value.Data.TryGetProperty("text", out var text)) assistant.Append(text.GetString() ?? ""); else if (value.Type is "ToolStarted" or "ToolCompleted") AiSdkStatus = _localization.Translate(value.Type == "ToolStarted" ? "Application tool running…" : "Application tool completed."); });
            runtime.Event += sink;
            var result = await runtime.RequestAsync(new { command = "chat-send", content }, token); assistant.Text = result.GetProperty("text").GetString() ?? assistant.Text; AiSdkStatus = _localization.Translate("CyMCPSDK turn completed.");
        }
        catch (Exception error) { AiSdkStatus = error is OperationCanceledException ? _localization.Translate("Turn cancelled.") : error.Message; if (assistant is not null) assistant.Append("\n\n" + AiSdkStatus); }
        finally
        {
            if (runtime is not null) { if (sink is not null) runtime.Event -= sink; try { runtime.Request(new { command = "reset-cancellation" }); if (AiSdkConnected) await runtime.RequestAsync(new { command = "chat-save", directory = Path.Combine(AiSdkDataDirectory, "conversations"), id = AiSdkHistoryId }); } catch (Exception error) { AiSdkStatus += " · " + _localization.Translate("History save failed.") + " " + error.Message; } }
            _aiSdkCancellation?.Dispose(); _aiSdkCancellation = null; IsAiSdkBusy = false;
        }
    }
    private async Task LoadAiSdkTranscriptAsync(CyMcpRuntime runtime)
    {
        var transcript = await runtime.RequestAsync(new { command = "chat-transcript" }); AiSdkMessages.Clear();
        var messages = transcript.ValueKind == JsonValueKind.Array ? transcript : transcript.GetProperty("messages");
        foreach (var message in messages.EnumerateArray())
        {
            string role = message.TryGetProperty("role", out var r) ? r.GetString() ?? "assistant" : "assistant";
            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) AiSdkMessages.Add(new(role, content.GetString() ?? "", displayName: AiSdkProviderId));
            else if (content.ValueKind == JsonValueKind.Array) AiSdkMessages.Add(new(role, string.Join("\n", content.EnumerateArray().Where(block => block.TryGetProperty("text", out _)).Select(block => block.GetProperty("text").GetString())), displayName: AiSdkProviderId));
            else if (message.TryGetProperty("text", out var text)) AiSdkMessages.Add(new(role, text.GetString() ?? "", displayName: AiSdkProviderId));
        }
    }
    public async Task SaveAiSdkSessionAsync() => await SdkAction(async runtime => { await runtime.RequestAsync(new { command = "chat-save", directory = Path.Combine(AiSdkDataDirectory, "conversations"), id = AiSdkHistoryId }); AiSdkStatus = _localization.Translate("Conversation saved."); });
    public async Task DisconnectAiSdkAsync() => await SdkAction(async runtime => { if (AiSdkConnected) await runtime.RequestAsync(new { command = "chat-disconnect" }); AiSdkConnected = false; AiSdkStatus = _localization.Translate("Disconnected. Settings will apply on the next connection."); });
    public async Task RestartAiSdkAsync() => await SdkAction(async _ => { await ReleaseAiSdkRuntimeAsync(); await EnsureAiSdkAsync(); AiSdkStatus = _localization.Translate("SDK settings applied. Reconnect the provider."); });
    public async Task StartAiSdkMcpServerAsync() => await SdkAction(async runtime =>
    {
        if (AiMcpEmergencyBlocked) throw new AutomationException("AccessDenied", "MCP is emergency-blocked.");
        if (AiSdkInstanceFile.Length > 0) return; string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var server = await runtime.RequestAsync(new { command = "serve", token }); string url = "http://127.0.0.1:" + server.GetProperty("port").GetInt32();
        string file = Path.Combine(AiSdkDataDirectory, "instance.json"); await runtime.RequestAsync(new { command = "private-write", path = file, text = JsonSerializer.Serialize(new { url, token, pid = Environment.ProcessId, project = _aiSdkEngine!.Root }, CyMcpRuntime.JsonOptions) });
        AiSdkInstanceFile = file; AiSdkStatus = url + "/mcp · " + _localization.Translate("Authenticated local MCP is running.");
    });
    public async Task StopAiSdkMcpServerAsync() => await SdkAction(async runtime => { await runtime.RequestAsync(new { command = "stop-server" }); if (File.Exists(AiSdkInstanceFile)) File.Delete(AiSdkInstanceFile); AiSdkInstanceFile = ""; });
    public async Task ConnectAiSdkRemoteToolsAsync() => await SdkAction(async runtime =>
    {
        UpdateAiSdkPermissions(); var selected = SelectedAiMcpServer ?? throw new InvalidOperationException("Select a configured MCP server first.");
        if (!AiMcpEnabled || AiMcpEmergencyBlocked || !selected.Enabled || !AiSdkRemoteEnabled) throw new AutomationException("AccessDenied", "Enable the project MCP policy, the selected server, and remote tools explicitly.");
        var allowed = selected.EnabledTools.Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Except(selected.DisabledTools.Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal).ToArray();
        if (allowed.Length == 0) throw new AutomationException("AccessDenied", "Select explicit allowed tool names; wildcard forwarding is not enabled.");
        JsonObject request = new() { ["command"] = "remote-connect", ["id"] = selected.Id, ["allowedTools"] = new JsonArray(allowed.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()), ["cacheTtlMs"] = SdkInteger(AiSdkCacheTtlMs) };
        request["startupTimeoutMs"] = Math.Clamp(SdkInteger(selected.StartupTimeoutSeconds), 1, 300) * 1000; request["toolTimeoutMs"] = Math.Clamp(SdkInteger(selected.ToolTimeoutSeconds), 1, 3600) * 1000;
        if ((selected.IsHttp || selected.RequiresNetwork) && !AiSdkAllowNetwork) throw new AutomationException("AccessDenied", "External network tools require network permission.");
        if (selected.Capability == AiMcpCapability.ReadWrite && !AiSdkAllowWrite) throw new AutomationException("AccessDenied", "The configured server requires write permission.");
        if (selected.IsHttp) { if (selected.HttpAuth != AiMcpHttpAuth.None) throw new AutomationException("Unsupported", "This SDK transport needs a configured bearer token; existing OAuth integrations remain available in the legacy mode."); request["url"] = selected.Url; request["token"] = string.IsNullOrWhiteSpace(selected.BearerTokenEnvironmentVariable) ? "" : Environment.GetEnvironmentVariable(selected.BearerTokenEnvironmentVariable) ?? ""; }
        else { request["executable"] = selected.Command; request["arguments"] = new JsonArray(selected.Arguments.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()); request["cwd"] = string.IsNullOrWhiteSpace(selected.WorkingDirectory) ? _aiSdkEngine!.Root : selected.WorkingDirectory; }
        if (!string.IsNullOrWhiteSpace(selected.HttpHeaders) || !string.IsNullOrWhiteSpace(selected.EnvironmentHttpHeaders)) throw new AutomationException("Unsupported", "Custom HTTP headers are not supported by this SDK adapter; use the existing MCP integration or a bearer profile.");
        if (!selected.IsHttp) { var environment = new JsonObject(); foreach (var line in selected.EnvironmentVariables.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) { int equals = line.IndexOf('='); if (equals <= 0) throw new ArgumentException("Environment overrides use NAME=value."); environment[line[..equals].Trim()] = line[(equals + 1)..]; } foreach (var name in selected.ForwardEnvironmentVariables.Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) environment[name] = Environment.GetEnvironmentVariable(name) ?? ""; request["environment"] = environment; }
        var registered = await runtime.ConnectRemoteAsync(request); foreach (var tool in registered.EnumerateArray()) { string name = tool.GetProperty("sourceName").GetString()!; bool readOnly = tool.GetProperty("readOnly").GetBoolean(); _aiSdkRemotePolicies[tool.GetProperty("name").GetString()!] = () => AiMcpEnabled && selected.Enabled && AiMcpServers.Contains(selected) && (!(selected.IsHttp || selected.RequiresNetwork) || AiSdkAllowNetwork) && (selected.Capability == AiMcpCapability.ReadOnly ? readOnly : AiSdkAllowWrite) && selected.EnabledTools.Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal) && !selected.DisabledTools.Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal); }
        AiSdkCatalogue = registered.GetRawText();
    });
    private async Task SdkAction(Func<CyMcpRuntime, Task> action)
    {
        if (IsAiSdkBusy) return; _aiSdkRunningTask = SdkActionCore(action); await _aiSdkRunningTask;
    }
    private async Task SdkActionCore(Func<CyMcpRuntime, Task> action)
    {
        if (IsAiSdkBusy) return; IsAiSdkBusy = true;
        try { await action(await EnsureAiSdkAsync()); } catch (Exception error) { AiSdkStatus = error.Message; } finally { IsAiSdkBusy = false; }
    }
    private async Task DisposeAiSdkAsync()
    {
        CancelAiSdkTurn(); if (!_aiSdkRunningTask.IsCompleted) await _aiSdkRunningTask;
        await ReleaseAiSdkRuntimeAsync();
    }
    private async Task ReleaseAiSdkRuntimeAsync()
    {
        if (_aiSdkUiChanges is not null) { PropertyChanged -= _aiSdkUiChanges; _aiSdkUiChanges = null; }
        if (_aiSdkRuntime is not null) { await Task.Run(_aiSdkRuntime.Dispose); _aiSdkRuntime = null; }
        _aiSdkRemotePolicies.Clear();
        if (File.Exists(AiSdkInstanceFile)) File.Delete(AiSdkInstanceFile); AiSdkInstanceFile = ""; AiSdkConnected = false; _aiSdkEngine = null;
    }
}
