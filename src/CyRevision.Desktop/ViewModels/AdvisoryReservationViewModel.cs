using CyRevision.Sync;

namespace CyRevision.Desktop.ViewModels;

public sealed class AdvisoryReservationViewModel
{
    public AdvisoryReservationViewModel(AdvisoryReservation reservation, DateTimeOffset now)
    {
        Reservation = reservation;
        IsExpired = reservation.IsExpired(now);
    }

    public AdvisoryReservation Reservation { get; }

    public Guid ReservationId => Reservation.ReservationId;

    public string Asset => Reservation.AssetPath;

    public string File => Reservation.RelativePath;

    public string Owner => Reservation.OwnerName;

    public string Machine => Reservation.MachineName;

    public string Note => Reservation.Note;

    public DateTimeOffset UpdatedAt => Reservation.UpdatedAtUtc.ToLocalTime();

    public DateTimeOffset ExpiresAt => Reservation.ExpiresAtUtc.ToLocalTime();

    public bool IsExpired { get; }

    public string State => IsExpired ? "Expirée" : "En cours";
}
