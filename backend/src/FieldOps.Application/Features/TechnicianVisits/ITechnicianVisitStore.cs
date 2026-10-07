namespace FieldOps.Application.Features.TechnicianVisits;

/// <summary>
/// Read-only persistence of the technician's own visits. Every query filters by the session organization and an
/// active assignment of the caller's profile; nothing here writes.
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

    /// <summary>One assigned visit of any date, not unscheduled or cancelled; null otherwise.</summary>
    Task<FoundVisit?> FindAsync(
        Guid organizationId, Guid technicianId, string zoneId, Guid visitId, CancellationToken cancellationToken);
}

public sealed record FoundVisit(TodayVisit Visit, string? DispatchNote);
