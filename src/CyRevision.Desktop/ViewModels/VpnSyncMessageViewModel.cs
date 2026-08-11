using CyRevision.Vpn;

namespace CyRevision.Desktop.ViewModels;

public sealed class VpnSyncMessageViewModel
{
    public VpnSyncMessageViewModel(VpnSyncMessage message) => Message = message;

    public VpnSyncMessage Message { get; }

    public string Kind => Message.Envelope.Kind.ToString();

    public string Sender => Message.Envelope.SenderDeviceId.ToString("N")[..8];

    public string Created => Message.Envelope.CreatedAt.ToLocalTime().ToString("g");

    public string Expires => Message.Envelope.ExpiresAt.ToLocalTime().ToString("g");

    public string Summary => Message.Summary;
}
