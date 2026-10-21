namespace FieldOps.Application.Features.Team;

// Raw request bodies and queries (team-availability-skills). Strings so a bad value maps to a 400 field key.

public sealed record TeamAvailabilityQuery(string? WeekStart, string? Search, string? BranchId, string? Availability, string? Page);

public sealed record SkillsMatrixQuery(
    string? Search, string? BranchId, string? SkillId, string? Coverage, string? Sort, string? Page);

public sealed record AvailabilityDayInput(string? DayOfWeek, bool? IsAvailable, string? Start, string? End);

public sealed record SaveAvailabilityInput(string? UpdatedAt, IReadOnlyList<AvailabilityDayInput>? Days);

public sealed record UpdatedAtInput(string? UpdatedAt);

public sealed record CreateTimeOffInput(string? StartDate, string? EndDate, string? Note);

public sealed record SaveSkillAssignmentsInput(string? UpdatedAt, IReadOnlyList<string>? SkillIds);

// Validated values.

/// <summary>One submitted day; <c>DayOfWeek</c> is 0 = Sunday like the stored rows.</summary>
public sealed record AvailabilityDayValues(int DayOfWeek, bool IsAvailable, TimeOnly? Start, TimeOnly? End);

public sealed record TimeOffValues(DateOnly StartDate, DateOnly EndDate, string? Note);

/// <summary>The instants and local dates of a validated time off range (BR-14).</summary>
public sealed record TimeOffWrite(DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateOnly StartDate, DateOnly EndDate, string? Note);

// Persistence data.

public sealed record AvailabilityGridFilter(string? Search, Guid? BranchId);

public sealed record AvailabilityGridProfile(
    Guid Id,
    string FirstName,
    string LastName,
    string? ColorHex,
    TeamOption Branch,
    string ZoneId,
    bool UsesBranchHours,
    IReadOnlyList<AvailabilitySlot> Slots,
    IReadOnlyList<AvailabilityOverride> Exceptions);

public sealed record TimeOffData(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note, DateTimeOffset UpdatedAt);

public sealed record TechnicianAvailabilityData(
    Guid Id,
    string FirstName,
    string LastName,
    TeamOption Branch,
    string ZoneId,
    bool UsesBranchHours,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AvailabilitySlot> Slots,
    IReadOnlyList<TimeOffData> TimeOff);

public sealed record SkillMatrixTechnician(
    Guid Id, string FirstName, string LastName, string? ColorHex, TeamOption Branch, IReadOnlyList<Guid> ActiveSkillIds);

public sealed record SkillMatrixData(IReadOnlyList<TeamOption> Skills, IReadOnlyList<SkillMatrixTechnician> Technicians);

public sealed record SkillAssignmentsData(
    Guid Id, string FirstName, string LastName, DateTimeOffset UpdatedAt, IReadOnlyList<Guid> SkillIds);

public enum AvailabilityWriteOutcome
{
    Saved,
    NotFound,
    Conflict,
    InvalidSkills,
}

public sealed record AvailabilityWriteResult(AvailabilityWriteOutcome Outcome);

public enum TimeOffOutcome
{
    Saved,
    NotFound,
    Overlap,
    Closed,
    Stale,
}

/// <summary><c>TimeOff</c> is null when a retire cancelled the range.</summary>
public sealed record TimeOffResult(TimeOffOutcome Outcome, TimeOffData? TimeOff = null);

// Response views.

public sealed record AvailabilityCellView(string Date, string State, string? Start, string? End);

public sealed record AvailabilityRowView(
    Guid TechnicianId,
    string FirstName,
    string LastName,
    string? ColorHex,
    TeamOption Branch,
    string Timezone,
    bool UsesBranchHours,
    IReadOnlyList<AvailabilityCellView> Days,
    int WeeklyMinutes);

public sealed record TeamAvailabilityPage(
    string WeekStart, IReadOnlyList<AvailabilityRowView> Items, int TotalCount, int Page, int PageSize);

public sealed record EditableDayView(string DayOfWeek, bool IsAvailable, string? Start, string? End);

public sealed record TimeOffView(Guid Id, string StartDate, string EndDate, string? Note, string UpdatedAt);

public sealed record TechnicianAvailabilityView(
    Guid TechnicianId,
    string FirstName,
    string LastName,
    TeamOption Branch,
    string Timezone,
    string TimezoneLabel,
    bool UsesBranchHours,
    string UpdatedAt,
    IReadOnlyList<EditableDayView> Days,
    IReadOnlyList<TimeOffView> TimeOff);

public sealed record RetireTimeOffView(TimeOffView? TimeOff);

public sealed record SkillMatrixRowView(
    Guid TechnicianId,
    string FirstName,
    string LastName,
    string? ColorHex,
    TeamOption Branch,
    IReadOnlyList<Guid> SkillIds,
    int SkillCount);

public sealed record SkillsMatrixPage(
    IReadOnlyList<TeamOption> Skills, IReadOnlyList<SkillMatrixRowView> Items, int TotalCount, int Page, int PageSize);

public sealed record SkillAssignmentsView(
    Guid TechnicianId, string FirstName, string LastName, string UpdatedAt, IReadOnlyList<Guid> SkillIds);

public static class TeamAvailabilityMessages
{
    public const string Required = "This field is required.";

    public const string DaysInvalid = "Enter a valid value.";

    public const string EndAfterStart = "End time must be after start time.";

    public const string StartDateInvalid = "Choose today or a future date.";

    public const string EndDateBeforeStart = "End date can't be before start date.";

    public const string NoteTooLong = "Use 250 characters or fewer.";

    public const string SkillDuplicated = "Choose each skill once.";

    public const string SkillNotActive = "Choose active skills from the catalog.";

    public const string ProfileStale = "This technician was updated by someone else. Reload to see the latest changes.";

    public const string TimeOffOverlap = "This technician already has time off or an exception on these dates.";

    public const string TimeOffClosed = "This time off has already ended or was removed.";

    public const string TimeOffStale = "This time off was changed by someone else. Reload to see the latest version.";

    public const string TimeOffNotFound = "This time off isn't available.";
}

/// <summary>Machine-readable <c>code</c> of the 409 responses of the availability and time off endpoints.</summary>
public static class TeamAvailabilityConflictCodes
{
    public const string Stale = "stale";

    public const string TimeOffOverlap = "time_off_overlap";

    public const string TimeOffClosed = "time_off_closed";
}

public static class TeamAvailabilityKeys
{
    public const string WeekStart = "weekStart";

    public const string Availability = "availability";

    public const string Coverage = "coverage";

    public const string UpdatedAt = "updatedAt";

    public const string Days = "days";

    public const string StartDate = "startDate";

    public const string EndDate = "endDate";

    public const string Note = "note";

    public const string SkillIds = "skillIds";
}
