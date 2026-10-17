using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The cancel transaction (technician-schedule-active-visits BR-10 to BR-14). Lock order: the technician profile rows of
/// the active assignments in id order (as every technician mutation), the visit row, then its work order row. Everything
/// after the locks reads current data. No customer email and no notification row are written.
/// </summary>
internal sealed partial class DispatchStore
{
    private static readonly VisitStatus[] OpenWorkStatuses =
        [VisitStatus.Scheduled, VisitStatus.Assigned, VisitStatus.OnTheWay, VisitStatus.InProgress, VisitStatus.Paused];

    public async Task<DispatchOutcome<VisitCancelResult>> CancelAsync(
        DispatchActor actor, Guid visitId, CancelInput input, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteCancelAsync(actor, visitId, input, cancellationToken);
        }
        catch (Exception exception) when (IsRetryable(exception))
        {
            // The whole transaction was rolled back (lock inversion with a dispatch or a deadlock): once more on a clean
            // tracker, then the caller reloads.
            dbContext.ChangeTracker.Clear();
        }

        try
        {
            return await ExecuteCancelAsync(actor, visitId, input, cancellationToken);
        }
        catch (Exception exception) when (IsRetryable(exception) || exception is DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();

            return new DispatchOutcome<VisitCancelResult>.Changed();
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is PostgresException { SqlState: SerializationFailure or DeadlockDetected or UniqueViolation }
        || (exception as DbUpdateException)?.InnerException is PostgresException
        {
            SqlState: SerializationFailure or DeadlockDetected or UniqueViolation,
        };

    private async Task<DispatchOutcome<VisitCancelResult>> ExecuteCancelAsync(
        DispatchActor actor, Guid visitId, CancelInput input, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var now = timeProvider.GetUtcNow();
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();

        // (1) The visit inside the organization and the caller scope, without a lock: out of scope is the identical 404.
        if (await ReadVisitInfoAsync(organizationId, actor.Scope, visitId, cancellationToken) is null)
        {
            return new DispatchOutcome<VisitCancelResult>.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // (2) The profile rows of the active assignees, in id order, like every technician mutation.
        var assignees = (await dbContext.VisitAssignments.AsNoTracking()
            .Where(assignment => assignment.VisitId == visitId && assignment.UnassignedAt == null)
            .Select(assignment => assignment.TechnicianId)
            .ToListAsync(cancellationToken))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (assignees.Length > 0)
        {
            await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT id AS "Value" FROM technician_profiles
                    WHERE organization_id = {organizationId} AND id = ANY({assignees})
                    ORDER BY id
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken);
        }

        // (3) The visit row, scope-checked again under the lock.
        var lockedOrders = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT w.id AS "Value" FROM visits v
                JOIN work_orders w ON w.id = v.work_order_id AND w.organization_id = v.organization_id
                WHERE v.id = {visitId} AND v.organization_id = {organizationId} AND w.status <> 'draft'
                  AND ({scopeAll} OR w.branch_id = ANY({scopeIds}))
                FOR UPDATE OF v
                """)
            .ToListAsync(cancellationToken);

        if (lockedOrders.Count != 1)
        {
            return new DispatchOutcome<VisitCancelResult>.NotFound();
        }

        var orderId = lockedOrders[0];

        // (4) The work order row, last.
        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM work_orders
                WHERE id = {orderId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        // (5) State read after the locks, so a concurrent request is already visible.
        var visit = await dbContext.Visits.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == visitId, cancellationToken);
        var order = await dbContext.WorkOrders.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == orderId, cancellationToken);

        if (Truncate(input.UpdatedAt) != visit.UpdatedAt)
        {
            return new DispatchOutcome<VisitCancelResult>.Changed();
        }

        if (!Visit.IsCancellable(visit.Status) || ClosedOrderStatuses.Contains(order.Status))
        {
            return new DispatchOutcome<VisitCancelResult>.NotCancellable();
        }

        // An assignee added after the first read has no profile lock: the caller reloads instead of racing.
        var currentAssignees = await dbContext.VisitAssignments.AsNoTracking()
            .Where(assignment => assignment.VisitId == visitId && assignment.UnassignedAt == null)
            .Select(assignment => assignment.TechnicianId)
            .ToListAsync(cancellationToken);

        if (currentAssignees.Any(id => !assignees.Contains(id)))
        {
            return new DispatchOutcome<VisitCancelResult>.Changed();
        }

        // BR-12 (a): every open entry is closed (strictly after its start); a closed pause adds its whole seconds.
        var openEntries = await dbContext.VisitTimeEntries
            .Where(entry => entry.VisitId == visitId && entry.EndedAt == null)
            .ToListAsync(cancellationToken);
        var closedPauseSeconds = 0;

        foreach (var entry in openEntries)
        {
            entry.Close(now);

            if (entry.EntryType == VisitTimeEntryType.Pause)
            {
                closedPauseSeconds += VisitProgressRules.WholeSeconds(entry.StartedAt, entry.EndedAt!.Value);
            }
        }

        // BR-12 (b), (c): the status and the one history row with the reason.
        var oldStatus = visit.Status;

        visit.Cancel(closedPauseSeconds, now);
        dbContext.VisitStatusHistories.Add(
            VisitStatusHistory.Create(visitId, oldStatus, VisitStatus.Cancelled, actor.UserId, now, input.Reason));

        // BR-14: a one-time work order gets one unscheduled replacement with the pending tasks; a recurring one skips the occurrence.
        Guid? replacementId = order.JobType == WorkOrderJobTypes.OneTime
            ? await AddReplacementAsync(actor, visit, order, now, cancellationToken)
            : null;

        // BR-13: nothing left to do on a scheduled or in progress work order sends it back to the queue.
        var orderBefore = order.Status;

        if (order.Status is WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress
            && !await dbContext.Visits.AsNoTracking().AnyAsync(
                candidate => candidate.WorkOrderId == orderId
                    && candidate.Id != visitId
                    && candidate.Id != replacementId
                    && OpenWorkStatuses.Contains(candidate.Status),
                cancellationToken))
        {
            order.ReturnToReadyToScheduleAfterCancel(now);
        }

        // BR-12 (e): ids, statuses and counts only; the reason lives in the history row alone.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "visit.cancelled",
            AuditEntityType,
            actor.UserId,
            visitId,
            order.BranchId,
            actor.IpAddress,
            Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(oldStatus) }),
            Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(VisitStatus.Cancelled) }),
            Serialize(new Dictionary<string, object?>
            {
                ["workOrderId"] = orderId,
                ["visitNumber"] = visit.VisitNumber,
                ["replacementVisitId"] = replacementId,
                ["closedTimeEntries"] = openEntries.Count,
            })));

        if (order.Status != orderBefore)
        {
            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                "work_order.status_changed",
                "work_order",
                actor.UserId,
                orderId,
                order.BranchId,
                actor.IpAddress,
                Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.StatusCode(orderBefore) }),
                Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.StatusCode(order.Status) }),
                Serialize(new Dictionary<string, object?> { ["visitId"] = visitId })));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var orderStatus = WorkOrderCodes.StatusCode(order.Status);

        dbContext.ChangeTracker.Clear();

        return new DispatchOutcome<VisitCancelResult>.Succeeded(new VisitCancelResult(
            await BuildDetailAsync(organizationId, visitId, true, now, cancellationToken), replacementId, orderStatus));
    }

    /// <summary>BR-14: next visit number over every visit of the order, history null to unscheduled, pending tasks copied.</summary>
    private async Task<Guid> AddReplacementAsync(
        DispatchActor actor, Visit cancelled, WorkOrder order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var maxNumber = await dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == actor.OrganizationId && candidate.WorkOrderId == order.Id)
            .MaxAsync(candidate => candidate.VisitNumber, cancellationToken);
        var pending = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(item => item.VisitId == cancelled.Id && !item.IsCompleted)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var replacement = Visit.Create(actor.OrganizationId, order.Id, maxNumber + 1);

        dbContext.Visits.Add(replacement);
        dbContext.VisitStatusHistories.Add(
            VisitStatusHistory.Create(replacement.Id, null, VisitStatus.Unscheduled, actor.UserId, now));
        dbContext.VisitChecklistItems.AddRange(pending.Select(item =>
            VisitChecklistItem.Create(replacement.Id, item.Label, item.TemplateItemId, item.SortOrder, item.IsRequired)));

        return replacement.Id;
    }
}
