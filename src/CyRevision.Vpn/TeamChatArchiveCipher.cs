using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Vpn;

internal static class TeamChatArchiveCipher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteJsonAsync<T>(
        string path,
        T value,
        string token,
        bool encrypt,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (encrypt) payload = JsonSerializer.SerializeToUtf8Bytes(Encrypt(payload, token), JsonOptions);
        await WriteAtomicAsync(path, payload, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T?> ReadJsonAsync<T>(
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        byte[] payload = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("cyRevisionEncrypted", out JsonElement encrypted) && encrypted.GetInt32() == 1)
        {
            EncryptedEnvelope envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(payload, JsonOptions)
                                         ?? throw new InvalidDataException("Encrypted chat archive is invalid.");
            payload = Decrypt(envelope, token);
        }
        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    public static async Task WriteBytesAsync(
        string path,
        byte[] bytes,
        string token,
        bool encrypt,
        CancellationToken cancellationToken)
    {
        byte[] payload = encrypt
            ? JsonSerializer.SerializeToUtf8Bytes(Encrypt(bytes, token), JsonOptions)
            : bytes;
        await WriteAtomicAsync(path, payload, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadBytesAsync(
        string path,
        string token,
        bool encrypted,
        CancellationToken cancellationToken)
    {
        byte[] payload = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (!encrypted) return payload;
        EncryptedEnvelope envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(payload, JsonOptions)
                                     ?? throw new InvalidDataException("Encrypted chat attachment is invalid.");
        return Decrypt(envelope, token);
    }

    private static EncryptedEnvelope Encrypt(byte[] payload, string token)
    {
        byte[] key = DeriveKey(token);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[payload.Length];
        byte[] tag = new byte[16];
        using AesGcm aes = new(key, tag.Length);
        aes.Encrypt(nonce, payload, cipher, tag, Encoding.UTF8.GetBytes("CyRevision.TeamChat.v1"));
        CryptographicOperations.ZeroMemory(key);
        return new EncryptedEnvelope(1, nonce, cipher, tag);
    }

    private static byte[] Decrypt(EncryptedEnvelope envelope, string token)
    {
        if (envelope.CyRevisionEncrypted != 1 || envelope.Nonce.Length != 12 || envelope.Tag.Length != 16)
            throw new InvalidDataException("Encrypted chat envelope is invalid.");
        byte[] key = DeriveKey(token);
        byte[] plain = new byte[envelope.Ciphertext.Length];
        try
        {
            using AesGcm aes = new(key, envelope.Tag.Length);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plain,
                Encoding.UTF8.GetBytes("CyRevision.TeamChat.v1"));
            return plain;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("Unable to decrypt the chat archive. Verify the project access token.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("A chat token is required for archive encryption.");
        return SHA256.HashData(Encoding.UTF8.GetBytes("CyRevision.TeamChat.Key.v1:" + token));
    }

    private static async Task WriteAtomicAsync(string path, byte[] payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Environment.ProcessId + ".tmp";
        await File.WriteAllBytesAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed record EncryptedEnvelope(
        int CyRevisionEncrypted,
        byte[] Nonce,
        byte[] Ciphertext,
        byte[] Tag);
}
