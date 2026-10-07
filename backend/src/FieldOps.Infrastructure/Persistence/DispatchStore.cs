using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Dispatch calendar persistence (dispatch-calendar). Reads are no-tracking and always filter by organization first,
/// then by the branch scope of the work order (BR-02); a missing, foreign or out-of-scope visit, branch or
/// technician is one identical "not found". The dispatch transaction lives in <c>DispatchStore.Dispatch</c>.
/// </summary>
internal sealed partial class DispatchStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IDispatchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly VisitStatus[] LockedVisitStatuses =
    [
        VisitStatus.OnTheWay,
        VisitStatus.InProgress,
        VisitStatus.Paused,
        VisitStatus.Completed,
        VisitStatus.NeedsCorrection,
        VisitStatus.Approved,
        VisitStatus.Cancelled,
    ];

    private static readonly WorkOrderStatus[] ClosedOrderStatuses =
        [WorkOrderStatus.Cancelled, WorkOrderStatus.Completed, WorkOrderStatus.ApprovedForBilling];

    public async Task<DispatchOptionsView> GetOptionsAsync(
        Guid organizationId, BranchScope scope, CancellationToken cancellationToken)
    {
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
        }

        var organizationTimezone = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);

        var rows = await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name, branch.Timezone, branch.IsMain })
            .ToListAsync(cancellationToken);

        var options = rows
            .Select(row => new DispatchBranchOption(row.Id, row.Name, BranchTime.ResolveZoneId(row.Timezone, organizationTimezone), row.IsMain))
            .ToList();

        var skills = await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId && skill.IsActive)
            .OrderBy(skill => skill.Name)
            .ThenBy(skill => skill.Id)
            .Select(skill => new DispatchNamed(skill.Id, skill.Name))
            .ToListAsync(cancellationToken);

        return new DispatchOptionsView(options, (options.FirstOrDefault(option => option.IsMain) ?? options.FirstOrDefault())?.Id, skills);
    }

    public Task<bool> SkillIsActiveAsync(Guid organizationId, Guid skillId, CancellationToken cancellationToken) =>
        dbContext.Skills.AsNoTracking().AnyAsync(
            skill => skill.OrganizationId == organizationId && skill.Id == skillId && skill.IsActive, cancellationToken);

    public async Task<DispatchVisitContext?> GetContextAsync(
        Guid organizationId, BranchScope scope, Guid visitId, CancellationToken cancellationToken)
    {
        var info = await ReadVisitInfoAsync(organizationId, scope, visitId, cancellationToken);

        if (info is null)
        {
            return null;
        }

        var contact = await ReadContactAsync(organizationId, info.CustomerId, cancellationToken);

        return new DispatchVisitContext(
            info.VisitId,
            info.Status,
            IsLocked(info.Status, info.OrderStatus),
            await ReadZoneIdAsync(organizationId, info.BranchId, cancellationToken),
            info.ScheduledStart,
            info.ScheduledEnd,
            !string.IsNullOrWhiteSpace(contact?.Email));
    }

    public async Task<VisitDispatchDetail?> GetDetailAsync(
        Guid organizationId,
        BranchScope scope,
        Guid visitId,
        bool canManage,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await ReadVisitInfoAsync(organizationId, scope, visitId, cancellationToken) is null
            ? null
            : await BuildDetailAsync(organizationId, visitId, canManage, now, cancellationToken);

    /// <summary>BR-13: the visit status or the work order status makes the visit read-only.</summary>
    private static bool IsLocked(VisitStatus status, WorkOrderStatus orderStatus) =>
        LockedVisitStatuses.Contains(status) || ClosedOrderStatuses.Contains(orderStatus);

    /// <summary>Work order visits of the organization whose work order is not a draft and whose branch is in the caller scope (BR-02).</summary>
    private IQueryable<Visit> VisibleVisits(Guid organizationId, BranchScope scope)
    {
        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId && order.Status != WorkOrderStatus.Draft);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            orders = orders.Where(order => ids.Contains(order.BranchId));
        }

        return dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && orders.Any(order => order.Id == visit.WorkOrderId));
    }

    private async Task<VisitInfo?> ReadVisitInfoAsync(
        Guid organizationId, BranchScope scope, Guid visitId, CancellationToken cancellationToken) =>
        await (
            from visit in VisibleVisits(organizationId, scope)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where visit.Id == visitId
            select new VisitInfo(
                visit.Id,
                visit.VisitNumber,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                visit.UpdatedAt,
                order.Id,
                order.Status,
                order.BranchId,
                order.CustomerId))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<string> ReadZoneIdAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken)
    {
        var branchTimezone = await dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.Id == branchId)
            .Select(branch => branch.Timezone)
            .SingleOrDefaultAsync(cancellationToken);
        var organizationTimezone = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);

        return BranchTime.ResolveZoneId(branchTimezone, organizationTimezone);
    }

    /// <summary>The primary active contact of the customer (BR-08, BR-17).</summary>
    private Task<ContactRow?> ReadContactAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken) =>
        dbContext.CustomerContacts.AsNoTracking()
            .Where(contact => contact.OrganizationId == organizationId
                && contact.CustomerId == customerId
                && contact.IsPrimary
                && contact.IsActive)
            .Select(contact => new ContactRow(contact.FirstName, contact.Email, contact.Phone))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<VisitDispatchDetail> BuildDetailAsync(
        Guid organizationId, Guid visitId, bool canManage, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var visit = await dbContext.Visits.AsNoTracking()
            .SingleAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == visitId, cancellationToken);
        var order = await dbContext.WorkOrders.AsNoTracking()
            .SingleAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == visit.WorkOrderId, cancellationToken);
        var prefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);
        var zoneId = await ReadZoneIdAsync(organizationId, order.BranchId, cancellationToken);
        var zone = OrganizationTime.FindZone(zoneId);
        var customer = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.CustomerId)
            .Select(candidate => candidate.DisplayName)
            .SingleAsync(cancellationToken);
        var property = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.PropertyId)
            .Select(candidate => new { candidate.AddressLine1, candidate.AddressLine2, candidate.City, candidate.StateRegion, candidate.PostalCode })
            .SingleAsync(cancellationToken);
        var contact = await ReadContactAsync(organizationId, order.CustomerId, cancellationToken);
        var hasEmail = !string.IsNullOrWhiteSpace(contact?.Email);

        var requiredSkills = await RequiredSkillsAsync(organizationId, [order.Id], cancellationToken);
        var materials = await dbContext.WorkOrderPlannedMaterials.AsNoTracking()
            .CountAsync(material => material.OrganizationId == organizationId && material.WorkOrderId == order.Id, cancellationToken);

        var assignments = await dbContext.VisitAssignments.AsNoTracking()
            .Where(assignment => assignment.VisitId == visitId && assignment.UnassignedAt == null)
            .OrderBy(assignment => assignment.AssignedAt)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => new { assignment.TechnicianId, assignment.IsPrimary })
            .ToListAsync(cancellationToken);

        var profiles = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId
                && profile.BranchId == order.BranchId
                && profile.Status == TechnicianStatus.Active)
            .OrderBy(profile => profile.FirstName)
            .ThenBy(profile => profile.LastName)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.FirstName, profile.LastName, profile.ColorHex })
            .ToListAsync(cancellationToken);
        var primarySkills = await PrimarySkillsAsync(organizationId, profiles.Select(profile => profile.Id).ToArray(), cancellationToken);

        var technicians = profiles
            .Select(profile =>
            {
                var name = FullName(profile.FirstName, profile.LastName);

                return new DispatchTechnicianOption(
                    profile.Id, name, RequestCardRules.Initials(name), profile.ColorHex, primarySkills.GetValueOrDefault(profile.Id));
            })
            .ToList();

        var recurring = order.JobType == WorkOrderJobTypes.Recurring && order.RecurrenceCount is not null;

        return new VisitDispatchDetail(
            visit.Id,
            visit.VisitNumber,
            recurring ? new VisitRecurrence(order.RecurrenceCount!.Value) : null,
            WorkOrderCodes.VisitStatusCode(visit.Status),
            visit.UpdatedAt,
            canManage,
            IsLocked(visit.Status, order.Status),
            zoneId,
            new DispatchWorkOrderView(
                order.Id,
                RequestCardRules.DisplayNumber(prefix, order.WorkOrderNumber),
                order.Title,
                WorkOrderCodes.PriorityCode(order.Priority),
                order.EstimatedDurationMinutes,
                requiredSkills.GetValueOrDefault(order.Id) ?? [],
                materials),
            new DispatchCustomerView(
                customer,
                RequestCardRules.Initials(customer),
                string.IsNullOrWhiteSpace(contact?.Phone) ? null : contact.Phone.Trim(),
                QuoteStore.FormatAddress(property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode) ?? string.Empty,
                hasEmail),
            ValuesOf(visit, order, assignments.Select(item => (item.TechnicianId, item.IsPrimary)).ToList(), zone, hasEmail, now),
            technicians);
    }

    /// <summary>The stored schedule, or the BR-08 prefill of an unscheduled visit.</summary>
    private static DispatchValuesView ValuesOf(
        Visit visit,
        WorkOrder order,
        IReadOnlyList<(Guid TechnicianId, bool IsPrimary)> assignments,
        TimeZoneInfo zone,
        bool hasEmail,
        DateTimeOffset now)
    {
        var notify = order.NotifyCustomerWhenScheduled && hasEmail;
        var technicianIds = assignments.Select(item => item.TechnicianId).ToList();
        Guid? primary = assignments.Where(item => item.IsPrimary).Select(item => (Guid?)item.TechnicianId).FirstOrDefault();

        if (visit.ScheduledStart is { } start && visit.ScheduledEnd is { } end)
        {
            var localStart = OrganizationTime.ToZone(start, zone);

            return new DispatchValuesView(
                DateOnly.FromDateTime(localStart.DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                TimeOnly.FromDateTime(localStart.DateTime).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                TimeOnly.FromDateTime(OrganizationTime.ToZone(end, zone).DateTime).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ArrivalCode(start, visit.ArrivalWindowStart, visit.ArrivalWindowEnd),
                technicianIds,
                primary,
                visit.DispatchNote,
                notify,
                order.SendTechnicianDetails);
        }

        string? date = null;
        string? startText = null;
        string? endText = null;

        if (visit.PreferredStart is { } preferred)
        {
            var local = OrganizationTime.ToZone(preferred, zone);
            var localDate = DateOnly.FromDateTime(local.DateTime);

            if (localDate >= OrganizationTime.LocalDate(now, zone))
            {
                date = localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            var startTime = TimeOnly.FromDateTime(local.DateTime);
            var minutes = order.EstimatedDurationMinutes ?? 60;
            var endTime = startTime.AddMinutes(minutes, out var wrapped);

            startText = startTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            endText = wrapped == 0 ? endTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        return new DispatchValuesView(
            date, startText, endText, DispatchCodes.StartPlus2h, technicianIds, primary, visit.DispatchNote, notify, order.SendTechnicianDetails);
    }

    private static string ArrivalCode(DateTimeOffset start, DateTimeOffset? windowStart, DateTimeOffset? windowEnd)
    {
        if (windowStart is not { } from || windowEnd is not { } to)
        {
            return DispatchCodes.StartPlus2h;
        }

        if (from == start && to == start)
        {
            return DispatchCodes.AtStart;
        }

        if (from == start && to == start.AddHours(1))
        {
            return DispatchCodes.StartPlus1h;
        }

        return from == start.AddHours(-1) && to == start.AddHours(1) ? DispatchCodes.Around1h : DispatchCodes.StartPlus2h;
    }

    private async Task<Dictionary<Guid, List<DispatchNamed>>> RequiredSkillsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> workOrderIds, CancellationToken cancellationToken)
    {
        var ids = workOrderIds.ToArray();
        var rows = await (
            from link in dbContext.WorkOrderRequiredSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on link.SkillId equals skill.Id
            where ids.Contains(link.WorkOrderId) && skill.OrganizationId == organizationId
            orderby skill.Name, skill.Id
            select new { link.WorkOrderId, skill.Id, skill.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.WorkOrderId)
            .ToDictionary(group => group.Key, group => group.Select(row => new DispatchNamed(row.Id, row.Name)).ToList());
    }

    /// <summary>The name of the first active primary skill of each technician (BR-05, BR-08).</summary>
    private async Task<Dictionary<Guid, string>> PrimarySkillsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> technicianIds, CancellationToken cancellationToken)
    {
        var ids = technicianIds.ToArray();
        var rows = await (
            from link in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on link.SkillId equals skill.Id
            where ids.Contains(link.TechnicianId) && link.IsPrimary && skill.IsActive && skill.OrganizationId == organizationId
            select new { link.TechnicianId, skill.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.TechnicianId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).First().Name);
    }

    private static string FullName(string? first, string? last) =>
        string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private sealed record VisitInfo(
        Guid VisitId,
        int VisitNumber,
        VisitStatus Status,
        DateTimeOffset? ScheduledStart,
        DateTimeOffset? ScheduledEnd,
        DateTimeOffset UpdatedAt,
        Guid WorkOrderId,
        WorkOrderStatus OrderStatus,
        Guid BranchId,
        Guid CustomerId);

    private sealed record ContactRow(string FirstName, string? Email, string? Phone);
}
