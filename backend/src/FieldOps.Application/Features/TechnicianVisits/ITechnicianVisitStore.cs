using System.Net;

namespace FieldOps.Application.Features.TechnicianVisits;

/// <summary>
/// Persistence of the technician's own visits. Every query filters by the session organization and an active
/// assignment of the caller's profile; only <see cref="TravelAsync"/> writes (mobile-job-details).
/// </summary>
public interface ITechnicianVisitStore
{
    /// <summary>The profile linked to the membership, or null; read before any visit.</summary>
    Task<TechnicianVisitProfile?> GetProfileAsync(Guid organizationId, Guid membershipId, CancellationToken cancellationToken);

    /// <summary>
    /// Assigned visits scheduled in <c>[from, to)</c> with a status other than unscheduled or cancelled, in
    /// route order (start, work order number, visit number). Instants are expressed in <paramref name="zoneId"/>.
    /// </summary>
    Task<IReadOnlyList<TodayVisit>> ListScheduledAsync(
        Guid organizationId,
        Guid technicianId,
        string zoneId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>One assigned visit of any date, not unscheduled or cancelled, with its Design 8 content; null otherwise.</summary>
    Task<FoundVisit?> FindAsync(
        Guid organizationId, Guid technicianId, string zoneId, Guid visitId, CancellationToken cancellationToken);

    /// <summary>
    /// Start travel or arrive in one transaction (BR-07 to BR-10): locks the caller's profile row, then the visit row,
    /// re-reads the state, applies the guards and writes. Nothing is written unless the outcome is a changed save.
    /// </summary>
    Task<TravelOutcome> TravelAsync(
        TravelActor actor, Guid visitId, TravelKind kind, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// The image of a photo of the latest completed assessment of the visit's originating request (BR-06); null for
    /// an unavailable visit, a photo of another assessment or organization, or a photo without content.
    /// </summary>
    Task<AssessmentPhotoImage?> FindAssessmentPhotoAsync(
        Guid organizationId, Guid technicianId, Guid visitId, Guid photoId, CancellationToken cancellationToken);
}

public sealed record FoundVisit(TodayVisit Visit, string? DispatchNote, TechnicianVisitExtras Extras);

public enum TravelKind
{
    Start,
    Arrive,
}

/// <summary>The caller of a travel mutation; <c>ZoneId</c> is the BR-04 zone of the technician profile.</summary>
public sealed record TravelActor(Guid OrganizationId, Guid UserId, Guid TechnicianId, string ZoneId, IPAddress? IpAddress);

public enum TravelOutcomeKind
{
    Saved,
    NotFound,
    NotPrimary,
    StatusInvalid,
    NotToday,
    AnotherActive,
}

/// <summary><c>Found</c> and <c>Changed</c> are set only for <see cref="TravelOutcomeKind.Saved"/>; <c>Email</c> only when a customer email is due.</summary>
public sealed record TravelOutcome(
    TravelOutcomeKind Kind, FoundVisit? Found = null, bool Changed = false, TravelEmail? Email = null);

/// <summary>Sends the customer "on the way" email after the commit; returns false when delivery failed (logged, never thrown).</summary>
public interface ITravelNotifier
{
    Task<bool> SendAsync(TravelEmail email, CancellationToken cancellationToken);
}
