namespace FieldOps.Application.Features.Team;

public sealed record TeamOption(Guid Id, string Name);

public sealed record TeamOptions(IReadOnlyList<TeamOption> Branches, IReadOnlyList<TeamOption> Skills);

public sealed record CapacityAlert(
    Guid TechnicianId, string Name, int? WorkloadPercent, bool NoAvailability, int MoreCount);

public sealed record UnlinkedAlert(Guid TechnicianId, string Name, int MoreCount);

public sealed record TeamMetrics(
    int ActiveProfiles,
    int AvailableNow,
    int OnJobs,
    int AtCapacity,
    int UnlinkedAccounts,
    CapacityAlert? CapacityAlert,
    UnlinkedAlert? UnlinkedAlert);

/// <summary>BR-07: <c>kind</c> is <c>now</c>, <c>time</c> (with <c>at</c>, a UTC instant) or <c>none</c>.</summary>
public sealed record NextAvailableView(string Kind, DateTimeOffset? At);

public sealed record TechnicianListItem(
    Guid Id,
    string FullName,
    string BranchName,
    IReadOnlyList<string> Skills,
    bool IsLinked,
    string ProfileStatus,
    string TodayStatus,
    int Jobs,
    int? WorkloadPercent,
    string WorkloadState,
    bool AtCapacity,
    NextAvailableView NextAvailable,
    string Timezone);

public sealed record TechnicianListPage(
    IReadOnlyList<TechnicianListItem> Items, int TotalCount, int TeamMembersCount, int Page, int PageSize);

public sealed record TechnicianAccountView(string Email, string RoleName);

public sealed record TechnicianSkillView(string Name, short? Proficiency, bool IsPrimary);

public sealed record AvailabilityWindowView(string Start, string End);

public sealed record AvailabilityDayView(int DayOfWeek, IReadOnlyList<AvailabilityWindowView> Windows);

public sealed record CurrentJobView(string Label);

public sealed record TechnicianTodayView(
    string TodayStatus,
    CurrentJobView? CurrentJob,
    int Jobs,
    int? WorkloadPercent,
    string WorkloadState,
    NextAvailableView NextAvailable);

public sealed record TechnicianDetail(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? EmployeeCode,
    string? Notes,
    TeamOption Branch,
    string ProfileStatus,
    TechnicianAccountView? Account,
    IReadOnlyList<TechnicianSkillView> Skills,
    IReadOnlyList<AvailabilityDayView> Availability,
    TechnicianTodayView Today,
    string Timezone);

public sealed record TechnicianCreated(Guid Id);

public sealed record LinkableAccount(Guid OrganizationUserId, string FullName, string Email, string RoleName);

public sealed record SkillCoverage(Guid SkillId, string Name, int TechnicianCount, string Health);

public enum TeamResultKind
{
    Succeeded,
    NoContent,
    Invalid,
    NotFound,
    Conflict,
}

/// <summary>Outcome of a team use case, mapped to HTTP by the controller.</summary>
public sealed record TeamResult<T>(
    TeamResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null,
    int? UpcomingVisitCount = null,
    string? Code = null)
{
    public static TeamResult<T> Ok(T value) => new(TeamResultKind.Succeeded, value);

    public static TeamResult<T> NoOp() => new(TeamResultKind.NoContent);

    public static TeamResult<T> NotFound() => new(TeamResultKind.NotFound);

    public static TeamResult<T> NotFound(string message) => new(TeamResultKind.NotFound, Message: message);

    public static TeamResult<T> Invalid(string key, string message) =>
        new(
            TeamResultKind.Invalid,
            Errors: new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    public static TeamResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(TeamResultKind.Invalid, Errors: errors);

    public static TeamResult<T> Conflict(string message, int? upcomingVisitCount = null) =>
        new(TeamResultKind.Conflict, Message: message, UpcomingVisitCount: upcomingVisitCount);

    /// <summary>A conflict with a machine-readable <c>code</c> extension (never displayed).</summary>
    public static TeamResult<T> Conflict(string message, string code) =>
        new(TeamResultKind.Conflict, Message: message, Code: code);
}

public readonly record struct TeamNoValue;

public static class TeamMessages
{
    public const string FirstNameRequired = "Enter a first name.";

    public const string LastNameRequired = "Enter a last name.";

    public const string NameTooLong = "Use 100 characters or fewer.";

    public const string EmailInvalid = "Enter a valid email address.";

    public const string EmailInUse = "Another technician profile already uses this email.";

    public const string PhoneInvalid = "Enter a valid phone number.";

    public const string EmployeeCodeInvalid = "Use letters, numbers, hyphens or underscores.";

    public const string EmployeeCodeInUse = "This profile ID is already in use.";

    public const string BranchNotAllowed = "Choose a branch you have access to.";

    public const string NotesTooLong = "Use 2000 characters or fewer.";

    public const string SearchTooLong = "Use 100 characters or fewer.";

    public const string QueryInvalid = "Enter a valid value.";

    public const string SkillInvalid = "Choose a skill from your organization.";

    public const string UpcomingVisits = "Reassign this technician's upcoming visits before deactivating the profile.";

    public const string CannotLink = "This user account can't be linked to this profile.";
}

public static class TeamFieldKeys
{
    public const string FirstName = "firstName";

    public const string LastName = "lastName";

    public const string Email = "email";

    public const string Phone = "phone";

    public const string EmployeeCode = "employeeCode";

    public const string BranchId = "branchId";

    public const string Notes = "notes";

    public const string SkillId = "skillId";

    public const string Search = "search";

    public const string Status = "status";

    public const string AccountLink = "accountLink";

    public const string Sort = "sort";

    public const string Period = "period";

    public const string Page = "page";
}

public static class TeamAuditActions
{
    public const string EntityType = "technician_profile";

    public const string Created = "technician_profile.created";

    public const string Updated = "technician_profile.updated";

    public const string Activated = "technician_profile.activated";

    public const string Deactivated = "technician_profile.deactivated";

    public const string AccountLinked = "technician_profile.account_linked";

    public const string AccountUnlinked = "technician_profile.account_unlinked";
}
