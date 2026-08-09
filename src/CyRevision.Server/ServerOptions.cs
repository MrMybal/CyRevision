using System.Security.Cryptography;

namespace CyRevision.Server;

public sealed record ServerOptions(
    string DataDirectory,
    string ProjectsDirectory,
    IReadOnlyList<string> AllowedProjectRoots,
    string ApiToken,
    TimeSpan BackupInterval)
{
    public string ProjectCatalogPath => Path.Combine(DataDirectory, "config", "projects.json");

    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public string SyncthingDirectory => Path.Combine(DataDirectory, "syncthing");

    public static ServerOptions Create(IConfiguration configuration, IHostEnvironment environment)
    {
        string defaultData = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyRevision", "server")
            : "/var/lib/cyrevision";
        string dataDirectory = Path.GetFullPath(configuration["CyRevision:DataDirectory"] ?? defaultData);
        string projectsDirectory = Path.GetFullPath(
            configuration["CyRevision:ProjectsDirectory"] ?? Path.Combine(dataDirectory, "projects"));
        string[] configuredRoots = configuration.GetSection("CyRevision:AllowedProjectRoots").Get<string[]>() ?? [];
        List<string> roots = configuredRoots.Select(Path.GetFullPath).ToList();
        if (!roots.Contains(projectsDirectory, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(projectsDirectory);
        }

        double intervalMinutes = configuration.GetValue("CyRevision:BackupIntervalMinutes", 1440d);
        string token = configuration["CyRevision:ApiToken"] ?? Environment.GetEnvironmentVariable("CYREVISION_SERVER_TOKEN") ?? string.Empty;
        string tokenPath = Path.Combine(dataDirectory, "config", "server-token.txt");
        if (string.IsNullOrWhiteSpace(token) && File.Exists(tokenPath))
        {
            token = File.ReadAllText(tokenPath).Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!);
            File.WriteAllText(tokenPath, token);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        return new ServerOptions(
            dataDirectory,
            projectsDirectory,
            roots,
            token,
            TimeSpan.FromMinutes(Math.Max(5, intervalMinutes)));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectCatalogPath)!);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(SyncthingDirectory);
    }

    public bool IsAllowedProjectPath(string path)
    {
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return AllowedProjectRoots.Any(root =>
        {
            string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return string.Equals(candidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }
}

public sealed record CreateServerProjectRequest(
    string Name,
    CyRevision.Core.Configuration.ProjectPresetKind Preset = CyRevision.Core.Configuration.ProjectPresetKind.GitWithPeerSync,
    string? ExistingPath = null);

public sealed record ConfigureServerSyncRequest(string ExecutablePath);

public sealed record CreatePeerInvitationRequest(CyRevision.Security.PeerRole Role = CyRevision.Security.PeerRole.Contributor);

public sealed record PeerExchangeRequest(string ExchangeText, string VerificationCode = "");
