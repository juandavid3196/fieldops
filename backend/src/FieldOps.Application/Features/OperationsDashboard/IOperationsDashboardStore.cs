using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.OperationsDashboard;

/// <summary>Raw query values before parsing; every id is optional text so malformed values become a 400.</summary>
public sealed record OperationsQueryText(
    string? BranchId, string? Date, string? TechnicianId, string? CategoryId, string? Period);

/// <summary>The effective branch set and time zone of a request (BR-03).</summary>
public sealed record DashboardScopeContext(string TimezoneId, IReadOnlyList<Guid> BranchIds);

/// <summary>A validated query: the effective branch set, the zone and the optional technician and category filters (BR-05).</summary>
public sealed record DashboardQuery(
    string TimezoneId, IReadOnlyList<Guid> BranchIds, DateOnly Date, Guid? TechnicianId, Guid? CategoryId);

public sealed record DashboardAssignee(Guid Id, string FullName);

/// <summary>A day visit in scope; assignees are the active assignments, primary first.</summary>
public sealed record DashboardVisit(
    Guid VisitId,
    Guid WorkOrderId,
    long WorkOrderNumber,
    string DisplayNumber,
    string Title,
    string CustomerName,
    string? CustomerPhone,
    Guid BranchId,
    VisitStatus Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? ArrivalWindowEnd,
    DateTimeOffset? ActualStartedAt,
    DateTimeOffset? ActualCompletedAt,
    int PauseSeconds,
    int? EstimatedMinutes,
    IReadOnlyList<DashboardAssignee> Assignees);

/// <summary>The fields of a visit that the on-time arrival KPI needs (BR-09).</summary>
public sealed record DashboardArrival(
    VisitStatus Status, DateTimeOffset ScheduledStart, DateTimeOffset? ArrivalWindowEnd, DateTimeOffset? ActualStartedAt);

public sealed record DashboardSkill(string Name, bool IsPrimary);

public sealed record DashboardInterval(DateTimeOffset Start, DateTimeOffset End);

/// <summary>A counted technician with the schedule (visits included) and the scheduled assessments of the day.</summary>
public sealed record DashboardTechnician(
    Guid Id,
    Guid BranchId,
    string FullName,
    string? ColorHex,
    TechnicianSchedule Schedule,
    IReadOnlyList<DashboardSkill> Skills,
    IReadOnlyList<DashboardInterval> Assessments);

public sealed record DashboardDuration(
    Guid CategoryId,
    string CategoryName,
    int EstimatedMinutes,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int PauseSeconds);

public sealed record DashboardEvent(
    string Type, DateTimeOffset At, Guid VisitId, Guid WorkOrderId, string DisplayNumber, string Title, string? TechnicianName);

public sealed record DashboardSource(
    IReadOnlyList<DashboardVisit> Visits,
    IReadOnlyList<DashboardArrival> PreviousWeek,
    IReadOnlyList<DashboardTechnician> Technicians,
    IReadOnlyList<DashboardDuration> Durations,
    IReadOnlyList<DashboardEvent> Events);

public sealed record PerformanceSource(
    int Completed,
    int Total,
    int Fixed,
    int FixTotal,
    int RatingCount,
    decimal? RatingAverage,
    IReadOnlyList<DateTimeOffset> Completions);

/// <summary>Persistence port of the operations dashboard. Every method is scoped to one organization (read-only).</summary>
public interface IOperationsDashboardStore
{
    Task<OperationsOptionsView> GetOptionsAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken);

    /// <summary>Null for a missing, foreign, inactive or out-of-scope branch; all in-scope branches when <paramref name="branchId"/> is null.</summary>
    Task<DashboardScopeContext?> ResolveAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken);

    /// <summary>An active technician of the organization whose branch is in <paramref name="branchIds"/>.</summary>
    Task<bool> TechnicianInScopeAsync(
        Guid organizationId, IReadOnlyList<Guid> branchIds, Guid technicianId, CancellationToken cancellationToken);

    Task<bool> CategoryIsActiveAsync(Guid organizationId, Guid categoryId, CancellationToken cancellationToken);

    Task<DashboardSource> LoadDashboardAsync(
        Guid organizationId, DashboardQuery query, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PerformanceSource> LoadPerformanceAsync(
        Guid organizationId, DashboardQuery query, int days, CancellationToken cancellationToken);
}
