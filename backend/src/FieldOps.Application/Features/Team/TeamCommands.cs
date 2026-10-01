using FieldOps.Domain.Technicians;

namespace FieldOps.Application.Features.Team;

/// <summary>Raw query of GET /team/technicians (BR-09, BR-10); strings so bad input maps to a 400 key.</summary>
public sealed record TeamListQuery(
    string? Search,
    string? BranchId,
    string? SkillId,
    string? Status,
    string? AccountLink,
    string? Sort,
    string? Period,
    string? Page);

/// <summary>Raw body of POST/PUT /team/technicians (BR-15). No organization identifier is accepted.</summary>
public sealed record TeamProfileInput(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? EmployeeCode,
    string? BranchId,
    string? Notes);

/// <summary>Validated, normalized profile fields: email lowercase, profile ID uppercase, empty optionals null.</summary>
public sealed record TeamProfileValues(
    string FirstName, string LastName, string? Email, string? Phone, string? EmployeeCode, string? Notes);

public enum TeamStatusFilter
{
    AllActive,
    Available,
    OnJob,
    Break,
    TimeOff,
    Off,
    Inactive,
    Suspended,
}

public enum TeamAccountFilter
{
    All,
    Linked,
    NotLinked,
}

public sealed record TeamListFilter(
    string? Search,
    Guid? BranchId,
    Guid? SkillId,
    TeamStatusFilter Status,
    TeamAccountFilter AccountLink,
    bool Descending,
    TeamPeriod Period,
    int Page);

/// <summary>Database-level selection of profiles; the derived status filter is applied by the handler.</summary>
public sealed record TeamFactsFilter(
    Guid? BranchId,
    Guid? SkillId,
    string? Search,
    TeamAccountFilter AccountLink,
    TechnicianStatus? ProfileStatus);

/// <summary>A profile with its derived-data inputs, loaded in batches without per-row queries.</summary>
public sealed record TechnicianFacts(
    Guid Id,
    Guid BranchId,
    string BranchName,
    string FirstName,
    string LastName,
    bool IsLinked,
    IReadOnlyList<string> SkillNames,
    TechnicianSchedule Schedule)
{
    public string FullName => $"{FirstName} {LastName}";
}

public sealed record TechnicianProfileData(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? EmployeeCode,
    string? Notes,
    TeamOption Branch,
    TechnicianStatus Status,
    TechnicianAccountView? Account,
    IReadOnlyList<TechnicianSkillView> Skills,
    IReadOnlyList<AvailabilityDayView> Availability,
    TechnicianSchedule Schedule);

public sealed record SkillCoverageRow(Guid SkillId, string Name, int TechnicianCount);

public enum TeamSaveOutcome
{
    Saved,
    NotFound,
    Duplicate,
}

/// <summary>Result of a create/update; <c>Errors</c> carries the duplicate-field messages.</summary>
public sealed record TeamSaveResult(
    TeamSaveOutcome Outcome, Guid Id = default, IReadOnlyDictionary<string, string[]>? Errors = null);

public enum TeamStatusOutcome
{
    NotFound,
    NoChange,
    Changed,
    HasUpcomingVisits,
}

public sealed record TeamStatusResult(TeamStatusOutcome Outcome, int UpcomingVisitCount = 0);

public enum TeamLinkOutcome
{
    NotFound,
    NoChange,
    Changed,
    Rejected,
}
