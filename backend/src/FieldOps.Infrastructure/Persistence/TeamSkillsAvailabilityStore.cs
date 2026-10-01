using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Skills and availability page: profile read, atomic save, exceptions and the skill catalog.</summary>
internal sealed partial class TeamStore
{
    private sealed record PageRow(
        Guid Id,
        string FirstName,
        string LastName,
        TechnicianStatus Status,
        Guid BranchId,
        string BranchName,
        string? BranchTimezone,
        DateTimeOffset UpdatedAt);

    public async Task<string?> GetZoneIdAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken)
    {
        var row = await PageRowAsync(ScopedProfiles(organizationId, scope), technicianId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        return BranchTime.ResolveZoneId(row.BranchTimezone, (await OrganizationContextAsync(organizationId, cancellationToken)).Timezone);
    }

    public async Task<SkillsAvailabilityData?> GetSkillsAvailabilityAsync(
        Guid organizationId,
        BranchScope scope,
        Guid? ownMembershipId,
        Guid technicianId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profiles = ownMembershipId is { } membershipId
            ? dbContext.TechnicianProfiles.AsNoTracking()
                .Where(profile => profile.OrganizationId == organizationId && profile.OrganizationUserId == membershipId)
            : ScopedProfiles(organizationId, scope);

        var row = await PageRowAsync(profiles, technicianId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var organizationTimezone = (await OrganizationContextAsync(organizationId, cancellationToken)).Timezone;
        var low = now.AddDays(-2);
        var high = now.AddDays(9);

        var slotRows = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
            .Where(slot => slot.TechnicianId == technicianId)
            .Select(slot => new { slot.Id, slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.CapacityPercent })
            .ToListAsync(cancellationToken);

        var slotIds = slotRows.Select(slot => slot.Id).ToArray();
        var breakRows = await dbContext.TechnicianBreaks.AsNoTracking()
            .Where(item => slotIds.Contains(item.AvailabilityId))
            .Select(item => new { item.AvailabilityId, item.StartTime, item.EndTime })
            .ToListAsync(cancellationToken);

        var breaksBySlot = breakRows
            .GroupBy(item => item.AvailabilityId)
            .ToDictionary(group => group.Key, group => group.Select(item => new TimeRange(item.StartTime, item.EndTime)).ToList());

        var skills = await (
            from technicianSkill in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on technicianSkill.SkillId equals skill.Id
            where technicianSkill.TechnicianId == technicianId && skill.IsActive && skill.OrganizationId == organizationId
            select new AssignedSkillData(skill.Id, skill.Name, technicianSkill.Proficiency, technicianSkill.IsPrimary))
            .ToListAsync(cancellationToken);

        var exceptions = await dbContext.TechnicianExceptions.AsNoTracking()
            .Where(item => item.TechnicianId == technicianId && item.EndsAt > low)
            .Select(item => new ExceptionData(
                item.Id, item.StartsAt, item.EndsAt, item.IsAvailable, item.Reason ?? string.Empty, item.Status, item.UpdatedAt))
            .ToListAsync(cancellationToken);

        // Every non-unscheduled, non-cancelled visit with an active assignment overlapping the 7-day range.
        var visits = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on assignment.VisitId equals visit.Id
            where assignment.TechnicianId == technicianId
                && assignment.UnassignedAt == null
                && visit.OrganizationId == organizationId
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart != null
                && visit.ScheduledStart < high
                && (visit.ScheduledEnd ?? visit.ScheduledStart) > low
            select new AssignedVisit(visit.Status, visit.ScheduledStart, visit.ScheduledEnd, null))
            .ToListAsync(cancellationToken);

        return new SkillsAvailabilityData(
            row.Id,
            row.FirstName,
            row.LastName,
            row.Status,
            new TeamOption(row.BranchId, row.BranchName),
            row.UpdatedAt,
            BranchTime.ResolveZoneId(row.BranchTimezone, organizationTimezone),
            [.. slotRows.Select(slot => new AvailabilitySlot(
                slot.DayOfWeek,
                slot.StartTime,
                slot.EndTime,
                slot.CapacityPercent,
                breaksBySlot.TryGetValue(slot.Id, out var breaks) ? breaks : []))],
            skills,
            exceptions,
            visits);
    }

    public async Task<SkillsSaveResult> SaveSkillsAvailabilityAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        SkillsAvailabilityValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await ScopedProfiles(organizationId, scope).AnyAsync(profile => profile.Id == technicianId, cancellationToken))
        {
            return new SkillsSaveResult(SkillsSaveOutcome.NotFound);
        }

        // Ownership and activity are validated before any write, so an invalid last item writes nothing.
        var submittedIds = values.Skills.Select(skill => skill.SkillId).ToArray();

        if (submittedIds.Length > 0
            && await dbContext.Skills.AsNoTracking()
                .CountAsync(skill => skill.OrganizationId == organizationId && skill.IsActive && submittedIds.Contains(skill.Id), cancellationToken)
                != submittedIds.Length)
        {
            return new SkillsSaveResult(SkillsSaveOutcome.InvalidSkills);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null)
        {
            return new SkillsSaveResult(SkillsSaveOutcome.NotFound);
        }

        if (!Same(profile.UpdatedAt, version))
        {
            return new SkillsSaveResult(SkillsSaveOutcome.Conflict);
        }

        var slots = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
            .Where(slot => slot.TechnicianId == technicianId)
            .ToListAsync(cancellationToken);

        var slotIds = slots.Select(slot => slot.Id).ToArray();
        var breaks = await dbContext.TechnicianBreaks.AsNoTracking()
            .Where(item => slotIds.Contains(item.AvailabilityId))
            .ToListAsync(cancellationToken);

        var slotsByDay = slots.GroupBy(slot => (int)slot.DayOfWeek).ToDictionary(group => group.Key, group => group.OrderBy(slot => slot.StartTime).First());

        // BR-25: legacy extras make the save a change even when the visible (normalized) rows match.
        var weeklyChanged = slots.Count != values.Weekly.Count
            || breaks.GroupBy(item => item.AvailabilityId).Any(group => group.Count() > 1)
            || values.Weekly.Any(day =>
            {
                if (!slotsByDay.TryGetValue(day.DayOfWeek, out var slot)
                    || slot.StartTime != day.Start
                    || slot.EndTime != day.End)
                {
                    return true;
                }

                var existing = breaks.FirstOrDefault(item => item.AvailabilityId == slot.Id);

                return day.Break is { } submitted
                    ? existing is null || existing.StartTime != submitted.Start || existing.EndTime != submitted.End
                    : existing is not null;
            });

        var assignments = await dbContext.TechnicianSkills
            .Where(item => item.TechnicianId == technicianId)
            .ToListAsync(cancellationToken);

        var assignedIds = assignments.Select(item => item.SkillId).ToArray();
        var activeIds = (await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId && skill.IsActive && assignedIds.Contains(skill.Id))
            .Select(skill => skill.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var active = assignments.Where(item => activeIds.Contains(item.SkillId)).ToList();
        var submittedBySkill = values.Skills.ToDictionary(skill => skill.SkillId);

        var skillsChanged = active.Count != submittedBySkill.Count
            || active.Any(item => !submittedBySkill.TryGetValue(item.SkillId, out var submitted)
                || item.Proficiency != submitted.Proficiency
                || item.IsPrimary != submitted.IsPrimary);

        if (!weeklyChanged && !skillsChanged)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new SkillsSaveResult(SkillsSaveOutcome.Saved);
        }

        var changedSections = new List<string>();

        if (weeklyChanged)
        {
            changedSections.Add("availability");

            // The cascade removes the breaks; capacity of a day that stays On is kept (BR-07).
            await dbContext.TechnicianWeeklyAvailabilities
                .Where(slot => slot.TechnicianId == technicianId)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var day in values.Weekly)
            {
                var capacity = slotsByDay.TryGetValue(day.DayOfWeek, out var previous) ? previous.CapacityPercent : (short)100;
                var slot = TechnicianWeeklyAvailability.Create(technicianId, (short)day.DayOfWeek, day.Start, day.End, capacity);

                dbContext.TechnicianWeeklyAvailabilities.Add(slot);

                if (day.Break is { } range)
                {
                    dbContext.TechnicianBreaks.Add(TechnicianBreak.Create(slot.Id, range.Start, range.End));
                }
            }
        }

        if (skillsChanged)
        {
            changedSections.Add("skills");

            var hasPrimary = values.Skills.Any(skill => skill.IsPrimary);

            foreach (var item in active.Where(item => !submittedBySkill.ContainsKey(item.SkillId)))
            {
                dbContext.TechnicianSkills.Remove(item);
            }

            // years_experience stays untouched on kept rows; inactive assignments only lose their primary flag.
            foreach (var item in assignments.Where(item => submittedBySkill.ContainsKey(item.SkillId) || (hasPrimary && item.IsPrimary)))
            {
                if (submittedBySkill.TryGetValue(item.SkillId, out var submitted))
                {
                    item.SetProficiency(submitted.Proficiency);
                }

                item.SetPrimary(false);
            }

            var added = new List<TechnicianSkill>();

            foreach (var skill in values.Skills.Where(skill => active.All(item => item.SkillId != skill.SkillId)))
            {
                var assignment = TechnicianSkill.Create(technicianId, skill.SkillId, skill.Proficiency);

                dbContext.TechnicianSkills.Add(assignment);
                added.Add(assignment);
            }

            // The partial unique index allows one primary: clear the old one first, then set the new one.
            await dbContext.SaveChangesAsync(cancellationToken);

            if (values.Skills.FirstOrDefault(skill => skill.IsPrimary) is { } primary)
            {
                dbContext.TechnicianSkills.Local.Single(item => item.TechnicianId == technicianId && item.SkillId == primary.SkillId)
                    .SetPrimary(true);
            }
        }

        profile.Touch(now);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            SkillsAvailabilityAuditActions.ProfileUpdated,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["changedSections"] = changedSections,
            })));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SkillsSaveResult(SkillsSaveOutcome.Saved);
    }

    public async Task<ExceptionResult> CreateExceptionAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        ExceptionWrite write,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null)
        {
            return new ExceptionResult(ExceptionOutcome.NotFound);
        }

        if (await DateTakenAsync(technicianId, write.DayStart, write.DayEnd, null, cancellationToken))
        {
            return new ExceptionResult(ExceptionOutcome.DateTaken);
        }

        var created = TechnicianException.Create(
            technicianId, write.StartsAt, write.EndsAt, write.IsAvailable, write.Reason, now);

        dbContext.TechnicianExceptions.Add(created);
        dbContext.AuditLogs.Add(ExceptionAudit(
            organizationId, SkillsAvailabilityAuditActions.ExceptionCreated, created.Id, profile, actorUserId, clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExceptionResult(ExceptionOutcome.Saved, ToData(created));
    }

    public async Task<ExceptionResult> UpdateExceptionAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid exceptionId,
        DateTimeOffset version,
        ExceptionWrite write,
        string zoneId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null)
        {
            return new ExceptionResult(ExceptionOutcome.NotFound);
        }

        var exception = await FindExceptionAsync(technicianId, exceptionId, cancellationToken);

        if (exception is null)
        {
            return new ExceptionResult(ExceptionOutcome.NotFound);
        }

        var zone = BranchTime.FindZone(zoneId);

        if (exception.StartsAt < BranchTime.Day(now, zone).Start)
        {
            return new ExceptionResult(ExceptionOutcome.Past);
        }

        if (!exception.IsActive)
        {
            return new ExceptionResult(ExceptionOutcome.Cancelled);
        }

        if (!Same(exception.UpdatedAt, version))
        {
            return new ExceptionResult(ExceptionOutcome.Stale);
        }

        if (await DateTakenAsync(technicianId, write.DayStart, write.DayEnd, exception.Id, cancellationToken))
        {
            return new ExceptionResult(ExceptionOutcome.DateTaken);
        }

        if (!exception.Update(write.StartsAt, write.EndsAt, write.IsAvailable, write.Reason, now))
        {
            await transaction.RollbackAsync(cancellationToken);

            return new ExceptionResult(ExceptionOutcome.Saved, ToData(exception));
        }

        dbContext.AuditLogs.Add(ExceptionAudit(
            organizationId, SkillsAvailabilityAuditActions.ExceptionUpdated, exception.Id, profile, actorUserId, clientIp));

        return await CommitExceptionAsync(transaction, exception, cancellationToken);
    }

    public async Task<ExceptionResult> SetExceptionActiveAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid exceptionId,
        DateTimeOffset version,
        bool activate,
        string zoneId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var profile = await LockProfileAsync(organizationId, scope, technicianId, cancellationToken);

        if (profile is null)
        {
            return new ExceptionResult(ExceptionOutcome.NotFound);
        }

        var exception = await FindExceptionAsync(technicianId, exceptionId, cancellationToken);

        if (exception is null)
        {
            return new ExceptionResult(ExceptionOutcome.NotFound);
        }

        var zone = BranchTime.FindZone(zoneId);

        if (exception.StartsAt < BranchTime.Day(now, zone).Start)
        {
            return new ExceptionResult(ExceptionOutcome.Past);
        }

        // The version is checked first, so a stale same-status call is a conflict, not a silent no-op.
        if (!Same(exception.UpdatedAt, version))
        {
            return new ExceptionResult(ExceptionOutcome.Stale);
        }

        if (exception.IsActive == activate)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new ExceptionResult(ExceptionOutcome.Saved, ToData(exception));
        }

        if (activate)
        {
            var date = BranchTime.LocalDate(exception.StartsAt, zone);
            var (dayStart, dayEnd) = (
                BranchTime.ToInstant(date, TimeOnly.MinValue, zone), BranchTime.ToInstant(date.AddDays(1), TimeOnly.MinValue, zone));

            if (await DateTakenAsync(technicianId, dayStart, dayEnd, exception.Id, cancellationToken))
            {
                return new ExceptionResult(ExceptionOutcome.DateTaken);
            }

            exception.Activate(now);
        }
        else
        {
            exception.Cancel(now);
        }

        dbContext.AuditLogs.Add(ExceptionAudit(
            organizationId,
            activate ? SkillsAvailabilityAuditActions.ExceptionActivated : SkillsAvailabilityAuditActions.ExceptionCancelled,
            exception.Id,
            profile,
            actorUserId,
            clientIp));

        return await CommitExceptionAsync(transaction, exception, cancellationToken);
    }

    public async Task<IReadOnlyList<SkillView>> ListSkillsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId)
            .OrderBy(skill => skill.Name).ThenBy(skill => skill.Id)
            .Select(skill => new SkillView(skill.Id, skill.Name, skill.Description, skill.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<SkillResult> CreateSkillAsync(
        Guid organizationId, SkillValues values, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        if (await NameTakenAsync(organizationId, values.Name, null, cancellationToken))
        {
            return new SkillResult(SkillOutcome.Duplicate);
        }

        var skill = Skill.Create(organizationId, values.Name, values.Description);

        dbContext.Skills.Add(skill);
        dbContext.AuditLogs.Add(SkillAudit(organizationId, SkillsAvailabilityAuditActions.SkillCreated, skill.Id, actorUserId, clientIp));

        return await SaveSkillAsync(skill, cancellationToken);
    }

    public async Task<SkillResult> UpdateSkillAsync(
        Guid organizationId, Guid skillId, SkillValues values, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var skill = await dbContext.Skills
            .SingleOrDefaultAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == skillId, cancellationToken);

        if (skill is null)
        {
            return new SkillResult(SkillOutcome.NotFound);
        }

        if (await NameTakenAsync(organizationId, values.Name, skill.Id, cancellationToken))
        {
            return new SkillResult(SkillOutcome.Duplicate);
        }

        if (!skill.Update(values.Name, values.Description))
        {
            return new SkillResult(SkillOutcome.Saved, ToView(skill));
        }

        dbContext.AuditLogs.Add(SkillAudit(organizationId, SkillsAvailabilityAuditActions.SkillUpdated, skill.Id, actorUserId, clientIp));

        return await SaveSkillAsync(skill, cancellationToken);
    }

    public async Task<SkillResult> SetSkillActiveAsync(
        Guid organizationId, Guid skillId, bool active, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var skill = await dbContext.Skills
            .SingleOrDefaultAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == skillId, cancellationToken);

        if (skill is null)
        {
            return new SkillResult(SkillOutcome.NotFound);
        }

        if (!skill.SetActive(active))
        {
            return new SkillResult(SkillOutcome.Saved, ToView(skill));
        }

        dbContext.AuditLogs.Add(SkillAudit(
            organizationId,
            active ? SkillsAvailabilityAuditActions.SkillActivated : SkillsAvailabilityAuditActions.SkillDeactivated,
            skill.Id,
            actorUserId,
            clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SkillResult(SkillOutcome.Saved, ToView(skill));
    }

    private async Task<PageRow?> PageRowAsync(
        IQueryable<TechnicianProfile> profiles, Guid technicianId, CancellationToken cancellationToken) =>
        await (
            from profile in profiles
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where profile.Id == technicianId
            select new PageRow(
                profile.Id,
                profile.FirstName,
                profile.LastName,
                profile.Status,
                profile.BranchId,
                branch.Name,
                branch.Timezone,
                profile.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Serializes writers of one profile: the in-scope profile is loaded, then locked and reloaded inside the
    /// caller's transaction. The lock also blocks the key-share lock a concurrent child insert takes.
    /// </summary>
    private async Task<TechnicianProfile?> LockProfileAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken)
    {
        var profile = await ScopedTrackedProfiles(organizationId, scope)
            .Where(candidate => candidate.Id == technicianId)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM technician_profiles
                WHERE id = {technicianId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        await dbContext.Entry(profile).ReloadAsync(cancellationToken);

        return profile;
    }

    private Task<TechnicianException?> FindExceptionAsync(Guid technicianId, Guid exceptionId, CancellationToken cancellationToken) =>
        dbContext.TechnicianExceptions
            .SingleOrDefaultAsync(item => item.Id == exceptionId && item.TechnicianId == technicianId, cancellationToken);

    /// <summary>BR-14: another active exception of the technician starting on the same local day.</summary>
    private Task<bool> DateTakenAsync(
        Guid technicianId, DateTimeOffset dayStart, DateTimeOffset dayEnd, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.TechnicianExceptions.AsNoTracking()
            .AnyAsync(
                item => item.TechnicianId == technicianId
                    && item.Status == TechnicianExceptionStatus.Active
                    && item.StartsAt >= dayStart
                    && item.StartsAt < dayEnd
                    && (excludeId == null || item.Id != excludeId),
                cancellationToken);

    private async Task<ExceptionResult> CommitExceptionAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        TechnicianException exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            return new ExceptionResult(ExceptionOutcome.Stale);
        }

        return new ExceptionResult(ExceptionOutcome.Saved, ToData(exception));
    }

    private async Task<SkillResult> SaveSkillAsync(Skill skill, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConstraintName(ex) == SkillConfiguration.NormalizedNameIndexName)
        {
            dbContext.ChangeTracker.Clear();

            return new SkillResult(SkillOutcome.Duplicate);
        }

        return new SkillResult(SkillOutcome.Saved, ToView(skill));
    }

    private Task<bool> NameTakenAsync(Guid organizationId, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();

        return dbContext.Skills.AsNoTracking()
            .AnyAsync(
                skill => skill.OrganizationId == organizationId
                    && skill.Name.ToLower() == lowered
                    && (excludeId == null || skill.Id != excludeId),
                cancellationToken);
    }

    /// <summary>Tokens compare at the microsecond precision PostgreSQL stores.</summary>
    private static bool Same(DateTimeOffset stored, DateTimeOffset requested) =>
        SkillsAvailabilityRules.TruncateToMicroseconds(stored) == SkillsAvailabilityRules.TruncateToMicroseconds(requested);

    private static ExceptionData ToData(TechnicianException exception) =>
        new(
            exception.Id,
            exception.StartsAt,
            exception.EndsAt,
            exception.IsAvailable,
            exception.Reason ?? string.Empty,
            exception.Status,
            exception.UpdatedAt);

    private static SkillView ToView(Skill skill) => new(skill.Id, skill.Name, skill.Description, skill.IsActive);

    // The reason never reaches the audit row (BR-23).
    private static AuditLog ExceptionAudit(
        Guid organizationId, string action, Guid exceptionId, TechnicianProfile profile, Guid actorUserId, IPAddress? clientIp) =>
        AuditLog.Create(
            organizationId,
            action,
            SkillsAvailabilityAuditActions.ExceptionEntity,
            actorUserId: actorUserId,
            entityId: exceptionId,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["technicianId"] = profile.Id,
            }));

    private static AuditLog SkillAudit(Guid organizationId, string action, Guid skillId, Guid actorUserId, IPAddress? clientIp) =>
        AuditLog.Create(
            organizationId,
            action,
            SkillsAvailabilityAuditActions.SkillEntity,
            actorUserId: actorUserId,
            entityId: skillId,
            ipAddress: clientIp);
}
