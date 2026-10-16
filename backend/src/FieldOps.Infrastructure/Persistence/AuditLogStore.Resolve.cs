using FieldOps.Application.Features.AuditTrail;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class AuditLogStore
{
    /// <summary>
    /// Resolves the actor names, memberships, branch names and references of already loaded rows with a fixed number of
    /// batched queries (BR-06). Only called with the rows of one page or one export batch.
    /// </summary>
    private async Task<IReadOnlyList<AuditResolved>> ResolveAsync(
        AuditViewer viewer, IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var organizationId = viewer.OrganizationId;
        var actorIds = records.Where(record => record.ActorUserId is not null)
            .Select(record => record.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        var names = new Dictionary<Guid, string>();
        var members = new HashSet<Guid>();

        if (actorIds.Length > 0)
        {
            foreach (var user in await dbContext.Users.AsNoTracking()
                         .Where(user => actorIds.Contains(user.Id))
                         .Select(user => new { user.Id, user.FirstName, user.LastName })
                         .ToListAsync(cancellationToken))
            {
                names[user.Id] = FullName(user.FirstName, user.LastName);
            }

            members.UnionWith(await dbContext.OrganizationUsers.AsNoTracking()
                .Where(member => member.OrganizationId == organizationId && actorIds.Contains(member.UserId))
                .Select(member => member.UserId)
                .Distinct()
                .ToListAsync(cancellationToken));
        }

        var branchIds = records.Where(record => record.BranchId is not null)
            .Select(record => record.BranchId!.Value)
            .Distinct()
            .ToArray();
        var branches = branchIds.Length == 0
            ? []
            : await dbContext.Branches.AsNoTracking()
                .Where(branch => branch.OrganizationId == organizationId && branchIds.Contains(branch.Id))
                .ToDictionaryAsync(branch => branch.Id, branch => branch.Name, cancellationToken);

        var references = await ReferencesAsync(organizationId, records, cancellationToken);

        return
        [
            .. records.Select(record => new AuditResolved(
                record,
                record.ActorUserId is { } actorId && names.TryGetValue(actorId, out var name) ? name : null,
                record.ActorUserId is { } memberId && members.Contains(memberId),
                record.EntityId is { } entityId && references.TryGetValue((record.EntityType, entityId), out var reference)
                    ? reference
                    : null,
                record.BranchId is { } branchId && branches.TryGetValue(branchId, out var branchName) ? branchName : null)),
        ];
    }

    private async Task<Dictionary<(string Type, Guid Id), string>> ReferencesAsync(
        Guid organizationId, IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<(string, Guid), string>();
        var byType = records
            .Where(record => record.EntityId is not null && ReferenceTypes.Contains(record.EntityType))
            .GroupBy(record => record.EntityType, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(record => record.EntityId!.Value).Distinct().ToArray());

        if (byType.Count == 0)
        {
            return resolved;
        }

        var facts = await LoadFactsAsync(organizationId, cancellationToken);

        foreach (var (type, ids) in byType)
        {
            var rows = await References(type, organizationId, facts)!
                .Where(reference => ids.Contains(reference.Id))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                resolved[(type, row.Id)] = row.Text;
            }
        }

        return resolved;
    }

    /// <summary>
    /// BR-25: a target only when the caller's role can open the module, the entity still resolves in the session
    /// organization and it is inside the caller's branch scope. A visit targets its work order, a payment its invoice.
    /// </summary>
    private async Task<AuditTarget?> TargetAsync(AuditViewer viewer, AuditRecord record, CancellationToken cancellationToken)
    {
        if (record.EntityId is not { } id
            || AuditTargetKinds.ForEntityType(record.EntityType) is not { } kind
            || !AuditAccess.CanOpenKind(viewer.RoleCode, kind))
        {
            return null;
        }

        var organizationId = viewer.OrganizationId;
        var all = viewer.Scope.All;
        var scoped = viewer.Scope.BranchIds.ToArray();
        Guid? target;

        switch (record.EntityType)
        {
            case "service_request":
                target = await dbContext.ServiceRequests.AsNoTracking()
                    .Where(request => request.OrganizationId == organizationId
                        && request.Id == id
                        && (all || request.BranchId == null || scoped.Contains(request.BranchId.Value)))
                    .Select(request => (Guid?)request.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "quote":
                target = await (
                    from quote in dbContext.Quotes.AsNoTracking()
                    join request in dbContext.ServiceRequests.AsNoTracking()
                        on new { quote.OrganizationId, Id = quote.RequestId } equals new { request.OrganizationId, request.Id }
                    where quote.OrganizationId == organizationId
                        && quote.Id == id
                        && (all || request.BranchId == null || scoped.Contains(request.BranchId.Value))
                    select (Guid?)quote.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "work_order":
                target = await dbContext.WorkOrders.AsNoTracking()
                    .Where(order => order.OrganizationId == organizationId && order.Id == id && (all || scoped.Contains(order.BranchId)))
                    .Select(order => (Guid?)order.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "visit":
                target = await (
                    from visit in dbContext.Visits.AsNoTracking()
                    join order in dbContext.WorkOrders.AsNoTracking()
                        on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
                    where visit.OrganizationId == organizationId && visit.Id == id && (all || scoped.Contains(order.BranchId))
                    select (Guid?)order.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "customer":
                target = await dbContext.Customers.AsNoTracking()
                    .Where(customer => customer.OrganizationId == organizationId
                        && customer.Id == id
                        && (all || scoped.Contains(customer.BranchId)))
                    .Select(customer => (Guid?)customer.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "invoice":
                target = await dbContext.Invoices.AsNoTracking()
                    .Where(invoice => invoice.OrganizationId == organizationId
                        && invoice.Id == id
                        && invoice.Status != InvoiceStatus.Void
                        && (all || scoped.Contains(invoice.BranchId)))
                    .Select(invoice => (Guid?)invoice.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "payment":
                target = await (
                    from allocation in dbContext.PaymentAllocations.AsNoTracking()
                    join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
                    join invoice in dbContext.Invoices.AsNoTracking() on allocation.InvoiceId equals invoice.Id
                    where payment.OrganizationId == organizationId
                        && invoice.OrganizationId == organizationId
                        && payment.Id == id
                        && invoice.Status != InvoiceStatus.Void
                        && (all || scoped.Contains(invoice.BranchId))
                    select (Guid?)invoice.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "catalog_item":
                target = await dbContext.CatalogItems.AsNoTracking()
                    .Where(item => item.OrganizationId == organizationId && item.Id == id)
                    .Select(item => (Guid?)item.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "technician_profile":
                target = await dbContext.TechnicianProfiles.AsNoTracking()
                    .Where(profile => profile.OrganizationId == organizationId
                        && profile.Id == id
                        && (all || scoped.Contains(profile.BranchId)))
                    .Select(profile => (Guid?)profile.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "organization_user":
                target = await dbContext.OrganizationUsers.AsNoTracking()
                    .Where(member => member.OrganizationId == organizationId && member.Id == id)
                    .Select(member => (Guid?)member.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "user_invitation":
                target = await dbContext.UserInvitations.AsNoTracking()
                    .Where(invitation => invitation.OrganizationId == organizationId && invitation.Id == id)
                    .Select(invitation => (Guid?)invitation.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            case "organization":
                target = id == organizationId ? id : null;
                break;
            case "branch":
                target = await dbContext.Branches.AsNoTracking()
                    .Where(branch => branch.OrganizationId == organizationId && branch.Id == id && (all || scoped.Contains(branch.Id)))
                    .Select(branch => (Guid?)branch.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                break;
            default:
                target = null;
                break;
        }

        return target is { } resolvedId ? new AuditTarget(kind, resolvedId) : null;
    }
}
