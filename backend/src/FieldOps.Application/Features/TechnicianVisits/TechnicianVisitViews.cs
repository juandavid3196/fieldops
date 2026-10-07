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

public sealed record PlannedMaterialView(string Description, decimal Quantity, string Unit, string Source);

public sealed record VisitTaskView(Guid Id, string Label, bool IsRequired, bool IsCompleted);

public sealed record AssessmentPhotoRef(Guid Id);

/// <summary>The latest completed assessment of the originating request (BR-06); never carries internal notes or photo content.</summary>
public sealed record VisitAssessmentView(
    DateTimeOffset CompletedAt, string? Diagnosis, string? RecommendedScope, IReadOnlyList<AssessmentPhotoRef> Photos);

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
    VisitAssessmentView? Assessment);

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
}

/// <summary>Response of start-travel and arrive; <c>Changed</c> is false on an idempotent repeat.</summary>
public sealed record TravelResult(bool Changed, TechnicianVisitDetail Visit);

/// <summary>The stored image of an assessment photo (BR-06).</summary>
public sealed record AssessmentPhotoImage(string MimeType, byte[] Content);

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
}

public static class TechnicianVisitCodes
{
    public const string ProfileNotLinked = "technician_profile_not_linked";

    public const string TechnicianInactive = "technician_inactive";

    public const string NotPrimaryTechnician = "not_primary_technician";

    public const string VisitStatusInvalid = "visit_status_invalid";

    public const string VisitNotToday = "visit_not_today";

    public const string AnotherVisitActive = "another_visit_active";
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

/// <summary>Outcome of a technician visit use case, mapped to HTTP by the controller.</summary>
public sealed record TechnicianVisitResult<T>(TechnicianVisitResultKind Kind, T? Value = default, string? Code = null)
{
    public static TechnicianVisitResult<T> Ok(T value) => new(TechnicianVisitResultKind.Succeeded, value);

    public static TechnicianVisitResult<T> NotFound(string? code = null) => new(TechnicianVisitResultKind.NotFound, Code: code);

    public static TechnicianVisitResult<T> Forbidden(string code) => new(TechnicianVisitResultKind.Forbidden, Code: code);

    public static TechnicianVisitResult<T> Conflict(string code) => new(TechnicianVisitResultKind.Conflict, Code: code);
}
