using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The mobile-job-progress transactions (BR-03 to BR-13). Same lock order and conflict retry as the travel
/// transaction: the caller's technician profile row, then the visit row, then a re-read of the state.
/// </summary>
internal sealed partial class TechnicianVisitStore
{
    private const int MaxTasks = 50;

    private const int MaxMaterials = 50;

    private const int MaxEvidence = 20;

    private const int CatalogLimit = 20;

    public async Task<ProgressAccess?> GetProgressAccessAsync(
        Guid organizationId, Guid technicianId, Guid visitId, CancellationToken cancellationToken)
    {
        var row = await (
            from visit in AssignedVisits(organizationId, technicianId)
            where visit.Id == visitId
            select new
            {
                visit.Status,
                IsPrimary = dbContext.VisitAssignments
                    .Where(assignment => assignment.VisitId == visit.Id
                        && assignment.TechnicianId == technicianId
                        && assignment.UnassignedAt == null)
                    .Select(assignment => assignment.IsPrimary)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new ProgressAccess(row.IsPrimary, row.Status);
    }

    public Task<ProgressOutcome> TransitionAsync(
        ProgressActor actor, Guid visitId, ProgressTransition transition, DateTimeOffset now, CancellationToken cancellationToken) =>
        RunLockedAsync(
            actor,
            visitId,
            requireEditable: false,
            (visit, order) => ApplyTransitionAsync(actor, visit, order, transition, now, cancellationToken),
            cancellationToken);

    public Task<ProgressOutcome> EditAsync(
        ProgressActor actor, Guid visitId, VisitEdit edit, DateTimeOffset now, CancellationToken cancellationToken) =>
        RunLockedAsync(
            actor,
            visitId,
            requireEditable: true,
            (visit, order) => ApplyEditAsync(actor, visit, order, edit, now, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<MaterialCatalogItem>> SearchMaterialCatalogAsync(
        Guid organizationId, string search, CancellationToken cancellationToken)
    {
        var pattern = $"%{search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

        return await dbContext.CatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.Type == CatalogItemType.Product
                && item.IsActive
                && (EF.Functions.ILike(item.Name, pattern) || (item.Sku != null && EF.Functions.ILike(item.Sku, pattern))))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Take(CatalogLimit)
            .Select(item => new MaterialCatalogItem(item.Id, item.Name, item.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<VisitEvidenceImage?> FindEvidenceAsync(
        Guid organizationId, Guid technicianId, Guid visitId, Guid evidenceId, CancellationToken cancellationToken)
    {
        // Content and type only, and only through an active assignment of the caller to that very visit (BR-12).
        var row = await (
            from visit in AssignedVisits(organizationId, technicianId)
            join evidence in dbContext.VisitEvidences.AsNoTracking() on visit.Id equals evidence.VisitId
            where visit.Id == visitId && evidence.Id == evidenceId && evidence.Content != null
            select new { evidence.MimeType, evidence.Content })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new VisitEvidenceImage(row.MimeType, row.Content!);
    }

    private sealed record Step(ProgressOutcomeKind Kind, bool Changed = false, string? LimitCode = null)
    {
        public static Step Unchanged { get; } = new(ProgressOutcomeKind.Saved);

        public static Step Done { get; } = new(ProgressOutcomeKind.Saved, true);

        public static Step Refused(ProgressOutcomeKind kind) => new(kind);
    }

    private async Task<ProgressOutcome> RunLockedAsync(
        ProgressActor actor,
        Guid visitId,
        bool requireEditable,
        Func<Visit, WorkOrder, Task<Step>> apply,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteLockedAsync(actor, visitId, requireEditable, apply, cancellationToken);
        }
        catch (Exception exception) when (IsLockConflict(exception))
        {
            // The whole transaction was rolled back: it runs once more on a clean tracker, then fails as a 500.
            dbContext.ChangeTracker.Clear();

            return await ExecuteLockedAsync(actor, visitId, requireEditable, apply, cancellationToken);
        }
    }

    private async Task<ProgressOutcome> ExecuteLockedAsync(
        ProgressActor actor,
        Guid visitId,
        bool requireEditable,
        Func<Visit, WorkOrder, Task<Step>> apply,
        CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var technicianId = actor.TechnicianId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // (1) The caller's profile row serializes every mutation of the technician (BR-07).
        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM technician_profiles
                WHERE id = {technicianId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        // (2) The visit row, only through an active assignment of the caller: any other visit is the identical 404.
        var locked = await dbContext.Database
            .SqlQuery<bool>(
                $"""
                SELECT a.is_primary AS "Value" FROM visits v
                JOIN visit_assignments a ON a.visit_id = v.id AND a.technician_id = {technicianId} AND a.unassigned_at IS NULL
                WHERE v.id = {visitId} AND v.organization_id = {organizationId}
                  AND v.status NOT IN ('unscheduled', 'cancelled')
                  AND v.scheduled_start IS NOT NULL AND v.scheduled_end IS NOT NULL
                FOR UPDATE OF v
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            return new ProgressOutcome(ProgressOutcomeKind.NotFound);
        }

        if (!locked[0])
        {
            return new ProgressOutcome(ProgressOutcomeKind.NotPrimary);
        }

        // (3) State read after the locks, so a concurrent request is already visible.
        var visit = await dbContext.Visits.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == visitId, cancellationToken);

        if (requireEditable && visit.Status is not (VisitStatus.InProgress or VisitStatus.Paused))
        {
            return new ProgressOutcome(ProgressOutcomeKind.StatusInvalid);
        }

        var order = await dbContext.WorkOrders.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == visit.WorkOrderId, cancellationToken);

        // (4) Guards and writes; a refusal or an unchanged repeat commits nothing but still releases the locks.
        var step = await apply(visit, order);

        if (step.Kind != ProgressOutcomeKind.Saved)
        {
            return new ProgressOutcome(step.Kind, LimitCode: step.LimitCode);
        }

        if (step.Changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // (5) The response is read fresh through the shared read path.
        dbContext.ChangeTracker.Clear();

        var found = await FindAsync(organizationId, technicianId, actor.ZoneId, visitId, cancellationToken);

        return found is null
            ? new ProgressOutcome(ProgressOutcomeKind.NotFound)
            : new ProgressOutcome(ProgressOutcomeKind.Saved, found, step.Changed);
    }

    private async Task<Step> ApplyTransitionAsync(
        ProgressActor actor, Visit visit, WorkOrder order, ProgressTransition transition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var technicianId = actor.TechnicianId;

        switch (transition)
        {
            case ProgressTransition.StartJob:
                {
                    if (visit.Status is VisitStatus.InProgress or VisitStatus.Paused)
                    {
                        return Step.Unchanged;
                    }

                    // BR-03: only an arrived visit starts; the latest travel entry must be closed.
                    var travel = visit.Status == VisitStatus.OnTheWay
                        ? await dbContext.VisitTimeEntries.AsNoTracking()
                            .Where(candidate => candidate.VisitId == visit.Id && candidate.EntryType == VisitTimeEntryType.Travel)
                            .OrderByDescending(candidate => candidate.StartedAt)
                            .ThenByDescending(candidate => candidate.Id)
                            .Select(candidate => new { candidate.EndedAt })
                            .FirstOrDefaultAsync(cancellationToken)
                        : null;

                    if (travel?.EndedAt is null)
                    {
                        return Step.Refused(ProgressOutcomeKind.StatusInvalid);
                    }

                    var workOrderChanged = order.Status == WorkOrderStatus.Scheduled;

                    visit.StartJob(now);
                    dbContext.VisitStatusHistories.Add(
                        VisitStatusHistory.Create(visit.Id, VisitStatus.OnTheWay, VisitStatus.InProgress, actor.UserId, now));
                    dbContext.VisitTimeEntries.Add(VisitTimeEntry.Create(visit.Id, technicianId, now, VisitTimeEntryType.Work));

                    if (workOrderChanged)
                    {
                        order.MarkInProgress(now);
                    }

                    AddAudit(
                        actor,
                        visit,
                        order,
                        "visit.job_started",
                        new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(VisitStatus.OnTheWay) },
                        new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(VisitStatus.InProgress) },
                        new Dictionary<string, object?>
                        {
                            ["workOrderId"] = order.Id,
                            ["visitNumber"] = visit.VisitNumber,
                            ["workOrderStatusChanged"] = workOrderChanged,
                        });

                    return Step.Done;
                }

            case ProgressTransition.Pause:
                {
                    if (visit.Status == VisitStatus.Paused)
                    {
                        return Step.Unchanged;
                    }

                    var work = visit.Status == VisitStatus.InProgress
                        ? await OpenEntryAsync(visit.Id, technicianId, VisitTimeEntryType.Work, cancellationToken)
                        : null;

                    if (work is null)
                    {
                        return Step.Refused(ProgressOutcomeKind.StatusInvalid);
                    }

                    work.Close(now);
                    dbContext.VisitTimeEntries.Add(VisitTimeEntry.Create(visit.Id, technicianId, work.EndedAt!.Value, VisitTimeEntryType.Pause));
                    visit.Pause(now);
                    dbContext.VisitStatusHistories.Add(
                        VisitStatusHistory.Create(visit.Id, VisitStatus.InProgress, VisitStatus.Paused, actor.UserId, now));
                    AddAudit(actor, visit, order, "visit.paused", null, null, TransitionMetadata(order, visit));

                    return Step.Done;
                }

            default:
                {
                    if (visit.Status == VisitStatus.InProgress)
                    {
                        return Step.Unchanged;
                    }

                    var pause = visit.Status == VisitStatus.Paused
                        ? await OpenEntryAsync(visit.Id, technicianId, VisitTimeEntryType.Pause, cancellationToken)
                        : null;

                    if (pause is null)
                    {
                        return Step.Refused(ProgressOutcomeKind.StatusInvalid);
                    }

                    pause.Close(now);

                    var closedAt = pause.EndedAt!.Value;

                    dbContext.VisitTimeEntries.Add(VisitTimeEntry.Create(visit.Id, technicianId, closedAt, VisitTimeEntryType.Work));
                    visit.Resume(VisitProgressRules.WholeSeconds(pause.StartedAt, closedAt), now);
                    dbContext.VisitStatusHistories.Add(
                        VisitStatusHistory.Create(visit.Id, VisitStatus.Paused, VisitStatus.InProgress, actor.UserId, now));
                    AddAudit(actor, visit, order, "visit.resumed", null, null, TransitionMetadata(order, visit));

                    return Step.Done;
                }
        }
    }

    private Task<VisitTimeEntry?> OpenEntryAsync(
        Guid visitId, Guid technicianId, VisitTimeEntryType type, CancellationToken cancellationToken) =>
        dbContext.VisitTimeEntries
            .Where(candidate => candidate.VisitId == visitId
                && candidate.TechnicianId == technicianId
                && candidate.EntryType == type
                && candidate.EndedAt == null)
            .OrderByDescending(candidate => candidate.StartedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static Dictionary<string, object?> TransitionMetadata(WorkOrder order, Visit visit) =>
        new() { ["workOrderId"] = order.Id, ["visitNumber"] = visit.VisitNumber };

    private Task<Step> ApplyEditAsync(
        ProgressActor actor, Visit visit, WorkOrder order, VisitEdit edit, DateTimeOffset now, CancellationToken cancellationToken) =>
        edit switch
        {
            UpdateTaskEdit task => UpdateTaskAsync(actor, visit, task, now, cancellationToken),
            AddTaskEdit task => AddTaskAsync(visit, task, cancellationToken),
            SetPlannedMaterialEdit planned => SetPlannedMaterialAsync(actor, visit, order, planned, cancellationToken),
            AddMaterialEdit material => AddMaterialAsync(actor, visit, order, material, cancellationToken),
            SetMaterialEdit material => SetMaterialAsync(actor, visit, order, material, cancellationToken),
            SetNotesEdit notes => Task.FromResult(SetNotes(visit, notes, now)),
            AddEvidenceEdit evidence => AddEvidenceAsync(actor, visit, order, evidence, cancellationToken),
            DeleteEvidenceEdit evidence => DeleteEvidenceAsync(actor, visit, order, evidence, cancellationToken),
            _ => throw new InvalidOperationException("Unknown visit edit."),
        };

    private async Task<Step> UpdateTaskAsync(
        ProgressActor actor, Visit visit, UpdateTaskEdit edit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await dbContext.VisitChecklistItems.FirstOrDefaultAsync(
            candidate => candidate.Id == edit.TaskId && candidate.VisitId == visit.Id, cancellationToken);

        if (task is null)
        {
            return Step.Refused(ProgressOutcomeKind.NotFound);
        }

        var changed = false;

        if (edit.IsCompleted is { } completed && completed != task.IsCompleted)
        {
            task.SetCompleted(completed, actor.UserId, now);
            changed = true;
        }

        if (edit.Notes is { } notes)
        {
            var next = notes.Length == 0 ? null : notes;

            if (!string.Equals(next, task.Notes, StringComparison.Ordinal))
            {
                task.SetNotes(next);
                changed = true;
            }
        }

        return changed ? Step.Done : Step.Unchanged;
    }

    private async Task<Step> AddTaskAsync(Visit visit, AddTaskEdit edit, CancellationToken cancellationToken)
    {
        var stats = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(candidate => candidate.VisitId == visit.Id)
            .GroupBy(candidate => candidate.VisitId)
            .Select(group => new { Count = group.Count(), MaxOrder = group.Max(candidate => candidate.SortOrder) })
            .FirstOrDefaultAsync(cancellationToken);

        if (stats is { Count: >= MaxTasks })
        {
            return new Step(ProgressOutcomeKind.LimitReached, LimitCode: TechnicianVisitCodes.TaskLimitReached);
        }

        dbContext.VisitChecklistItems.Add(
            VisitChecklistItem.Create(visit.Id, edit.Label, null, stats is null ? 0 : stats.MaxOrder + 1, isRequired: false));

        return Step.Done;
    }

    private async Task<Step> SetPlannedMaterialAsync(
        ProgressActor actor, Visit visit, WorkOrder order, SetPlannedMaterialEdit edit, CancellationToken cancellationToken)
    {
        var planned = await dbContext.WorkOrderPlannedMaterials.AsNoTracking()
            .Where(candidate => candidate.Id == edit.PlannedMaterialId
                && candidate.OrganizationId == actor.OrganizationId
                && candidate.WorkOrderId == visit.WorkOrderId)
            .Select(candidate => new { candidate.CatalogItemId, candidate.Description, candidate.Unit })
            .FirstOrDefaultAsync(cancellationToken);

        if (planned is null)
        {
            return Step.Refused(ProgressOutcomeKind.NotFound);
        }

        var existing = await dbContext.VisitMaterials.FirstOrDefaultAsync(
            candidate => candidate.VisitId == visit.Id && candidate.PlannedMaterialId == edit.PlannedMaterialId, cancellationToken);

        if (existing is null)
        {
            if (edit.UsedQuantity == 0)
            {
                return Step.Unchanged;
            }

            var unitCost = planned.CatalogItemId is { } catalogItemId
                ? await dbContext.CatalogItems.AsNoTracking()
                    .Where(candidate => candidate.Id == catalogItemId && candidate.OrganizationId == actor.OrganizationId)
                    .Select(candidate => (decimal?)candidate.UnitCost)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0m
                : 0m;
            var created = VisitMaterial.Create(
                visit.Id, planned.Description, edit.UsedQuantity, planned.Unit, planned.CatalogItemId, unitCost, edit.PlannedMaterialId);

            dbContext.VisitMaterials.Add(created);
            AddMaterialAudit(actor, visit, order, created.Id, edit.PlannedMaterialId, 0m, edit.UsedQuantity);

            return Step.Done;
        }

        return ChangeQuantity(actor, visit, order, existing, edit.UsedQuantity);
    }

    private async Task<Step> AddMaterialAsync(
        ProgressActor actor, Visit visit, WorkOrder order, AddMaterialEdit edit, CancellationToken cancellationToken)
    {
        VisitMaterial created;

        if (edit.CatalogItemId is { } catalogItemId)
        {
            // BR-10: an active product of the session organization; anything else is the identical 404.
            var item = await dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.Id == catalogItemId
                    && candidate.OrganizationId == actor.OrganizationId
                    && candidate.Type == CatalogItemType.Product
                    && candidate.IsActive)
                .Select(candidate => new { candidate.Name, candidate.Unit, candidate.UnitCost })
                .FirstOrDefaultAsync(cancellationToken);

            if (item is null)
            {
                return Step.Refused(ProgressOutcomeKind.NotFound);
            }

            if (await AdditionalMaterialCountAsync(visit.Id, cancellationToken) >= MaxMaterials)
            {
                return new Step(ProgressOutcomeKind.LimitReached, LimitCode: TechnicianVisitCodes.MaterialLimitReached);
            }

            created = VisitMaterial.Create(visit.Id, item.Name, edit.Quantity, item.Unit, catalogItemId, item.UnitCost);
        }
        else
        {
            if (await AdditionalMaterialCountAsync(visit.Id, cancellationToken) >= MaxMaterials)
            {
                return new Step(ProgressOutcomeKind.LimitReached, LimitCode: TechnicianVisitCodes.MaterialLimitReached);
            }

            created = VisitMaterial.Create(visit.Id, edit.Description!, edit.Quantity, edit.Unit!);
        }

        dbContext.VisitMaterials.Add(created);
        AddMaterialAudit(actor, visit, order, created.Id, null, 0m, edit.Quantity);

        return Step.Done;
    }

    private Task<int> AdditionalMaterialCountAsync(Guid visitId, CancellationToken cancellationToken) =>
        dbContext.VisitMaterials.AsNoTracking()
            .CountAsync(candidate => candidate.VisitId == visitId && candidate.PlannedMaterialId == null, cancellationToken);

    private async Task<Step> SetMaterialAsync(
        ProgressActor actor, Visit visit, WorkOrder order, SetMaterialEdit edit, CancellationToken cancellationToken)
    {
        // A planned-linked row belongs to the planned-material endpoint only: here it is the identical 404.
        var material = await dbContext.VisitMaterials.FirstOrDefaultAsync(
            candidate => candidate.Id == edit.MaterialId && candidate.VisitId == visit.Id && candidate.PlannedMaterialId == null,
            cancellationToken);

        return material is null
            ? Step.Refused(ProgressOutcomeKind.NotFound)
            : ChangeQuantity(actor, visit, order, material, edit.Quantity);
    }

    /// <summary>BR-09, BR-10: zero deletes the row, another quantity updates it, the same quantity writes nothing.</summary>
    private Step ChangeQuantity(ProgressActor actor, Visit visit, WorkOrder order, VisitMaterial material, decimal quantity)
    {
        var before = material.Quantity;

        if (quantity == before)
        {
            return Step.Unchanged;
        }

        if (quantity == 0)
        {
            dbContext.VisitMaterials.Remove(material);
        }
        else
        {
            material.SetQuantity(quantity);
        }

        AddMaterialAudit(actor, visit, order, material.Id, material.PlannedMaterialId, before, quantity);

        return Step.Done;
    }

    private void AddMaterialAudit(
        ProgressActor actor, Visit visit, WorkOrder order, Guid materialId, Guid? plannedMaterialId, decimal before, decimal after) =>
        AddAudit(
            actor,
            visit,
            order,
            "visit.material_recorded",
            null,
            null,
            new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["visitNumber"] = visit.VisitNumber,
                ["materialId"] = materialId,
                ["plannedMaterialId"] = plannedMaterialId,
                ["quantityBefore"] = before,
                ["quantityAfter"] = after,
            });

    private static Step SetNotes(Visit visit, SetNotesEdit edit, DateTimeOffset now)
    {
        // BR-13: the text is stored and never logged or audited.
        visit.SetTechnicianNotes(edit.Notes, now);

        return Step.Done;
    }

    private async Task<Step> AddEvidenceAsync(
        ProgressActor actor, Visit visit, WorkOrder order, AddEvidenceEdit edit, CancellationToken cancellationToken)
    {
        if (await dbContext.VisitEvidences.AsNoTracking().CountAsync(candidate => candidate.VisitId == visit.Id, cancellationToken) >= MaxEvidence)
        {
            return new Step(ProgressOutcomeKind.LimitReached, LimitCode: TechnicianVisitCodes.EvidenceLimitReached);
        }

        var evidence = VisitEvidence.CreateInline(visit.Id, edit.FileName, edit.MimeType, edit.Content, edit.Type, actor.UserId);

        dbContext.VisitEvidences.Add(evidence);
        AddAudit(
            actor,
            visit,
            order,
            "visit.evidence_added",
            null,
            null,
            new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["visitNumber"] = visit.VisitNumber,
                ["evidenceId"] = evidence.Id,
                ["evidenceType"] = EvidenceTypeCode(evidence.EvidenceType),
                ["sizeBytes"] = evidence.SizeBytes,
            });

        return Step.Done;
    }

    private async Task<Step> DeleteEvidenceAsync(
        ProgressActor actor, Visit visit, WorkOrder order, DeleteEvidenceEdit edit, CancellationToken cancellationToken)
    {
        // The content is never loaded: only the type for the audit row, then a set-based delete inside the transaction.
        var stored = await dbContext.VisitEvidences.AsNoTracking()
            .Where(candidate => candidate.Id == edit.EvidenceId && candidate.VisitId == visit.Id)
            .Select(candidate => new { candidate.EvidenceType })
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return Step.Refused(ProgressOutcomeKind.NotFound);
        }

        var evidenceType = stored.EvidenceType;

        await dbContext.VisitEvidences
            .Where(candidate => candidate.Id == edit.EvidenceId && candidate.VisitId == visit.Id)
            .ExecuteDeleteAsync(cancellationToken);
        AddAudit(
            actor,
            visit,
            order,
            "visit.evidence_deleted",
            null,
            null,
            new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["visitNumber"] = visit.VisitNumber,
                ["evidenceId"] = edit.EvidenceId,
                ["evidenceType"] = EvidenceTypeCode(evidenceType),
            });

        return Step.Done;
    }

    private static string EvidenceTypeCode(VisitEvidenceType type) => type.ToString().ToLowerInvariant();

    // Ids, statuses, quantities and flags only: never addresses, contact data, names, notes, comments or file data.
    private void AddAudit(
        ProgressActor actor,
        Visit visit,
        WorkOrder order,
        string action,
        Dictionary<string, object?>? before,
        Dictionary<string, object?>? after,
        Dictionary<string, object?> metadata) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            action,
            AuditEntityType,
            actor.UserId,
            visit.Id,
            order.BranchId,
            actor.IpAddress,
            Serialize(before),
            Serialize(after),
            Serialize(metadata)));
}
