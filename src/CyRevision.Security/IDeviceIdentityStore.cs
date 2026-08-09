namespace CyRevision.Security;

public interface IDeviceIdentityStore : IDisposable
{
    DeviceIdentity Identity { get; }

    string Sign(ReadOnlySpan<byte> data);

    bool Verify(ReadOnlySpan<byte> data, string signature);
}
