using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The dispatch transaction (dispatch-calendar BR-14). Lock order: the visit row, its work order row, then the
/// technician profile rows of the old and new selection in id order. Everything after the locks reads current data.
/// </summary>
internal sealed partial class DispatchStore
{
    private const string AuditEntityType = "visit";

    private const string SerializationFailure = "40001";

    private const string DeadlockDetected = "40P01";

    private const string UniqueViolation = "23505";

    private const string MaterializeSavepoint = "materialize_occurrence";

    public async Task<DispatchOutcome<DispatchSaved>> DispatchAsync(
        DispatchActor actor, Guid visitId, DispatchInput input, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteDispatchAsync(actor, visitId, input, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();

            return new DispatchOutcome<DispatchSaved>.Changed();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            dbContext.ChangeTracker.Clear();

            // A lost race on a partial unique index or a serialization failure reads as "changed by someone else".
            if (postgres.SqlState is SerializationFailure or DeadlockDetected or UniqueViolation)
            {
                return new DispatchOutcome<DispatchSaved>.Changed();
            }

            // The database detail can quote the failing row: only state and constraint travel.
            throw new InvalidOperationException(
                $"The visit dispatch could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }
        catch (PostgresException postgres) when (postgres.SqlState is SerializationFailure or DeadlockDetected)
        {
            dbContext.ChangeTracker.Clear();

            return new DispatchOutcome<DispatchSaved>.Changed();
        }
    }

    private async Task<DispatchOutcome<DispatchSaved>> ExecuteDispatchAsync(
        DispatchActor actor, Guid visitId, DispatchInput input, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var now = timeProvider.GetUtcNow();
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // (1) The visit row inside the organization and the caller scope; a draft work order is never visible.
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
            return new DispatchOutcome<DispatchSaved>.NotFound();
        }

        var orderId = lockedOrders[0];

        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM work_orders
                WHERE id = {orderId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == visitId, cancellationToken);
        var order = await dbContext.WorkOrders.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == orderId, cancellationToken);

        if (IsLocked(visit.Status, order.Status))
        {
            return new DispatchOutcome<DispatchSaved>.Locked();
        }

        if (Truncate(input.UpdatedAt) != visit.UpdatedAt)
        {
            return new DispatchOutcome<DispatchSaved>.Changed();
        }

        // (2) The technician rows of the old and the new selection, then the BR-12 rules on the locked rows.
        var active = await dbContext.VisitAssignments
            .Where(assignment => assignment.VisitId == visitId && assignment.UnassignedAt == null)
            .ToListAsync(cancellationToken);
        var lockIds = active.Select(assignment => assignment.TechnicianId)
            .Concat(input.TechnicianIds)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (lockIds.Length > 0)
        {
            await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT id AS "Value" FROM technician_profiles
                    WHERE organization_id = {organizationId} AND id = ANY({lockIds})
                    ORDER BY id
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken);
        }

        var resolution = await ResolveTechniciansAsync(
            organizationId, actor.Scope, order.BranchId, input.TechnicianIds, cancellationToken);

        if (resolution.Failure is { } failure)
        {
            return failure == TechnicianFailure.Missing
                ? new DispatchOutcome<DispatchSaved>.NotFound()
                : new DispatchOutcome<DispatchSaved>.Invalid(TechnicianErrors);
        }

        var oldIds = active.Select(assignment => assignment.TechnicianId).ToHashSet();
        var oldPrimary = active.Where(assignment => assignment.IsPrimary).Select(assignment => (Guid?)assignment.TechnicianId).FirstOrDefault();

        // (5) A request identical to the current state changes nothing: no write, audit, history or email.
        if (visit.ScheduledStart == input.Start
            && visit.ScheduledEnd == input.End
            && visit.ArrivalWindowStart == input.ArrivalStart
            && visit.ArrivalWindowEnd == input.ArrivalEnd
            && visit.DispatchNote == input.Note
            && oldIds.SetEquals(input.TechnicianIds)
            && oldPrimary == input.PrimaryTechnicianId)
        {
            var unchanged = new VisitDispatchResult(
                await BuildDetailAsync(organizationId, visitId, true, now, cancellationToken),
                WorkOrderCodes.StatusCode(order.Status),
                false,
                null);

            return new DispatchOutcome<DispatchSaved>.Succeeded(new DispatchSaved(unchanged, null));
        }

        // (3) and (4) The commitments are read after the locks, so a concurrent dispatch is already visible.
        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Name, candidate.Phone, candidate.Timezone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);
        var zoneId = await ReadZoneIdAsync(organizationId, order.BranchId, cancellationToken);
        var zone = OrganizationTime.FindZone(zoneId);
        var (dayFrom, dayTo) = BranchTime.Day(input.Start, zone);
        var technicians = await BuildTechniciansAsync(
            organizationId, resolution.Profiles, organization.Timezone, organization.WorkOrderPrefix, dayFrom, dayTo, cancellationToken);
        var required = (await RequiredSkillsAsync(organizationId, [orderId], cancellationToken))
            .GetValueOrDefault(orderId)?.Select(skill => new DispatchSkill(skill.Id, skill.Name)).ToList() ?? [];
        var selection = input.TechnicianIds.Select(id => technicians[id]).ToList();
        var classification = DispatchConflictClassifier.Classify(input.Start, input.End, required, selection, zone, visitId);

        if (classification.Conflicts.Count > 0 && input.OverrideReason is null)
        {
            return new DispatchOutcome<DispatchSaved>.Conflicts(classification.Conflicts);
        }

        // Everything below is the BR-14 write. The previous state is captured first for the audit and the email.
        var oldStatus = visit.Status;
        var oldStart = visit.ScheduledStart;
        var oldEnd = visit.ScheduledEnd;
        var oldArrivalStart = visit.ArrivalWindowStart;
        var oldArrivalEnd = visit.ArrivalWindowEnd;
        var oldNote = visit.DispatchNote;
        var before = Snapshot(oldStatus, oldStart, oldEnd, oldArrivalStart, oldArrivalEnd, oldIds, oldPrimary);

        var newIds = input.TechnicianIds;
        var removed = active.Where(assignment => !newIds.Contains(assignment.TechnicianId)).ToList();
        var kept = active.Where(assignment => newIds.Contains(assignment.TechnicianId)).ToList();
        var addedIds = newIds.Where(id => active.All(assignment => assignment.TechnicianId != id)).ToList();

        // (6) Phase one frees the partial unique indexes: closed assignments and demoted primaries are saved first.
        foreach (var assignment in removed)
        {
            assignment.Unassign(now);
        }

        foreach (var assignment in kept.Where(item => item.IsPrimary && item.TechnicianId != input.PrimaryTechnicianId))
        {
            assignment.SetPrimary(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var assignment in kept.Where(item => !item.IsPrimary && item.TechnicianId == input.PrimaryTechnicianId))
        {
            assignment.SetPrimary(true);
        }

        foreach (var technicianId in addedIds)
        {
            dbContext.VisitAssignments.Add(VisitAssignment.Create(
                visitId, technicianId, actor.UserId, technicianId == input.PrimaryTechnicianId, now));
        }

        var newStatus = newIds.Count > 0 ? VisitStatus.Assigned : VisitStatus.Scheduled;

        visit.ApplyDispatch(input.Start, input.End, input.ArrivalStart, input.ArrivalEnd, input.Note, now);

        if (newStatus != oldStatus)
        {
            visit.SetStatus(newStatus);
            dbContext.VisitStatusHistories.Add(VisitStatusHistory.Create(visitId, oldStatus, newStatus, actor.UserId, now));
        }

        // BR-16: the visit now has a schedule, so a ready work order becomes scheduled. The reverse (no visit keeps a
        // schedule) cannot happen here because every dispatch carries one.
        var orderBefore = order.Status;

        if (order.Status == WorkOrderStatus.ReadyToSchedule)
        {
            order.MarkScheduled(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // BR-18: the next occurrence, once, in a savepoint so a unique violation only undoes the occurrence.
        var materializedId = await MaterializeNextOccurrenceAsync(
            transaction, actor, visit, order, oldStatus, input, zone, now, cancellationToken);

        var emailPlan = await PlanEmailAsync(
            organizationId, order, visit, input, oldStart, oldEnd, oldArrivalStart, oldArrivalEnd, oldIds, selection, cancellationToken);

        // BR-19: ids, statuses and counts only; never the note text, contact data or the email body.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "visit.dispatched",
            AuditEntityType,
            actor.UserId,
            visitId,
            order.BranchId,
            actor.IpAddress,
            Serialize(before),
            Serialize(Snapshot(newStatus, input.Start, input.End, input.ArrivalStart, input.ArrivalEnd, newIds, input.PrimaryTechnicianId)),
            Serialize(new Dictionary<string, object?>
            {
                ["workOrderId"] = orderId,
                ["visitNumber"] = visit.VisitNumber,
                ["noteChanged"] = oldNote != input.Note,
                ["conflicts"] = classification.Conflicts
                    .Select(conflict => new Dictionary<string, object?> { ["technicianId"] = conflict.TechnicianId, ["code"] = conflict.Code })
                    .ToList(),
                ["overrideReason"] = classification.Conflicts.Count > 0 ? input.OverrideReason : null,
                ["notified"] = emailPlan is null ? "none" : "email",
                ["materializedVisitId"] = materializedId,
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

        var result = new VisitDispatchResult(
            await BuildDetailAsync(organizationId, visitId, true, now, cancellationToken), orderStatus, false, materializedId);

        return new DispatchOutcome<DispatchSaved>.Succeeded(new DispatchSaved(result, emailPlan));
    }

    private async Task<Guid?> MaterializeNextOccurrenceAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        DispatchActor actor,
        Visit visit,
        WorkOrder order,
        VisitStatus oldStatus,
        DispatchInput input,
        TimeZoneInfo zone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (oldStatus != VisitStatus.Unscheduled
            || order.JobType != WorkOrderJobTypes.Recurring
            || order.RecurrenceCount is not { } count
            || visit.VisitNumber >= count)
        {
            return null;
        }

        var nextNumber = visit.VisitNumber + 1;

        if (await dbContext.Visits.AsNoTracking().AnyAsync(
                candidate => candidate.WorkOrderId == order.Id && candidate.VisitNumber == nextNumber, cancellationToken))
        {
            return null;
        }

        // The preferred window is the arrival window of visit k on its local date, plus the interval. An arrival at the
        // start has no length, so the window then ends where the visit ends.
        var windowStart = TimeOnly.FromDateTime(OrganizationTime.ToZone(input.ArrivalStart, zone).DateTime);
        var windowEnd = TimeOnly.FromDateTime(OrganizationTime.ToZone(input.ArrivalEnd, zone).DateTime);

        if (windowEnd <= windowStart)
        {
            windowEnd = TimeOnly.FromDateTime(OrganizationTime.ToZone(input.End, zone).DateTime);
        }

        if (RecurrenceCalculator.NextWindow(
                order.RecurrenceFrequency, OrganizationTime.LocalDate(input.Start, zone), windowStart, windowEnd, zone) is not { } next)
        {
            return null;
        }

        var templates = await dbContext.WorkOrderChecklistTemplates.AsNoTracking()
            .Where(template => template.WorkOrderId == order.Id)
            .OrderBy(template => template.SortOrder)
            .ThenBy(template => template.Id)
            .ToListAsync(cancellationToken);

        var created = Visit.Create(actor.OrganizationId, order.Id, nextNumber, preferredStart: next.Start, preferredEnd: next.End);
        var history = VisitStatusHistory.Create(created.Id, null, VisitStatus.Unscheduled, actor.UserId, now);
        var items = templates
            .Select((template, index) => VisitChecklistItem.Create(created.Id, template.Label, template.Id, index))
            .ToList();

        await transaction.CreateSavepointAsync(MaterializeSavepoint, cancellationToken);
        dbContext.Visits.Add(created);
        dbContext.VisitStatusHistories.Add(history);
        dbContext.VisitChecklistItems.AddRange(items);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return created.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Someone else already materialized the occurrence: it counts as done and nothing of ours is kept.
            await transaction.RollbackToSavepointAsync(MaterializeSavepoint, cancellationToken);
            dbContext.Entry(created).State = EntityState.Detached;
            dbContext.Entry(history).State = EntityState.Detached;

            foreach (var item in items)
            {
                dbContext.Entry(item).State = EntityState.Detached;
            }

            return null;
        }
    }

    /// <summary>BR-17: the one customer email of this save, or null when none applies.</summary>
    private async Task<VisitEmail?> PlanEmailAsync(
        Guid organizationId,
        WorkOrder order,
        Visit visit,
        DispatchInput input,
        DateTimeOffset? oldStart,
        DateTimeOffset? oldEnd,
        DateTimeOffset? oldArrivalStart,
        DateTimeOffset? oldArrivalEnd,
        HashSet<Guid> oldIds,
        IReadOnlyList<DispatchTechnician> selection,
        CancellationToken cancellationToken)
    {
        if (!input.NotifyCustomer)
        {
            return null;
        }

        var contact = await ReadContactAsync(organizationId, order.CustomerId, cancellationToken);

        if (string.IsNullOrWhiteSpace(contact?.Email))
        {
            return null;
        }

        var first = oldStart is null;
        var scheduleChanged = first || oldStart != input.Start || oldEnd != input.End;
        var windowChanged = oldArrivalStart != input.ArrivalStart || oldArrivalEnd != input.ArrivalEnd;
        var techniciansChanged = !oldIds.SetEquals(input.TechnicianIds);
        var techniciansOnly = techniciansChanged && input.TechnicianIds.Count > 0 && input.SendTechnicianDetails;

        if (!scheduleChanged && !windowChanged && !techniciansOnly)
        {
            return null;
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Name, candidate.Phone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);
        var names = input.SendTechnicianDetails && selection.Count > 0
            ? selection.OrderByDescending(technician => technician.Id == input.PrimaryTechnicianId).Select(technician => technician.Name).ToList()
            : null;

        return new VisitEmail(
            first ? VisitEmailKind.Scheduled : scheduleChanged || windowChanged ? VisitEmailKind.Rescheduled : VisitEmailKind.TechnicianUpdate,
            visit.Id,
            contact.Email.Trim(),
            contact.FirstName,
            organization.Name,
            organization.Phone,
            RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber),
            order.Title,
            await ReadZoneIdAsync(organizationId, order.BranchId, cancellationToken),
            input.Start,
            input.ArrivalStart,
            input.ArrivalEnd,
            names);
    }

    private static Dictionary<string, object?> Snapshot(
        VisitStatus status,
        DateTimeOffset? start,
        DateTimeOffset? end,
        DateTimeOffset? arrivalStart,
        DateTimeOffset? arrivalEnd,
        IEnumerable<Guid> technicianIds,
        Guid? primaryTechnicianId) =>
        new()
        {
            ["status"] = WorkOrderCodes.VisitStatusCode(status),
            ["scheduledStart"] = start,
            ["scheduledEnd"] = end,
            ["arrivalWindowStart"] = arrivalStart,
            ["arrivalWindowEnd"] = arrivalEnd,
            ["technicianIds"] = technicianIds.OrderBy(id => id).ToList(),
            ["primaryTechnicianId"] = primaryTechnicianId,
        };

    /// <summary>The concurrency value as stored: UTC, truncated to microseconds (the PostgreSQL precision).</summary>
    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;

        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
