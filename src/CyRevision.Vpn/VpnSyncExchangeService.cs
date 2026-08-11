using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Vpn;

public enum VpnSyncMessageKind
{
    Invitation,
    JoinResponse
}

public sealed record VpnSyncEnvelope(
    int Version,
    Guid MessageId,
    VpnSyncMessageKind Kind,
    Guid ProjectId,
    Guid InvitationId,
    Guid SenderDeviceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string PayloadSha256,
    string Payload);

public sealed record VpnSyncMessage(
    string Path,
    VpnSyncEnvelope Envelope,
    string Summary);

public sealed class VpnSyncExchangeService
{
    private const int MaximumPayloadBytes = 256 * 1024;
    private const int MaximumEnvelopeBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "privateKey",
        "privateKeyPath",
        "apiKey",
        "token",
        "password",
        "secret",
        "webhook",
        "webhookUrl"
    };

    public async Task<VpnSyncMessage> PublishAsync(
        string exchangeDirectory,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (payloadBytes.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The VPN Sync message is too large.");
        }

        RejectSecrets(payload);
        VpnSyncEnvelope envelope = CreateEnvelope(payload, payloadBytes);
        string directory = GetBootstrapDirectory(exchangeDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"{envelope.CreatedAt:yyyyMMddTHHmmssfffZ}-{envelope.MessageId:N}.vpn.json");
        string temporaryPath = path + ".new";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: false);
        return new VpnSyncMessage(path, envelope, BuildSummary(envelope));
    }

    public async Task<IReadOnlyList<VpnSyncMessage>> ListAsync(
        string exchangeDirectory,
        CancellationToken cancellationToken = default)
    {
        string directory = GetBootstrapDirectory(exchangeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<VpnSyncMessage> messages = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.vpn.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                FileInfo info = new(path);
                if (info.Length is <= 0 or > MaximumEnvelopeBytes)
                {
                    continue;
                }

                await using FileStream stream = File.OpenRead(path);
                VpnSyncEnvelope? envelope = await JsonSerializer.DeserializeAsync<VpnSyncEnvelope>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                if (envelope is null)
                {
                    continue;
                }

                ValidateEnvelope(envelope);
                messages.Add(new VpnSyncMessage(path, envelope, BuildSummary(envelope)));
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or
                                              UnauthorizedAccessException or ArgumentException or
                                              System.Security.Cryptography.CryptographicException)
            {
                // A corrupt or untrusted synchronized file is ignored. Loading still validates the signed payload again.
            }
        }

        return messages;
    }

    public string LoadPayload(VpnSyncMessage message)
    {
        ValidateEnvelope(message.Envelope);
        return message.Envelope.Payload;
    }

    private static VpnSyncEnvelope CreateEnvelope(string payload, byte[] payloadBytes)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The VPN Sync payload must be a signed invitation or join response object.");
        }

        if (document.RootElement.TryGetProperty("invitation", out _))
        {
            SignedVpnInvitation invitation = VpnPeerExchangeCodec.ImportInvitation(payload);
            VpnPeerExchangeCodec.ValidateInvitation(invitation);
            return new VpnSyncEnvelope(
                1,
                Guid.NewGuid(),
                VpnSyncMessageKind.Invitation,
                invitation.Invitation.ProjectId,
                invitation.Invitation.InvitationId,
                invitation.IssuerIdentity.DeviceId,
                DateTimeOffset.UtcNow,
                invitation.Invitation.ExpiresAt,
                Sha256(payloadBytes),
                payload);
        }

        if (!document.RootElement.TryGetProperty("invitationOffer", out _))
        {
            throw new InvalidDataException("The VPN Sync payload must be a signed invitation or join response.");
        }

        VpnJoinResponse response = VpnPeerExchangeCodec.ImportJoinResponse(payload);
        VpnPeerExchangeCodec.ValidateJoinResponseSignature(response);
        return new VpnSyncEnvelope(
            1,
            Guid.NewGuid(),
            VpnSyncMessageKind.JoinResponse,
            response.InvitationOffer.Invitation.ProjectId,
            response.InvitationOffer.Invitation.InvitationId,
            response.JoiningIdentity.DeviceId,
            DateTimeOffset.UtcNow,
            response.InvitationOffer.Invitation.ExpiresAt,
            Sha256(payloadBytes),
            payload);
    }

    private static void ValidateEnvelope(VpnSyncEnvelope envelope)
    {
        if (envelope.Version != 1 || envelope.MessageId == Guid.Empty ||
            envelope.ProjectId == Guid.Empty || envelope.InvitationId == Guid.Empty ||
            envelope.SenderDeviceId == Guid.Empty || envelope.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            envelope.ExpiresAt <= envelope.CreatedAt)
        {
            throw new InvalidDataException("The synchronized VPN envelope metadata is invalid.");
        }

        string payload = envelope.Payload ?? string.Empty;
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (payloadBytes.Length is <= 0 or > MaximumPayloadBytes ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Sha256(payloadBytes)),
                Encoding.ASCII.GetBytes(envelope.PayloadSha256?.ToLowerInvariant() ?? string.Empty)))
        {
            throw new InvalidDataException("The synchronized VPN payload hash is invalid.");
        }

        RejectSecrets(payload);
        if (envelope.Kind == VpnSyncMessageKind.Invitation)
        {
            SignedVpnInvitation invitation = VpnPeerExchangeCodec.ImportInvitation(payload);
            VpnPeerExchangeCodec.ValidateInvitation(invitation);
            if (invitation.Invitation.ProjectId != envelope.ProjectId ||
                invitation.Invitation.InvitationId != envelope.InvitationId ||
                invitation.IssuerIdentity.DeviceId != envelope.SenderDeviceId ||
                invitation.Invitation.ExpiresAt != envelope.ExpiresAt)
            {
                throw new InvalidDataException("The synchronized invitation metadata does not match its signed payload.");
            }

            return;
        }

        VpnJoinResponse response = VpnPeerExchangeCodec.ImportJoinResponse(payload);
        VpnPeerExchangeCodec.ValidateJoinResponseSignature(response);
        if (response.InvitationOffer.Invitation.ProjectId != envelope.ProjectId ||
            response.InvitationOffer.Invitation.InvitationId != envelope.InvitationId ||
            response.JoiningIdentity.DeviceId != envelope.SenderDeviceId ||
            response.InvitationOffer.Invitation.ExpiresAt != envelope.ExpiresAt)
        {
            throw new InvalidDataException("The synchronized response metadata does not match its signed payload.");
        }
    }

    private static void RejectSecrets(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            Inspect(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The VPN Sync payload is not valid JSON.", exception);
        }

        return;

        static void Inspect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (ForbiddenPropertyNames.Contains(property.Name))
                    {
                        throw new InvalidDataException(
                            $"The VPN Sync payload contains forbidden secret field '{property.Name}'.");
                    }

                    Inspect(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    Inspect(child);
                }
            }
        }
    }

    private static string GetBootstrapDirectory(string exchangeDirectory)
    {
        string root = Path.GetFullPath(exchangeDirectory);
        return Path.Combine(root, "vpn-bootstrap", "messages");
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string BuildSummary(VpnSyncEnvelope envelope) =>
        $"{envelope.Kind} · {envelope.SenderDeviceId.ToString("N")[..8]} · " +
        $"expires {envelope.ExpiresAt.ToLocalTime():g}";
}
