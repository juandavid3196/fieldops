using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class WorkOrderStore
{
    private const string SerializationFailure = "40001";

    private const string DeadlockDetected = "40P01";

    private const string UniqueViolation = "23505";

    public Task<WorkOrderOutcome<WorkOrderDraftSaved>> SaveDraftAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset? updatedAt, WorkOrderInput input, CancellationToken cancellationToken) =>
        MutateAsync<WorkOrderDraftSaved>(
            actor,
            quoteId,
            async held =>
            {
                var quote = held.Quote;

                if (GuardQuote(quote) is { } guard)
                {
                    return Fail<WorkOrderDraftSaved>(Conflict<WorkOrderDraftSaved>(guard));
                }

                var versionId = quote.ApprovedVersionId!.Value;
                var existing = await LoadOrderAsync(actor.OrganizationId, quote.Id, cancellationToken);

                if (existing is not null && existing.QuoteVersionId != versionId)
                {
                    return Fail<WorkOrderDraftSaved>(Conflict<WorkOrderDraftSaved>(WorkOrderMessages.QuoteNotApprovedCode));
                }

                if (existing is not null && existing.Status != WorkOrderStatus.Draft)
                {
                    return Fail<WorkOrderDraftSaved>(Conflict<WorkOrderDraftSaved>(WorkOrderMessages.WorkOrderCreatedCode));
                }

                if (IsStale(existing, updatedAt))
                {
                    return Fail<WorkOrderDraftSaved>(Conflict<WorkOrderDraftSaved>(WorkOrderMessages.WorkOrderChangedCode));
                }

                var checkedInput = await CheckReferencesAsync(actor, versionId, input, cancellationToken);

                if (checkedInput.Errors.Count > 0)
                {
                    return Fail<WorkOrderDraftSaved>(new WorkOrderOutcome<WorkOrderDraftSaved>.Invalid(checkedInput.Errors));
                }

                var now = timeProvider.GetUtcNow();
                var order = await UpsertOrderAsync(actor, quote, versionId, existing, updatedAt, input, checkedInput, now, cancellationToken);
                await ReplaceChildrenAsync(actor.OrganizationId, order, existing is not null, input, cancellationToken);

                return Step<WorkOrderDraftSaved>.Done(async () => new WorkOrderDraftSaved(
                    await EditorAsync(actor, quote, cancellationToken),
                    existing is null));
            },
            cancellationToken);

    public Task<WorkOrderOutcome<WorkOrderCreated>> CreateAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset? updatedAt, WorkOrderInput input, CancellationToken cancellationToken) =>
        MutateAsync<WorkOrderCreated>(
            actor,
            quoteId,
            async held =>
            {
                var quote = held.Quote;

                if (quote.Status != QuoteStatus.Approved || quote.ApprovedVersionId is not { } versionId)
                {
                    return Fail<WorkOrderCreated>(Conflict<WorkOrderCreated>(WorkOrderMessages.QuoteNotApprovedCode));
                }

                var existing = await LoadOrderAsync(actor.OrganizationId, quote.Id, cancellationToken);

                if (existing is not null && existing.QuoteVersionId != versionId)
                {
                    return Fail<WorkOrderCreated>(Conflict<WorkOrderCreated>(WorkOrderMessages.QuoteNotApprovedCode));
                }

                // BR-20: an order that already left the draft status answers first and ignores the body.
                if (existing is not null && existing.Status != WorkOrderStatus.Draft)
                {
                    var prefix = await ReadWorkOrderPrefixAsync(actor.OrganizationId, cancellationToken);
                    var repeated = new WorkOrderCreated(
                        new WorkOrderRef(existing.Id, RequestCardRules.DisplayNumber(prefix, existing.WorkOrderNumber)), false);

                    return Step<WorkOrderCreated>.Done(() => Task.FromResult(repeated));
                }

                if (GuardQuote(quote) is { } guard)
                {
                    return Fail<WorkOrderCreated>(Conflict<WorkOrderCreated>(guard));
                }

                if (held.Request.Status != RequestStatus.Quoted)
                {
                    return Fail<WorkOrderCreated>(Conflict<WorkOrderCreated>(ServiceRequestMessages.RequestChangedCode));
                }

                if (IsStale(existing, updatedAt))
                {
                    return Fail<WorkOrderCreated>(Conflict<WorkOrderCreated>(WorkOrderMessages.WorkOrderChangedCode));
                }

                var checkedInput = await CheckReferencesAsync(actor, versionId, input, cancellationToken);

                if (checkedInput.Errors.Count > 0)
                {
                    return Fail<WorkOrderCreated>(new WorkOrderOutcome<WorkOrderCreated>.Invalid(checkedInput.Errors));
                }

                var now = timeProvider.GetUtcNow();
                var order = await UpsertOrderAsync(actor, quote, versionId, existing, updatedAt, input, checkedInput, now, cancellationToken);
                var (tasks, materials) = await ReplaceChildrenAsync(actor.OrganizationId, order, existing is not null, input, cancellationToken);

                order.MarkReady(now);

                MoveRequestToConverted(actor, held.Request);

                // Dispatch-calendar BR-22: visit #1 carries the work order preferred window.
                var visit = Visit.Create(
                    actor.OrganizationId, order.Id, 1, preferredStart: order.PreferredStart, preferredEnd: order.PreferredEnd);
                dbContext.Visits.Add(visit);
                dbContext.VisitStatusHistories.Add(VisitStatusHistory.Create(visit.Id, null, VisitStatus.Unscheduled));

                // BR-18: each task is copied in order, keeping the task row as the template item.
                for (var index = 0; index < tasks.Count; index++)
                {
                    dbContext.VisitChecklistItems.Add(VisitChecklistItem.Create(visit.Id, tasks[index].Label, tasks[index].Id, index));
                }

                // BR-19: ids, counts and the status only; never contact data, instructions or material descriptions.
                dbContext.AuditLogs.Add(AuditLog.Create(
                    actor.OrganizationId,
                    "work_order.created",
                    AuditEntityType,
                    actor.UserId,
                    order.Id,
                    order.BranchId,
                    actor.IpAddress,
                    null,
                    Serialize(new Dictionary<string, object?> { ["status"] = "ready_to_schedule" }),
                    Serialize(new
                    {
                        workOrderNumber = order.WorkOrderNumber,
                        quoteId = quote.Id,
                        quoteVersionId = versionId,
                        visitId = visit.Id,
                        taskCount = tasks.Count,
                        materialCount = materials.Count,
                    })));

                var created = new WorkOrderCreated(
                    new WorkOrderRef(
                        order.Id,
                        RequestCardRules.DisplayNumber(
                            await ReadWorkOrderPrefixAsync(actor.OrganizationId, cancellationToken), order.WorkOrderNumber)),
                    true);

                return Step<WorkOrderCreated>.Done(() => Task.FromResult(created));
            },
            cancellationToken);

    private static bool IsStale(WorkOrder? existing, DateTimeOffset? updatedAt) =>
        existing is null ? updatedAt is not null : updatedAt != existing.UpdatedAt;

    private static WorkOrderOutcome<T> Conflict<T>(string code) => new WorkOrderOutcome<T>.Conflict(code);

    private static Step<T> Fail<T>(WorkOrderOutcome<T> failure) => Step<T>.Fail(failure);

    private Task<WorkOrder?> LoadOrderAsync(Guid organizationId, Guid quoteId, CancellationToken cancellationToken) =>
        OrdersOfQuote(organizationId, quoteId).OrderBy(order => order.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    private async Task<WorkOrderEditor> EditorAsync(QuoteActor actor, Quote quote, CancellationToken cancellationToken) =>
        await BuildEditorAsync(actor.OrganizationId, actor.Scope, quote, cancellationToken)
            is WorkOrderOutcome<WorkOrderEditor>.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("The work order editor is no longer available.");

    /// <summary>Inserts a draft with the next number (organization row lock) or replaces the fields of the existing draft.</summary>
    private async Task<WorkOrder> UpsertOrderAsync(
        QuoteActor actor,
        Quote quote,
        Guid versionId,
        WorkOrder? existing,
        DateTimeOffset? updatedAt,
        WorkOrderInput input,
        CheckedInput checkedInput,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fields = new WorkOrderFields(
            input.Title,
            input.JobType,
            input.ServiceCategoryId,
            input.Priority,
            input.EstimatedDurationMinutes,
            input.RecurrenceFrequency,
            input.RecurrenceCount,
            input.Instructions,
            checkedInput.PreferredStart,
            checkedInput.PreferredEnd,
            input.NotifyCustomerWhenScheduled,
            input.SendTechnicianDetails,
            input.SendArrivalReminder);

        if (existing is not null)
        {
            dbContext.Entry(existing).Property(order => order.UpdatedAt).OriginalValue = updatedAt!.Value;
            existing.ReplaceDraft(input.BranchId, fields, now);

            return existing;
        }

        var scope = await dbContext.QuoteVersions.AsNoTracking()
            .Where(version => version.OrganizationId == actor.OrganizationId && version.Id == versionId)
            .Select(version => version.Scope)
            .SingleAsync(cancellationToken);
        var number = await AllocateNumberAsync(actor.OrganizationId, cancellationToken);
        var order = WorkOrder.Create(
            actor.OrganizationId,
            input.BranchId,
            number,
            versionId,
            quote.CustomerId!.Value,
            quote.PropertyId!.Value,
            scope,
            actor.UserId,
            fields);

        dbContext.WorkOrders.Add(order);

        return order;
    }

    // Only after every validation passed, so a 400 or 409 never consumes a number (BR-16).
    private async Task<long> AllocateNumberAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var locked = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT next_work_order_number AS "Value" FROM organizations
                WHERE id = {organizationId} AND is_active = true
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            throw new InvalidOperationException("The organization is no longer available.");
        }

        var number = locked[0];

        await dbContext.Organizations
            .Where(candidate => candidate.Id == organizationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.NextWorkOrderNumber, number + 1),
                cancellationToken);

        return number;
    }

    // Skills, tasks and planned materials are replaced as a whole: delete, then insert (BR-16).
    private async Task<(List<WorkOrderChecklistTemplate> Tasks, List<WorkOrderPlannedMaterial> Materials)> ReplaceChildrenAsync(
        Guid organizationId, WorkOrder order, bool replace, WorkOrderInput input, CancellationToken cancellationToken)
    {
        if (replace)
        {
            await dbContext.WorkOrderRequiredSkills.Where(skill => skill.WorkOrderId == order.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.WorkOrderChecklistTemplates.Where(task => task.WorkOrderId == order.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.WorkOrderPlannedMaterials
                .Where(material => material.OrganizationId == organizationId && material.WorkOrderId == order.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        dbContext.WorkOrderRequiredSkills.AddRange(input.SkillIds.Select(skillId => WorkOrderRequiredSkill.Create(order.Id, skillId)));

        var tasks = input.Tasks
            .Select((label, index) => WorkOrderChecklistTemplate.Create(order.Id, label, index))
            .ToList();
        var materials = input.Materials
            .Select((material, index) => WorkOrderPlannedMaterial.Create(
                organizationId,
                order.Id,
                material.QuoteLineId,
                material.CatalogItemId,
                material.Description,
                material.Quantity,
                material.Unit,
                material.Source,
                index))
            .ToList();

        dbContext.WorkOrderChecklistTemplates.AddRange(tasks);
        dbContext.WorkOrderPlannedMaterials.AddRange(materials);

        return (tasks, materials);
    }

    private void MoveRequestToConverted(QuoteActor actor, ServiceRequest request)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.ConvertToWorkOrder, out var to))
        {
            throw new InvalidOperationException("The request cannot move to converted.");
        }

        var from = request.Status;
        request.ChangeStatus(to);
        dbContext.RequestStatusHistories.Add(
            RequestStatusHistory.Create(actor.OrganizationId, request.Id, from, to, actor.UserId));
    }

    /// <summary>
    /// The database-backed checks of BR-03, BR-09 and BR-14: branch, category, skills, catalog items, quote lines
    /// and the preferred window. Failures name only the key; no foreign data is ever returned.
    /// </summary>
    private async Task<CheckedInput> CheckReferencesAsync(
        QuoteActor actor, Guid versionId, WorkOrderInput input, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var branch = await dbContext.Branches.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == input.BranchId && candidate.IsActive)
            .Select(candidate => new { candidate.Timezone })
            .SingleOrDefaultAsync(cancellationToken);

        if (branch is null || !actor.Scope.Contains(input.BranchId))
        {
            errors["branchId"] = [WorkOrderMessages.BranchMessage];
        }

        if (!await dbContext.ServiceCategories.AsNoTracking().AnyAsync(
                candidate => candidate.OrganizationId == organizationId && candidate.Id == input.ServiceCategoryId && candidate.IsActive,
                cancellationToken))
        {
            errors["serviceCategoryId"] = [WorkOrderMessages.CategoryMessage];
        }

        if (input.SkillIds.Count > 0)
        {
            var skillIds = input.SkillIds.ToArray();
            var found = await dbContext.Skills.AsNoTracking().CountAsync(
                candidate => candidate.OrganizationId == organizationId && skillIds.Contains(candidate.Id) && candidate.IsActive,
                cancellationToken);

            if (found != skillIds.Length)
            {
                errors["skillIds"] = [WorkOrderMessages.SkillIdsInvalidMessage];
            }
        }

        var catalogIds = input.Materials.Where(material => material.CatalogItemId is not null)
            .Select(material => material.CatalogItemId!.Value)
            .Distinct()
            .ToArray();
        var validCatalog = catalogIds.Length == 0
            ? []
            : await dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId
                    && catalogIds.Contains(candidate.Id)
                    && candidate.Type == CatalogItemType.Product
                    && candidate.IsActive)
                .Select(candidate => candidate.Id)
                .ToListAsync(cancellationToken);
        var validLines = input.Materials.Any(material => material.QuoteLineId is not null)
            ? (await ApprovedLinesAsync(organizationId, versionId, cancellationToken))
                .Where(line => line.LineType == CatalogItemType.Product)
                .Select(line => line.Id)
                .ToHashSet()
            : [];
        var usedLines = new HashSet<Guid>();

        for (var index = 0; index < input.Materials.Count; index++)
        {
            var material = input.Materials[index];

            if (material.CatalogItemId is { } catalogItemId && !validCatalog.Contains(catalogItemId))
            {
                errors[$"materials[{index}].catalogItemId"] = [WorkOrderMessages.CatalogItemMessage];
            }

            if (material.QuoteLineId is { } lineId && (!validLines.Contains(lineId) || !usedLines.Add(lineId)))
            {
                errors[$"materials[{index}].quoteLineId"] = [WorkOrderMessages.QuoteLineMessage];
            }
        }

        DateTimeOffset? start = null;
        DateTimeOffset? end = null;

        if (input.PreferredDate is { } date && branch is not null)
        {
            var organizationTimezone = await dbContext.Organizations.AsNoTracking()
                .Where(candidate => candidate.Id == organizationId)
                .Select(candidate => candidate.Timezone)
                .SingleAsync(cancellationToken);
            var zone = OrganizationTime.FindZone(branch.Timezone ?? organizationTimezone);

            if (WorkOrderWindow.TryResolve(date, input.ArrivalWindow, zone, timeProvider.GetUtcNow(), out var from, out var to))
            {
                start = from;
                end = to;
            }
            else
            {
                errors["preferredDate"] = [WorkOrderMessages.PreferredDateMessage];
            }
        }

        return new CheckedInput(errors, start, end);
    }

    private async Task<WorkOrderOutcome<T>> MutateAsync<T>(
        QuoteActor actor,
        Guid quoteId,
        Func<Held, Task<Step<T>>> apply,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(actor, quoteId, apply, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();

            return Conflict<T>(WorkOrderMessages.WorkOrderChangedCode);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            dbContext.ChangeTracker.Clear();

            // The unique quote_version_id index is the backstop of BR-20; a lost race reads as "changed".
            if (IsConflictState(postgres.SqlState) || postgres.SqlState == UniqueViolation)
            {
                return Conflict<T>(WorkOrderMessages.WorkOrderChangedCode);
            }

            // The database detail can quote the failing row: only state and constraint travel.
            throw new InvalidOperationException(
                $"The work order change could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }
        catch (PostgresException postgres) when (IsConflictState(postgres.SqlState))
        {
            dbContext.ChangeTracker.Clear();

            return Conflict<T>(WorkOrderMessages.WorkOrderChangedCode);
        }
    }

    private static bool IsConflictState(string sqlState) => sqlState is SerializationFailure or DeadlockDetected;

    // Lock order: request row, quote row, then (only when numbering) the organization row. Tenant and branch scope
    // are part of the first statement, so a missing, foreign or out-of-scope quote is one identical "not found".
    private async Task<WorkOrderOutcome<T>> ExecuteAsync<T>(
        QuoteActor actor,
        Guid quoteId,
        Func<Held, Task<Step<T>>> apply,
        CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var requestId = await VisibleQuotes(organizationId, actor.Scope)
            .Where(candidate => candidate.Id == quoteId)
            .Select(candidate => (Guid?)candidate.RequestId)
            .SingleOrDefaultAsync(cancellationToken);

        if (requestId is null)
        {
            return new WorkOrderOutcome<T>.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await LockRequestAsync(actor, requestId.Value, cancellationToken) is false)
        {
            return new WorkOrderOutcome<T>.NotFound();
        }

        var lockedQuote = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM quotes
                WHERE id = {quoteId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (lockedQuote.Count != 1)
        {
            return new WorkOrderOutcome<T>.NotFound();
        }

        // The quote is only read: no endpoint of this feature writes quotes (BR-17).
        var quote = await dbContext.Quotes.AsNoTracking().SingleAsync(
            candidate => candidate.Id == quoteId && candidate.OrganizationId == organizationId, cancellationToken);
        var request = await dbContext.ServiceRequests.SingleAsync(
            candidate => candidate.Id == requestId && candidate.OrganizationId == organizationId, cancellationToken);

        var step = await apply(new Held(quote, request));

        if (step.Failure is not null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return step.Failure;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WorkOrderOutcome<T>.Succeeded(await step.Build!());
    }

    /// <summary>Locks the request row inside the caller scope; false when it is missing, foreign or out of scope.</summary>
    private async Task<bool> LockRequestAsync(QuoteActor actor, Guid requestId, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();
        var locked = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT status::text AS "Value" FROM service_requests
                WHERE id = {requestId} AND organization_id = {organizationId}
                  AND ({scopeAll} OR branch_id IS NULL OR branch_id = ANY({scopeIds}))
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return locked.Count == 1;
    }

    private sealed record Held(Quote Quote, ServiceRequest Request);

    private sealed record CheckedInput(
        Dictionary<string, string[]> Errors, DateTimeOffset? PreferredStart, DateTimeOffset? PreferredEnd);

    private readonly record struct Step<T>(WorkOrderOutcome<T>? Failure, Func<Task<T>>? Build)
    {
        public static Step<T> Done(Func<Task<T>> build) => new(null, build);

        public static Step<T> Fail(WorkOrderOutcome<T> failure) => new(failure, null);
    }
}
