using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Planner and calendar reads of the schedule-assessment page (FR-03, FR-04) and the shared commitment loading.</summary>
internal sealed partial class ServiceRequestStore
{
    private static readonly VisitStatus[] NonBlockingVisitStatuses =
        [VisitStatus.Unscheduled, VisitStatus.Cancelled, VisitStatus.Completed, VisitStatus.Approved];

    public async Task<ServiceRequestResult<PlannerLoad>> LoadPlannerAsync(
        Guid organizationId,
        BranchScope scope,
        Guid requestId,
        Guid? branchId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var request = await Visible(organizationId, scope)
            .Where(candidate => candidate.Id == requestId)
            .Select(candidate => new { candidate.Status, candidate.BranchId })
            .SingleOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return ServiceRequestResult<PlannerLoad>.NotFound();
        }

        if (!RequestTransitions.TryApply(request.Status, RequestAction.ScheduleAssessment, out _)
            && request.Status != RequestStatus.AssessmentScheduled)
        {
            return ServiceRequestResult<PlannerLoad>.Conflict();
        }

        Guid effectiveBranch;

        if (request.BranchId is { } ownBranch)
        {
            if (branchId is not null)
            {
                return ServiceRequestResult<PlannerLoad>.Invalid("branchId", ServiceRequestMessages.BranchAlreadySet);
            }

            effectiveBranch = ownBranch;
        }
        else
        {
            if (branchId is not { } requested || !await IsSelectableBranchAsync(organizationId, scope, requested, cancellationToken))
            {
                return ServiceRequestResult<PlannerLoad>.Invalid("branchId", ServiceRequestMessages.BranchNotAllowed);
            }

            effectiveBranch = requested;
        }

        var organization = await GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");
        var currentAssessmentId = await ActiveAssessmentIdAsync(organizationId, requestId, cancellationToken);

        var profiles = await (
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where profile.OrganizationId == organizationId
                && profile.BranchId == effectiveBranch
                && profile.Status == TechnicianStatus.Active
            select new { profile.Id, profile.FirstName, profile.LastName, profile.Status, BranchTimezone = branch.Timezone })
            .ToListAsync(cancellationToken);

        var ids = profiles.Select(profile => profile.Id).ToArray();

        var schedules = await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [.. profiles.Select(profile => new ScheduleSubject(profile.Id, profile.Status, profile.BranchTimezone))],
            organization.Timezone,
            string.Empty,
            from.AddDays(-1),
            to.AddDays(1),
            onlyActiveExceptions: true,
            cancellationToken);

        var skills = await (
            from technicianSkill in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on technicianSkill.SkillId equals skill.Id
            where ids.Contains(technicianSkill.TechnicianId) && technicianSkill.IsPrimary && skill.IsActive
            select new { technicianSkill.TechnicianId, skill.Name })
            .ToListAsync(cancellationToken);

        var candidates = new List<PlannerCandidate>(profiles.Count);

        foreach (var profile in profiles)
        {
            candidates.Add(new PlannerCandidate(
                profile.Id,
                FullName(profile.FirstName, profile.LastName),
                skills.Where(skill => skill.TechnicianId == profile.Id)
                    .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(skill => skill.Name)
                    .FirstOrDefault(),
                schedules[profile.Id],
                await LoadCommitmentsAsync(organizationId, profile.Id, from, to, currentAssessmentId, cancellationToken)));
        }

        return ServiceRequestResult<PlannerLoad>.Ok(new PlannerLoad(effectiveBranch, candidates));
    }

    public async Task<ServiceRequestResult<CalendarLoad>> LoadCalendarAsync(
        Guid organizationId,
        BranchScope scope,
        Guid requestId,
        Guid technicianId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var request = await Visible(organizationId, scope)
            .Where(candidate => candidate.Id == requestId)
            .Select(candidate => new { candidate.BranchId })
            .SingleOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return ServiceRequestResult<CalendarLoad>.NotFound();
        }

        // A foreign, inactive or other-branch technician is a 400 with no data. A branchless request has no branch
        // yet, so the technician must belong to a branch of the caller's scope.
        var profiles = dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.Id == technicianId
                && profile.OrganizationId == organizationId
                && profile.Status == TechnicianStatus.Active);

        if (request.BranchId is { } branchId)
        {
            profiles = profiles.Where(profile => profile.BranchId == branchId);
        }
        else if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            profiles = profiles.Where(profile => ids.Contains(profile.BranchId));
        }

        var subject = await (
            from profile in profiles
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where branch.OrganizationId == organizationId && branch.IsActive
            select new ScheduleSubject(profile.Id, profile.Status, branch.Timezone))
            .SingleOrDefaultAsync(cancellationToken);

        if (subject is null)
        {
            return ServiceRequestResult<CalendarLoad>.Invalid("technicianId", ServiceRequestMessages.TechnicianInvalid);
        }

        var organization = await GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");
        var schedule = (await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [subject],
            organization.Timezone,
            string.Empty,
            from.AddDays(-1),
            to.AddDays(1),
            onlyActiveExceptions: true,
            cancellationToken))[subject.Id];

        return ServiceRequestResult<CalendarLoad>.Ok(new CalendarLoad(
            schedule,
            await LoadCommitmentsAsync(organizationId, technicianId, from, to, null, cancellationToken),
            await ActiveAssessmentIdAsync(organizationId, requestId, cancellationToken)));
    }

    /// <summary>
    /// BR-05 commitments overlapping the range: scheduled assessments of the technician (except the one being
    /// rescheduled) and visits with an active assignment that are not unscheduled, cancelled, completed or approved.
    /// Assessment labels are the request title; visits are labelled "Visit". No customer data is read.
    /// </summary>
    internal async Task<IReadOnlyList<SlotCommitment>> LoadCommitmentsAsync(
        Guid organizationId,
        Guid technicianId,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        Guid? excludeAssessmentId,
        CancellationToken cancellationToken)
    {
        var assessments = await (
            from assessment in dbContext.Assessments.AsNoTracking()
            join request in dbContext.ServiceRequests.AsNoTracking()
                on new { assessment.OrganizationId, Id = assessment.RequestId } equals new { request.OrganizationId, request.Id }
            where assessment.OrganizationId == organizationId
                && assessment.TechnicianId == technicianId
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart < rangeEnd
                && assessment.ScheduledEnd > rangeStart
                && (excludeAssessmentId == null || assessment.Id != excludeAssessmentId)
            select new
            {
                assessment.Id,
                assessment.ScheduledStart,
                assessment.ScheduledEnd,
                request.CatalogItemId,
                request.CategoryId,
                request.Description,
            })
            .ToListAsync(cancellationToken);

        var itemIds = assessments.Where(row => row.CatalogItemId != null).Select(row => row.CatalogItemId!.Value).Distinct().ToArray();
        var categoryIds = assessments.Where(row => row.CategoryId != null).Select(row => row.CategoryId!.Value).Distinct().ToArray();

        var itemNames = itemIds.Length == 0
            ? []
            : await dbContext.CatalogItems.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && itemIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

        var categoryNames = categoryIds.Length == 0
            ? []
            : await dbContext.ServiceCategories.AsNoTracking()
                .Where(category => category.OrganizationId == organizationId && categoryIds.Contains(category.Id))
                .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);

        var visits = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on assignment.VisitId equals visit.Id
            where assignment.TechnicianId == technicianId
                && assignment.UnassignedAt == null
                && visit.OrganizationId == organizationId
                && !NonBlockingVisitStatuses.Contains(visit.Status)
                && visit.ScheduledStart != null
                && visit.ScheduledStart < rangeEnd
                && ((visit.ScheduledEnd == null && visit.ScheduledStart >= rangeStart) || visit.ScheduledEnd > rangeStart)
            select new { visit.Id, visit.ScheduledStart, visit.ScheduledEnd })
            .ToListAsync(cancellationToken);

        var commitments = new List<SlotCommitment>();

        foreach (var row in assessments)
        {
            commitments.Add(new SlotCommitment(
                row.ScheduledStart,
                row.ScheduledEnd,
                CommitmentKinds.Assessment,
                RequestCardRules.Title(
                    row.CatalogItemId is { } itemId && itemNames.TryGetValue(itemId, out var itemName) ? itemName : null,
                    row.CategoryId is { } categoryId && categoryNames.TryGetValue(categoryId, out var categoryName) ? categoryName : null,
                    row.Description),
                row.Id));
        }

        foreach (var row in visits.DistinctBy(visit => visit.Id))
        {
            var start = row.ScheduledStart!.Value;

            commitments.Add(new SlotCommitment(start, row.ScheduledEnd ?? start, CommitmentKinds.Visit, "Visit", null));
        }

        return commitments;
    }

    private async Task<TechnicianSchedule> LoadTechnicianScheduleAsync(
        Guid organizationId,
        Guid technicianId,
        string organizationTimezone,
        DateTimeOffset low,
        DateTimeOffset high,
        CancellationToken cancellationToken)
    {
        var subject = await (
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where profile.Id == technicianId && profile.OrganizationId == organizationId
            select new ScheduleSubject(profile.Id, profile.Status, branch.Timezone))
            .SingleAsync(cancellationToken);

        return (await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [subject],
            organizationTimezone,
            string.Empty,
            low,
            high,
            onlyActiveExceptions: true,
            cancellationToken))[technicianId];
    }

    private Task<Guid?> ActiveAssessmentIdAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.RequestId == requestId
                && assessment.Status == AssessmentStatus.Scheduled)
            .OrderByDescending(assessment => assessment.CreatedAt)
            .Select(assessment => (Guid?)assessment.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> IsSelectableBranchAsync(
        Guid organizationId, BranchScope scope, Guid branchId, CancellationToken cancellationToken)
    {
        var query = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.Id == branchId && branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(branch => ids.Contains(branch.Id));
        }

        return query.AnyAsync(cancellationToken);
    }
}
