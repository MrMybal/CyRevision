using System.Text;

namespace CyRevision.Vpn;

public sealed class WireGuardConfigService
{
    private readonly string _rootDirectory;

    public WireGuardConfigService(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string GetConfigurationPath(VpnProjectProfile profile) =>
        Path.Combine(_rootDirectory, "config", profile.ProjectId.ToString("N"), profile.InterfaceName + ".conf");

    public async Task<string> RenderAsync(
        VpnProjectProfile profile,
        bool redactPrivateKey = true,
        CancellationToken cancellationToken = default)
    {
        VpnProfileValidator.Validate(profile);
        string privateKey = redactPrivateKey
            ? "<clé privée masquée>"
            : (await File.ReadAllTextAsync(profile.PrivateKeyPath, cancellationToken)).Trim();
        if (!redactPrivateKey)
        {
            VpnProfileValidator.ValidateWireGuardKey(privateKey, "privée");
        }

        (_, int prefix) = VpnProfileValidator.ParseCidr(profile.NetworkCidr);
        StringBuilder text = new();
        text.AppendLine("# Géré exclusivement par CyRevision");
        text.AppendLine($"# Projet {profile.ProjectId:N} — interface {profile.InterfaceName}");
        text.AppendLine("[Interface]");
        text.AppendLine($"PrivateKey = {privateKey}");
        text.AppendLine($"Address = {profile.LocalAddress}/{prefix}");
        text.AppendLine($"ListenPort = {profile.ListenPort}");

        foreach (VpnPeerDefinition peer in profile.Peers.Where(peer => peer.Enabled).OrderBy(peer => peer.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine();
            text.AppendLine($"# {SanitizeComment(peer.DisplayName)} — {peer.Capabilities}");
            text.AppendLine("[Peer]");
            text.AppendLine($"PublicKey = {peer.PublicKey}");
            string[] routes = [$"{peer.TunnelAddress}/32", .. peer.RoutedSubnets];
            text.AppendLine($"AllowedIPs = {string.Join(", ", routes.Distinct(StringComparer.Ordinal))}");
            if (!string.IsNullOrWhiteSpace(peer.Endpoint))
            {
                text.AppendLine($"Endpoint = {peer.Endpoint}");
            }

            if (peer.PersistentKeepaliveSeconds > 0)
            {
                text.AppendLine($"PersistentKeepalive = {peer.PersistentKeepaliveSeconds}");
            }
        }

        return text.ToString();
    }

    public async Task<string> WriteAsync(VpnProjectProfile profile, CancellationToken cancellationToken = default)
    {
        string path = GetConfigurationPath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string contents = await RenderAsync(profile, redactPrivateKey: false, cancellationToken);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, cancellationToken);
            File.Move(temporary, path, true);
            JsonVpnProfileStore.Restrict(path);
        }
        finally
        {
            File.Delete(temporary);
        }

        return path;
    }

    private static string SanitizeComment(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
