using CyRevision.Vpn;

namespace CyRevision.Desktop.ViewModels;

public sealed class VpnPeerViewModel
{
    public VpnPeerViewModel(VpnPeerDefinition peer) => Peer = peer;

    public VpnPeerDefinition Peer { get; }
    public Guid PeerId => Peer.PeerId;
    public string DisplayName => Peer.DisplayName;
    public string TunnelAddress => Peer.TunnelAddress;
    public string Endpoint => Peer.Endpoint ?? "Connexion entrante";
    public string Capabilities => Peer.Capabilities.ToString();
}
