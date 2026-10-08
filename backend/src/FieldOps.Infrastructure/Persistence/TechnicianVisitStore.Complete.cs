using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The Complete job transaction (mobile-job-completion BR-03, BR-04, BR-08 to BR-10). Lock order: the caller's profile
/// row, the visit row, then the work order row; sibling visits are read afterwards and never locked.
/// </summary>
internal sealed partial class TechnicianVisitStore
{
    private const string UniqueViolation = "23505";

    public async Task<ProgressOutcome> CompleteAsync(
        ProgressActor actor, Guid visitId, VisitCompletion completion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await RunLockedAsync(
                actor,
                visitId,
                requireEditable: false,
                (visit, order) => ApplyCompleteAsync(actor, visit, order, completion, now, cancellationToken),
                cancellationToken,
                lockWorkOrder: true);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation, TableName: "customer_signoffs" })
        {
            // BR-10 backstop: another completion stored the signoff first, so nothing of this one was written.
            dbContext.ChangeTracker.Clear();

            var found = await FindAsync(actor.OrganizationId, actor.TechnicianId, actor.ZoneId, visitId, cancellationToken);

            return found is null
                ? new ProgressOutcome(ProgressOutcomeKind.NotFound)
                : new ProgressOutcome(ProgressOutcomeKind.Saved, found, Changed: false);
        }
    }

    private async Task<Step> ApplyCompleteAsync(
        ProgressActor actor, Visit visit, WorkOrder order, VisitCompletion completion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // BR-03: a completed visit is an unchanged repeat; anything but in progress or paused is refused.
        if (visit.Status == VisitStatus.Completed)
        {
            return Step.Unchanged;
        }

        if (visit.Status is not (VisitStatus.InProgress or VisitStatus.Paused))
        {
            return Step.Refused(ProgressOutcomeKind.StatusInvalid);
        }

        // BR-04: required tasks and at least one before and one after photo, read inside the transaction.
        var requiredOpen = await dbContext.VisitChecklistItems.AsNoTracking()
            .AnyAsync(candidate => candidate.VisitId == visit.Id && candidate.IsRequired && !candidate.IsCompleted, cancellationToken);
        var photoTypes = await dbContext.VisitEvidences.AsNoTracking()
            .Where(candidate => candidate.VisitId == visit.Id
                && (candidate.EvidenceType == VisitEvidenceType.Before || candidate.EvidenceType == VisitEvidenceType.After))
            .Select(candidate => candidate.EvidenceType)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (requiredOpen || photoTypes.Count < 2)
        {
            return Step.Refused(ProgressOutcomeKind.RequirementsUnmet);
        }

        // BR-08 (a): every open work or pause entry is closed; a closed pause adds its whole seconds to pause_seconds.
        var entries = await dbContext.VisitTimeEntries
            .Where(candidate => candidate.VisitId == visit.Id && candidate.EntryType != VisitTimeEntryType.Travel)
            .ToListAsync(cancellationToken);
        var closedPauseSeconds = 0;

        foreach (var entry in entries.Where(candidate => candidate.EndedAt is null))
        {
            entry.Close(now);

            if (entry.EntryType == VisitTimeEntryType.Pause)
            {
                closedPauseSeconds += VisitProgressRules.WholeSeconds(entry.StartedAt, entry.EndedAt!.Value);
            }
        }

        var workSeconds = entries
            .Where(candidate => candidate.EntryType == VisitTimeEntryType.Work)
            .Sum(candidate => VisitProgressRules.WholeSeconds(candidate.StartedAt, candidate.EndedAt!.Value));

        // BR-09, decided on the rows committed by earlier completions: the work order row is locked.
        var siblings = await dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == actor.OrganizationId && candidate.WorkOrderId == order.Id)
            .Select(candidate => new OrderVisitState(candidate.Id, candidate.VisitNumber, candidate.Status))
            .ToListAsync(cancellationToken);
        var completesOrder = VisitCompletionRules.CompletesWorkOrder(
            order.Status, order.JobType, order.RecurrenceCount, siblings, visit.Id);
        var orderBefore = order.Status;
        var visitBefore = visit.Status;

        // BR-08 (b) to (d).
        visit.Complete(closedPauseSeconds, completion.WithoutSignatureReason, now);
        dbContext.VisitStatusHistories.Add(
            VisitStatusHistory.Create(visit.Id, visitBefore, VisitStatus.Completed, actor.UserId, now));
        dbContext.CustomerSignoffs.Add(CustomerSignoff.Create(
            visit.Id,
            completion.Method,
            actor.UserId,
            now,
            completion.SignerName,
            completion.Relationship,
            completion.Signature,
            completion.ReviewConfirmed,
            completion.Comments,
            completion.AbsenceReason));

        if (completesOrder)
        {
            order.MarkCompleted(now);
        }

        // Ids, statuses, counts and flags only: never names, relationships, comments, reasons or signature data.
        AddAudit(
            actor,
            visit,
            order,
            "visit.completed",
            new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(visitBefore) },
            new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(VisitStatus.Completed) },
            new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["visitNumber"] = visit.VisitNumber,
                ["acknowledgementMethod"] = completion.Method,
                ["hasSignature"] = completion.Signature is not null,
                ["workSeconds"] = workSeconds,
                ["pauseSeconds"] = visit.PauseSeconds,
                ["workOrderStatusChanged"] = completesOrder,
            });

        if (completesOrder)
        {
            dbContext.AuditLogs.Add(AuditLog.Create(
                actor.OrganizationId,
                "work_order.status_changed",
                "work_order",
                actor.UserId,
                order.Id,
                order.BranchId,
                actor.IpAddress,
                Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.StatusCode(orderBefore) }),
                Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.StatusCode(order.Status) }),
                Serialize(new Dictionary<string, object?> { ["visitId"] = visit.Id })));
        }

        return Step.Done;
    }
}
