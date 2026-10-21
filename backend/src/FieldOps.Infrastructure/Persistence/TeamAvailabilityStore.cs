using System.Globalization;
using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Availability grid, skills matrix, availability/time off/skill assignment writes (team-availability-skills).</summary>
internal sealed partial class TeamStore
{
    private const short NewAssignmentProficiency = 3;

    private readonly record struct DayInterval(TimeOnly Start, TimeOnly End);

    private sealed record GridProfileRow(
        Guid Id,
        string FirstName,
        string LastName,
        string? ColorHex,
        Guid BranchId,
        string BranchName,
        string? BranchTimezone,
        bool UsesBranchHours,
        string BranchBusinessHours);

    private IQueryable<TechnicianProfile> ActiveProfiles(Guid organizationId, BranchScope scope) =>
        ScopedProfiles(organizationId, scope).Where(profile => profile.Status == TechnicianStatus.Active);

    public async Task<string?> GetActiveProfileZoneIdAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken)
    {
        var row = await PageRowAsync(ActiveProfiles(organizationId, scope), technicianId, cancellationToken);

        return row is null
            ? null
            : BranchTime.ResolveZoneId(row.BranchTimezone, (await OrganizationContextAsync(organizationId, cancellationToken)).Timezone);
    }

    public async Task<string?> GetOrganizationTimezoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (await OrganizationContextAsync(organizationId, cancellationToken)).Timezone;

    public async Task<IReadOnlyList<AvailabilityGridProfile>> ListAvailabilityGridAsync(
        Guid organizationId,
        BranchScope scope,
        AvailabilityGridFilter filter,
        DateTimeOffset low,
        DateTimeOffset high,
        CancellationToken cancellationToken)
    {
        var rows = await GridRowsAsync(organizationId, scope, filter, cancellationToken);
        var organizationTimezone = (await OrganizationContextAsync(organizationId, cancellationToken)).Timezone;

        var ids = rows.Select(row => row.Id).ToArray();
        var ownIds = rows.Where(row => !row.UsesBranchHours).Select(row => row.Id).ToArray();

        var slotRows = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
            .Where(slot => ownIds.Contains(slot.TechnicianId))
            .Select(slot => new { slot.TechnicianId, slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.CapacityPercent })
            .ToListAsync(cancellationToken);

        var exceptionRows = await dbContext.TechnicianExceptions.AsNoTracking()
            .Where(item => ids.Contains(item.TechnicianId)
                && item.Status == TechnicianExceptionStatus.Active
                && item.EndsAt > low
                && item.StartsAt < high)
            .Select(item => new { item.TechnicianId, item.StartsAt, item.EndsAt, item.IsAvailable })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new AvailabilityGridProfile(
            row.Id,
            row.FirstName,
            row.LastName,
            row.ColorHex,
            new TeamOption(row.BranchId, row.BranchName),
            BranchTime.ResolveZoneId(row.BranchTimezone, organizationTimezone),
            row.UsesBranchHours,
            EffectiveWeeklyAvailability.Resolve(
                row.UsesBranchHours,
                row.BranchBusinessHours,
                [.. slotRows.Where(slot => slot.TechnicianId == row.Id)
                    .Select(slot => new AvailabilitySlot(slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.CapacityPercent, []))]),
            [.. exceptionRows.Where(item => item.TechnicianId == row.Id)
                .Select(item => new AvailabilityOverride(item.StartsAt, item.EndsAt, item.IsAvailable))]))];
    }

    public async Task<SkillMatrixData> GetSkillMatrixAsync(
        Guid organizationId, BranchScope scope, AvailabilityGridFilter filter, CancellationToken cancellationToken)
    {
        var skills = await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId && skill.IsActive)
            .OrderBy(skill => skill.Name).ThenBy(skill => skill.Id)
            .Select(skill => new TeamOption(skill.Id, skill.Name))
            .ToListAsync(cancellationToken);

        var rows = await GridRowsAsync(organizationId, scope, filter, cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var skillIds = skills.Select(skill => skill.Id).ToArray();

        var assignments = await dbContext.TechnicianSkills.AsNoTracking()
            .Where(item => ids.Contains(item.TechnicianId) && skillIds.Contains(item.SkillId))
            .Select(item => new { item.TechnicianId, item.SkillId })
            .ToListAsync(cancellationToken);

        var order = skills.Select((skill, index) => (skill.Id, index)).ToDictionary(item => item.Id, item => item.index);

        return new SkillMatrixData(
            skills,
            [.. rows.Select(row => new SkillMatrixTechnician(
                row.Id,
                row.FirstName,
                row.LastName,
                row.ColorHex,
                new TeamOption(row.BranchId, row.BranchName),
                [.. assignments.Where(item => item.TechnicianId == row.Id)
                    .Select(item => item.SkillId)
                    .OrderBy(skillId => order[skillId])]))]);
    }

    public async Task<TechnicianAvailabilityData?> GetTechnicianAvailabilityAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await PageRowAsync(ActiveProfiles(organizationId, scope), technicianId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var own = new List<AvailabilitySlot>();

        if (!row.UsesBranchHours)
        {
            var slotRows = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
                .Where(slot => slot.TechnicianId == technicianId)
                .ToListAsync(cancellationToken);

            var slotIds = slotRows.Select(slot => slot.Id).ToArray();
            var breakRows = await dbContext.TechnicianBreaks.AsNoTracking()
                .Where(item => slotIds.Contains(item.AvailabilityId))
                .ToListAsync(cancellationToken);

            own.AddRange(slotRows.Select(slot => new AvailabilitySlot(
                slot.DayOfWeek,
                slot.StartTime,
                slot.EndTime,
                slot.CapacityPercent,
                [.. breakRows.Where(item => item.AvailabilityId == slot.Id).Select(item => new TimeRange(item.StartTime, item.EndTime))])));
        }

        var low = now.AddDays(-2);
        var timeOff = await dbContext.TechnicianExceptions.AsNoTracking()
            .Where(item => item.TechnicianId == technicianId
                && item.Status == TechnicianExceptionStatus.Active
                && !item.IsAvailable
                && item.EndsAt > low)
            .Select(item => new TimeOffData(item.Id, item.StartsAt, item.EndsAt, item.Reason, item.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new TechnicianAvailabilityData(
            row.Id,
            row.FirstName,
            row.LastName,
            new TeamOption(row.BranchId, row.BranchName),
            BranchTime.ResolveZoneId(row.BranchTimezone, (await OrganizationContextAsync(organizationId, cancellationToken)).Timezone),
            row.UsesBranchHours,
            row.UpdatedAt,
            EffectiveWeeklyAvailability.Resolve(row.UsesBranchHours, row.BranchBusinessHours, own),
            timeOff);
    }

    public async Task<AvailabilityWriteResult> SaveAvailabilityAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        IReadOnlyList<AvailabilityDayValues> days,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null || profile.Status != TechnicianStatus.Active)
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.NotFound);
        }

        if (!Same(profile.UpdatedAt, version))
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.Conflict);
        }

        var slots = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
            .Where(slot => slot.TechnicianId == technicianId)
            .ToListAsync(cancellationToken);

        var slotIds = slots.Select(slot => slot.Id).ToArray();
        var breaks = await dbContext.TechnicianBreaks.AsNoTracking()
            .Where(item => slotIds.Contains(item.AvailabilityId))
            .ToListAsync(cancellationToken);

        var businessHours = await BranchBusinessHoursAsync(profile.BranchId, cancellationToken);
        var own = ToSlots(slots, breaks);
        var before = Intervals(EffectiveWeeklyAvailability.Resolve(profile.UsesBranchHours, businessHours, own));
        var after = days.Where(day => day.IsAvailable).ToDictionary(day => day.DayOfWeek, day => new DayInterval(day.Start!.Value, day.End!.Value));
        var changed = ChangedDays(before, after);

        // BR-10: a custom schedule with the same days, no legacy extras and no break to drop is a no-op.
        var slotById = slots.ToDictionary(slot => slot.Id);
        var dirtyStore = slots.GroupBy(slot => slot.DayOfWeek).Any(group => group.Count() > 1)
            || breaks.Any(item => !(slotById.TryGetValue(item.AvailabilityId, out var host)
                && item.StartTime > host.StartTime
                && item.EndTime < host.EndTime));

        if (!profile.UsesBranchHours && changed.Count == 0 && !dirtyStore)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new AvailabilityWriteResult(AvailabilityWriteOutcome.Saved);
        }

        var previousByDay = profile.UsesBranchHours
            ? []
            : slots.GroupBy(slot => (int)slot.DayOfWeek)
                .ToDictionary(group => group.Key, group => group.OrderBy(slot => slot.StartTime).ThenBy(slot => slot.EndTime).First());

        // The cascade removes the breaks of every deleted row.
        await dbContext.TechnicianWeeklyAvailabilities
            .Where(slot => slot.TechnicianId == technicianId)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var day in days.Where(item => item.IsAvailable))
        {
            previousByDay.TryGetValue(day.DayOfWeek, out var previous);

            var slot = TechnicianWeeklyAvailability.Create(
                technicianId, (short)day.DayOfWeek, day.Start!.Value, day.End!.Value, previous?.CapacityPercent ?? 100);

            dbContext.TechnicianWeeklyAvailabilities.Add(slot);

            if (previous is not null)
            {
                foreach (var item in breaks.Where(item => item.AvailabilityId == previous.Id
                    && item.StartTime > day.Start.Value
                    && item.EndTime < day.End.Value))
                {
                    dbContext.TechnicianBreaks.Add(TechnicianBreak.Create(slot.Id, item.StartTime, item.EndTime));
                }
            }
        }

        var previouslyInheriting = profile.UsesBranchHours;

        profile.UseCustomSchedule(now);
        dbContext.AuditLogs.Add(AvailabilityAudit(
            organizationId, profile, actorUserId, clientIp, changed, before, after, previouslyInheriting, false, "custom"));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AvailabilityWriteResult(AvailabilityWriteOutcome.Saved);
    }

    public async Task<AvailabilityWriteResult> UseBranchHoursAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null || profile.Status != TechnicianStatus.Active)
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.NotFound);
        }

        if (!Same(profile.UpdatedAt, version))
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.Conflict);
        }

        if (profile.UsesBranchHours)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new AvailabilityWriteResult(AvailabilityWriteOutcome.Saved);
        }

        var slots = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
            .Where(slot => slot.TechnicianId == technicianId)
            .ToListAsync(cancellationToken);

        var businessHours = await BranchBusinessHoursAsync(profile.BranchId, cancellationToken);
        var before = Intervals(ToSlots(slots, []));
        var after = Intervals(EffectiveWeeklyAvailability.Resolve(true, businessHours, []));

        await dbContext.TechnicianWeeklyAvailabilities
            .Where(slot => slot.TechnicianId == technicianId)
            .ExecuteDeleteAsync(cancellationToken);

        profile.UseBranchHours(now);
        dbContext.AuditLogs.Add(AvailabilityAudit(
            organizationId, profile, actorUserId, clientIp, ChangedDays(before, after), before, after, false, true, "branch_hours"));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AvailabilityWriteResult(AvailabilityWriteOutcome.Saved);
    }

    public async Task<TimeOffResult> CreateTimeOffAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        TimeOffWrite write,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null || profile.Status != TechnicianStatus.Active)
        {
            return new TimeOffResult(TimeOffOutcome.NotFound);
        }

        // BR-14: any active exception of the technician that intersects the range; serialized by the profile lock.
        if (await dbContext.TechnicianExceptions.AsNoTracking().AnyAsync(
            item => item.TechnicianId == technicianId
                && item.Status == TechnicianExceptionStatus.Active
                && item.StartsAt < write.EndsAt
                && item.EndsAt > write.StartsAt,
            cancellationToken))
        {
            return new TimeOffResult(TimeOffOutcome.Overlap);
        }

        var created = TechnicianException.Create(technicianId, write.StartsAt, write.EndsAt, false, write.Note, now);

        dbContext.TechnicianExceptions.Add(created);

        // The note never reaches the audit row.
        dbContext.AuditLogs.Add(TimeOffAudit(
            organizationId,
            SkillsAvailabilityAuditActions.TimeOffCreated,
            created.Id,
            profile,
            actorUserId,
            clientIp,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["technicianId"] = profile.Id,
                ["startDate"] = DateText(write.StartDate),
                ["endDate"] = DateText(write.EndDate),
            }));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TimeOffResult(TimeOffOutcome.Saved, ToTimeOff(created));
    }

    public async Task<TimeOffResult> RetireTimeOffAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid timeOffId,
        DateTimeOffset version,
        string zoneId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null || profile.Status != TechnicianStatus.Active)
        {
            return new TimeOffResult(TimeOffOutcome.NotFound);
        }

        var exception = await FindExceptionAsync(technicianId, timeOffId, cancellationToken);
        var zone = BranchTime.FindZone(zoneId);

        if (exception is null
            || exception.IsAvailable
            || !TimeOffRules.TryFullDayRange(exception.StartsAt, exception.EndsAt, zone, out var startDate, out var endDate))
        {
            return new TimeOffResult(TimeOffOutcome.NotFound);
        }

        var todayStart = BranchTime.Day(now, zone).Start;

        if (!exception.IsActive || exception.EndsAt <= todayStart)
        {
            return new TimeOffResult(TimeOffOutcome.Closed);
        }

        if (!Same(exception.UpdatedAt, version))
        {
            return new TimeOffResult(TimeOffOutcome.Stale);
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal) { ["technicianId"] = profile.Id };

        if (exception.StartsAt >= todayStart)
        {
            exception.Cancel(now);
            metadata["mode"] = "cancelled";
            metadata["startDate"] = DateText(startDate);
            metadata["endDate"] = DateText(endDate);
        }
        else
        {
            exception.Shorten(todayStart, now);
            metadata["mode"] = "shortened";
            metadata["startDate"] = DateText(startDate);
            metadata["endDate"] = DateText(BranchTime.LocalDate(todayStart, zone).AddDays(-1));
            metadata["previousEndDate"] = DateText(endDate);
        }

        dbContext.AuditLogs.Add(TimeOffAudit(
            organizationId, SkillsAvailabilityAuditActions.TimeOffRemoved, exception.Id, profile, actorUserId, clientIp, metadata));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            return new TimeOffResult(TimeOffOutcome.Stale);
        }

        return new TimeOffResult(TimeOffOutcome.Saved, exception.IsActive ? ToTimeOff(exception) : null);
    }

    public async Task<SkillAssignmentsData?> GetSkillAssignmentsAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken)
    {
        var row = await PageRowAsync(ActiveProfiles(organizationId, scope), technicianId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var skillIds = await (
            from assignment in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on assignment.SkillId equals skill.Id
            where assignment.TechnicianId == technicianId && skill.IsActive && skill.OrganizationId == organizationId
            orderby skill.Name, skill.Id
            select skill.Id)
            .ToListAsync(cancellationToken);

        return new SkillAssignmentsData(row.Id, row.FirstName, row.LastName, row.UpdatedAt, skillIds);
    }

    public async Task<AvailabilityWriteResult> SaveSkillAssignmentsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        IReadOnlyList<Guid> skillIds,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await ActiveProfiles(organizationId, scope).AnyAsync(profile => profile.Id == technicianId, cancellationToken))
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.NotFound);
        }

        var submitted = skillIds.ToArray();

        // Ownership and activity are validated before the lock, so an invalid last id writes nothing.
        if (submitted.Length > 0
            && await dbContext.Skills.AsNoTracking()
                .CountAsync(skill => skill.OrganizationId == organizationId && skill.IsActive && submitted.Contains(skill.Id), cancellationToken)
                != submitted.Length)
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.InvalidSkills);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null || profile.Status != TechnicianStatus.Active)
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.NotFound);
        }

        if (!Same(profile.UpdatedAt, version))
        {
            return new AvailabilityWriteResult(AvailabilityWriteOutcome.Conflict);
        }

        var assignments = await dbContext.TechnicianSkills
            .Where(item => item.TechnicianId == technicianId)
            .ToListAsync(cancellationToken);

        var relevantIds = assignments.Select(item => item.SkillId).Concat(submitted).Distinct().ToArray();
        var skills = (await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId && relevantIds.Contains(skill.Id))
            .Select(skill => new { skill.Id, skill.Name, skill.IsActive })
            .ToListAsync(cancellationToken)).ToDictionary(skill => skill.Id);

        // Archived-skill assignments are never read or changed.
        var active = assignments.Where(item => skills.TryGetValue(item.SkillId, out var skill) && skill.IsActive).ToList();
        var currentIds = active.Select(item => item.SkillId).ToHashSet();
        var added = submitted.Where(id => !currentIds.Contains(id)).ToList();
        var removed = active.Where(item => !submitted.Contains(item.SkillId)).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new AvailabilityWriteResult(AvailabilityWriteOutcome.Saved);
        }

        var previousPrimary = active.FirstOrDefault(item => item.IsPrimary);
        Guid? promoted = null;

        if (previousPrimary is not null && removed.Contains(previousPrimary) && submitted.Length > 0)
        {
            promoted = submitted
                .OrderBy(id => skills[id].Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id)
                .First();
        }

        foreach (var item in removed)
        {
            dbContext.TechnicianSkills.Remove(item);
        }

        foreach (var id in added)
        {
            dbContext.TechnicianSkills.Add(TechnicianSkill.Create(technicianId, id, NewAssignmentProficiency));
        }

        // The partial unique index allows one primary: the old one is gone before the new one is set.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (promoted is { } primaryId)
        {
            dbContext.TechnicianSkills.Local
                .Single(item => item.TechnicianId == technicianId && item.SkillId == primaryId)
                .SetPrimary(true);
        }

        profile.Touch(now);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            SkillsAvailabilityAuditActions.SkillsUpdated,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["addedSkillIds"] = added,
                ["removedSkillIds"] = removed.Select(item => item.SkillId).ToList(),
            })));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AvailabilityWriteResult(AvailabilityWriteOutcome.Saved);
    }

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string TimeText(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static TimeOffData ToTimeOff(TechnicianException exception) =>
        new(exception.Id, exception.StartsAt, exception.EndsAt, exception.Reason, exception.UpdatedAt);

    private static List<AvailabilitySlot> ToSlots(
        IReadOnlyList<TechnicianWeeklyAvailability> slots, IReadOnlyList<TechnicianBreak> breaks) =>
        [.. slots.Select(slot => new AvailabilitySlot(
            slot.DayOfWeek,
            slot.StartTime,
            slot.EndTime,
            slot.CapacityPercent,
            [.. breaks.Where(item => item.AvailabilityId == slot.Id).Select(item => new TimeRange(item.StartTime, item.EndTime))]))];

    /// <summary>The earliest-starting window of each stored weekday number that has one.</summary>
    private static Dictionary<int, DayInterval> Intervals(IEnumerable<AvailabilitySlot> slots) =>
        slots.GroupBy(slot => slot.DayOfWeek)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.OrderBy(slot => slot.Start).ThenBy(slot => slot.End).First();

                    return new DayInterval(first.Start, first.End);
                });

    private static List<int> ChangedDays(Dictionary<int, DayInterval> before, Dictionary<int, DayInterval> after) =>
        [.. AvailabilityDays.Names
            .Select(AvailabilityDays.Number)
            .Where(day => before.TryGetValue(day, out var previous) != after.TryGetValue(day, out var next)
                || (before.ContainsKey(day) && previous != next))];

    private static List<Dictionary<string, object?>> DayAudit(IEnumerable<int> changed, Dictionary<int, DayInterval> intervals) =>
        [.. changed.Select(day =>
        {
            var available = intervals.TryGetValue(day, out var interval);

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dayOfWeek"] = AvailabilityDays.Name(day),
                ["isAvailable"] = available,
                ["start"] = available ? TimeText(interval.Start) : null,
                ["end"] = available ? TimeText(interval.End) : null,
            };
        })];

    // Before/after carry only the changed days and the source flag; no personal data.
    private static AuditLog AvailabilityAudit(
        Guid organizationId,
        TechnicianProfile profile,
        Guid actorUserId,
        IPAddress? clientIp,
        List<int> changed,
        Dictionary<int, DayInterval> before,
        Dictionary<int, DayInterval> after,
        bool wasInheriting,
        bool nowInheriting,
        string source) =>
        AuditLog.Create(
            organizationId,
            SkillsAvailabilityAuditActions.AvailabilityUpdated,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            beforeData: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["usesBranchHours"] = wasInheriting,
                ["days"] = DayAudit(changed, before),
            }),
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["usesBranchHours"] = nowInheriting,
                ["days"] = DayAudit(changed, after),
            }),
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["changedDays"] = changed.Select(AvailabilityDays.Name).ToList(),
                ["source"] = source,
            }));

    private static AuditLog TimeOffAudit(
        Guid organizationId,
        string action,
        Guid exceptionId,
        TechnicianProfile profile,
        Guid actorUserId,
        IPAddress? clientIp,
        Dictionary<string, object?> metadata) =>
        AuditLog.Create(
            organizationId,
            action,
            SkillsAvailabilityAuditActions.ExceptionEntity,
            actorUserId: actorUserId,
            entityId: exceptionId,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(metadata));

    private Task<string> BranchBusinessHoursAsync(Guid branchId, CancellationToken cancellationToken) =>
        dbContext.Branches.AsNoTracking()
            .Where(branch => branch.Id == branchId)
            .Select(branch => branch.BusinessHours)
            .SingleAsync(cancellationToken);

    private async Task<List<GridProfileRow>> GridRowsAsync(
        Guid organizationId, BranchScope scope, AvailabilityGridFilter filter, CancellationToken cancellationToken)
    {
        var profiles = ActiveProfiles(organizationId, scope);

        if (filter.BranchId is { } branchId)
        {
            profiles = profiles.Where(profile => profile.BranchId == branchId);
        }

        var rows = await (
            from profile in profiles
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            select new GridProfileRow(
                profile.Id,
                profile.FirstName,
                profile.LastName,
                profile.ColorHex,
                profile.BranchId,
                branch.Name,
                branch.Timezone,
                profile.UsesBranchHours,
                branch.BusinessHours))
            .ToListAsync(cancellationToken);

        if (filter.Search is { Length: > 0 } search)
        {
            rows = [.. rows.Where(row =>
                row.FirstName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.LastName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || $"{row.FirstName} {row.LastName}".Contains(search, StringComparison.OrdinalIgnoreCase))];
        }

        return rows;
    }
}
