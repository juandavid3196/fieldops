using FieldOps.Domain.Technicians;

namespace FieldOps.Application.Features.Team;

// Raw request bodies. Strings and nullable numbers so a bad value maps to a 400 field key, not a binder error.

public sealed record WeeklyDayInput(int? DayOfWeek, string? Start, string? End, string? BreakStart, string? BreakEnd);

public sealed record SkillAssignmentInput(string? SkillId, int? Proficiency, bool? IsPrimary);

public sealed record SaveSkillsAvailabilityInput(
    string? Version, IReadOnlyList<WeeklyDayInput>? WeeklyAvailability, IReadOnlyList<SkillAssignmentInput>? Skills);

/// <summary>Body of POST/PUT exceptions; <c>Version</c> is only read by PUT.</summary>
public sealed record ExceptionInput(string? Version, string? Date, string? Kind, string? Start, string? End, string? Reason);

public sealed record VersionInput(string? Version);

public sealed record SkillInput(string? Name, string? Description);

// Validated values.

public sealed record WeeklyDayValues(int DayOfWeek, TimeOnly Start, TimeOnly End, TimeRange? Break);

public sealed record SkillAssignmentValues(Guid SkillId, short Proficiency, bool IsPrimary);

public sealed record SkillsAvailabilityValues(
    IReadOnlyList<WeeklyDayValues> Weekly, IReadOnlyList<SkillAssignmentValues> Skills);

public static class ExceptionKinds
{
    public const string Extended = "extended";

    public const string Partial = "partial";

    public const string Unavailable = "unavailable";
}

public sealed record ExceptionValues(DateOnly Date, string Kind, TimeOnly? Start, TimeOnly? End, string Reason);

/// <summary>Instants of a validated exception plus the local day it falls on (BR-12, BR-14).</summary>
public sealed record ExceptionWrite(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsAvailable,
    string Reason,
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd);

public sealed record SkillValues(string Name, string? Description);

// Persistence data.

public sealed record AssignedSkillData(Guid SkillId, string Name, short? Proficiency, bool IsPrimary);

public sealed record ExceptionData(
    Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsAvailable, string Reason, string Status, DateTimeOffset UpdatedAt);

/// <summary>A profile with everything the skills and availability page derives from; weekly rows are raw (BR-25).</summary>
public sealed record SkillsAvailabilityData(
    Guid Id,
    string FirstName,
    string LastName,
    TechnicianStatus Status,
    TeamOption Branch,
    DateTimeOffset UpdatedAt,
    string ZoneId,
    IReadOnlyList<AvailabilitySlot> Slots,
    IReadOnlyList<AssignedSkillData> Skills,
    IReadOnlyList<ExceptionData> Exceptions,
    IReadOnlyList<AssignedVisit> Visits);

public enum SkillsSaveOutcome
{
    Saved,
    NotFound,
    Conflict,
    InvalidSkills,
}

public sealed record SkillsSaveResult(SkillsSaveOutcome Outcome);

public enum ExceptionOutcome
{
    Saved,
    NotFound,
    Past,
    Cancelled,
    Stale,
    DateTaken,
}

public sealed record ExceptionResult(ExceptionOutcome Outcome, ExceptionData? Exception = null);

public enum SkillOutcome
{
    Saved,
    NotFound,
    Duplicate,
}

public sealed record SkillView(Guid Id, string Name, string? Description, bool IsActive);

public sealed record SkillResult(SkillOutcome Outcome, SkillView? Skill = null);

// Response views.

public sealed record SkillsHeaderView(
    Guid Id, string FirstName, string LastName, string ProfileStatus, TeamOption Branch);

public sealed record WeeklyDayView(
    int DayOfWeek, string Start, string End, string? BreakStart, string? BreakEnd, int CapacityPercent);

public sealed record AssignedSkillView(Guid SkillId, string Name, int? Proficiency, bool IsPrimary);

public sealed record ExceptionView(
    Guid Id, string Date, string Kind, string? Start, string? End, string Reason, string Status, string Version);

public sealed record UpcomingWindowView(string Start, string End);

public sealed record UpcomingDayView(
    string Date,
    string Label,
    string State,
    DateTimeOffset? NextAvailableAt,
    IReadOnlyList<UpcomingWindowView> Windows,
    bool Limited);

public sealed record CapacityView(
    int AvailableMinutes, int ScheduledMinutes, int RemainingMinutes, int? UtilizationPercent);

public sealed record TodaySummaryView(int Jobs, int? BookedPercent);

public sealed record SkillsAvailabilityView(
    SkillsHeaderView Technician,
    string Version,
    string Timezone,
    string TimezoneLabel,
    IReadOnlyList<WeeklyDayView> WeeklyAvailability,
    IReadOnlyList<AssignedSkillView> Skills,
    IReadOnlyList<ExceptionView> Exceptions,
    IReadOnlyList<UpcomingDayView> Upcoming,
    CapacityView Capacity,
    TodaySummaryView Today);

/// <summary>Machine-readable <c>code</c> of the exception 409 responses.</summary>
public static class ExceptionConflictCodes
{
    public const string DateTaken = "exception_date_taken";

    public const string Past = "exception_past";

    public const string Cancelled = "exception_cancelled";

    public const string Stale = "exception_stale";
}

public static class SkillsAvailabilityMessages
{
    public const string TimesRequired = "Choose a start and end time.";

    public const string EndAfterStart = "End time must be after start time.";

    public const string BreakMustFit = "The break must fit inside the working hours.";

    public const string DayDuplicated = "Choose each day only once.";

    public const string PrimaryRequired = "Choose one primary skill.";

    public const string SkillDuplicated = "Choose each skill only once.";

    public const string LevelRequired = "Choose a level for each skill.";

    public const string DateInvalid = "Choose today or a future date.";

    public const string KindInvalid = "Choose an availability type.";

    public const string TimesNotAllowed = "Times aren't allowed for an all-day exception.";

    public const string ReasonRequired = "Enter a reason.";

    public const string ReasonTooLong = "Use 200 characters or fewer.";

    public const string SkillNameRequired = "Enter a skill name.";

    public const string SkillNameTooLong = "Use 120 characters or fewer.";

    public const string SkillDescriptionTooLong = "Use 500 characters or fewer.";

    public const string SkillNameTaken = "A skill with this name already exists.";

    public const string ProfileStaleVersion = "This technician was updated by someone else. Reload to see the latest changes.";

    public const string ExceptionDateTaken = "This technician already has an active exception on this date.";

    public const string ExceptionPast = "Past exceptions can't be changed.";

    public const string ExceptionCancelled = "Activate this exception before editing it.";

    public const string ExceptionStale = "This exception was changed by someone else. Reload to see the latest version.";

    public const string ExceptionNotFound = "This exception isn't available.";

    public const string SkillNotFound = "This skill isn't available.";
}

public static class SkillsAvailabilityAuditActions
{
    public const string ProfileUpdated = "technician_profile.skills_availability_updated";

    public const string ExceptionEntity = "technician_exception";

    public const string ExceptionCreated = "technician_exception.created";

    public const string ExceptionUpdated = "technician_exception.updated";

    public const string ExceptionCancelled = "technician_exception.cancelled";

    public const string ExceptionActivated = "technician_exception.activated";

    public const string SkillEntity = "skill";

    public const string SkillCreated = "skill.created";

    public const string SkillUpdated = "skill.updated";

    public const string SkillActivated = "skill.activated";

    public const string SkillDeactivated = "skill.deactivated";
}
