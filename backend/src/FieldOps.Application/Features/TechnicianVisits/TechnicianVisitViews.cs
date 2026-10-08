using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.TechnicianVisits;

public sealed record TodayAddress(
    string Line1, string? Line2, string City, string? StateRegion, string? PostalCode, string CountryCode);

/// <summary>One visit of the caller (technician-todays-jobs API contract). Instants carry the offset of the profile zone.</summary>
public record TodayVisit(
    Guid VisitId,
    int VisitNumber,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    string Status,
    DateTimeOffset Start,
    DateTimeOffset End,
    DateTimeOffset? ArrivalWindowStart,
    DateTimeOffset? ArrivalWindowEnd,
    short Priority,
    string ServiceCategory,
    string CustomerName,
    string? Phone,
    TodayAddress Address,
    decimal? Latitude,
    decimal? Longitude,
    int PlannedMaterialsCount,
    bool IsPrimary);

/// <summary>Access details of the job (mobile-job-details BR-03); the contact preference is email, sms, email_or_sms or null.</summary>
public sealed record VisitAccess(string? Instructions, string? ContactPreference, bool ActiveDamage);

/// <summary>Travel of the visit from its most recent travel time entry (BR-04).</summary>
public sealed record VisitTravel(DateTimeOffset? StartedAt, DateTimeOffset? ArrivedAt, int? DurationMinutes);

public sealed record PlannedMaterialView(
    Guid Id, string Description, decimal Quantity, string Unit, string Source, decimal UsedQuantity);

public sealed record VisitTaskView(
    Guid Id, string Label, bool IsRequired, bool IsCompleted, string? Notes, DateTimeOffset? CompletedAt);

/// <summary>An additional (non-planned) material of the visit (mobile-job-progress BR-10); never carries a price.</summary>
public sealed record AdditionalMaterialView(Guid Id, string Description, decimal Quantity, string Unit, Guid? CatalogItemId);

/// <summary>A photo of the visit (BR-11); the content is served only by the evidence endpoint.</summary>
public sealed record VisitEvidenceView(Guid Id, string Type, DateTimeOffset CreatedAt);

/// <summary>The open work or pause entry of the visit (BR-14).</summary>
public sealed record ActiveTimeEntry(string Type, DateTimeOffset StartedAt);

/// <summary>Closed work and pause seconds, the open entry and the estimate in minutes (BR-14).</summary>
public sealed record VisitTime(int WorkSeconds, int PauseSeconds, ActiveTimeEntry? ActiveEntry, int? EstimatedMinutes);

/// <summary>The progress content of the detail (mobile-job-progress BR-14).</summary>
public sealed record VisitProgressView(
    DateTimeOffset? ActualStartedAt,
    VisitTime Time,
    IReadOnlyList<AdditionalMaterialView> AdditionalMaterials,
    IReadOnlyList<VisitEvidenceView> Evidence,
    string? TechnicianNotes);

/// <summary>A material found by the catalog lookup (BR-10): no prices.</summary>
public sealed record MaterialCatalogItem(Guid Id, string Name, string Unit);

public sealed record AssessmentPhotoRef(Guid Id);

/// <summary>The latest completed assessment of the originating request (BR-06); never carries internal notes or photo content.</summary>
public sealed record VisitAssessmentView(
    DateTimeOffset CompletedAt, string? Diagnosis, string? RecommendedScope, IReadOnlyList<AssessmentPhotoRef> Photos);

/// <summary>Completion readiness of the visit (mobile-job-completion BR-11); never carries acknowledgment or signature data.</summary>
public sealed record VisitCompletionView(
    bool RequiredTasksComplete, bool HasBeforePhoto, bool HasAfterPhoto, bool Ready, bool CompletesWorkOrder);

/// <summary>The additive Design 8 content of the visit detail (BR-03 to BR-06). Instants carry the offset of the profile zone.</summary>
public sealed record TechnicianVisitExtras(
    string CustomerType,
    VisitAccess Access,
    string? Instructions,
    string Scope,
    IReadOnlyList<string> RequiredSkills,
    string? OfficePhone,
    VisitTravel Travel,
    IReadOnlyList<PlannedMaterialView> PlannedMaterials,
    IReadOnlyList<VisitTaskView> Tasks,
    VisitAssessmentView? Assessment,
    VisitProgressView Progress,
    string? PrimaryTechnicianName,
    VisitCompletionView Completion);

/// <summary><see cref="TodayVisit"/> plus the local date, zone, dispatch note and the Design 8 content; fields are additive.</summary>
public sealed record TechnicianVisitDetail : TodayVisit
{
    public TechnicianVisitDetail(
        TodayVisit visit, string date, string timezone, string? dispatchNote, TechnicianVisitExtras extras)
        : base(visit)
    {
        Date = date;
        Timezone = timezone;
        DispatchNote = dispatchNote;
        CustomerType = extras.CustomerType;
        Access = extras.Access;
        Instructions = extras.Instructions;
        Scope = extras.Scope;
        RequiredSkills = extras.RequiredSkills;
        OfficePhone = extras.OfficePhone;
        Travel = extras.Travel;
        PlannedMaterials = extras.PlannedMaterials;
        Tasks = extras.Tasks;
        Assessment = extras.Assessment;
        ActualStartedAt = extras.Progress.ActualStartedAt;
        Time = extras.Progress.Time;
        AdditionalMaterials = extras.Progress.AdditionalMaterials;
        Evidence = extras.Progress.Evidence;
        TechnicianNotes = extras.Progress.TechnicianNotes;
        PrimaryTechnicianName = extras.PrimaryTechnicianName;
        Completion = extras.Completion;
    }

    public string Date { get; }

    public string Timezone { get; }

    public string? DispatchNote { get; }

    public string CustomerType { get; }

    public VisitAccess Access { get; }

    public string? Instructions { get; }

    public string Scope { get; }

    public IReadOnlyList<string> RequiredSkills { get; }

    public string? OfficePhone { get; }

    public VisitTravel Travel { get; }

    public IReadOnlyList<PlannedMaterialView> PlannedMaterials { get; }

    public IReadOnlyList<VisitTaskView> Tasks { get; }

    public VisitAssessmentView? Assessment { get; }

    public DateTimeOffset? ActualStartedAt { get; }

    public VisitTime Time { get; }

    public IReadOnlyList<AdditionalMaterialView> AdditionalMaterials { get; }

    public IReadOnlyList<VisitEvidenceView> Evidence { get; }

    public string? TechnicianNotes { get; }

    public string? PrimaryTechnicianName { get; }

    public VisitCompletionView Completion { get; }
}

/// <summary>Response of start-travel and arrive; <c>Changed</c> is false on an idempotent repeat.</summary>
public sealed record TravelResult(bool Changed, TechnicianVisitDetail Visit);

/// <summary>Response of start-job, pause and resume; <c>Changed</c> is false on an idempotent repeat (mobile-job-progress BR-03, BR-05).</summary>
public sealed record VisitActionResult(bool Changed, TechnicianVisitDetail Visit);

/// <summary>The stored image of an assessment photo (BR-06).</summary>
public sealed record AssessmentPhotoImage(string MimeType, byte[] Content);

/// <summary>The stored image of a visit photo (mobile-job-progress BR-12).</summary>
public sealed record VisitEvidenceImage(string MimeType, byte[] Content);

public sealed record TodayTechnician(string FirstName, string Initials, string? ColorHex);

public sealed record TodayMetrics(int Jobs, int ScheduledMinutes, int Completed, int Remaining);

public sealed record TodayJobs(
    string Date,
    string Timezone,
    TodayTechnician Technician,
    TodayMetrics Metrics,
    Guid? NextVisitId,
    IReadOnlyList<TodayVisit> Visits);

/// <summary>The caller's profile, loaded before any visit (BR-02). <c>ZoneId</c> is the BR-04 zone.</summary>
public sealed record TechnicianVisitProfile(
    Guid Id,
    TechnicianStatus Status,
    string FirstName,
    string LastName,
    string? ColorHex,
    string ZoneId);

public enum TechnicianVisitResultKind
{
    Succeeded,
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
}

public static class TechnicianVisitCodes
{
    public const string ProfileNotLinked = "technician_profile_not_linked";

    public const string TechnicianInactive = "technician_inactive";

    public const string NotPrimaryTechnician = "not_primary_technician";

    public const string VisitStatusInvalid = "visit_status_invalid";

    public const string VisitNotToday = "visit_not_today";

    public const string AnotherVisitActive = "another_visit_active";

    public const string TaskLimitReached = "task_limit_reached";

    public const string MaterialLimitReached = "material_limit_reached";

    public const string EvidenceLimitReached = "evidence_limit_reached";

    public const string CompletionRequirementsUnmet = "completion_requirements_unmet";
}

/// <summary>Safe ProblemDetails titles by code (mobile-job-details Error behavior); a null code is the identical 404.</summary>
public static class TechnicianVisitMessages
{
    public const string NotAvailable = "This job isn't available.";

    public static string Title(string? code) => code switch
    {
        TechnicianVisitCodes.ProfileNotLinked => "Your team profile isn't linked yet.",
        TechnicianVisitCodes.TechnicianInactive => "Your technician profile is inactive.",
        TechnicianVisitCodes.NotPrimaryTechnician => "The primary technician manages travel for this job.",
        TechnicianVisitCodes.VisitStatusInvalid => "Travel cannot be managed for this job in its current state.",
        TechnicianVisitCodes.VisitNotToday => "Travel can only be started on the day of the visit.",
        TechnicianVisitCodes.AnotherVisitActive => "You're already traveling to or working on another job.",
        _ => NotAvailable,
    };
}

/// <summary>Safe ProblemDetails titles of the mobile-job-progress mutations (BR-02, BR-03, BR-05, BR-06, limits).</summary>
public static class VisitProgressMessages
{
    public const string NotPrimary = "The primary technician manages this job.";

    public const string StartInvalid = "This job can't be started in its current state.";

    public const string PauseResumeInvalid = "This job can't be paused or resumed in its current state.";

    public const string EditInvalid = "This job can't be updated in its current state.";

    public const string TaskLimit = "This job already has the maximum number of tasks.";

    public const string MaterialLimit = "This job already has the maximum number of materials.";

    public const string EvidenceLimit = "This job already has the maximum number of photos.";

    public const string CompleteInvalid = "This job can't be completed in its current state.";

    public const string CompletionRequirementsUnmet = "Complete required tasks and add before and after photos before completing this job.";
}

/// <summary>
/// Outcome of a technician visit use case, mapped to HTTP by the controller. <c>Message</c> overrides the title of
/// <see cref="TechnicianVisitMessages.Title"/>; <c>Errors</c> carries the field errors of an invalid request.
/// </summary>
public sealed record TechnicianVisitResult<T>(
    TechnicianVisitResultKind Kind,
    T? Value = default,
    string? Code = null,
    string? Message = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static TechnicianVisitResult<T> Ok(T value) => new(TechnicianVisitResultKind.Succeeded, value);

    public static TechnicianVisitResult<T> NotFound(string? code = null) => new(TechnicianVisitResultKind.NotFound, Code: code);

    public static TechnicianVisitResult<T> Forbidden(string code, string? message = null) =>
        new(TechnicianVisitResultKind.Forbidden, Code: code, Message: message);

    public static TechnicianVisitResult<T> Conflict(string code, string? message = null) =>
        new(TechnicianVisitResultKind.Conflict, Code: code, Message: message);

    public static TechnicianVisitResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(TechnicianVisitResultKind.Invalid, Errors: errors);
}
