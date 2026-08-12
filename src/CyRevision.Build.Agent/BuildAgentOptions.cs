using CyRevision.Core.Configuration;

namespace CyRevision.Build.Agent;

public sealed record BuildAgentOptions(
    string DataDirectory,
    string ListenUrl,
    string TokenFile,
    string ConfigurationFile,
    bool AllowPrivateHttp,
    bool PrintToken,
    bool ShowHelp)
{
    public static BuildAgentOptions Parse(string[] arguments)
    {
        string data = Path.Combine(ApplicationPaths.CreateDefault().ConfigurationDirectory, "build-agent");
        string listen = "http://127.0.0.1:47841";
        string? token = null;
        string? configuration = null;
        bool allowPrivateHttp = false;
        bool printToken = false;
        bool help = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--data" when index + 1 < arguments.Length:
                    data = arguments[++index];
                    break;
                case "--listen" when index + 1 < arguments.Length:
                    listen = arguments[++index];
                    break;
                case "--token-file" when index + 1 < arguments.Length:
                    token = arguments[++index];
                    break;
                case "--config" when index + 1 < arguments.Length:
                    configuration = arguments[++index];
                    break;
                case "--allow-private-http":
                    allowPrivateHttp = true;
                    break;
                case "--print-token":
                    printToken = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                default:
                    if (arguments[index].StartsWith('-'))
                        throw new ArgumentException($"Unknown build agent option: {arguments[index]}");
                    break;
            }
        }

        data = Path.GetFullPath(data);
        token = Path.GetFullPath(token ?? Path.Combine(data, "access-token.txt"));
        configuration = Path.GetFullPath(configuration ?? Path.Combine(data, "agent.json"));
        ValidateListenUrl(listen, allowPrivateHttp);
        return new BuildAgentOptions(data, listen.TrimEnd('/'), token, configuration,
            allowPrivateHttp, printToken, help);
    }

    private static void ValidateListenUrl(string value, bool allowPrivateHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("The build agent listen URL must use HTTP or HTTPS.");
        bool loopback = uri.IsLoopback || uri.Host is "localhost" or "127.0.0.1" or "::1";
        if (uri.Scheme == "http" && !loopback && !allowPrivateHttp)
            throw new ArgumentException("Non-loopback HTTP requires --allow-private-http and must be restricted to WireGuard by the firewall.");
    }

    public static string HelpText => """
        CyRevision remote build agent

        Options:
          --data <directory>        Agent configuration, jobs, and logs.
          --listen <url>            Listen URL (default http://127.0.0.1:47841).
          --token-file <path>       Bearer token file, created when missing.
          --config <path>           Local allowlisted project/recipe configuration.
          --allow-private-http      Permit HTTP on a trusted private WireGuard address.
          --print-token             Print the pairing token.
          --help                    Show this help.

        The client can select only recipes declared locally in agent.json. It cannot submit commands.
        Never expose the agent port to the public Internet; bind it to loopback or a WireGuard address.
        """;
}
