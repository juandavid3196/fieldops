using System.Globalization;
using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Domain.Technicians;

namespace FieldOps.Application.Features.Team;

internal static class SkillsAvailabilityMapping
{
    /// <summary>BR-25: the earliest-starting window of each day with its earliest-starting break.</summary>
    public static List<AvailabilitySlot> Normalize(IEnumerable<AvailabilitySlot> slots) =>
        [.. slots
            .GroupBy(slot => slot.DayOfWeek)
            .Select(group =>
            {
                var first = group.OrderBy(slot => slot.Start).ThenBy(slot => slot.End).First();

                return first with { Breaks = [.. first.Breaks.OrderBy(item => item.Start).Take(1)] };
            })];

    public static string TimeText(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"(UTC-05:00) America/Chicago" at the current offset (BR-03).</summary>
    public static string ZoneLabel(string zoneId, TimeZoneInfo zone, DateTimeOffset now)
    {
        var offset = zone.GetUtcOffset(now);
        var sign = offset < TimeSpan.Zero ? "-" : "+";

        return $"(UTC{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}) {zoneId}";
    }

    public static ExceptionView ToView(ExceptionData data, TimeZoneInfo zone)
    {
        var date = BranchTime.LocalDate(data.StartsAt, zone);
        var (dayStart, dayEnd) = (
            BranchTime.ToInstant(date, TimeOnly.MinValue, zone), BranchTime.ToInstant(date.AddDays(1), TimeOnly.MinValue, zone));

        // BR-13: derived, never stored.
        var kind = data.IsAvailable
            ? ExceptionKinds.Extended
            : data.StartsAt == dayStart && data.EndsAt == dayEnd ? ExceptionKinds.Unavailable : ExceptionKinds.Partial;

        string? start = null;
        string? end = null;

        if (kind != ExceptionKinds.Unavailable)
        {
            start = TimeText(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(data.StartsAt, zone).DateTime));
            end = TimeText(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(data.EndsAt, zone).DateTime));
        }

        return new ExceptionView(
            data.Id,
            DateText(date),
            kind,
            start,
            end,
            data.Reason,
            data.Status,
            SkillsAvailabilityRules.FormatVersion(data.UpdatedAt));
    }

    public static SkillsAvailabilityView ToView(SkillsAvailabilityData data, DateTimeOffset now)
    {
        var zone = BranchTime.FindZone(data.ZoneId);
        var today = BranchTime.LocalDate(now, zone);
        var slots = Normalize(data.Slots);

        var schedule = new TechnicianSchedule(
            data.Status,
            data.ZoneId,
            slots,
            [.. data.Exceptions
                .Where(item => item.Status == TechnicianExceptionStatus.Active)
                .Select(item => new AvailabilityOverride(item.StartsAt, item.EndsAt, item.IsAvailable))],
            data.Visits);

        var weekly = slots
            .OrderBy(slot => (slot.DayOfWeek + 6) % 7)
            .Select(slot => new WeeklyDayView(
                slot.DayOfWeek,
                TimeText(slot.Start),
                TimeText(slot.End),
                slot.Breaks.Count > 0 ? TimeText(slot.Breaks[0].Start) : null,
                slot.Breaks.Count > 0 ? TimeText(slot.Breaks[0].End) : null,
                slot.CapacityPercent))
            .ToList();

        var exceptions = data.Exceptions
            .Select(item => (Item: item, View: ToView(item, zone)))
            .Where(item => string.CompareOrdinal(item.View.Date, DateText(today)) >= 0)
            .OrderBy(item => item.View.Date, StringComparer.Ordinal)
            .ThenBy(item => item.Item.StartsAt)
            .Select(item => item.View)
            .ToList();

        var upcoming = TechnicianAvailabilityCalculator.UpcomingAvailability(schedule, now)
            .Select(day => new UpcomingDayView(
                DateText(day.Date),
                day.Label,
                day.State,
                day.NextAvailableAt,
                [.. day.Windows.Select(window => new UpcomingWindowView(TimeText(window.Start), TimeText(window.End)))],
                day.Limited))
            .ToList();

        var week = TechnicianAvailabilityCalculator.SevenDayLoad(schedule, now);
        var day = TechnicianAvailabilityCalculator.TodayLoad(schedule, now);

        return new SkillsAvailabilityView(
            new SkillsHeaderView(data.Id, data.FirstName, data.LastName, TeamMapping.ProfileStatusText(data.Status), data.Branch),
            SkillsAvailabilityRules.FormatVersion(data.UpdatedAt),
            data.ZoneId,
            ZoneLabel(data.ZoneId, zone, now),
            weekly,
            [.. data.Skills
                .OrderByDescending(skill => skill.IsPrimary)
                .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                .Select(skill => new AssignedSkillView(skill.SkillId, skill.Name, skill.Proficiency, skill.IsPrimary))],
            exceptions,
            upcoming,
            new CapacityView(
                Minutes(week.AvailableMinutes),
                Minutes(week.ScheduledMinutes),
                Minutes(week.RemainingMinutes),
                week.UtilizationPercent),
            new TodaySummaryView(day.Jobs, day.UtilizationPercent));
    }

    private static int Minutes(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>GET /team/technicians/{id}/skills-availability (FR-02, FR-08, FR-09, BR-01 to BR-04, BR-18, BR-19, BR-25).</summary>
public sealed class GetSkillsAvailabilityHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    /// <param name="ownProfileOnly">True for technician callers: only the profile linked to their membership is visible.</param>
    public async Task<TeamResult<SkillsAvailabilityView>> HandleAsync(
        Guid organizationId, Guid membershipId, bool ownProfileOnly, Guid technicianId, CancellationToken cancellationToken)
    {
        var scope = ownProfileOnly
            ? BranchScope.Nothing
            : await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var data = await store.GetSkillsAvailabilityAsync(
            organizationId, scope, ownProfileOnly ? membershipId : null, technicianId, now, cancellationToken);

        return data is null
            ? TeamResult<SkillsAvailabilityView>.NotFound()
            : TeamResult<SkillsAvailabilityView>.Ok(SkillsAvailabilityMapping.ToView(data, now));
    }
}

/// <summary>PUT /team/technicians/{id}/skills-availability (FR-04, FR-05, BR-06 to BR-11, BR-25). Hidden ids are 404 first.</summary>
public sealed class SaveSkillsAvailabilityHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<SkillsAvailabilityView>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        SaveSkillsAvailabilityInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is null)
        {
            return TeamResult<SkillsAvailabilityView>.NotFound();
        }

        var values = SkillsAvailabilityRules.ValidateSave(input, out var errors);
        var version = SkillsAvailabilityRules.ParseVersion(input.Version);

        if (values is null || version is null || errors.Count > 0)
        {
            return TeamResult<SkillsAvailabilityView>.Invalid(errors);
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());

        var result = await store.SaveSkillsAvailabilityAsync(
            organizationId, scope, technicianId, version.Value, values, actorUserId, clientIp, now, cancellationToken);

        switch (result.Outcome)
        {
            case SkillsSaveOutcome.NotFound:
                return TeamResult<SkillsAvailabilityView>.NotFound();
            case SkillsSaveOutcome.Conflict:
                return TeamResult<SkillsAvailabilityView>.Conflict(SkillsAvailabilityMessages.ProfileStaleVersion);
            case SkillsSaveOutcome.InvalidSkills:
                return TeamResult<SkillsAvailabilityView>.Invalid(SkillsAvailabilityRules.SkillsKey, TeamMessages.SkillInvalid);
        }

        var data = await store.GetSkillsAvailabilityAsync(organizationId, scope, null, technicianId, now, cancellationToken);

        return data is null
            ? TeamResult<SkillsAvailabilityView>.NotFound()
            : TeamResult<SkillsAvailabilityView>.Ok(SkillsAvailabilityMapping.ToView(data, now));
    }
}

/// <summary>Exception endpoints (FR-06, BR-12 to BR-16). The profile is resolved first, so a hidden id is 404 whatever the body is.</summary>
public sealed class TechnicianExceptionsHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<ExceptionView>> CreateAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        ExceptionInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is not { } zoneId)
        {
            return TeamResult<ExceptionView>.NotFound();
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var zone = BranchTime.FindZone(zoneId);

        if (Build(input, zone, now) is not { } write)
        {
            return TeamResult<ExceptionView>.Invalid(Errors(input, zone, now));
        }

        var result = await store.CreateExceptionAsync(
            organizationId, scope, technicianId, write, actorUserId, clientIp, now, cancellationToken);

        return Map(result, zone);
    }

    public async Task<TeamResult<ExceptionView>> UpdateAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid exceptionId,
        Guid actorUserId,
        IPAddress? clientIp,
        ExceptionInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is not { } zoneId)
        {
            return TeamResult<ExceptionView>.NotFound();
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var zone = BranchTime.FindZone(zoneId);
        var version = SkillsAvailabilityRules.ParseVersion(input.Version);
        var write = Build(input, zone, now);

        if (write is null || version is null)
        {
            var errors = Errors(input, zone, now);

            if (version is null)
            {
                errors[SkillsAvailabilityRules.VersionKey] = [FieldOps.Application.Validation.UpdatedAtValidation.InvalidMessage];
            }

            return TeamResult<ExceptionView>.Invalid(errors);
        }

        var result = await store.UpdateExceptionAsync(
            organizationId, scope, technicianId, exceptionId, version.Value, write, zoneId, actorUserId, clientIp, now, cancellationToken);

        return Map(result, zone);
    }

    public async Task<TeamResult<ExceptionView>> SetActiveAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid exceptionId,
        bool activate,
        Guid actorUserId,
        IPAddress? clientIp,
        VersionInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is not { } zoneId)
        {
            return TeamResult<ExceptionView>.NotFound();
        }

        if (SkillsAvailabilityRules.ParseVersion(input.Version) is not { } version)
        {
            return TeamResult<ExceptionView>.Invalid(
                SkillsAvailabilityRules.VersionKey, FieldOps.Application.Validation.UpdatedAtValidation.InvalidMessage);
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());

        var result = await store.SetExceptionActiveAsync(
            organizationId, scope, technicianId, exceptionId, version, activate, zoneId, actorUserId, clientIp, now, cancellationToken);

        return Map(result, BranchTime.FindZone(zoneId));
    }

    private static Dictionary<string, string[]> Errors(ExceptionInput input, TimeZoneInfo zone, DateTimeOffset now)
    {
        SkillsAvailabilityRules.ValidateException(input, BranchTime.LocalDate(now, zone), out var errors);

        if (errors.Count == 0)
        {
            errors["end"] = [SkillsAvailabilityMessages.EndAfterStart];
        }

        return errors;
    }

    /// <summary>The instants of a valid exception (BR-12), or null when the input is invalid.</summary>
    private static ExceptionWrite? Build(ExceptionInput input, TimeZoneInfo zone, DateTimeOffset now)
    {
        var values = SkillsAvailabilityRules.ValidateException(input, BranchTime.LocalDate(now, zone), out _);

        if (values is null)
        {
            return null;
        }

        var dayStart = BranchTime.ToInstant(values.Date, TimeOnly.MinValue, zone);
        var dayEnd = BranchTime.ToInstant(values.Date.AddDays(1), TimeOnly.MinValue, zone);

        if (values.Kind == ExceptionKinds.Unavailable)
        {
            return new ExceptionWrite(dayStart, dayEnd, false, values.Reason, dayStart, dayEnd);
        }

        var start = BranchTime.ToInstant(values.Date, values.Start!.Value, zone);
        var end = BranchTime.ToInstant(values.Date, values.End!.Value, zone);

        // A window entirely inside a skipped DST hour collapses; it is reported as an end-time error.
        return end > start
            ? new ExceptionWrite(start, end, values.Kind == ExceptionKinds.Extended, values.Reason, dayStart, dayEnd)
            : null;
    }

    private static TeamResult<ExceptionView> Map(ExceptionResult result, TimeZoneInfo zone) =>
        result.Outcome switch
        {
            ExceptionOutcome.NotFound => TeamResult<ExceptionView>.NotFound(SkillsAvailabilityMessages.ExceptionNotFound),
            ExceptionOutcome.Past => TeamResult<ExceptionView>.Conflict(
                SkillsAvailabilityMessages.ExceptionPast, ExceptionConflictCodes.Past),
            ExceptionOutcome.Cancelled => TeamResult<ExceptionView>.Conflict(
                SkillsAvailabilityMessages.ExceptionCancelled, ExceptionConflictCodes.Cancelled),
            ExceptionOutcome.Stale => TeamResult<ExceptionView>.Conflict(
                SkillsAvailabilityMessages.ExceptionStale, ExceptionConflictCodes.Stale),
            ExceptionOutcome.DateTaken => TeamResult<ExceptionView>.Conflict(
                SkillsAvailabilityMessages.ExceptionDateTaken, ExceptionConflictCodes.DateTaken),
            _ => TeamResult<ExceptionView>.Ok(SkillsAvailabilityMapping.ToView(result.Exception!, zone)),
        };
}

/// <summary>Skill catalog endpoints (FR-07, BR-17). Every id is verified against the session organization.</summary>
public sealed class SkillCatalogHandler(ITeamStore store)
{
    public async Task<IReadOnlyList<SkillView>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await store.ListSkillsAsync(organizationId, cancellationToken);

    public async Task<TeamResult<SkillView>> CreateAsync(
        Guid organizationId, Guid actorUserId, IPAddress? clientIp, SkillInput input, CancellationToken cancellationToken)
    {
        var values = SkillsAvailabilityRules.ValidateSkill(input, out var errors);

        if (values is null)
        {
            return TeamResult<SkillView>.Invalid(errors);
        }

        return Map(await store.CreateSkillAsync(organizationId, values, actorUserId, clientIp, cancellationToken));
    }

    public async Task<TeamResult<SkillView>> UpdateAsync(
        Guid organizationId, Guid skillId, Guid actorUserId, IPAddress? clientIp, SkillInput input, CancellationToken cancellationToken)
    {
        var values = SkillsAvailabilityRules.ValidateSkill(input, out var errors);

        if (values is null)
        {
            return TeamResult<SkillView>.Invalid(errors);
        }

        return Map(await store.UpdateSkillAsync(organizationId, skillId, values, actorUserId, clientIp, cancellationToken));
    }

    public async Task<TeamResult<TeamNoValue>> SetActiveAsync(
        Guid organizationId, Guid skillId, bool active, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var result = await store.SetSkillActiveAsync(organizationId, skillId, active, actorUserId, clientIp, cancellationToken);

        return result.Outcome == SkillOutcome.NotFound
            ? TeamResult<TeamNoValue>.NotFound(SkillsAvailabilityMessages.SkillNotFound)
            : TeamResult<TeamNoValue>.NoOp();
    }

    private static TeamResult<SkillView> Map(SkillResult result) =>
        result.Outcome switch
        {
            SkillOutcome.NotFound => TeamResult<SkillView>.NotFound(SkillsAvailabilityMessages.SkillNotFound),
            SkillOutcome.Duplicate => TeamResult<SkillView>.Invalid("name", SkillsAvailabilityMessages.SkillNameTaken),
            _ => TeamResult<SkillView>.Ok(result.Skill!),
        };
}
