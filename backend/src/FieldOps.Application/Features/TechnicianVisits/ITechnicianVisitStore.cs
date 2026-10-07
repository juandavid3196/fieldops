using System.Net;
using FieldOps.Domain.WorkOrders;

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

    /// <summary>
    /// Lock-free, read-only access check of the mobile-job-progress mutations: the caller's primary flag and the
    /// visit status for a visible visit, otherwise null (the identical 404). Never authoritative: the locked
    /// transaction of <see cref="TransitionAsync"/> and <see cref="EditAsync"/> repeats the chain.
    /// </summary>
    Task<ProgressAccess?> GetProgressAccessAsync(
        Guid organizationId, Guid technicianId, Guid visitId, CancellationToken cancellationToken);

    /// <summary>
    /// Start job, pause or resume in one transaction (mobile-job-progress BR-03 to BR-07): locks the caller's profile
    /// row, then the visit row, re-reads the state, applies the guards and writes. Nothing is written for a refusal
    /// or an unchanged repeat.
    /// </summary>
    Task<ProgressOutcome> TransitionAsync(
        ProgressActor actor, Guid visitId, ProgressTransition transition, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>One task, material, photo or notes edit in one locked transaction (BR-06 to BR-13).</summary>
    Task<ProgressOutcome> EditAsync(
        ProgressActor actor, Guid visitId, VisitEdit edit, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// At most 20 active products of the organization whose name or SKU contains <paramref name="search"/>
    /// (case-insensitive), ordered by name (BR-10).
    /// </summary>
    Task<IReadOnlyList<MaterialCatalogItem>> SearchMaterialCatalogAsync(
        Guid organizationId, string search, CancellationToken cancellationToken);

    /// <summary>The content of a photo of a visible visit (BR-12); null for any other photo, visit or organization.</summary>
    Task<VisitEvidenceImage?> FindEvidenceAsync(
        Guid organizationId, Guid technicianId, Guid visitId, Guid evidenceId, CancellationToken cancellationToken);
}

/// <summary>The caller's primary flag and the status of a visible visit (BR-02, BR-06).</summary>
public sealed record ProgressAccess(bool IsPrimary, VisitStatus Status);

public enum ProgressTransition
{
    StartJob,
    Pause,
    Resume,
}

/// <summary>The caller of a mobile-job-progress mutation; the identifiers come from the session and the linked profile.</summary>
public sealed record ProgressActor(Guid OrganizationId, Guid UserId, Guid TechnicianId, string ZoneId, IPAddress? IpAddress);

/// <summary>One edit of the job progress; the store applies it inside the locked transaction.</summary>
public abstract record VisitEdit;

/// <summary><c>IsCompleted</c> and <c>Notes</c> are applied when not null; <c>Notes</c> is already trimmed, empty means clear.</summary>
public sealed record UpdateTaskEdit(Guid TaskId, bool? IsCompleted, string? Notes) : VisitEdit;

public sealed record AddTaskEdit(string Label) : VisitEdit;

public sealed record SetPlannedMaterialEdit(Guid PlannedMaterialId, decimal UsedQuantity) : VisitEdit;

/// <summary>Either <c>CatalogItemId</c> or <c>Description</c> with <c>Unit</c>.</summary>
public sealed record AddMaterialEdit(decimal Quantity, Guid? CatalogItemId, string? Description, string? Unit) : VisitEdit;

public sealed record SetMaterialEdit(Guid MaterialId, decimal Quantity) : VisitEdit;

/// <summary><c>Notes</c> is trimmed; null means empty.</summary>
public sealed record SetNotesEdit(string? Notes) : VisitEdit;

public sealed record AddEvidenceEdit(VisitEvidenceType Type, string FileName, string MimeType, byte[] Content) : VisitEdit;

public sealed record DeleteEvidenceEdit(Guid EvidenceId) : VisitEdit;

public enum ProgressOutcomeKind
{
    Saved,
    NotFound,
    NotPrimary,
    StatusInvalid,
    LimitReached,
}

/// <summary><c>Found</c> and <c>Changed</c> are set for <see cref="ProgressOutcomeKind.Saved"/>; <c>LimitCode</c> for <see cref="ProgressOutcomeKind.LimitReached"/>.</summary>
public sealed record ProgressOutcome(
    ProgressOutcomeKind Kind, FoundVisit? Found = null, bool Changed = false, string? LimitCode = null);

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
