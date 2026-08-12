using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed class AiMcpServerViewModel : ObservableObject
{
    private string _id;
    private string _name;
    private AiMcpTransport _transport;
    private bool _enabled;
    private bool _required;
    private AiMcpCapability _capability;
    private bool _requiresNetwork;
    private string _command;
    private string _arguments;
    private string _workingDirectory;
    private string _environmentVariables;
    private string _forwardEnvironmentVariables;
    private string _url;
    private string _bearerTokenEnvironmentVariable;
    private string _httpHeaders;
    private string _environmentHttpHeaders;
    private AiMcpHttpAuth _httpAuth;
    private string _oauthScopes;
    private string _oauthResource;
    private string _enabledTools;
    private string _disabledTools;
    private string _toolApprovalOverrides;
    private AiMcpApprovalMode _approvalMode;
    private string _startupTimeoutSeconds;
    private string _toolTimeoutSeconds;

    public AiMcpServerViewModel(AiMcpServerConfiguration configuration)
    {
        _id = configuration.Id;
        _name = configuration.Name;
        _transport = configuration.Transport;
        _enabled = configuration.Enabled;
        _required = configuration.Required;
        _capability = configuration.Capability;
        _requiresNetwork = configuration.RequiresNetwork;
        _command = configuration.Command;
        _arguments = configuration.Arguments;
        _workingDirectory = configuration.WorkingDirectory;
        _environmentVariables = configuration.EnvironmentVariables;
        _forwardEnvironmentVariables = configuration.ForwardEnvironmentVariables;
        _url = configuration.Url;
        _bearerTokenEnvironmentVariable = configuration.BearerTokenEnvironmentVariable;
        _httpHeaders = configuration.HttpHeaders;
        _environmentHttpHeaders = configuration.EnvironmentHttpHeaders;
        _httpAuth = configuration.HttpAuth;
        _oauthScopes = configuration.OAuthScopes;
        _oauthResource = configuration.OAuthResource;
        _enabledTools = configuration.EnabledTools;
        _disabledTools = configuration.DisabledTools;
        _toolApprovalOverrides = configuration.ToolApprovalOverrides;
        _approvalMode = configuration.ApprovalMode;
        _startupTimeoutSeconds = configuration.StartupTimeoutSeconds.ToString();
        _toolTimeoutSeconds = configuration.ToolTimeoutSeconds.ToString();
    }

    public string Id { get => _id; set => SetAndRefresh(ref _id, value); }
    public string Name { get => _name; set => SetAndRefresh(ref _name, value); }
    public AiMcpTransport Transport { get => _transport; set => SetAndRefresh(ref _transport, value); }
    public bool Enabled { get => _enabled; set => SetAndRefresh(ref _enabled, value); }
    public bool Required { get => _required; set => SetProperty(ref _required, value); }
    public AiMcpCapability Capability { get => _capability; set => SetAndRefresh(ref _capability, value); }
    public bool RequiresNetwork { get => _requiresNetwork; set => SetProperty(ref _requiresNetwork, value); }
    public string Command { get => _command; set => SetAndRefresh(ref _command, value); }
    public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
    public string WorkingDirectory { get => _workingDirectory; set => SetProperty(ref _workingDirectory, value); }
    public string EnvironmentVariables { get => _environmentVariables; set => SetProperty(ref _environmentVariables, value); }
    public string ForwardEnvironmentVariables { get => _forwardEnvironmentVariables; set => SetProperty(ref _forwardEnvironmentVariables, value); }
    public string Url { get => _url; set => SetAndRefresh(ref _url, value); }
    public string BearerTokenEnvironmentVariable { get => _bearerTokenEnvironmentVariable; set => SetProperty(ref _bearerTokenEnvironmentVariable, value); }
    public string HttpHeaders { get => _httpHeaders; set => SetProperty(ref _httpHeaders, value); }
    public string EnvironmentHttpHeaders { get => _environmentHttpHeaders; set => SetProperty(ref _environmentHttpHeaders, value); }
    public AiMcpHttpAuth HttpAuth { get => _httpAuth; set => SetProperty(ref _httpAuth, value); }
    public string OAuthScopes { get => _oauthScopes; set => SetProperty(ref _oauthScopes, value); }
    public string OAuthResource { get => _oauthResource; set => SetProperty(ref _oauthResource, value); }
    public string EnabledTools { get => _enabledTools; set => SetAndRefresh(ref _enabledTools, value); }
    public string DisabledTools { get => _disabledTools; set => SetAndRefresh(ref _disabledTools, value); }
    public string ToolApprovalOverrides { get => _toolApprovalOverrides; set => SetProperty(ref _toolApprovalOverrides, value); }
    public AiMcpApprovalMode ApprovalMode { get => _approvalMode; set => SetProperty(ref _approvalMode, value); }
    public string StartupTimeoutSeconds { get => _startupTimeoutSeconds; set => SetProperty(ref _startupTimeoutSeconds, value); }
    public string ToolTimeoutSeconds { get => _toolTimeoutSeconds; set => SetProperty(ref _toolTimeoutSeconds, value); }

    public bool IsStdio => Transport == AiMcpTransport.Stdio;
    public bool IsHttp => Transport == AiMcpTransport.StreamableHttp;
    public string State => Enabled ? "Enabled" : "Blocked";
    public string EndpointSummary => Transport == AiMcpTransport.Stdio
        ? (string.IsNullOrWhiteSpace(Command) ? "Command not configured" : Command)
        : (string.IsNullOrWhiteSpace(Url) ? "URL not configured" : Url);
    public string PolicySummary => $"{Capability} · {ApprovalMode}" +
                                   (string.IsNullOrWhiteSpace(DisabledTools) ? string.Empty : " · deny list");

    public AiMcpServerConfiguration ToConfiguration() => new(
        NormalizeId(Id),
        string.IsNullOrWhiteSpace(Name) ? "MCP server" : Name.Trim(),
        Transport,
        Enabled,
        Required,
        Capability,
        RequiresNetwork,
        Command.Trim(),
        Arguments.Trim(),
        WorkingDirectory.Trim(),
        EnvironmentVariables.Trim(),
        ForwardEnvironmentVariables.Trim(),
        Url.Trim(),
        BearerTokenEnvironmentVariable.Trim(),
        HttpHeaders.Trim(),
        EnvironmentHttpHeaders.Trim(),
        HttpAuth,
        OAuthScopes.Trim(),
        OAuthResource.Trim(),
        EnabledTools.Trim(),
        DisabledTools.Trim(),
        ToolApprovalOverrides.Trim(),
        ApprovalMode,
        ParseTimeout(StartupTimeoutSeconds, 10, 1, 300),
        ParseTimeout(ToolTimeoutSeconds, 60, 1, 3_600));

    public static AiMcpServerViewModel Create(AiMcpTransport transport, int number) => new(
        new AiMcpServerConfiguration(
            $"server-{number}",
            transport == AiMcpTransport.Stdio ? $"Local MCP {number}" : $"Remote MCP {number}",
            transport,
            false,
            false,
            AiMcpCapability.ReadOnly,
            transport == AiMcpTransport.StreamableHttp,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            AiMcpHttpAuth.None,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            AiMcpApprovalMode.Writes,
            10,
            60));

    private bool SetAndRefresh<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return false;
        OnPropertyChanged(nameof(IsStdio));
        OnPropertyChanged(nameof(IsHttp));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(EndpointSummary));
        OnPropertyChanged(nameof(PolicySummary));
        return true;
    }

    private static int ParseTimeout(string value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, out int parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;

    private static string NormalizeId(string value)
    {
        char[] normalized = value.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-' ? char.ToLowerInvariant(character) : '_').ToArray();
        return normalized.Length == 0 ? "server" : new string(normalized);
    }
}
