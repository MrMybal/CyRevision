using CyRevision.Security;

namespace CyRevision.Desktop.ViewModels;

public sealed class PeerMemberViewModel
{
    public PeerMemberViewModel(MembershipCertificate certificate)
    {
        Certificate = certificate;
    }

    public MembershipCertificate Certificate { get; }

    public Guid DeviceId => Certificate.Device.DeviceId;

    public string DisplayName => Certificate.Device.DisplayName;

    public string Role => Certificate.Role.ToString();

    public string DeviceIdShort => Certificate.Device.SyncthingDeviceId.Length > 12
        ? Certificate.Device.SyncthingDeviceId[..12] + "…"
        : Certificate.Device.SyncthingDeviceId;
}
