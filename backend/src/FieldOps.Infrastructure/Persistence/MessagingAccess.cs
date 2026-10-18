using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Messaging;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Messaging;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>The resolved caller of a messaging request; nothing here comes from the client (BR-01).</summary>
internal sealed record MessagingCaller(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    string RoleCode,
    Guid? TechnicianId,
    Guid? TechnicianBranchId,
    BranchScope Scope)
{
    public bool IsTechnician => TechnicianId is not null;
}

/// <summary>
/// Participation (BR-02) defined once. Everything composes from two roots, <see cref="WorkOrders"/> and <see cref="Branches"/>:
/// a conversation is accessible exactly when its work order (Job) or active branch (Team) is in the root. The Job scope
/// uses the work order's current branch, never <c>conversations.branch_id</c> (creation time only).
/// </summary>
internal static class MessagingAccess
{
    /// <summary>
    /// Resolves the caller: a technician needs an active linked profile (same semantics as the technician visit rules);
    /// staff use the shared branch scope. A membership that does not match the session is the identical not found.
    /// </summary>
    public static async Task<(MessagingCaller? Caller, MessagingStatus Status)> ResolveAsync(
        FieldOpsDbContext dbContext,
        IBranchScopeResolver scopes,
        MessagingActor actor,
        CancellationToken cancellationToken)
    {
        var roleCode = await (
            from member in dbContext.OrganizationUsers.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
            where member.Id == actor.MembershipId
                && member.OrganizationId == actor.OrganizationId
                && member.UserId == actor.UserId
            select role.Code)
            .SingleOrDefaultAsync(cancellationToken);

        if (roleCode is null)
        {
            return (null, MessagingStatus.NotFound);
        }

        if (roleCode == "technician")
        {
            var profile = await dbContext.TechnicianProfiles.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == actor.OrganizationId
                    && candidate.OrganizationUserId == actor.MembershipId)
                .Select(candidate => new { candidate.Id, candidate.BranchId, candidate.Status })
                .SingleOrDefaultAsync(cancellationToken);

            if (profile is null)
            {
                return (null, MessagingStatus.ProfileNotLinked);
            }

            return profile.Status == TechnicianStatus.Active
                ? (new MessagingCaller(
                    actor.OrganizationId, actor.UserId, actor.MembershipId, roleCode, profile.Id, profile.BranchId, BranchScope.Nothing),
                    MessagingStatus.Ok)
                : (null, MessagingStatus.TechnicianInactive);
        }

        if (!MessagingCatalog.Roles.Contains(roleCode))
        {
            return (null, MessagingStatus.NotFound);
        }

        var scope = await scopes.ResolveAsync(actor.OrganizationId, actor.MembershipId, cancellationToken);

        return (
            new MessagingCaller(actor.OrganizationId, actor.UserId, actor.MembershipId, roleCode, null, null, scope),
            MessagingStatus.Ok);
    }

    /// <summary>Non-draft work orders of the organization: staff by current branch scope, technicians by an active assignment on a non-cancelled visit.</summary>
    public static IQueryable<WorkOrder> WorkOrders(FieldOpsDbContext dbContext, MessagingCaller caller)
    {
        var organizationId = caller.OrganizationId;
        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId && order.Status != WorkOrderStatus.Draft);

        if (caller.TechnicianId is { } technicianId)
        {
            return orders.Where(order => dbContext.Visits.Any(visit =>
                visit.OrganizationId == organizationId
                && visit.WorkOrderId == order.Id
                && visit.Status != VisitStatus.Cancelled
                && dbContext.VisitAssignments.Any(assignment =>
                    assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null)));
        }

        if (caller.Scope.All)
        {
            return orders;
        }

        var branchIds = caller.Scope.BranchIds.ToArray();

        return orders.Where(order => branchIds.Contains(order.BranchId));
    }

    /// <summary>Active branches whose Team conversation the caller participates in.</summary>
    public static IQueryable<Branch> Branches(FieldOpsDbContext dbContext, MessagingCaller caller)
    {
        var organizationId = caller.OrganizationId;
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (caller.TechnicianBranchId is { } technicianBranchId)
        {
            return branches.Where(branch => branch.Id == technicianBranchId);
        }

        if (caller.Scope.All)
        {
            return branches;
        }

        var branchIds = caller.Scope.BranchIds.ToArray();

        return branches.Where(branch => branchIds.Contains(branch.Id));
    }

    /// <summary>The conversations the caller may read, send to and mark read.</summary>
    public static IQueryable<Conversation> Conversations(FieldOpsDbContext dbContext, MessagingCaller caller)
    {
        var organizationId = caller.OrganizationId;
        var orders = WorkOrders(dbContext, caller);
        var branches = Branches(dbContext, caller);

        return dbContext.Conversations.AsNoTracking()
            .Where(conversation => conversation.OrganizationId == organizationId
                && ((conversation.Type == ConversationTypes.Job && orders.Any(order => order.Id == conversation.WorkOrderId))
                    || (conversation.Type == ConversationTypes.Team && branches.Any(branch => branch.Id == conversation.BranchId))));
    }

    /// <summary>BR-06: messages of other authors after the caller's read state (all when none).</summary>
    public static IQueryable<ConversationMessage> Unread(FieldOpsDbContext dbContext, Guid organizationId, Guid userId) =>
        dbContext.ConversationMessages.AsNoTracking()
            .Where(message => message.OrganizationId == organizationId
                && message.AuthorUserId != userId
                && !dbContext.ConversationReadStates.Any(state =>
                    state.ConversationId == message.ConversationId && state.UserId == userId && state.LastReadAt >= message.CreatedAt));
}
