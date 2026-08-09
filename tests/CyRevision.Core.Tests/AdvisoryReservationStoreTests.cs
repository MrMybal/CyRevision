using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class AdvisoryReservationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CyRevisionReservationTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MultiplePeopleCanReserveTheSameAssetWithoutBlockingEachOther()
    {
        Guid projectId = Guid.NewGuid();
        Guid aliceId = Guid.NewGuid();
        Guid bobId = Guid.NewGuid();
        JsonAdvisoryReservationStore store = new(projectId, _root);

        await store.ReserveAsync(
            "/Game/Characters/Hero",
            "Content/Characters/Hero.uasset",
            aliceId,
            "Alice",
            "WORKSTATION-A",
            TimeSpan.FromMinutes(30));
        await store.ReserveAsync(
            "/Game/Characters/Hero",
            "Content/Characters/Hero.uasset",
            bobId,
            "Bob",
            "WORKSTATION-B",
            TimeSpan.FromMinutes(30));

        IReadOnlyList<AdvisoryReservation> reservations = await store.GetAllAsync(includeExpired: false);

        Assert.Equal(2, reservations.Count);
        Assert.Contains(reservations, reservation => reservation.OwnerId == aliceId);
        Assert.Contains(reservations, reservation => reservation.OwnerId == bobId);
    }

    [Fact]
    public async Task ReservingAgainRefreshesTheOwnersMarkerAndKeepsItsIdentity()
    {
        Guid ownerId = Guid.NewGuid();
        JsonAdvisoryReservationStore store = new(Guid.NewGuid(), _root);
        AdvisoryReservation first = await store.ReserveAsync(
            "/Game/Maps/Main",
            "Content/Maps/Main.umap",
            ownerId,
            "Alice",
            "WORKSTATION-A",
            TimeSpan.FromMinutes(5),
            "Lighting pass");

        AdvisoryReservation refreshed = await store.ReserveAsync(
            "/Game/Maps/Main",
            "Content/Maps/Main.umap",
            ownerId,
            "Alice",
            "WORKSTATION-A",
            TimeSpan.FromMinutes(30),
            "Lighting pass");

        Assert.Equal(first.ReservationId, refreshed.ReservationId);
        Assert.Equal(first.CreatedAtUtc, refreshed.CreatedAtUtc);
        Assert.True(refreshed.ExpiresAtUtc > first.ExpiresAtUtc);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task OwnerCanReleaseOnlyTheirOwnMarker()
    {
        Guid projectId = Guid.NewGuid();
        Guid aliceId = Guid.NewGuid();
        Guid bobId = Guid.NewGuid();
        JsonAdvisoryReservationStore store = new(projectId, _root);
        await store.ReserveAsync("/Game/T_A", "Content/T_A.uasset", aliceId, "Alice", "A", TimeSpan.FromMinutes(30));
        await store.ReserveAsync("/Game/T_A", "Content/T_A.uasset", bobId, "Bob", "B", TimeSpan.FromMinutes(30));

        Assert.True(await store.ReleaseAsync(aliceId, "/Game/T_A"));
        IReadOnlyList<AdvisoryReservation> remaining = await store.GetAllAsync();

        AdvisoryReservation reservation = Assert.Single(remaining);
        Assert.Equal(bobId, reservation.OwnerId);
    }

    [Fact]
    public async Task ExpiredMarkersCanBeHiddenAndCleaned()
    {
        Guid projectId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_root, "reservations"));
        AdvisoryReservation expired = new(
            AdvisoryReservation.CurrentSchemaVersion,
            Guid.NewGuid(),
            projectId,
            "/Game/Old",
            "Content/Old.uasset",
            Guid.NewGuid(),
            "Alice",
            "A",
            string.Empty,
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "reservations", "expired.json"),
            System.Text.Json.JsonSerializer.Serialize(expired, new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web)));
        JsonAdvisoryReservationStore store = new(projectId, _root);

        Assert.Empty(await store.GetAllAsync(includeExpired: false));
        Assert.Single(await store.GetAllAsync(includeExpired: true));
        Assert.Equal(1, await store.RemoveExpiredAsync());
        Assert.Empty(await store.GetAllAsync(includeExpired: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
