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

/// <summary>Calendar, unscheduled panel and evaluation reads (dispatch-calendar FR-03 to FR-05) and the shared loaders.</summary>
internal sealed partial class DispatchStore
{
    public async Task<CalendarSource?> LoadCalendarAsync(
        Guid organizationId,
        BranchScope scope,
        Guid branchId,
        string view,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var branch = await VisibleBranch(organizationId, scope)
            .Where(candidate => candidate.Id == branchId)
            .Select(candidate => new { candidate.Id, candidate.Timezone })
            .SingleOrDefaultAsync(cancellationToken);

        if (branch is null)
        {
            return null;
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Timezone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);
        var zoneId = BranchTime.ResolveZoneId(branch.Timezone, organization.Timezone);
        var (rangeFrom, rangeTo, _) = DispatchCalendarBuilder.Range(view, date, OrganizationTime.FindZone(zoneId));

        // Visits of the branch whose schedule intersects the range (BR-05), not unscheduled or cancelled.
        var visits = await (
            from visit in dbContext.Visits.AsNoTracking()
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join property in dbContext.Properties.AsNoTracking()
                on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
            where visit.OrganizationId == organizationId
                && order.BranchId == branchId
                && order.Status != WorkOrderStatus.Draft
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart != null
                && visit.ScheduledEnd != null
                && visit.ScheduledStart < rangeTo
                && visit.ScheduledEnd > rangeFrom
            select new
            {
                VisitId = visit.Id,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                WorkOrderId = order.Id,
                order.WorkOrderNumber,
                order.Title,
                property.AddressLine1,
            })
            .ToListAsync(cancellationToken);

        var visitIds = visits.Select(row => row.VisitId).ToArray();
        var assignments = await dbContext.VisitAssignments.AsNoTracking()
            .Where(assignment => visitIds.Contains(assignment.VisitId) && assignment.UnassignedAt == null)
            .OrderBy(assignment => assignment.AssignedAt)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => new { assignment.VisitId, assignment.TechnicianId, assignment.IsPrimary })
            .ToListAsync(cancellationToken);
        var requiredSkills = await RequiredSkillsAsync(
            organizationId, [.. visits.Select(row => row.WorkOrderId).Distinct()], cancellationToken);

        var profiles = await ReadProfilesAsync(
            organizationId,
            dbContext.TechnicianProfiles.AsNoTracking()
                .Where(profile => profile.OrganizationId == organizationId
                    && profile.BranchId == branchId
                    && profile.Status == TechnicianStatus.Active),
            cancellationToken);
        var branchIds = profiles.Select(profile => profile.Id).ToHashSet();
        var holderIds = assignments.Select(item => item.TechnicianId).Where(id => !branchIds.Contains(id)).Distinct().ToArray();

        if (holderIds.Length > 0)
        {
            profiles.AddRange(await ReadProfilesAsync(
                organizationId,
                dbContext.TechnicianProfiles.AsNoTracking().Where(profile => profile.OrganizationId == organizationId && holderIds.Contains(profile.Id)),
                cancellationToken));
        }

        var technicians = await BuildTechniciansAsync(organizationId, profiles, organization.Timezone, organization.WorkOrderPrefix, rangeFrom, rangeTo, cancellationToken);
        var primarySkills = await PrimarySkillsAsync(organizationId, [.. profiles.Select(profile => profile.Id)], cancellationToken);

        var lanes = profiles
            .Select(profile => new CalendarLane(
                profile.Id,
                FullName(profile.FirstName, profile.LastName),
                profile.ColorHex,
                primarySkills.GetValueOrDefault(profile.Id),
                branchIds.Contains(profile.Id) ? null : profile.BranchId == branchId ? "inactive" : "other_branch",
                technicians[profile.Id]))
            .ToList();

        var sources = visits
            .Select(row =>
            {
                var rows = assignments.Where(item => item.VisitId == row.VisitId).ToList();
                var ordered = rows.OrderByDescending(item => item.IsPrimary).Select(item => item.TechnicianId).ToList();

                return new CalendarVisitSource(
                    row.VisitId,
                    row.WorkOrderId,
                    RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, row.WorkOrderNumber),
                    row.Title,
                    row.AddressLine1,
                    row.Status,
                    row.ScheduledStart!.Value,
                    row.ScheduledEnd!.Value,
                    ordered,
                    rows.Where(item => item.IsPrimary).Select(item => (Guid?)item.TechnicianId).FirstOrDefault(),
                    requiredSkills.GetValueOrDefault(row.WorkOrderId)?.Select(skill => new DispatchSkill(skill.Id, skill.Name)).ToList() ?? []);
            })
            .ToList();

        return new CalendarSource(zoneId, rangeFrom, rangeTo, lanes, sources);
    }

    public async Task<DispatchOutcome<UnscheduledPageView>> ListUnscheduledAsync(
        Guid organizationId,
        BranchScope scope,
        UnscheduledQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var branch = await VisibleBranch(organizationId, scope)
            .Where(candidate => candidate.Id == query.BranchId)
            .Select(candidate => new { candidate.Timezone })
            .SingleOrDefaultAsync(cancellationToken);

        if (branch is null)
        {
            return new DispatchOutcome<UnscheduledPageView>.NotFound();
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Timezone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(BranchTime.ResolveZoneId(branch.Timezone, organization.Timezone));
        var (dayStart, dayEnd) = BranchTime.Day(now, zone);
        var prefix = organization.WorkOrderPrefix;

        var rows =
            from visit in dbContext.Visits.AsNoTracking()
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join customer in dbContext.Customers.AsNoTracking()
                on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
            join property in dbContext.Properties.AsNoTracking()
                on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
            where visit.OrganizationId == organizationId
                && visit.Status == VisitStatus.Unscheduled
                && order.BranchId == query.BranchId
                && (order.Status == WorkOrderStatus.ReadyToSchedule
                    || order.Status == WorkOrderStatus.Scheduled
                    || order.Status == WorkOrderStatus.InProgress)
            select new
            {
                VisitId = visit.Id,
                visit.VisitNumber,
                visit.PreferredStart,
                visit.PreferredEnd,
                WorkOrderId = order.Id,
                order.WorkOrderNumber,
                order.Title,
                order.Priority,
                order.EstimatedDurationMinutes,
                order.JobType,
                order.RecurrenceCount,
                CustomerName = customer.DisplayName,
                property.AddressLine1,
                property.AddressLine2,
                property.City,
                property.StateRegion,
                property.PostalCode,
            };

        if (query.Filter == "today")
        {
            rows = rows.Where(row => row.PreferredStart != null && row.PreferredEnd != null
                && row.PreferredStart < dayEnd && row.PreferredEnd > dayStart);
        }
        else if (query.Filter == "overdue")
        {
            rows = rows.Where(row => row.PreferredEnd != null && row.PreferredEnd < now);
        }

        if (query.SkillId is { } skillId)
        {
            rows = rows.Where(row => dbContext.WorkOrderRequiredSkills.Any(link => link.WorkOrderId == row.WorkOrderId && link.SkillId == skillId));
        }

        if (query.Search is { Length: > 0 } search)
        {
            var pattern = RequestSearch.ContainsPattern(search);

            rows = rows.Where(row =>
                EF.Functions.ILike(prefix + "-" + row.WorkOrderNumber.ToString(), pattern)
                || EF.Functions.ILike(row.Title, pattern)
                || EF.Functions.ILike(row.CustomerName, pattern)
                || EF.Functions.ILike(row.AddressLine1, pattern)
                || (row.AddressLine2 != null && EF.Functions.ILike(row.AddressLine2, pattern))
                || EF.Functions.ILike(row.City, pattern)
                || (row.StateRegion != null && EF.Functions.ILike(row.StateRegion, pattern))
                || (row.PostalCode != null && EF.Functions.ILike(row.PostalCode, pattern)));
        }

        var total = await rows.CountAsync(cancellationToken);

        // Overdue first, then the preferred start (nulls last), priority (1 = urgent) and work order number (BR-07).
        var page = await rows
            .OrderByDescending(row => row.PreferredEnd != null && row.PreferredEnd < now)
            .ThenBy(row => row.PreferredStart == null)
            .ThenBy(row => row.PreferredStart)
            .ThenBy(row => row.Priority)
            .ThenBy(row => row.WorkOrderNumber)
            .ThenBy(row => row.VisitId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var skills = await RequiredSkillsAsync(organizationId, [.. page.Select(row => row.WorkOrderId).Distinct()], cancellationToken);

        return new DispatchOutcome<UnscheduledPageView>.Succeeded(new UnscheduledPageView(
            [.. page.Select(row => new UnscheduledItemView(
                row.VisitId,
                row.VisitNumber,
                row.JobType == WorkOrderJobTypes.Recurring ? row.RecurrenceCount : null,
                row.WorkOrderId,
                RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                row.Title,
                WorkOrderCodes.PriorityCode(row.Priority),
                row.EstimatedDurationMinutes,
                row.CustomerName,
                QuoteStore.FormatAddress(row.AddressLine1, row.AddressLine2, row.City, row.StateRegion, row.PostalCode) ?? string.Empty,
                row.PreferredStart is { } start ? OrganizationTime.ToZone(start, zone) : null,
                row.PreferredEnd is { } end ? OrganizationTime.ToZone(end, zone) : null,
                row.PreferredEnd is { } due && due < now,
                skills.GetValueOrDefault(row.WorkOrderId) ?? []))],
            total));
    }

    public async Task<DispatchOutcome<VisitEvaluation>> EvaluateAsync(
        Guid organizationId, BranchScope scope, Guid visitId, EvaluationInput input, CancellationToken cancellationToken)
    {
        var info = await ReadVisitInfoAsync(organizationId, scope, visitId, cancellationToken);

        if (info is null)
        {
            return new DispatchOutcome<VisitEvaluation>.NotFound();
        }

        if (IsLocked(info.Status, info.OrderStatus))
        {
            return new DispatchOutcome<VisitEvaluation>.Locked();
        }

        var resolution = await ResolveTechniciansAsync(organizationId, scope, info.BranchId, input.TechnicianIds, cancellationToken);

        if (resolution.Failure is not null)
        {
            return resolution.Failure is TechnicianFailure.Missing
                ? new DispatchOutcome<VisitEvaluation>.NotFound()
                : new DispatchOutcome<VisitEvaluation>.Invalid(TechnicianErrors);
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Timezone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);
        var zoneId = await ReadZoneIdAsync(organizationId, info.BranchId, cancellationToken);
        var zone = OrganizationTime.FindZone(zoneId);
        var (dayFrom, dayTo) = BranchTime.Day(input.Start, zone);

        var candidates = await ReadProfilesAsync(
            organizationId,
            dbContext.TechnicianProfiles.AsNoTracking()
                .Where(profile => profile.OrganizationId == organizationId
                    && profile.BranchId == info.BranchId
                    && profile.Status == TechnicianStatus.Active),
            cancellationToken);
        var technicians = await BuildTechniciansAsync(
            organizationId, candidates, organization.Timezone, organization.WorkOrderPrefix, dayFrom, dayTo, cancellationToken);

        var required = (await RequiredSkillsAsync(organizationId, [info.WorkOrderId], cancellationToken))
            .GetValueOrDefault(info.WorkOrderId)?.Select(skill => new DispatchSkill(skill.Id, skill.Name)).ToList() ?? [];
        var selection = input.TechnicianIds.Select(id => technicians[id]).ToList();

        var classification = DispatchConflictClassifier.Classify(input.Start, input.End, required, selection, zone, visitId);
        var impact = selection
            .Select(technician => DispatchConflictClassifier.Impact(technician, input.Start, input.End, dayFrom, dayTo, zone, visitId))
            .ToList();
        var ranking = DispatchConflictClassifier.Rank(
            input.Start, input.End, required, [.. candidates.Select(profile => technicians[profile.Id])], dayFrom, dayTo, zone, visitId);

        return new DispatchOutcome<VisitEvaluation>.Succeeded(
            new VisitEvaluation(classification.Conflicts, classification.Skills, classification.Checks, impact, ranking));
    }

    private IQueryable<FieldOps.Domain.Branches.Branch> VisibleBranch(Guid organizationId, BranchScope scope)
    {
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (scope.All)
        {
            return branches;
        }

        var ids = scope.BranchIds.ToArray();

        return branches.Where(branch => ids.Contains(branch.Id));
    }

    private static readonly Dictionary<string, string[]> TechnicianErrors = new(StringComparer.Ordinal)
    {
        ["technicianIds"] = [DispatchMessages.TechniciansInvalid],
    };

    private enum TechnicianFailure
    {
        Missing,
        Ineligible,
    }

    private sealed record TechnicianResolution(TechnicianFailure? Failure, IReadOnlyList<ProfileRow> Profiles);

    /// <summary>
    /// BR-12: a technician of another organization or outside the caller scope is "missing" (404); one in scope that is
    /// not active or not in the work order branch is "ineligible" (400). Reads the current rows (under the caller's lock).
    /// </summary>
    private async Task<TechnicianResolution> ResolveTechniciansAsync(
        Guid organizationId,
        BranchScope scope,
        Guid workOrderBranchId,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new TechnicianResolution(null, []);
        }

        var wanted = ids.ToArray();
        var profiles = await ReadProfilesAsync(
            organizationId,
            dbContext.TechnicianProfiles.AsNoTracking().Where(profile => profile.OrganizationId == organizationId && wanted.Contains(profile.Id)),
            cancellationToken);

        if (profiles.Count != wanted.Length || profiles.Any(profile => !scope.Contains(profile.BranchId)))
        {
            return new TechnicianResolution(TechnicianFailure.Missing, []);
        }

        return profiles.Any(profile => profile.Status != TechnicianStatus.Active || profile.BranchId != workOrderBranchId)
            ? new TechnicianResolution(TechnicianFailure.Ineligible, [])
            : new TechnicianResolution(null, profiles);
    }

    private async Task<List<ProfileRow>> ReadProfilesAsync(
        Guid organizationId, IQueryable<TechnicianProfile> profiles, CancellationToken cancellationToken) =>
        await (
            from profile in profiles
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where branch.OrganizationId == organizationId
            orderby profile.FirstName, profile.LastName, profile.Id
            select new ProfileRow(
                profile.Id, profile.BranchId, profile.FirstName, profile.LastName, profile.Status, profile.ColorHex, branch.Timezone))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The schedules, skills and commitments of the profiles for the range, as plain data. Commitments cover every
    /// visit of the technician that is not unscheduled or cancelled plus scheduled assessments (BR-05, BR-10).
    /// </summary>
    private async Task<Dictionary<Guid, DispatchTechnician>> BuildTechniciansAsync(
        Guid organizationId,
        IReadOnlyList<ProfileRow> profiles,
        string organizationTimezone,
        string workOrderPrefix,
        DateTimeOffset rangeFrom,
        DateTimeOffset rangeTo,
        CancellationToken cancellationToken)
    {
        var ids = profiles.Select(profile => profile.Id).ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var schedules = await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [.. profiles.Select(profile => new ScheduleSubject(profile.Id, profile.Status, profile.BranchTimezone))],
            organizationTimezone,
            string.Empty,
            rangeFrom.AddDays(-1),
            rangeTo.AddDays(1),
            onlyActiveExceptions: true,
            cancellationToken);

        var skillRows = await (
            from link in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on link.SkillId equals skill.Id
            where ids.Contains(link.TechnicianId) && skill.OrganizationId == organizationId
            select new { link.TechnicianId, link.SkillId })
            .ToListAsync(cancellationToken);
        var commitments = await LoadCommitmentsAsync(organizationId, ids, rangeFrom, rangeTo, workOrderPrefix, cancellationToken);

        return profiles.ToDictionary(
            profile => profile.Id,
            profile => new DispatchTechnician(
                profile.Id,
                FullName(profile.FirstName, profile.LastName),
                schedules[profile.Id],
                skillRows.Where(row => row.TechnicianId == profile.Id).Select(row => row.SkillId).ToHashSet(),
                [.. commitments.Where(item => item.TechnicianId == profile.Id)]));
    }

    private async Task<List<DispatchCommitment>> LoadCommitmentsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> technicianIds,
        DateTimeOffset rangeFrom,
        DateTimeOffset rangeTo,
        string workOrderPrefix,
        CancellationToken cancellationToken)
    {
        var ids = technicianIds.ToArray();

        var visits = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on assignment.VisitId equals visit.Id
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where ids.Contains(assignment.TechnicianId)
                && assignment.UnassignedAt == null
                && visit.OrganizationId == organizationId
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart != null
                && visit.ScheduledEnd != null
                && visit.ScheduledStart < rangeTo
                && visit.ScheduledEnd > rangeFrom
            select new
            {
                assignment.TechnicianId,
                VisitId = visit.Id,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                order.WorkOrderNumber,
            })
            .ToListAsync(cancellationToken);

        var assessments = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.TechnicianId != null
                && ids.Contains(assessment.TechnicianId.Value)
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart < rangeTo
                && assessment.ScheduledEnd > rangeFrom)
            .Select(assessment => new { TechnicianId = assessment.TechnicianId!.Value, assessment.ScheduledStart, assessment.ScheduledEnd })
            .ToListAsync(cancellationToken);

        var result = new List<DispatchCommitment>();

        result.AddRange(visits.Select(row => new DispatchCommitment(
            row.TechnicianId,
            CommitmentKinds.Visit,
            row.ScheduledStart!.Value,
            row.ScheduledEnd!.Value,
            row.VisitId,
            row.Status,
            RequestCardRules.DisplayNumber(workOrderPrefix, row.WorkOrderNumber))));
        result.AddRange(assessments.Select(row => new DispatchCommitment(
            row.TechnicianId, CommitmentKinds.Assessment, row.ScheduledStart, row.ScheduledEnd, null, null, null)));

        return result;
    }

    private sealed record ProfileRow(
        Guid Id, Guid BranchId, string FirstName, string LastName, TechnicianStatus Status, string? ColorHex, string? BranchTimezone);
}
