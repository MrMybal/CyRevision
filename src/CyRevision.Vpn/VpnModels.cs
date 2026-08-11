using System.Net;
using System.Net.Sockets;

namespace CyRevision.Vpn;

[Flags]
public enum VpnNodeCapabilities
{
    None = 0,
    GeneralAccess = 1,
    SwarmAgent = 2,
    SwarmCoordinator = 4,
    CiWorker = 8,
    ServiceHost = 16
}

public enum VpnRuntimeState
{
    NotConfigured,
    Stopped,
    Running,
    Unavailable,
    Collision,
    Faulted
}

public enum VpnBackendMode
{
    SystemInstallation,
    IntegratedRuntime
}

public sealed record VpnPeerDefinition(
    Guid PeerId,
    string DisplayName,
    string PublicKey,
    string TunnelAddress,
    string? Endpoint,
    IReadOnlyList<string> RoutedSubnets,
    int PersistentKeepaliveSeconds,
    VpnNodeCapabilities Capabilities,
    bool Enabled = true);

public sealed record VpnProjectProfile(
    Guid ProjectId,
    string InterfaceName,
    string NetworkCidr,
    string LocalAddress,
    int ListenPort,
    string? PublicEndpoint,
    string PrivateKeyPath,
    string PublicKey,
    string? WireGuardExecutablePath,
    string? WgExecutablePath,
    string? WgQuickExecutablePath,
    VpnNodeCapabilities LocalCapabilities,
    bool StartAutomatically,
    IReadOnlyList<VpnPeerDefinition> Peers,
    DateTimeOffset UpdatedAt)
{
    public VpnBackendMode BackendMode { get; init; } = VpnBackendMode.SystemInstallation;

    public string? UserspaceExecutablePath { get; init; }
}

public sealed record VpnEngineStatus(
    VpnRuntimeState State,
    string Message,
    string InterfaceName,
    string? ConfigurationPath = null);

public sealed record WireGuardInstallation(
    string? WireGuardExecutablePath,
    string? WgExecutablePath,
    string? WgQuickExecutablePath)
{
    public VpnBackendMode BackendMode { get; init; } = VpnBackendMode.SystemInstallation;

    public string? UserspaceExecutablePath { get; init; }

    public string? RuntimeDirectory { get; init; }

    public string? ValidationMessage { get; init; }

    public bool CanGenerateKeys => !string.IsNullOrWhiteSpace(WgExecutablePath);

    public bool CanManageTunnel => OperatingSystem.IsWindows()
        ? !string.IsNullOrWhiteSpace(WireGuardExecutablePath)
        : !string.IsNullOrWhiteSpace(WgQuickExecutablePath);
}

public static class VpnProfileFactory
{
    public static VpnProjectProfile CreateDefault(Guid projectId, string privateKeyPath)
    {
        byte[] bytes = projectId.ToByteArray();
        int secondOctet = 20 + bytes[0] % 180;
        int thirdOctet = bytes[1];
        string network = $"10.{secondOctet}.{thirdOctet}.0/24";
        string interfaceName = "cyrev-" + projectId.ToString("N")[..8];
        return new VpnProjectProfile(
            projectId,
            interfaceName,
            network,
            $"10.{secondOctet}.{thirdOctet}.1",
            51820 + bytes[2] % 1000,
            null,
            privateKeyPath,
            string.Empty,
            null,
            null,
            null,
            VpnNodeCapabilities.GeneralAccess,
            false,
            [],
            DateTimeOffset.UtcNow);
    }

    public static string FindAvailableAddress(VpnProjectProfile profile)
    {
        (uint network, int prefix) = VpnProfileValidator.ParseCidr(profile.NetworkCidr);
        HashSet<uint> used = profile.Peers.Select(peer => VpnProfileValidator.ToUInt32(ParseIpv4(peer.TunnelAddress))).ToHashSet();
        used.Add(VpnProfileValidator.ToUInt32(ParseIpv4(profile.LocalAddress)));
        uint broadcast = network | (uint.MaxValue >> prefix);
        for (uint candidate = network + 2; candidate < broadcast; candidate++)
        {
            if (!used.Contains(candidate))
            {
                return VpnProfileValidator.FromUInt32(candidate).ToString();
            }
        }

        throw new InvalidOperationException("Le réseau VPN du projet ne contient plus d'adresse disponible.");
    }

    private static IPAddress ParseIpv4(string value) =>
        IPAddress.TryParse(value, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetwork
            ? address
            : throw new InvalidDataException($"L'adresse VPN '{value}' est invalide.");
}

public static class VpnProfileValidator
{
    public static void Validate(VpnProjectProfile profile)
    {
        if (profile.ProjectId == Guid.Empty)
        {
            throw new InvalidDataException("Le profil VPN ne référence aucun projet.");
        }

        if (string.IsNullOrWhiteSpace(profile.InterfaceName) || profile.InterfaceName.Length > 15 ||
            profile.InterfaceName.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException("Le nom de l'interface VPN doit contenir au maximum 15 lettres, chiffres, '-' ou '_'.");
        }

        (uint network, int prefix) = ParseCidr(profile.NetworkCidr);
        if (prefix is < 16 or > 30 || !IsPrivate(network))
        {
            throw new InvalidDataException("CyRevision exige un réseau VPN IPv4 privé compris entre /16 et /30.");
        }

        ValidateAddressInNetwork(profile.LocalAddress, network, prefix, "locale");
        if (profile.ListenPort is < 1 or > 65535)
        {
            throw new InvalidDataException("Le port WireGuard doit être compris entre 1 et 65535.");
        }

        if (!string.IsNullOrWhiteSpace(profile.PublicKey))
        {
            ValidateWireGuardKey(profile.PublicKey, "publique locale");
        }

        HashSet<string> addresses = new(StringComparer.Ordinal) { profile.LocalAddress };
        HashSet<string> keys = new(StringComparer.Ordinal) { profile.PublicKey };
        foreach (VpnPeerDefinition peer in profile.Peers)
        {
            if (peer.PeerId == Guid.Empty || string.IsNullOrWhiteSpace(peer.DisplayName))
            {
                throw new InvalidDataException("Un pair VPN possède une identité incomplète.");
            }

            ValidateWireGuardKey(peer.PublicKey, $"publique de {peer.DisplayName}");
            ValidateAddressInNetwork(peer.TunnelAddress, network, prefix, $"de {peer.DisplayName}");
            if (!addresses.Add(peer.TunnelAddress) || !keys.Add(peer.PublicKey))
            {
                throw new InvalidDataException("Deux nœuds VPN utilisent la même adresse ou la même clé publique.");
            }

            ValidateEndpoint(peer.Endpoint);
            if (peer.PersistentKeepaliveSeconds is < 0 or > 65535)
            {
                throw new InvalidDataException("PersistentKeepalive doit être compris entre 0 et 65535 secondes.");
            }

            foreach (string route in peer.RoutedSubnets)
            {
                if (route is "0.0.0.0/0" or "::/0")
                {
                    throw new InvalidDataException("Les routes Internet complètes sont désactivées par sécurité.");
                }

                ParseCidr(route);
            }
        }

        ValidateEndpoint(profile.PublicEndpoint);
    }

    public static (uint Network, int Prefix) ParseCidr(string cidr)
    {
        string[] parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out int prefix) ||
            prefix is < 0 or > 32)
        {
            throw new InvalidDataException($"Le réseau IPv4 '{cidr}' est invalide.");
        }

        uint value = ToUInt32(address);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        if ((value & mask) != value)
        {
            throw new InvalidDataException($"'{cidr}' n'est pas une adresse de réseau.");
        }

        return (value, prefix);
    }

    public static uint ToUInt32(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    public static IPAddress FromUInt32(uint value) => new(new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    });

    public static void ValidateWireGuardKey(string key, string label)
    {
        try
        {
            if (Convert.FromBase64String(key).Length != 32)
            {
                throw new InvalidDataException($"La clé WireGuard {label} doit contenir 32 octets.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"La clé WireGuard {label} n'est pas en Base64.", exception);
        }
    }

    private static void ValidateAddressInNetwork(string value, uint network, int prefix, string label)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException($"L'adresse VPN {label} est invalide.");
        }

        uint numeric = ToUInt32(address);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        uint broadcast = network | ~mask;
        if ((numeric & mask) != network || numeric == network || numeric == broadcast)
        {
            throw new InvalidDataException($"L'adresse VPN {label} n'appartient pas au réseau utilisable du projet.");
        }
    }

    private static bool IsPrivate(uint network) =>
        (network & 0xff000000) == 0x0a000000 ||
        (network & 0xfff00000) == 0xac100000 ||
        (network & 0xffff0000) == 0xc0a80000;

    private static void ValidateEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        int separator = endpoint.LastIndexOf(':');
        if (separator < 1 || !int.TryParse(endpoint[(separator + 1)..], out int port) || port is < 1 or > 65535)
        {
            throw new InvalidDataException($"Le point d'accès WireGuard '{endpoint}' doit être au format hôte:port.");
        }
    }
}
