namespace FieldOps.Application.Features.OperationsDashboard;

public sealed record DashboardBranchOption(Guid Id, string Name, string? Timezone);

public sealed record DashboardTechnicianOption(Guid Id, string FullName, Guid BranchId);

public sealed record DashboardCategoryOption(Guid Id, string Name);

public sealed record OperationsOptionsView(
    IReadOnlyList<DashboardBranchOption> Branches,
    string OrganizationTimezone,
    IReadOnlyList<DashboardTechnicianOption> Technicians,
    IReadOnlyList<DashboardCategoryOption> Categories);

public sealed record DashboardPerson(Guid Id, string FullName);

public sealed record DayVisitView(
    Guid VisitId,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    string CustomerName,
    string? CustomerPhone,
    Guid BranchId,
    string Status,
    string StatusGroup,
    bool IsDelayed,
    int? DelayMinutes,
    bool IsAtRisk,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? ActualCompletedAt,
    DateTimeOffset? ProjectedEnd,
    DashboardPerson? Technician,
    int OtherTechnicianCount);

public sealed record AttentionTechnicianView(
    Guid Id, string FullName, int ScheduledMinutes, int AvailableMinutes, int Percent);

public sealed record AttentionView(
    string Type,
    Guid? VisitId,
    Guid? WorkOrderId,
    string? DisplayNumber,
    string? Title,
    string? VisitStatus,
    Guid BranchId,
    DateTimeOffset? At,
    int? Minutes,
    AttentionTechnicianView? Technician);

public sealed record JobsKpiView(int Total, int Completed, int Active, int Scheduled);

public sealed record UtilizationKpiView(int ScheduledMinutes, int AvailableMinutes, int? Percent);

public sealed record OnTimeKpiView(int? Percent, int Eligible, int? PreviousPercent);

public sealed record DashboardKpisView(
    JobsKpiView Jobs, int Unassigned, int AtRiskOrDelayed, UtilizationKpiView Utilization, OnTimeKpiView OnTimeArrival);

public sealed record TechnicianRowView(
    Guid Id,
    string FullName,
    string Initials,
    string? ColorHex,
    IReadOnlyList<string> Skills,
    int ExtraSkillCount,
    string? CurrentStatus,
    int ScheduledMinutes,
    int AvailableMinutes,
    int? Percent,
    DateTimeOffset? NextAvailable);

public sealed record DurationCategoryView(
    Guid CategoryId, string Name, int VisitCount, int EstimatedMinutes, int ActualMinutes);

public sealed record DurationsView(int VisitCount, IReadOnlyList<DurationCategoryView> Categories);

public sealed record ActivityView(
    string Type,
    DateTimeOffset At,
    Guid VisitId,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    string? TechnicianName);

public sealed record OperationsDashboardView(
    string Timezone,
    DateOnly Date,
    DateTimeOffset Reference,
    DashboardKpisView Kpis,
    IReadOnlyList<DayVisitView> Visits,
    IReadOnlyList<AttentionView> Attention,
    IReadOnlyList<TechnicianRowView> Technicians,
    DurationsView Durations,
    IReadOnlyList<ActivityView> Activity);

public sealed record CompletedRatioView(int Completed, int Total);

public sealed record FixRateView(int Fixed, int Total);

public sealed record RatingView(decimal? Average, int Count);

public sealed record DailyCompletedView(DateOnly Date, int Completed);

public sealed record PerformanceView(
    DateOnly From,
    DateOnly To,
    CompletedRatioView JobsCompleted,
    FixRateView FirstTimeFix,
    RatingView Rating,
    IReadOnlyList<DailyCompletedView> Daily);
