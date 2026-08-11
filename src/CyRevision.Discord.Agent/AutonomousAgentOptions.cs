using CyRevision.Core.Configuration;
using CyRevision.Discord.Control;

namespace CyRevision.Discord.Agent;

public sealed record AutonomousAgentOptions(
    string DataDirectory,
    string ListenUrl,
    string? TokenFile,
    bool AllowPrivateHttp,
    bool PrintToken,
    bool ShowHelp)
{
    public static AutonomousAgentOptions Parse(string[] arguments)
    {
        string dataDirectory = Path.Combine(ApplicationPaths.CreateDefault().DiscordDirectory, "agent-host");
        string listenUrl = "http://127.0.0.1:47831";
        string? tokenFile = null;
        bool allowPrivateHttp = false;
        bool printToken = false;
        bool showHelp = false;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            switch (argument)
            {
                case "--data" when index + 1 < arguments.Length:
                    dataDirectory = arguments[++index];
                    break;
                case "--listen" when index + 1 < arguments.Length:
                    listenUrl = arguments[++index];
                    break;
                case "--token-file" when index + 1 < arguments.Length:
                    tokenFile = arguments[++index];
                    break;
                case "--allow-private-http":
                    allowPrivateHttp = true;
                    break;
                case "--print-token":
                    printToken = true;
                    break;
                case "--help" or "-h":
                    showHelp = true;
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown autonomous agent option: {argument}");
                    }
                    break;
            }
        }

        dataDirectory = Path.GetFullPath(dataDirectory);
        tokenFile = string.IsNullOrWhiteSpace(tokenFile)
            ? Path.Combine(dataDirectory, "control-token.txt")
            : Path.GetFullPath(tokenFile);
        _ = DiscordAgentEndpoint.Create(listenUrl, allowPrivateHttp);
        return new AutonomousAgentOptions(
            dataDirectory,
            listenUrl,
            tokenFile,
            allowPrivateHttp,
            printToken,
            showHelp);
    }

    public static string HelpText => """
        CyRevision autonomous Discord agent

        Options:
          --data <directory>        Configuration and checkpoint directory.
          --listen <url>            Control API URL (default http://127.0.0.1:47831).
          --token-file <path>       Bearer token file. Created securely when missing.
          --allow-private-http      Allow non-loopback HTTP only inside a trusted VPN.
          --print-token             Print the configured token to pair a controller.
          --help                    Show this help.

        Environment:
          CYREVISION_DISCORD_AGENT_TOKEN overrides the token file.
          ASPNETCORE_Kestrel__Certificates__Default__Path configures an HTTPS certificate.
        """;
}
