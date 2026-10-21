using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Team;

internal static class TeamAvailabilityMapping
{
    public static TeamAvailabilityPage ToPage(
        DateOnly weekStart, IReadOnlyList<(AvailabilityGridProfile Profile, GridRow Row)> rows, int totalCount, int page) =>
        new(
            SkillsAvailabilityMapping.DateText(weekStart),
            [.. rows.Select(item => new AvailabilityRowView(
                item.Profile.Id,
                item.Profile.FirstName,
                item.Profile.LastName,
                item.Profile.ColorHex,
                item.Profile.Branch,
                item.Profile.ZoneId,
                item.Profile.UsesBranchHours,
                [.. item.Row.Days.Select(cell => new AvailabilityCellView(
                    SkillsAvailabilityMapping.DateText(cell.Date),
                    cell.State,
                    cell.Start is { } start ? SkillsAvailabilityMapping.TimeText(start) : null,
                    cell.End is { } end ? SkillsAvailabilityMapping.TimeText(end) : null))],
                item.Row.WeeklyMinutes))],
            totalCount,
            page,
            TeamQueryParser.PageSize);

    public static TechnicianAvailabilityView ToView(TechnicianAvailabilityData data, DateTimeOffset now)
    {
        var zone = BranchTime.FindZone(data.ZoneId);
        var today = BranchTime.LocalDate(now, zone);

        var days = AvailabilityDays.Names
            .Select(name =>
            {
                var slot = data.Slots
                    .Where(item => item.DayOfWeek == AvailabilityDays.Number(name))
                    .OrderBy(item => item.Start)
                    .ThenBy(item => item.End)
                    .FirstOrDefault();

                return slot is null
                    ? new EditableDayView(name, false, null, null)
                    : new EditableDayView(
                        name,
                        true,
                        SkillsAvailabilityMapping.TimeText(slot.Start),
                        SkillsAvailabilityMapping.TimeText(slot.End));
            })
            .ToList();

        return new TechnicianAvailabilityView(
            data.Id,
            data.FirstName,
            data.LastName,
            data.Branch,
            data.ZoneId,
            SkillsAvailabilityMapping.ZoneLabel(data.ZoneId, zone, now),
            data.UsesBranchHours,
            SkillsAvailabilityRules.FormatVersion(data.UpdatedAt),
            days,
            ToTimeOffList(data.TimeOff, zone, today));
    }

    /// <summary>BR-13: active full-day ranges whose end date is today or later, ordered by start.</summary>
    public static List<TimeOffView> ToTimeOffList(IEnumerable<TimeOffData> rows, TimeZoneInfo zone, DateOnly today) =>
        [.. rows
            .Select(item => (Item: item, Ok: TimeOffRules.TryFullDayRange(item.StartsAt, item.EndsAt, zone, out var start, out var end), Start: start, End: end))
            .Where(item => item.Ok && item.End >= today)
            .OrderBy(item => item.Item.StartsAt)
            .Select(item => ToView(item.Item, item.Start, item.End))];

    public static TimeOffView ToView(TimeOffData data, TimeZoneInfo zone)
    {
        TimeOffRules.TryFullDayRange(data.StartsAt, data.EndsAt, zone, out var start, out var end);

        return ToView(data, start, end);
    }

    private static TimeOffView ToView(TimeOffData data, DateOnly start, DateOnly end) =>
        new(
            data.Id,
            SkillsAvailabilityMapping.DateText(start),
            SkillsAvailabilityMapping.DateText(end),
            data.Note,
            SkillsAvailabilityRules.FormatVersion(data.UpdatedAt));
}

/// <summary>GET /team/availability (FR-02, BR-02 to BR-06). Read policy; scope per branch.</summary>
public sealed class GetTeamAvailabilityHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TeamAvailabilityPage>> HandleAsync(
        Guid organizationId, Guid membershipId, TeamAvailabilityQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var organizationZone = BranchTime.FindZone(BranchTime.ResolveZoneId(
            null, await store.GetOrganizationTimezoneAsync(organizationId, cancellationToken)));
        var defaultWeek = BranchTime.MondayOf(BranchTime.LocalDate(now, organizationZone));

        var parsed = TeamAvailabilityRules.ParseGrid(query, defaultWeek, out var errors);

        if (parsed is null)
        {
            return TeamResult<TeamAvailabilityPage>.Invalid(errors);
        }

        var (weekStart, search, branchId, availability, page) = parsed.Value;
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (branchId is { } branch
            && !await store.IsBranchAllowedAsync(organizationId, scope, branch, requireActive: true, cancellationToken))
        {
            return TeamResult<TeamAvailabilityPage>.Invalid(TeamFieldKeys.BranchId, TeamMessages.BranchNotAllowed);
        }

        // Exceptions near the week in any zone: the week plus a margin for the zone offsets.
        var low = new DateTimeOffset(weekStart.AddDays(-2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var high = new DateTimeOffset(weekStart.AddDays(9).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var profiles = await store.ListAvailabilityGridAsync(
            organizationId, scope, new AvailabilityGridFilter(search, branchId), low, high, cancellationToken);

        var rows = profiles
            .Select(profile => (Profile: profile, Row: TeamAvailabilityGrid.Build(weekStart, profile.ZoneId, profile.Slots, profile.Exceptions)))
            .Where(item => TeamAvailabilityGrid.Matches(availability, item.Row))
            .OrderBy(item => item.Profile.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Profile.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Profile.Id)
            .ToList();

        var items = rows.Skip((page - 1) * TeamQueryParser.PageSize).Take(TeamQueryParser.PageSize).ToList();

        return TeamResult<TeamAvailabilityPage>.Ok(TeamAvailabilityMapping.ToPage(weekStart, items, rows.Count, page));
    }
}

/// <summary>Drawer read and the availability writes (BR-08 to BR-12). A hidden profile is 404 before any validation.</summary>
public sealed class TechnicianAvailabilityHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TechnicianAvailabilityView>> GetAsync(
        Guid organizationId, Guid membershipId, Guid technicianId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var data = await store.GetTechnicianAvailabilityAsync(organizationId, scope, technicianId, now, cancellationToken);

        return data is null
            ? TeamResult<TechnicianAvailabilityView>.NotFound()
            : TeamResult<TechnicianAvailabilityView>.Ok(TeamAvailabilityMapping.ToView(data, now));
    }

    public async Task<TeamResult<TechnicianAvailabilityView>> SaveAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        SaveAvailabilityInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetActiveProfileZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is null)
        {
            return TeamResult<TechnicianAvailabilityView>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var version = TeamAvailabilityRules.ParseUpdatedAt(input.UpdatedAt);

        if (version is null)
        {
            errors[TeamAvailabilityKeys.UpdatedAt] = [FieldOps.Application.Validation.UpdatedAtValidation.InvalidMessage];
        }

        var days = TeamAvailabilityRules.ValidateDays(input.Days, errors);

        if (days is null || version is null || errors.Count > 0)
        {
            return TeamResult<TechnicianAvailabilityView>.Invalid(errors);
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var result = await store.SaveAvailabilityAsync(
            organizationId, scope, technicianId, version.Value, days, actorUserId, clientIp, now, cancellationToken);

        return await MapAsync(result, organizationId, scope, technicianId, now, cancellationToken);
    }

    public async Task<TeamResult<TechnicianAvailabilityView>> UseBranchHoursAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        UpdatedAtInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetActiveProfileZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is null)
        {
            return TeamResult<TechnicianAvailabilityView>.NotFound();
        }

        if (TeamAvailabilityRules.ParseUpdatedAt(input.UpdatedAt) is not { } version)
        {
            return TeamResult<TechnicianAvailabilityView>.Invalid(
                TeamAvailabilityKeys.UpdatedAt, FieldOps.Application.Validation.UpdatedAtValidation.InvalidMessage);
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var result = await store.UseBranchHoursAsync(
            organizationId, scope, technicianId, version, actorUserId, clientIp, now, cancellationToken);

        return await MapAsync(result, organizationId, scope, technicianId, now, cancellationToken);
    }

    private async Task<TeamResult<TechnicianAvailabilityView>> MapAsync(
        AvailabilityWriteResult result,
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (result.Outcome == AvailabilityWriteOutcome.NotFound)
        {
            return TeamResult<TechnicianAvailabilityView>.NotFound();
        }

        if (result.Outcome == AvailabilityWriteOutcome.Conflict)
        {
            return TeamResult<TechnicianAvailabilityView>.Conflict(
                TeamAvailabilityMessages.ProfileStale, TeamAvailabilityConflictCodes.Stale);
        }

        var data = await store.GetTechnicianAvailabilityAsync(organizationId, scope, technicianId, now, cancellationToken);

        return data is null
            ? TeamResult<TechnicianAvailabilityView>.NotFound()
            : TeamResult<TechnicianAvailabilityView>.Ok(TeamAvailabilityMapping.ToView(data, now));
    }
}

/// <summary>Time off endpoints (BR-13 to BR-15). A hidden profile is 404 before any validation.</summary>
public sealed class TechnicianTimeOffHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TimeOffView>> CreateAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        CreateTimeOffInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetActiveProfileZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is not { } zoneId)
        {
            return TeamResult<TimeOffView>.NotFound();
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var zone = BranchTime.FindZone(zoneId);
        var values = TeamAvailabilityRules.ValidateTimeOff(input, BranchTime.LocalDate(now, zone), out var errors);

        if (values is null)
        {
            return TeamResult<TimeOffView>.Invalid(errors);
        }

        var (startsAt, endsAt) = TimeOffRules.Instants(values.StartDate, values.EndDate, zone);
        var result = await store.CreateTimeOffAsync(
            organizationId,
            scope,
            technicianId,
            new TimeOffWrite(startsAt, endsAt, values.StartDate, values.EndDate, values.Note),
            actorUserId,
            clientIp,
            now,
            cancellationToken);

        return result.Outcome switch
        {
            TimeOffOutcome.NotFound => TeamResult<TimeOffView>.NotFound(),
            TimeOffOutcome.Overlap => TeamResult<TimeOffView>.Conflict(
                TeamAvailabilityMessages.TimeOffOverlap, TeamAvailabilityConflictCodes.TimeOffOverlap),
            _ => TeamResult<TimeOffView>.Ok(TeamAvailabilityMapping.ToView(result.TimeOff!, zone)),
        };
    }

    public async Task<TeamResult<RetireTimeOffView>> RetireAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid timeOffId,
        Guid actorUserId,
        IPAddress? clientIp,
        UpdatedAtInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetActiveProfileZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is not { } zoneId)
        {
            return TeamResult<RetireTimeOffView>.NotFound();
        }

        if (TeamAvailabilityRules.ParseUpdatedAt(input.UpdatedAt) is not { } version)
        {
            return TeamResult<RetireTimeOffView>.Invalid(
                TeamAvailabilityKeys.UpdatedAt, FieldOps.Application.Validation.UpdatedAtValidation.InvalidMessage);
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var result = await store.RetireTimeOffAsync(
            organizationId, scope, technicianId, timeOffId, version, zoneId, actorUserId, clientIp, now, cancellationToken);

        return result.Outcome switch
        {
            TimeOffOutcome.NotFound => TeamResult<RetireTimeOffView>.NotFound(TeamAvailabilityMessages.TimeOffNotFound),
            TimeOffOutcome.Closed => TeamResult<RetireTimeOffView>.Conflict(
                TeamAvailabilityMessages.TimeOffClosed, TeamAvailabilityConflictCodes.TimeOffClosed),
            TimeOffOutcome.Stale => TeamResult<RetireTimeOffView>.Conflict(
                TeamAvailabilityMessages.TimeOffStale, TeamAvailabilityConflictCodes.Stale),
            _ => TeamResult<RetireTimeOffView>.Ok(new RetireTimeOffView(
                result.TimeOff is null ? null : TeamAvailabilityMapping.ToView(result.TimeOff, BranchTime.FindZone(zoneId)))),
        };
    }
}
