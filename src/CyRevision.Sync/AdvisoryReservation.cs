using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyRevision.Sync;

/// <summary>
/// Informational ownership marker for an asset. It never changes file permissions,
/// performs a checkout, or creates a Git LFS lock.
/// </summary>
public sealed record AdvisoryReservation(
    int SchemaVersion,
    Guid ReservationId,
    Guid ProjectId,
    string AssetPath,
    string RelativePath,
    Guid OwnerId,
    string OwnerName,
    string MachineName,
    string Note,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc <= now;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported advisory reservation schema: {SchemaVersion}.");
        }

        if (ReservationId == Guid.Empty || ProjectId == Guid.Empty || OwnerId == Guid.Empty)
        {
            throw new InvalidDataException("Reservation, project and owner identifiers are required.");
        }

        ValidateText(AssetPath, nameof(AssetPath), 1024, required: true);
        ValidateText(RelativePath, nameof(RelativePath), 1024, required: false);
        ValidateText(OwnerName, nameof(OwnerName), 160, required: true);
        ValidateText(MachineName, nameof(MachineName), 160, required: false);
        ValidateText(Note, nameof(Note), 512, required: false);

        if (CreatedAtUtc > UpdatedAtUtc || UpdatedAtUtc > ExpiresAtUtc)
        {
            throw new InvalidDataException("Reservation timestamps are inconsistent.");
        }
    }

    private static void ValidateText(string value, string name, int maximumLength, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{name} is required.");
        }

        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"{name} is invalid.");
        }
    }
}

public interface IAdvisoryReservationStore
{
    Task<IReadOnlyList<AdvisoryReservation>> GetAllAsync(
        bool includeExpired = true,
        CancellationToken cancellationToken = default);

    Task<AdvisoryReservation> ReserveAsync(
        string assetPath,
        string relativePath,
        Guid ownerId,
        string ownerName,
        string machineName,
        TimeSpan lifetime,
        string note = "",
        CancellationToken cancellationToken = default);

    Task<int> RefreshOwnedAsync(
        Guid ownerId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        Guid ownerId,
        string assetPath,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseByIdAsync(Guid reservationId, CancellationToken cancellationToken = default);

    Task<int> RemoveExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// File-per-owner/asset store designed for an eventually-consistent shared folder.
/// Independent files avoid a central JSON file and therefore avoid merge conflicts.
/// </summary>
public sealed class JsonAdvisoryReservationStore : IAdvisoryReservationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Guid _projectId;
    private readonly string _reservationDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAdvisoryReservationStore(Guid projectId, string presenceDirectory)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project ID is required.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(presenceDirectory);
        _projectId = projectId;
        _reservationDirectory = Path.Combine(Path.GetFullPath(presenceDirectory), "reservations");
    }

    public async Task<IReadOnlyList<AdvisoryReservation>> GetAllAsync(
        bool includeExpired = true,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            IReadOnlyList<(string Path, AdvisoryReservation Value)> entries = await ReadAllUnsafeAsync(cancellationToken)
                .ConfigureAwait(false);
            return entries
                .Select(entry => entry.Value)
                .Where(reservation => includeExpired || !reservation.IsExpired(now))
                .GroupBy(reservation => reservation.ReservationId)
                .Select(group => group.OrderByDescending(item => item.UpdatedAtUtc).First())
                .OrderBy(reservation => reservation.IsExpired(now))
                .ThenBy(reservation => reservation.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reservation => reservation.OwnerName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AdvisoryReservation> ReserveAsync(
        string assetPath,
        string relativePath,
        Guid ownerId,
        string ownerName,
        string machineName,
        TimeSpan lifetime,
        string note = "",
        CancellationToken cancellationToken = default)
    {
        ValidateLifetime(lifetime);
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An owner ID is required.", nameof(ownerId));
        }

        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_reservationDirectory);
            string path = GetOwnedAssetPath(ownerId, normalizedAssetPath);
            AdvisoryReservation? existing = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AdvisoryReservation reservation = new(
                AdvisoryReservation.CurrentSchemaVersion,
                existing?.ReservationId ?? Guid.NewGuid(),
                _projectId,
                normalizedAssetPath,
                NormalizeRelativePath(relativePath),
                ownerId,
                ownerName.Trim(),
                machineName.Trim(),
                note.Trim(),
                existing?.CreatedAtUtc ?? now,
                now,
                now.Add(lifetime));
            reservation.Validate();
            await WriteAtomicallyAsync(path, reservation, cancellationToken).ConfigureAwait(false);
            return reservation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RefreshOwnedAsync(
        Guid ownerId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ValidateLifetime(lifetime);
        if (ownerId == Guid.Empty)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int refreshed = 0;
            foreach ((string path, AdvisoryReservation existing) in await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false))
            {
                if (existing.OwnerId != ownerId || existing.IsExpired(now))
                {
                    continue;
                }

                AdvisoryReservation updated = existing with
                {
                    UpdatedAtUtc = now,
                    ExpiresAtUtc = now.Add(lifetime)
                };
                await WriteAtomicallyAsync(path, updated, cancellationToken).ConfigureAwait(false);
                refreshed++;
            }

            return refreshed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ReleaseAsync(
        Guid ownerId,
        string assetPath,
        CancellationToken cancellationToken = default)
    {
        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool removed = false;
            foreach ((string path, AdvisoryReservation reservation) in await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reservation.OwnerId == ownerId &&
                    string.Equals(reservation.AssetPath, normalizedAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    removed = true;
                }
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ReleaseByIdAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool removed = false;
            foreach ((string path, AdvisoryReservation reservation) in await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reservation.ReservationId == reservationId)
                {
                    File.Delete(path);
                    removed = true;
                }
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int removed = 0;
            foreach ((string path, AdvisoryReservation reservation) in await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reservation.IsExpired(now))
                {
                    File.Delete(path);
                    removed++;
                }
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<(string Path, AdvisoryReservation Value)>> ReadAllUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_reservationDirectory))
        {
            return [];
        }

        List<(string Path, AdvisoryReservation Value)> entries = [];
        foreach (string path in Directory.EnumerateFiles(_reservationDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdvisoryReservation? reservation = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (reservation is not null && reservation.ProjectId == _projectId)
            {
                entries.Add((path, reservation));
            }
        }

        return entries;
    }

    private static async Task<AdvisoryReservation?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            AdvisoryReservation? reservation = await JsonSerializer.DeserializeAsync<AdvisoryReservation>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            reservation?.Validate();
            return reservation;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            // Syncthing can expose a temporary or conflicting file while it converges.
            return null;
        }
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        AdvisoryReservation reservation,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, reservation, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string GetOwnedAssetPath(Guid ownerId, string assetPath)
    {
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{_projectId:N}\n{ownerId:N}\n{assetPath.ToUpperInvariant()}"));
        return Path.Combine(_reservationDirectory, Convert.ToHexString(key).ToLowerInvariant() + ".json");
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        return assetPath.Trim().Replace('\\', '/');
    }

    private static string NormalizeRelativePath(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath) ? string.Empty : relativePath.Trim().Replace('\\', '/');

    private static void ValidateLifetime(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be between one minute and seven days.");
        }
    }
}
