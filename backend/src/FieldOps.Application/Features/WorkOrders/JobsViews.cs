using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.WorkOrders;

/// <summary>jobs-management: the property block of a job row.</summary>
public sealed record JobRowProperty(string Name, string? PostalCode, string City);

public sealed record JobRowBranch(Guid Id, string Name);

/// <summary>The current visit of a job (BR-04); the instants are null for an unscheduled visit.</summary>
public sealed record JobCurrentVisit(Guid Id, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd);

public sealed record JobTechnician(string FullName, string Initials, string? ColorHex);

/// <summary>API contract JobRow. <c>Status</c> is the BR-05 code, <c>Timezone</c> the branch zone with the organization fallback.</summary>
public sealed record JobRow(
    Guid Id,
    string DisplayNumber,
    string Title,
    string CustomerName,
    JobRowProperty Property,
    JobRowBranch Branch,
    string Timezone,
    string Status,
    string Priority,
    JobCurrentVisit? CurrentVisit,
    JobTechnician? Technician,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Actions);

public sealed record JobTabCounts(int All, int Active, int Unscheduled, int Completed);

public sealed record JobsPage(IReadOnlyList<JobRow> Items, int Total, JobTabCounts Counts);

public sealed record JobsOptionBranch(Guid Id, string Name, string? Timezone);

public sealed record JobsOptions(IReadOnlyList<JobsOptionBranch> Branches, string OrganizationTimezone, bool CanManage);

public sealed record JobsActiveMetric(int Total, int InProgress);

public sealed record JobsTodayMetric(int Total, int Assigned, int Completed);

public sealed record JobsSummary(
    string Timezone,
    JobsActiveMetric ActiveJobs,
    int ReadyToSchedule,
    JobsTodayMetric Today,
    int NeedsAttention);

/// <summary>The raw query values of <c>GET /work-orders</c>, validated by the handler.</summary>
public sealed record JobsListRequest(
    string? Tab,
    string? Q,
    string? BranchId,
    string? Status,
    string? Priority,
    string? Sort,
    int? Page,
    int? PageSize);

/// <summary>A validated list request. <c>BranchId</c> is already authorized against the caller scope.</summary>
public sealed record JobsFilter(
    JobsTab Tab,
    string? Search,
    Guid? BranchId,
    JobStatus? Status,
    (short Min, short Max)? Priority,
    JobsSort Sort,
    int Page,
    int PageSize);

/// <summary>Persistence port of the jobs workspace. Every method is scoped to one organization and the caller's branch scope.</summary>
public interface IJobsStore
{
    /// <summary>True when the branch belongs to the organization, is active and is inside the scope.</summary>
    Task<bool> BranchAvailableAsync(Guid organizationId, BranchScope scope, Guid branchId, CancellationToken cancellationToken);

    Task<JobsPage> ListAsync(
        Guid organizationId, BranchScope scope, JobsFilter filter, bool canManage, CancellationToken cancellationToken);

    Task<JobsSummary> SummaryAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken);

    Task<JobsOptions> OptionsAsync(
        Guid organizationId, BranchScope scope, bool canManage, CancellationToken cancellationToken);
}
