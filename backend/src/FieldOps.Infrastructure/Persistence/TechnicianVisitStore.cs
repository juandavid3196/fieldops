using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The signed-in technician's own visits. Reads never track or write (technician-todays-jobs, mobile-job-details
/// BR-03 to BR-06); the travel transaction lives in <c>TechnicianVisitStore.Travel.cs</c>.
/// </summary>
internal sealed partial class TechnicianVisitStore(FieldOpsDbContext dbContext) : ITechnicianVisitStore
{
    public async Task<TechnicianVisitProfile?> GetProfileAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var row = await (
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking()
                on profile.BranchId equals branch.Id
            join organization in dbContext.Organizations.AsNoTracking()
                on profile.OrganizationId equals organization.Id
            where profile.OrganizationId == organizationId && profile.OrganizationUserId == membershipId
            select new
            {
                profile.Id,
                profile.Status,
                profile.FirstName,
                profile.LastName,
                profile.ColorHex,
                BranchTimezone = branch.Timezone,
                OrganizationTimezone = organization.Timezone,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new TechnicianVisitProfile(
                row.Id,
                row.Status,
                row.FirstName,
                row.LastName,
                row.ColorHex,
                BranchTime.ResolveZoneId(row.BranchTimezone, row.OrganizationTimezone));
    }

    public async Task<IReadOnlyList<TodayVisit>> ListScheduledAsync(
        Guid organizationId,
        Guid technicianId,
        string zoneId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var rows = await Query(organizationId, technicianId, from, to, null).ToListAsync(cancellationToken);

        return await MapAsync(organizationId, zoneId, rows, cancellationToken);
    }

    public async Task<FoundVisit?> FindAsync(
        Guid organizationId, Guid technicianId, string zoneId, Guid visitId, CancellationToken cancellationToken)
    {
        var rows = await Query(organizationId, technicianId, null, null, visitId).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var visit = (await MapAsync(organizationId, zoneId, rows, cancellationToken))[0];
        var extras = await ReadExtrasAsync(
            organizationId, visitId, rows[0].WorkOrderId, BranchTime.FindZone(zoneId), cancellationToken);

        return new FoundVisit(visit, rows[0].DispatchNote, extras);
    }

    public async Task<AssessmentPhotoImage?> FindAssessmentPhotoAsync(
        Guid organizationId, Guid technicianId, Guid visitId, Guid photoId, CancellationToken cancellationToken)
    {
        // The visit must be the caller's (BR-01); its request comes from the work order's quote version.
        var requestId = await (
            from visit in AssignedVisits(organizationId, technicianId)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join version in dbContext.QuoteVersions.AsNoTracking()
                on new { order.OrganizationId, Id = order.QuoteVersionId } equals new { version.OrganizationId, version.Id }
            join quote in dbContext.Quotes.AsNoTracking()
                on new { version.OrganizationId, Id = version.QuoteId } equals new { quote.OrganizationId, quote.Id }
            where visit.Id == visitId
            select (Guid?)quote.RequestId)
            .FirstOrDefaultAsync(cancellationToken);

        if (requestId is not { } request)
        {
            return null;
        }

        // Only the assessment the detail shows: an older assessment of the same request is not served (BR-06).
        var assessmentId = await LatestCompletedAssessmentIdAsync(organizationId, request, cancellationToken);

        if (assessmentId is not { } assessment)
        {
            return null;
        }

        var row = await dbContext.AssessmentAttachments.AsNoTracking()
            .Where(photo => photo.Id == photoId
                && photo.AssessmentId == assessment
                && photo.OrganizationId == organizationId
                && photo.Content != null)
            .Select(photo => new { photo.MimeType, photo.Content })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new AssessmentPhotoImage(row.MimeType, row.Content!);
    }

    /// <summary>The same selection as quote-builder BR-03: the latest completed assessment by completion, then id.</summary>
    private async Task<Guid?> LatestCompletedAssessmentIdAsync(
        Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.RequestId == requestId
                && assessment.Status == AssessmentStatus.Completed
                && assessment.CompletedAt != null)
            .OrderByDescending(assessment => assessment.CompletedAt)
            .ThenByDescending(assessment => assessment.Id)
            .Select(assessment => (Guid?)assessment.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>BR-03 to BR-06 content of one visit. Photo content is never projected; only ids.</summary>
    private async Task<TechnicianVisitExtras> ReadExtrasAsync(
        Guid organizationId, Guid visitId, Guid workOrderId, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var order = await dbContext.WorkOrders.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == workOrderId)
            .Select(candidate => new
            {
                candidate.CustomerId,
                candidate.PropertyId,
                candidate.QuoteVersionId,
                candidate.InternalInstructions,
                candidate.ScopeSnapshot,
            })
            .SingleAsync(cancellationToken);
        var customerType = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.CustomerId)
            .Select(candidate => candidate.Type)
            .SingleAsync(cancellationToken);
        var accessInstructions = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.PropertyId)
            .Select(candidate => candidate.AccessInstructions)
            .SingleAsync(cancellationToken);
        var contact = await dbContext.CustomerContacts.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.CustomerId == order.CustomerId
                && candidate.IsPrimary
                && candidate.IsActive)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.PrefersEmail, candidate.PrefersSms })
            .FirstOrDefaultAsync(cancellationToken);
        var request = await (
            from version in dbContext.QuoteVersions.AsNoTracking()
            join quote in dbContext.Quotes.AsNoTracking()
                on new { version.OrganizationId, Id = version.QuoteId } equals new { quote.OrganizationId, quote.Id }
            join serviceRequest in dbContext.ServiceRequests.AsNoTracking()
                on new { quote.OrganizationId, Id = quote.RequestId } equals new { serviceRequest.OrganizationId, serviceRequest.Id }
            where version.OrganizationId == organizationId && version.Id == order.QuoteVersionId
            select new { serviceRequest.Id, serviceRequest.HasActiveDamage })
            .FirstOrDefaultAsync(cancellationToken);
        var skills = await (
            from link in dbContext.WorkOrderRequiredSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on link.SkillId equals skill.Id
            where link.WorkOrderId == workOrderId && skill.OrganizationId == organizationId
            orderby skill.Name, skill.Id
            select skill.Name)
            .ToListAsync(cancellationToken);
        var officePhone = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Phone)
            .SingleAsync(cancellationToken);
        var materials = await dbContext.WorkOrderPlannedMaterials.AsNoTracking()
            .Where(material => material.OrganizationId == organizationId && material.WorkOrderId == workOrderId)
            .OrderBy(material => material.SortOrder)
            .ThenBy(material => material.Description)
            .ThenBy(material => material.Id)
            .Select(material => new PlannedMaterialView(material.Description, material.Quantity, material.Unit, material.Source))
            .ToListAsync(cancellationToken);
        var tasks = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(item => item.VisitId == visitId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Label)
            .ThenBy(item => item.Id)
            .Select(item => new VisitTaskView(item.Id, item.Label, item.IsRequired, item.IsCompleted))
            .ToListAsync(cancellationToken);
        var entry = await dbContext.VisitTimeEntries.AsNoTracking()
            .Where(candidate => candidate.VisitId == visitId && candidate.EntryType == VisitTimeEntryType.Travel)
            .OrderByDescending(candidate => candidate.StartedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.StartedAt, candidate.EndedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var travel = entry is null
            ? new VisitTravel(null, null, null)
            : new VisitTravel(
                OrganizationTime.ToZone(entry.StartedAt, zone),
                entry.EndedAt is { } arrived ? OrganizationTime.ToZone(arrived, zone) : null,
                entry.EndedAt is { } ended ? (int)Math.Floor((ended - entry.StartedAt).TotalMinutes) : null);

        return new TechnicianVisitExtras(
            customerType == CustomerType.Company ? "company" : "person",
            new VisitAccess(accessInstructions, ContactPreference(contact?.PrefersEmail, contact?.PrefersSms), request?.HasActiveDamage ?? false),
            order.InternalInstructions,
            order.ScopeSnapshot,
            skills,
            officePhone,
            travel,
            materials,
            tasks,
            request is null ? null : await ReadAssessmentAsync(organizationId, request.Id, zone, cancellationToken));
    }

    private async Task<VisitAssessmentView?> ReadAssessmentAsync(
        Guid organizationId, Guid requestId, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (await LatestCompletedAssessmentIdAsync(organizationId, requestId, cancellationToken) is not { } assessmentId)
        {
            return null;
        }

        var row = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId && assessment.Id == assessmentId)
            .Select(assessment => new { assessment.Id, assessment.CompletedAt, assessment.Diagnosis, assessment.RecommendedScope })
            .SingleAsync(cancellationToken);

        var photos = await dbContext.AssessmentAttachments.AsNoTracking()
            .Where(photo => photo.OrganizationId == organizationId && photo.AssessmentId == row.Id)
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Select(photo => new AssessmentPhotoRef(photo.Id))
            .ToListAsync(cancellationToken);

        return new VisitAssessmentView(
            OrganizationTime.ToZone(row.CompletedAt!.Value, zone), row.Diagnosis, row.RecommendedScope, photos);
    }

    /// <summary>BR-03: both channels is email_or_sms, one channel is that channel, none or no primary active contact is null.</summary>
    private static string? ContactPreference(bool? prefersEmail, bool? prefersSms) =>
        (prefersEmail, prefersSms) switch
        {
            (true, true) => "email_or_sms",
            (true, _) => "email",
            (_, true) => "sms",
            _ => null,
        };

    /// <summary>The caller's actively assigned visits that are scheduled and not cancelled (BR-01).</summary>
    private IQueryable<Visit> AssignedVisits(Guid organizationId, Guid technicianId) =>
        dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart != null
                && visit.ScheduledEnd != null
                && dbContext.VisitAssignments.Any(assignment =>
                    assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null));

    /// <summary>
    /// The caller's actively assigned, scheduled and not cancelled visits of the organization (BR-03), in route
    /// order (BR-05), optionally limited to a start range or one visit.
    /// </summary>
    private IQueryable<VisitRow> Query(
        Guid organizationId, Guid technicianId, DateTimeOffset? rangeFrom, DateTimeOffset? rangeTo, Guid? visitId) =>
        from visit in dbContext.Visits.AsNoTracking()
        join order in dbContext.WorkOrders.AsNoTracking()
            on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
        join property in dbContext.Properties.AsNoTracking()
            on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
        join customer in dbContext.Customers.AsNoTracking()
            on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
        join category in dbContext.ServiceCategories.AsNoTracking()
            on new { order.OrganizationId, Id = order.ServiceCategoryId } equals new { category.OrganizationId, category.Id }
        where visit.OrganizationId == organizationId
            && visit.Status != VisitStatus.Unscheduled
            && visit.Status != VisitStatus.Cancelled
            && visit.ScheduledStart != null
            && visit.ScheduledEnd != null
            && dbContext.VisitAssignments.Any(assignment =>
                assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null)
            && (rangeFrom == null || visit.ScheduledStart >= rangeFrom)
            && (rangeTo == null || visit.ScheduledStart < rangeTo)
            && (visitId == null || visit.Id == visitId)
        orderby visit.ScheduledStart, order.WorkOrderNumber, visit.VisitNumber
        select new VisitRow(
            visit.Id,
            visit.VisitNumber,
            order.Id,
            order.WorkOrderNumber,
            order.Title,
            visit.Status,
            visit.ScheduledStart!.Value,
            visit.ScheduledEnd!.Value,
            visit.ArrivalWindowStart,
            visit.ArrivalWindowEnd,
            order.Priority,
            category.Name,
            customer.DisplayName,
            dbContext.CustomerContacts
                .Where(contact => contact.OrganizationId == order.OrganizationId
                    && contact.CustomerId == order.CustomerId
                    && contact.IsPrimary
                    && contact.IsActive)
                .Select(contact => contact.Phone)
                .FirstOrDefault(),
            property.AddressLine1,
            property.AddressLine2,
            property.City,
            property.StateRegion,
            property.PostalCode,
            property.CountryCode,
            property.Latitude,
            property.Longitude,
            dbContext.WorkOrderPlannedMaterials.Count(material =>
                material.OrganizationId == order.OrganizationId && material.WorkOrderId == order.Id),
            visit.DispatchNote,
            dbContext.VisitAssignments
                .Where(assignment => assignment.VisitId == visit.Id
                    && assignment.TechnicianId == technicianId
                    && assignment.UnassignedAt == null)
                .Select(assignment => assignment.IsPrimary)
                .FirstOrDefault());

    private async Task<IReadOnlyList<TodayVisit>> MapAsync(
        Guid organizationId, string zoneId, List<VisitRow> rows, CancellationToken cancellationToken)
    {
        var prefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);
        var zone = BranchTime.FindZone(zoneId);

        return
        [
            .. rows.Select(row => new TodayVisit(
                row.VisitId,
                row.VisitNumber,
                row.WorkOrderId,
                RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                row.Title,
                WorkOrderCodes.VisitStatusCode(row.Status),
                OrganizationTime.ToZone(row.Start, zone),
                OrganizationTime.ToZone(row.End, zone),
                row.ArrivalWindowStart is { } windowStart ? OrganizationTime.ToZone(windowStart, zone) : null,
                row.ArrivalWindowEnd is { } windowEnd ? OrganizationTime.ToZone(windowEnd, zone) : null,
                row.Priority,
                row.ServiceCategory,
                row.CustomerName,
                row.Phone,
                new TodayAddress(row.Line1, row.Line2, row.City, row.StateRegion, row.PostalCode, row.CountryCode),
                row.Latitude,
                row.Longitude,
                row.PlannedMaterialsCount,
                row.IsPrimary)),
        ];
    }

    private sealed record VisitRow(
        Guid VisitId,
        int VisitNumber,
        Guid WorkOrderId,
        long WorkOrderNumber,
        string Title,
        VisitStatus Status,
        DateTimeOffset Start,
        DateTimeOffset End,
        DateTimeOffset? ArrivalWindowStart,
        DateTimeOffset? ArrivalWindowEnd,
        short Priority,
        string ServiceCategory,
        string CustomerName,
        string? Phone,
        string Line1,
        string? Line2,
        string City,
        string? StateRegion,
        string? PostalCode,
        string CountryCode,
        decimal? Latitude,
        decimal? Longitude,
        int PlannedMaterialsCount,
        string? DispatchNote,
        bool IsPrimary);
}
