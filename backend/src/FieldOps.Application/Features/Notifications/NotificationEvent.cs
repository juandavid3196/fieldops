namespace FieldOps.Application.Features.Notifications;

/// <summary>
/// One BR-05 business event, prepared by the producer store from the entities it already loaded. The writer resolves the
/// recipients (BR-04) and adds one row each; the producer's own SaveChanges commits them with the event (BR-09).
/// </summary>
public sealed record NotificationEvent(
    string Code,
    Guid OrganizationId,
    Guid? BranchId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    Guid EntityId,
    NotificationContent Content);

/// <summary>
/// Port of the producer stores. Adds notification rows to the ambient scoped unit of work: it never saves and never opens a
/// transaction, so the rows commit or roll back with the event.
/// </summary>
public interface INotificationEventWriter
{
    Task AddAsync(NotificationEvent notificationEvent, CancellationToken cancellationToken);
}
