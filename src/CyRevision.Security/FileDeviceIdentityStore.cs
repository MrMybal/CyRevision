using System.Security.Cryptography;
using System.Text.Json;

namespace CyRevision.Security;

public sealed class FileDeviceIdentityStore : IDeviceIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ECDsa _signingKey;

    private FileDeviceIdentityStore(DeviceIdentity identity, ECDsa signingKey)
    {
        Identity = identity;
        _signingKey = signingKey;
    }

    public DeviceIdentity Identity { get; }

    public static async Task<FileDeviceIdentityStore> OpenOrCreateAsync(
        string directory,
        string displayName,
        string syncthingDeviceId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        string identityPath = Path.Combine(root, "device-identity.json");
        string keyPath = Path.Combine(root, "device-signing-key.pk8");

        if (File.Exists(identityPath) != File.Exists(keyPath))
        {
            throw new InvalidDataException("The device identity is incomplete; both identity and private key are required.");
        }

        if (File.Exists(identityPath))
        {
            await using FileStream identityStream = new(identityPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DeviceIdentity identity = await JsonSerializer.DeserializeAsync<DeviceIdentity>(identityStream, JsonOptions, cancellationToken)
                                      ?? throw new InvalidDataException("The device identity file is invalid.");
            StoredEcKey storedKey = JsonSerializer.Deserialize<StoredEcKey>(
                                       await File.ReadAllBytesAsync(keyPath, cancellationToken),
                                       JsonOptions)
                                   ?? throw new InvalidDataException("The device private key is invalid.");
            ECDsa existingKey = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = Convert.FromBase64String(storedKey.D),
                Q = new ECPoint
                {
                    X = Convert.FromBase64String(storedKey.Qx),
                    Y = Convert.FromBase64String(storedKey.Qy)
                }
            });
            string exportedPublicKey = Convert.ToBase64String(existingKey.ExportSubjectPublicKeyInfo());
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(identity.SigningPublicKey),
                    Convert.FromBase64String(exportedPublicKey)))
            {
                existingKey.Dispose();
                throw new CryptographicException("The device identity does not match its private key.");
            }

            return new FileDeviceIdentityStore(identity, existingKey);
        }

        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        DeviceIdentity created = new(
            userId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            displayName.Trim(),
            syncthingDeviceId.Trim(),
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        await WriteAtomicallyAsync(identityPath, JsonSerializer.SerializeToUtf8Bytes(created, JsonOptions), cancellationToken);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: true);
        StoredEcKey stored = new(
            Convert.ToBase64String(parameters.D!),
            Convert.ToBase64String(parameters.Q.X!),
            Convert.ToBase64String(parameters.Q.Y!));
        await WriteAtomicallyAsync(keyPath, JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions), cancellationToken);
        RestrictPrivateKeyPermissions(keyPath);
        return new FileDeviceIdentityStore(created, key);
    }

    public string Sign(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(_signingKey.SignData(data, HashAlgorithmName.SHA256));

    public bool Verify(ReadOnlySpan<byte> data, string signature)
    {
        try
        {
            return _signingKey.VerifyData(data, Convert.FromBase64String(signature), HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Dispose() => _signingKey.Dispose();

    private static async Task WriteAtomicallyAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, contents, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void RestrictPrivateKeyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed record StoredEcKey(string D, string Qx, string Qy);
}
