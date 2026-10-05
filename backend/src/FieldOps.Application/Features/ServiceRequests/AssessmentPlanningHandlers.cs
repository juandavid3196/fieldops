using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Team;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>Raw planner query values (FR-03) before parsing.</summary>
public sealed record PlannerQueryText(string? Date, string? Start, string? DurationMinutes, string? BranchId);

/// <summary>Raw calendar query values (FR-04) before parsing.</summary>
public sealed record CalendarQueryText(string? TechnicianId, string? From, string? To);

/// <summary>
/// GET /service-requests/{id}/assessment/planner: the eligible technicians with the BR-05 slot state and the BR-06
/// weekly workload. Tenant, branch scope, status and branch eligibility are checked by the store.
/// </summary>
public sealed class GetAssessmentPlannerHandler(IServiceRequestStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<AssessmentPlanner>> HandleAsync(
        MembershipCall call, Guid requestId, PlannerQueryText query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var dateOk = DateOnly.TryParseExact(
            query.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);

        if (!dateOk)
        {
            errors["date"] = [ServiceRequestMessages.DateInvalid];
        }

        var startOk = TimeOnly.TryParseExact(
            query.Start?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start);

        if (!startOk)
        {
            errors["start"] = [ServiceRequestMessages.StartInvalid];
        }
        else if (start.Minute != 0)
        {
            errors["start"] = [ServiceRequestMessages.ArrivalWindowInvalid];
        }

        if (!int.TryParse(query.DurationMinutes, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !AssessmentSlotRules.IsAllowedDuration(TimeSpan.FromMinutes(minutes)))
        {
            errors["durationMinutes"] = [ServiceRequestMessages.DurationInvalid];
        }

        Guid? branchId = null;

        if (!string.IsNullOrWhiteSpace(query.BranchId))
        {
            if (Guid.TryParse(query.BranchId, out var parsedBranch) && parsedBranch != Guid.Empty)
            {
                branchId = parsedBranch;
            }
            else
            {
                errors["branchId"] = [ServiceRequestMessages.BranchNotAllowed];
            }
        }

        if (errors.Count > 0)
        {
            return ServiceRequestResult<AssessmentPlanner>.Invalid(errors);
        }

        var organization = await store.GetOrganizationContextAsync(call.OrganizationId, cancellationToken);

        if (organization is null)
        {
            return ServiceRequestResult<AssessmentPlanner>.NotFound();
        }

        var zone = OrganizationTime.FindZone(organization.Timezone);
        var local = date.ToDateTime(start, DateTimeKind.Unspecified).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

        if (!OrganizationTime.TryParseLocal(local, zone, out var slotStart))
        {
            return ServiceRequestResult<AssessmentPlanner>.Invalid("start", ServiceRequestMessages.StartInvalid);
        }

        var slotEnd = slotStart.AddMinutes(minutes);
        var (weekFrom, weekTo) = BranchTime.Week(slotStart, zone);
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var loaded = await store.LoadPlannerAsync(call.OrganizationId, scope, requestId, branchId, weekFrom, weekTo, cancellationToken);

        if (loaded.Kind != ServiceRequestResultKind.Succeeded)
        {
            return loaded.Kind switch
            {
                ServiceRequestResultKind.Invalid => ServiceRequestResult<AssessmentPlanner>.Invalid(loaded.Errors!),
                ServiceRequestResultKind.Conflict => ServiceRequestResult<AssessmentPlanner>.Conflict(loaded.Message, loaded.Code!),
                _ => ServiceRequestResult<AssessmentPlanner>.NotFound(),
            };
        }

        var technicians = loaded.Value!.Candidates
            .Select(candidate =>
            {
                var state = AssessmentSlotEvaluator.Evaluate(candidate.Schedule, candidate.Commitments, slotStart, slotEnd, zone);
                var workload = AssessmentSlotEvaluator.Workload(candidate.Schedule, candidate.Commitments, weekFrom, weekTo);

                return new PlannerTechnician(
                    candidate.Id,
                    candidate.Name,
                    RequestCardRules.Initials(candidate.Name),
                    candidate.PrimarySkill,
                    new PlannerSlot(
                        state.State,
                        state.From is { } from ? OrganizationTime.ToZone(from, zone) : null,
                        state.To is { } to ? OrganizationTime.ToZone(to, zone) : null,
                        state.AvailableAfter is { } after ? OrganizationTime.ToZone(after, zone) : null,
                        state.Blocking),
                    new PlannerWorkload(workload.Percent, workload.State));
            })
            .OrderBy(item => AssessmentSlotEvaluator.Rank(item.Slot.State))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id)
            .ToList();

        return ServiceRequestResult<AssessmentPlanner>.Ok(
            new AssessmentPlanner(organization.Timezone, loaded.Value.BranchId, technicians));
    }
}

/// <summary>GET /service-requests/{id}/assessment/calendar: the read-only calendar of one eligible technician (BR-07).</summary>
public sealed class GetAssessmentCalendarHandler(IServiceRequestStore store, IBranchScopeResolver scopes)
{
    public const int MaxDays = 7;

    public async Task<ServiceRequestResult<AssessmentCalendar>> HandleAsync(
        MembershipCall call, Guid requestId, CalendarQueryText query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!Guid.TryParse(query.TechnicianId, out var technicianId) || technicianId == Guid.Empty)
        {
            errors["technicianId"] = [ServiceRequestMessages.TechnicianInvalid];
        }

        var fromOk = DateOnly.TryParseExact(
            query.From?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from);
        var toOk = DateOnly.TryParseExact(
            query.To?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to);

        if (!fromOk)
        {
            errors["from"] = [ServiceRequestMessages.DateInvalid];
        }

        if (!toOk)
        {
            errors["to"] = [ServiceRequestMessages.DateInvalid];
        }
        else if (fromOk && (to < from || to.DayNumber - from.DayNumber + 1 > MaxDays))
        {
            errors["to"] = [ServiceRequestMessages.RangeInvalid];
        }

        if (errors.Count > 0)
        {
            return ServiceRequestResult<AssessmentCalendar>.Invalid(errors);
        }

        var organization = await store.GetOrganizationContextAsync(call.OrganizationId, cancellationToken);

        if (organization is null)
        {
            return ServiceRequestResult<AssessmentCalendar>.NotFound();
        }

        var zone = OrganizationTime.FindZone(organization.Timezone);
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var loaded = await store.LoadCalendarAsync(
            call.OrganizationId,
            scope,
            requestId,
            technicianId,
            OrganizationTime.StartOfDayUtc(from, zone),
            OrganizationTime.StartOfDayUtc(to.AddDays(1), zone),
            cancellationToken);

        return loaded.Kind switch
        {
            ServiceRequestResultKind.Succeeded => ServiceRequestResult<AssessmentCalendar>.Ok(
                AssessmentCalendarBuilder.Build(loaded.Value!, from, to, organization.Timezone, zone)),
            ServiceRequestResultKind.Invalid => ServiceRequestResult<AssessmentCalendar>.Invalid(loaded.Errors!),
            ServiceRequestResultKind.Conflict => ServiceRequestResult<AssessmentCalendar>.Conflict(loaded.Message, loaded.Code!),
            _ => ServiceRequestResult<AssessmentCalendar>.NotFound(),
        };
    }
}
