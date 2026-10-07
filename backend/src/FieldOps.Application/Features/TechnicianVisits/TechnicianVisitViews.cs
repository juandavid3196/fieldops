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
    int PlannedMaterialsCount);

/// <summary><see cref="TodayVisit"/> plus the local date, zone and dispatch note; fields are additive.</summary>
public sealed record TechnicianVisitDetail : TodayVisit
{
    public TechnicianVisitDetail(TodayVisit visit, string date, string timezone, string? dispatchNote)
        : base(visit)
    {
        Date = date;
        Timezone = timezone;
        DispatchNote = dispatchNote;
    }

    public string Date { get; }

    public string Timezone { get; }

    public string? DispatchNote { get; }
}

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
}

public static class TechnicianVisitCodes
{
    public const string ProfileNotLinked = "technician_profile_not_linked";

    public const string TechnicianInactive = "technician_inactive";
}

/// <summary>Outcome of a technician visit use case, mapped to HTTP by the controller.</summary>
public sealed record TechnicianVisitResult<T>(TechnicianVisitResultKind Kind, T? Value = default, string? Code = null)
{
    public static TechnicianVisitResult<T> Ok(T value) => new(TechnicianVisitResultKind.Succeeded, value);

    public static TechnicianVisitResult<T> NotFound(string? code = null) => new(TechnicianVisitResultKind.NotFound, Code: code);

    public static TechnicianVisitResult<T> Forbidden(string code) => new(TechnicianVisitResultKind.Forbidden, Code: code);
}
