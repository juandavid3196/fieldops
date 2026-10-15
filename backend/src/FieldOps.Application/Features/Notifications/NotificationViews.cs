using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Notifications;

/// <summary>Raw list query values; each is optional text so a malformed value becomes a 400 (BR-11, BR-13).</summary>
public sealed record NotificationListRequest(
    string? Tab, string? Q, string? Type, string? BranchId, string? Date, int? Page, int? PageSize);

public sealed record NotificationBranchRef(Guid Id, string Name);

public sealed record NotificationTarget(string Kind, Guid Id);

public sealed record NotificationItem(
    Guid Id,
    string Type,
    string EventCode,
    string Title,
    string Description,
    string EntityKind,
    string EntityLabel,
    string? SecondaryLabel,
    NotificationBranchRef? Branch,
    DateTimeOffset CreatedAt,
    bool IsRead,
    NotificationTarget? Target);

public sealed record NotificationCounts(int All, int Unread);

public sealed record NotificationPage(
    IReadOnlyList<NotificationItem> Items, int Total, int Page, int PageSize, NotificationCounts Counts, string TimeZone);

public sealed record NotificationSummary(int Unread, int Today);

public sealed record NotificationMarkResult(NotificationSummary Summary);

public sealed record NotificationReadAllResult(int Updated, NotificationSummary Summary);

public sealed record NotificationOptions(IReadOnlyList<NotificationBranchRef> Branches);

/// <summary>The caller as the store sees it: always the session organization, user, current role and current branch scope.</summary>
public sealed record NotificationViewer(Guid OrganizationId, Guid UserId, string RoleCode, BranchScope Scope);

/// <summary>
/// Validated list filter. The date window is a half-open UTC range computed from the organization time zone; the store
/// only applies it.
/// </summary>
public sealed record NotificationFilter(
    bool UnreadOnly,
    string? Search,
    string? Type,
    Guid? BranchId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public interface INotificationInboxStore
{
    Task<string> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>An active branch of the organization inside the caller's scope.</summary>
    Task<bool> BranchAvailableAsync(NotificationViewer viewer, Guid branchId, CancellationToken cancellationToken);

    Task<NotificationOptions> OptionsAsync(NotificationViewer viewer, CancellationToken cancellationToken);

    Task<NotificationPage> ListAsync(
        NotificationViewer viewer, NotificationFilter filter, string timeZone, CancellationToken cancellationToken);

    Task<NotificationSummary> SummaryAsync(
        NotificationViewer viewer, DateTimeOffset todayStart, DateTimeOffset tomorrowStart, CancellationToken cancellationToken);

    /// <summary>Sets the read state of one visible notification; false when it is not visible to the caller (BR-01, BR-03).</summary>
    Task<bool> SetReadAsync(NotificationViewer viewer, Guid notificationId, bool read, CancellationToken cancellationToken);

    /// <summary>Marks every visible unread notification created up to <paramref name="upTo"/> as read; returns the count.</summary>
    Task<int> ReadAllAsync(NotificationViewer viewer, DateTimeOffset upTo, CancellationToken cancellationToken);
}
