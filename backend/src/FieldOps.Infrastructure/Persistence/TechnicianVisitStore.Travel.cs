using System.Text.Json;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The Start travel and arrive transaction (mobile-job-details BR-07 to BR-11). Lock order: the caller's technician
/// profile row, then the visit row. Everything after the locks reads current data. The work order row is never
/// locked, so the only inversion with the dispatch transaction (visit, work order, profiles) resolves as a deadlock
/// that is retried once.
/// </summary>
internal sealed partial class TechnicianVisitStore
{
    private const string AuditEntityType = "visit";

    private const string SerializationFailure = "40001";

    private const string DeadlockDetected = "40P01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TravelOutcome> TravelAsync(
        TravelActor actor, Guid visitId, TravelKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteTravelAsync(actor, visitId, kind, now, cancellationToken);
        }
        catch (Exception exception) when (IsLockConflict(exception))
        {
            // The whole transaction was rolled back: it runs once more on a clean tracker, then fails as a 500.
            dbContext.ChangeTracker.Clear();

            return await ExecuteTravelAsync(actor, visitId, kind, now, cancellationToken);
        }
    }

    private static bool IsLockConflict(Exception exception) =>
        (exception as DbUpdateException)?.InnerException is PostgresException { SqlState: SerializationFailure or DeadlockDetected }
        || exception is PostgresException { SqlState: SerializationFailure or DeadlockDetected };

    private async Task<TravelOutcome> ExecuteTravelAsync(
        TravelActor actor, Guid visitId, TravelKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var technicianId = actor.TechnicianId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // (1) The caller's profile row serializes every travel mutation of the technician (BR-10).
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
            return new TravelOutcome(TravelOutcomeKind.NotFound);
        }

        if (!locked[0])
        {
            return new TravelOutcome(TravelOutcomeKind.NotPrimary);
        }

        // (3) State read after the locks, so a concurrent request is already visible.
        var visit = await dbContext.Visits.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == visitId, cancellationToken);
        var entry = await dbContext.VisitTimeEntries
            .Where(candidate => candidate.VisitId == visitId && candidate.EntryType == VisitTimeEntryType.Travel)
            .OrderByDescending(candidate => candidate.StartedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // (4) Guards in the BR-07 / BR-09 order; a refusal returns before any write and the transaction rolls back.
        TravelDecision decision;

        if (kind == TravelKind.Start)
        {
            var anotherActive = visit.Status == VisitStatus.Assigned
                && await dbContext.Visits.AsNoTracking().AnyAsync(
                    candidate => candidate.OrganizationId == organizationId
                        && candidate.Id != visitId
                        && (candidate.Status == VisitStatus.OnTheWay || candidate.Status == VisitStatus.InProgress)
                        && dbContext.VisitAssignments.Any(assignment =>
                            assignment.VisitId == candidate.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null),
                    cancellationToken);

            decision = TravelRules.Start(visit.Status, visit.ScheduledStart, now, actor.ZoneId, anotherActive);
        }
        else
        {
            var state = entry is null
                ? TravelEntryState.None
                : entry.EndedAt is not null
                    ? TravelEntryState.Closed
                    : entry.TechnicianId == technicianId ? TravelEntryState.OpenOwn : TravelEntryState.OpenOther;

            decision = TravelRules.Arrive(visit.Status, state);
        }

        switch (decision)
        {
            case TravelDecision.StatusInvalid:
                return new TravelOutcome(TravelOutcomeKind.StatusInvalid);
            case TravelDecision.NotToday:
                return new TravelOutcome(TravelOutcomeKind.NotToday);
            case TravelDecision.AnotherActive:
                return new TravelOutcome(TravelOutcomeKind.AnotherActive);
        }

        // (5) The write. An unchanged repeat commits nothing but still releases the locks before the read.
        var changed = decision == TravelDecision.Proceed;
        TravelEmail? email = null;

        if (changed)
        {
            var order = await dbContext.WorkOrders.AsNoTracking().SingleAsync(
                candidate => candidate.OrganizationId == organizationId && candidate.Id == visit.WorkOrderId, cancellationToken);

            if (kind == TravelKind.Start)
            {
                // (6) Planned inside the transaction, sent by the handler after the commit.
                email = await PlanEmailAsync(actor, order, visit, cancellationToken);
                StartTravel(actor, visit, order, email is not null, now);
            }
            else
            {
                Arrive(actor, visit, order, entry!, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // (7) The response is read fresh through the shared read path.
        dbContext.ChangeTracker.Clear();

        var found = await FindAsync(organizationId, technicianId, actor.ZoneId, visitId, cancellationToken);

        return found is null
            ? new TravelOutcome(TravelOutcomeKind.NotFound)
            : new TravelOutcome(TravelOutcomeKind.Saved, found, changed, email);
    }

    /// <summary>BR-08: status, history, the open travel entry and the audit row; the work order is untouched.</summary>
    private void StartTravel(TravelActor actor, Visit visit, WorkOrder order, bool notified, DateTimeOffset now)
    {
        visit.SetStatus(VisitStatus.OnTheWay);
        visit.Touch(now);
        dbContext.VisitStatusHistories.Add(
            VisitStatusHistory.Create(visit.Id, VisitStatus.Assigned, VisitStatus.OnTheWay, actor.UserId, now));
        dbContext.VisitTimeEntries.Add(
            VisitTimeEntry.Create(visit.Id, actor.TechnicianId, now, VisitTimeEntryType.Travel));

        // Ids, statuses and flags only: never the address, contact data, names or the email body.
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            "visit.travel_started",
            AuditEntityType,
            actor.UserId,
            visit.Id,
            order.BranchId,
            actor.IpAddress,
            Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(VisitStatus.Assigned) }),
            Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.VisitStatusCode(VisitStatus.OnTheWay) }),
            Serialize(new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["visitNumber"] = visit.VisitNumber,
                ["notified"] = notified ? "email" : "none",
            })));
    }

    /// <summary>BR-09: closes only the open travel entry; the status stays on_the_way and no history row is written.</summary>
    private void Arrive(TravelActor actor, Visit visit, WorkOrder order, VisitTimeEntry entry, DateTimeOffset now)
    {
        entry.Close(now);
        visit.Touch(now);

        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            "visit.arrived",
            AuditEntityType,
            actor.UserId,
            visit.Id,
            order.BranchId,
            actor.IpAddress,
            metadata: Serialize(new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["visitNumber"] = visit.VisitNumber,
                ["travelMinutes"] = (int)Math.Floor((entry.EndedAt!.Value - entry.StartedAt).TotalMinutes),
            })));
    }

    /// <summary>BR-11: the one customer email of this start, or null when the reminder is off or the contact has no email.</summary>
    private async Task<TravelEmail?> PlanEmailAsync(
        TravelActor actor, WorkOrder order, Visit visit, CancellationToken cancellationToken)
    {
        if (!order.SendArrivalReminder)
        {
            return null;
        }

        var organizationId = actor.OrganizationId;
        var contact = await dbContext.CustomerContacts.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.CustomerId == order.CustomerId
                && candidate.IsPrimary
                && candidate.IsActive)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.FirstName, candidate.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(contact?.Email))
        {
            return null;
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Name, candidate.Phone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);
        var address = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.PropertyId)
            .Select(candidate => candidate.AddressLine1)
            .SingleAsync(cancellationToken);
        string? technicianName = null;

        if (order.SendTechnicianDetails)
        {
            var profile = await dbContext.TechnicianProfiles.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == actor.TechnicianId)
                .Select(candidate => new { candidate.FirstName, candidate.LastName })
                .SingleAsync(cancellationToken);

            technicianName = string.Join(' ', new[] { profile.FirstName, profile.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        }

        return new TravelEmail(
            visit.Id,
            contact.Email.Trim(),
            contact.FirstName,
            organization.Name,
            organization.Phone,
            RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber),
            order.Title,
            address,
            actor.ZoneId,
            visit.ArrivalWindowStart,
            visit.ArrivalWindowEnd,
            technicianName);
    }

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
