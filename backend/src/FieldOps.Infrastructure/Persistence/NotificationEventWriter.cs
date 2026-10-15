using System.Text.Json;
using FieldOps.Application.Features.Notifications;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Adds the in-app notifications of one BR-05 event to the ambient unit of work (notifications-center BR-04, BR-06, BR-09).
/// It resolves every recipient with one query, never saves and never opens a transaction: the producer store's own
/// SaveChanges commits the rows with the event, and a rollback leaves none.
/// </summary>
internal sealed class NotificationEventWriter(FieldOpsDbContext dbContext) : INotificationEventWriter
{
    // The branch scope rule of IBranchScopeResolver: these roles see every branch whatever their link rows say.
    private static readonly string[] AllBranchRoles = ["owner", "operations_manager", "viewer"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddAsync(NotificationEvent notificationEvent, CancellationToken cancellationToken)
    {
        var roles = NotificationCatalog.Get(notificationEvent.Code).Roles.ToArray();
        var organizationId = notificationEvent.OrganizationId;
        var branchId = notificationEvent.BranchId;
        var actorUserId = notificationEvent.ActorUserId;

        var recipients = await (
            from member in dbContext.OrganizationUsers.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
            where member.OrganizationId == organizationId
                && member.Status == UserStatus.Active
                && roles.Contains(role.Code)
                && (actorUserId == null || member.UserId != actorUserId)
                && (branchId == null
                    || member.IsAllBranches
                    || AllBranchRoles.Contains(role.Code)
                    || dbContext.OrganizationUserBranches.Any(link =>
                        link.OrganizationUserId == member.Id && link.BranchId == branchId))
            select member.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            return;
        }

        var content = notificationEvent.Content;
        var subject = NotificationCopy.Truncate(content.Title);
        var payload = JsonSerializer.Serialize(
            new
            {
                type = NotificationCatalog.Get(notificationEvent.Code).Type,
                description = content.Description,
                entityKind = content.EntityKind,
                entityId = notificationEvent.EntityId,
                entityLabel = content.EntityLabel,
                secondaryLabel = content.SecondaryLabel,
                branchId,
            },
            JsonOptions);

        foreach (var userId in recipients)
        {
            dbContext.Notifications.Add(Notification.CreateInApp(
                organizationId,
                userId,
                notificationEvent.Code,
                subject,
                payload,
                notificationEvent.OccurredAt));
        }
    }
}
