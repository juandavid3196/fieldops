using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Dispatch;

/// <summary>Machine codes of the dispatch calendar (dispatch-calendar BR-10, BR-11, BR-13, BR-14).</summary>
public static class DispatchCodes
{
    public const string MissingSkills = "missing_skills";

    public const string TimeOff = "time_off";

    public const string Break = "break";

    public const string OutsideAvailability = "outside_availability";

    public const string Overlap = "overlap";

    public const string VisitLocked = "visit_locked";

    public const string VisitChanged = "visit_changed";

    public const string SchedulingConflicts = "scheduling_conflicts";

    public const string AtStart = "at_start";

    public const string StartPlus1h = "start_plus_1h";

    public const string StartPlus2h = "start_plus_2h";

    public const string Around1h = "around_1h";

    public static readonly string[] ArrivalWindows = [AtStart, StartPlus1h, StartPlus2h, Around1h];

    public static readonly string[] Views = ["day", "week"];

    public static readonly string[] StatusFilters = ["all", "unassigned", "assigned", "conflicts"];

    public static readonly string[] UnscheduledFilters = ["all", "today", "overdue"];
}

/// <summary>Messages and limits of the dispatch calendar.</summary>
public static class DispatchMessages
{
    public const int MaxTechnicians = 5;

    public const int MaxSearchLength = 100;

    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 25;

    public const int MaxNoteLength = 1000;

    public const int MinReasonLength = 10;

    public const int MaxReasonLength = 500;

    public const string VisitLockedTitle = "This visit has started or is closed and can't be changed.";

    public const string VisitChangedTitle = "This visit was changed by someone else. Reload to see the latest version.";

    public const string SchedulingConflictsTitle = "This schedule has conflicts. Enter a reason to confirm despite the conflicts.";

    public const string DateRequired = "Select a date.";

    public const string DatePast = "Select today or a later date.";

    public const string StartRequired = "Select a start time.";

    public const string EndRequired = "Select an end time.";

    public const string EndBeforeStart = "End time must be after start time.";

    public const string StartPast = "Select a time in the future.";

    public const string ArrivalWindowInvalid = "Select an arrival window.";

    public const string TechniciansTooMany = "Select up to 5 technicians.";

    public const string TechniciansInvalid = "Choose active technicians from this work order's branch.";

    public const string PrimaryRequired = "Choose a primary technician.";

    public const string NoteTooLong = "Note must be 1000 characters or fewer.";

    public const string ReasonTooShort = "Enter at least 10 characters.";

    public const string ReasonTooLong = "Reason must be 500 characters or fewer.";

    public const string NoEmail = "This customer has no email address.";

    public const string OptionRequired = "Select an option.";

    public const string UpdatedAtInvalid = "Enter a valid value.";

    public const string BranchInvalid = "Select a branch.";

    public const string ViewInvalid = "Select day or week.";

    public const string DateInvalid = "Enter a valid date.";

    public const string SkillInvalid = "Select a valid skill.";

    public const string StatusInvalid = "Select a valid status.";

    public const string FilterInvalid = "Select a valid filter.";

    public const string SearchTooLong = "Search must be 100 characters or fewer.";

    public const string PagingInvalid = "Enter a valid page.";

    public static string ConflictTitle(string code) => code switch
    {
        DispatchCodes.VisitLocked => VisitLockedTitle,
        DispatchCodes.SchedulingConflicts => SchedulingConflictsTitle,
        _ => VisitChangedTitle,
    };
}

public sealed record DispatchNamed(Guid Id, string Name);

public sealed record DispatchBranchOption(Guid Id, string Name, string Timezone, bool IsMain);

/// <summary>GET /dispatch/options.</summary>
public sealed record DispatchOptionsView(
    IReadOnlyList<DispatchBranchOption> Branches, Guid? DefaultBranchId, IReadOnlyList<DispatchNamed> Skills);

public sealed record DispatchConflict(Guid? TechnicianId, string Code, DateTimeOffset? From, DateTimeOffset? To, string Label);

public sealed record DispatchCheck(bool Passed, string? Code, string Label);

public sealed record TechnicianChecks(Guid TechnicianId, DispatchCheck Availability, DispatchCheck Overlap);

public sealed record TechnicianImpact(
    Guid TechnicianId, int Jobs, double ScheduledMinutes, double AvailableMinutes, string Previous, string Next);

public sealed record TechnicianRanking(Guid TechnicianId, bool BestMatch, int ConflictCount, int? LoadPercent);

/// <summary>POST /dispatch/visits/{id}/evaluation.</summary>
public sealed record VisitEvaluation(
    IReadOnlyList<DispatchConflict> Conflicts,
    DispatchCheck Skills,
    IReadOnlyList<TechnicianChecks> Checks,
    IReadOnlyList<TechnicianImpact> Impact,
    IReadOnlyList<TechnicianRanking> Ranking);

public sealed record DispatchLoad(double ScheduledMinutes, double AvailableMinutes, string State);

public sealed record DispatchCalendarDay(
    DateOnly Date,
    IReadOnlyList<CalendarRange> Availability,
    IReadOnlyList<CalendarRange> Breaks,
    IReadOnlyList<CalendarRange> TimeOff);

public sealed record CalendarTechnicianView(
    Guid Id,
    string Name,
    string Initials,
    string? ColorHex,
    string? PrimarySkill,
    string? Tag,
    DispatchLoad Load,
    IReadOnlyList<DispatchCalendarDay> Days,
    IReadOnlyList<CalendarRange> Assessments);

public sealed record CalendarVisitView(
    Guid VisitId,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    string Street,
    string Status,
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<Guid> TechnicianIds,
    Guid? PrimaryTechnicianId,
    IReadOnlyList<DispatchConflict> Conflicts);

/// <summary>GET /dispatch/calendar.</summary>
public sealed record DispatchCalendarView(
    string Timezone,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<CalendarTechnicianView> Technicians,
    IReadOnlyList<CalendarVisitView> Visits);

public sealed record UnscheduledItemView(
    Guid VisitId,
    int VisitNumber,
    int? RecurrenceCount,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    string Priority,
    int? EstimatedDurationMinutes,
    string CustomerName,
    string Address,
    DateTimeOffset? PreferredStart,
    DateTimeOffset? PreferredEnd,
    bool IsOverdue,
    IReadOnlyList<DispatchNamed> Skills);

/// <summary>GET /dispatch/unscheduled.</summary>
public sealed record UnscheduledPageView(IReadOnlyList<UnscheduledItemView> Items, int Total);

public sealed record VisitRecurrence(int Count);

public sealed record DispatchWorkOrderView(
    Guid Id,
    string DisplayNumber,
    string Title,
    string Priority,
    int? EstimatedDurationMinutes,
    IReadOnlyList<DispatchNamed> RequiredSkills,
    int PlannedMaterialsCount);

public sealed record DispatchCustomerView(string Name, string Initials, string? Phone, string Address, bool HasEmail);

public sealed record DispatchValuesView(
    string? Date,
    string? Start,
    string? End,
    string ArrivalWindow,
    IReadOnlyList<Guid> TechnicianIds,
    Guid? PrimaryTechnicianId,
    string? DispatchNote,
    bool NotifyCustomer,
    bool SendTechnicianDetails);

public sealed record DispatchTechnicianOption(Guid Id, string Name, string Initials, string? ColorHex, string? PrimarySkill);

/// <summary>GET /dispatch/visits/{id}; also the visit of a dispatch result.</summary>
public sealed record VisitDispatchDetail(
    Guid VisitId,
    int VisitNumber,
    VisitRecurrence? Recurrence,
    string Status,
    DateTimeOffset UpdatedAt,
    bool CanManage,
    bool IsLocked,
    string Timezone,
    DispatchWorkOrderView WorkOrder,
    DispatchCustomerView Customer,
    DispatchValuesView Values,
    IReadOnlyList<DispatchTechnicianOption> Technicians);

public sealed record VisitDispatchResult(
    VisitDispatchDetail Visit, string WorkOrderStatus, bool Notified, Guid? MaterializedVisitId);

/// <summary>Everything the handler needs to validate a request before the transaction.</summary>
public sealed record DispatchVisitContext(
    Guid VisitId,
    VisitStatus Status,
    bool IsLocked,
    string Timezone,
    DateTimeOffset? ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    bool HasEmail);

public sealed record CalendarQuery(
    Guid BranchId, string View, DateOnly Date, IReadOnlyList<Guid> TechnicianIds, Guid? SkillId, string Status);

public sealed record UnscheduledQuery(
    Guid BranchId, string Filter, string? Search, Guid? SkillId, int Page, int PageSize);

public sealed record EvaluationInput(
    DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<Guid> TechnicianIds, Guid? PrimaryTechnicianId);

/// <summary>A validated dispatch request (dispatch-calendar BR-11); the note and reason are trimmed.</summary>
public sealed record DispatchInput(
    DateTimeOffset Start,
    DateTimeOffset End,
    DateTimeOffset ArrivalStart,
    DateTimeOffset ArrivalEnd,
    IReadOnlyList<Guid> TechnicianIds,
    Guid? PrimaryTechnicianId,
    string? Note,
    bool NotifyCustomer,
    bool SendTechnicianDetails,
    string? OverrideReason,
    DateTimeOffset UpdatedAt);

public sealed record DispatchActor(Guid OrganizationId, Guid UserId, BranchScope Scope, IPAddress? IpAddress);

public enum VisitEmailKind
{
    Scheduled,
    Rescheduled,
    TechnicianUpdate,
}

/// <summary>One customer email of a dispatch (dispatch-calendar BR-17); never carries the note, the reason or technician contact data.</summary>
public sealed record VisitEmail(
    VisitEmailKind Kind,
    Guid VisitId,
    string RecipientEmail,
    string? ContactFirstName,
    string OrganizationName,
    string? OrganizationPhone,
    string DisplayNumber,
    string Title,
    string TimezoneId,
    DateTimeOffset Start,
    DateTimeOffset ArrivalStart,
    DateTimeOffset ArrivalEnd,
    IReadOnlyList<string>? TechnicianNames);

public sealed record DispatchSaved(VisitDispatchResult Result, VisitEmail? Email);

public abstract record DispatchOutcome<T>
{
    private DispatchOutcome()
    {
    }

    public sealed record Succeeded(T Value) : DispatchOutcome<T>;

    /// <summary>A missing, foreign or out-of-scope visit, branch or technician: one identical outcome.</summary>
    public sealed record NotFound : DispatchOutcome<T>;

    public sealed record Locked : DispatchOutcome<T>;

    public sealed record Changed : DispatchOutcome<T>;

    public sealed record Conflicts(IReadOnlyList<DispatchConflict> Items) : DispatchOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : DispatchOutcome<T>;
}

/// <summary>A technician and everything the slot rules need: schedule, skills and commitments (plain data).</summary>
public sealed record DispatchTechnician(
    Guid Id,
    string Name,
    TechnicianSchedule Schedule,
    IReadOnlySet<Guid> SkillIds,
    IReadOnlyList<DispatchCommitment> Commitments);

/// <summary>A scheduled assessment or an assigned visit of one technician.</summary>
public sealed record DispatchCommitment(
    Guid TechnicianId,
    string Kind,
    DateTimeOffset Start,
    DateTimeOffset End,
    Guid? VisitId,
    VisitStatus? Status,
    string? DisplayNumber);

public sealed record DispatchSkill(Guid Id, string Name);

public sealed record CalendarLane(
    Guid Id, string Name, string? ColorHex, string? PrimarySkill, string? Tag, DispatchTechnician Technician);

public sealed record CalendarVisitSource(
    Guid VisitId,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    string Street,
    VisitStatus Status,
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<Guid> TechnicianIds,
    Guid? PrimaryTechnicianId,
    IReadOnlyList<DispatchSkill> RequiredSkills);

/// <summary>The plain data of one calendar range; <see cref="Lanes"/> holds the branch technicians and the assignment holders.</summary>
public sealed record CalendarSource(
    string Timezone,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<CalendarLane> Lanes,
    IReadOnlyList<CalendarVisitSource> Visits);

/// <summary>Persistence port of the dispatch calendar. Every method is scoped to one organization and the caller's branch scope.</summary>
public interface IDispatchStore
{
    Task<DispatchOptionsView> GetOptionsAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken);

    /// <summary>Null for a missing, foreign, inactive or out-of-scope branch.</summary>
    Task<CalendarSource?> LoadCalendarAsync(
        Guid organizationId, BranchScope scope, Guid branchId, string view, DateOnly date, CancellationToken cancellationToken);

    /// <summary>NotFound for a missing, foreign, inactive or out-of-scope branch.</summary>
    Task<DispatchOutcome<UnscheduledPageView>> ListUnscheduledAsync(
        Guid organizationId, BranchScope scope, UnscheduledQuery query, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> SkillIsActiveAsync(Guid organizationId, Guid skillId, CancellationToken cancellationToken);

    Task<VisitDispatchDetail?> GetDetailAsync(
        Guid organizationId, BranchScope scope, Guid visitId, bool canManage, DateTimeOffset now, CancellationToken cancellationToken);

    Task<DispatchVisitContext?> GetContextAsync(
        Guid organizationId, BranchScope scope, Guid visitId, CancellationToken cancellationToken);

    Task<DispatchOutcome<VisitEvaluation>> EvaluateAsync(
        Guid organizationId, BranchScope scope, Guid visitId, EvaluationInput input, CancellationToken cancellationToken);

    Task<DispatchOutcome<DispatchSaved>> DispatchAsync(
        DispatchActor actor, Guid visitId, DispatchInput input, CancellationToken cancellationToken);
}

/// <summary>Sends the customer email after the commit; returns false when delivery failed (the failure is logged, never thrown).</summary>
public interface IVisitNotifier
{
    Task<bool> SendAsync(VisitEmail email, CancellationToken cancellationToken);
}
