using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.AuditTrail;

/// <summary>Raw list and export query values; each is optional text so a malformed value becomes a 400 key (BR-03, BR-05).</summary>
public sealed record AuditLogQueryText(
    string? From,
    string? To,
    string? Q,
    string? ActorId,
    string? Action,
    string? EntityType,
    string? EntityId,
    string? BranchId,
    string? Impact,
    string? Sort,
    string? Direction,
    string? Page,
    string? PageSize);

/// <summary>The caller as the store sees it: the session organization, user, current role and current branch scope.</summary>
public sealed record AuditViewer(Guid OrganizationId, Guid UserId, string RoleCode, BranchScope Scope);

/// <summary>
/// A validated query. The date window is a half-open UTC range of local days; <see cref="ActorSystem"/> selects the
/// rows without an actor.
/// </summary>
public sealed record AuditQuery(
    DateOnly From,
    DateOnly To,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Search,
    Guid? ActorId,
    bool ActorSystem,
    string? Action,
    string? EntityType,
    Guid? EntityId,
    Guid? BranchId,
    AuditImpact? Impact,
    bool SortByImpact,
    bool Descending,
    int Page,
    int PageSize);

/// <summary>A stored event with the values resolved at read time (AS-02), before presentation.</summary>
public sealed record AuditRecord(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    Guid? EntityId,
    Guid? BranchId,
    string? BeforeData,
    string? AfterData,
    string Metadata,
    string? IpAddress);

public sealed record AuditResolved(
    AuditRecord Record, string? ActorName, bool ActorIsMember, string? Reference, string? BranchName);

public sealed record AuditResolvedPage(IReadOnlyList<AuditResolved> Rows, int Total);

public sealed record AuditResolvedDetail(AuditResolved Row, string? ActorRoleName, AuditTarget? Target);

public sealed record AuditSummaryCounts(int Events, int ActiveUsers, int HighImpact, int AccessChanges);

public sealed record AuditUserOption(string Id, string Name);

public sealed record AuditBranchOption(Guid Id, string Name, bool IsActive);

public sealed record AuditFilterData(IReadOnlyList<AuditUserOption> Users, IReadOnlyList<AuditBranchOption> Branches);

public sealed record AuditActorRef(Guid? Id, string Name);

public sealed record AuditActionRef(string Code, string Label);

public sealed record AuditEntityRef(string Type, string Label, Guid? Id);

public sealed record AuditBranchRef(Guid Id, string Name);

public sealed record AuditItem(
    long Id,
    string EventId,
    DateTimeOffset OccurredAt,
    AuditActorRef Actor,
    AuditActionRef Action,
    AuditEntityRef Entity,
    string Reference,
    string Summary,
    AuditBranchRef? Branch,
    string Source,
    string? IpAddress,
    string Impact);

public sealed record AuditPage(IReadOnlyList<AuditItem> Items, int Total, int Page, int PageSize, string TimeZone);

public sealed record AuditSummary(int EventsToday, int ActiveUsersToday, int HighImpactToday, int AccessChangesToday, string TimeZone);

public sealed record AuditFilterOption(string Code, string Label);

public sealed record AuditFilters(
    IReadOnlyList<AuditFilterOption> Actions,
    IReadOnlyList<AuditFilterOption> EntityTypes,
    IReadOnlyList<AuditUserOption> Users,
    IReadOnlyList<AuditBranchOption> Branches);

public sealed record AuditDetailActor(Guid? Id, string Name, string? RoleName);

public sealed record AuditChange(string Field, string Label, string Before, string After);

public sealed record AuditMetadataPair(string Key, string Value);

public sealed record AuditTarget(string Kind, Guid Id);

/// <summary>The list item plus the actor role, the change rows, the metadata and the target (BR-13).</summary>
public sealed record AuditDetail(
    long Id,
    string EventId,
    DateTimeOffset OccurredAt,
    AuditDetailActor Actor,
    AuditActionRef Action,
    AuditEntityRef Entity,
    string Reference,
    string Summary,
    AuditBranchRef? Branch,
    string Source,
    string? IpAddress,
    string Impact,
    IReadOnlyList<AuditChange> Changes,
    IReadOnlyList<AuditMetadataPair> Metadata,
    AuditTarget? Target,
    string TimeZone);

public sealed record AuditCsvFile(string FileName, byte[] Content);

public static class AuditLogMessages
{
    public const int SearchMaxLength = 100;

    public const int MaxRangeDays = 366;

    public const int DefaultPageSize = 25;

    public const string BranchNotFoundCode = "branch_not_found";

    public const string SortOccurredAt = "occurredAt";

    public const string SortImpact = "impact";
}

/// <summary>Read side of the audit trail. Every method applies the single visibility predicate of the viewer.</summary>
public interface IAuditLogStore
{
    Task<string> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>A user with a membership in the organization, in any status.</summary>
    Task<bool> ActorExistsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>An active or inactive branch of the organization inside the caller's scope.</summary>
    Task<bool> BranchAvailableAsync(AuditViewer viewer, Guid branchId, CancellationToken cancellationToken);

    Task<AuditResolvedPage> ListAsync(AuditViewer viewer, AuditQuery query, CancellationToken cancellationToken);

    /// <summary>Every matching event in the query order; null when more than <paramref name="maxRows"/> match.</summary>
    Task<IReadOnlyList<AuditResolved>?> ExportAsync(
        AuditViewer viewer, AuditQuery query, int maxRows, CancellationToken cancellationToken);

    Task<AuditSummaryCounts> SummaryAsync(
        AuditViewer viewer, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<AuditFilterData> FilterDataAsync(AuditViewer viewer, CancellationToken cancellationToken);

    Task<AuditResolvedDetail?> GetAsync(AuditViewer viewer, long id, CancellationToken cancellationToken);
}
