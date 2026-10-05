using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class TeamStore(FieldOpsDbContext dbContext) : ITeamStore
{
    private static readonly string[] LinkableRoles = ["technician", "dispatcher", "operations_manager"];

    private static readonly VisitStatus[] UpcomingStatuses =
        [VisitStatus.Scheduled, VisitStatus.Assigned, VisitStatus.OnTheWay, VisitStatus.InProgress, VisitStatus.Paused];

    private sealed record ProfileRow(
        Guid Id,
        Guid BranchId,
        string BranchName,
        string? BranchTimezone,
        string FirstName,
        string LastName,
        string? Email,
        string? Phone,
        string? EmployeeCode,
        string? Notes,
        TechnicianStatus Status,
        Guid? OrganizationUserId);

    public async Task<TeamOptions> GetOptionsAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken)
    {
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
        }

        return new TeamOptions(
            await branches.OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
                .Select(branch => new TeamOption(branch.Id, branch.Name)).ToListAsync(cancellationToken),
            await dbContext.Skills.AsNoTracking()
                .Where(skill => skill.OrganizationId == organizationId && skill.IsActive)
                .OrderBy(skill => skill.Name).ThenBy(skill => skill.Id)
                .Select(skill => new TeamOption(skill.Id, skill.Name)).ToListAsync(cancellationToken));
    }

    public Task<bool> IsBranchAllowedAsync(
        Guid organizationId, BranchScope scope, Guid branchId, bool requireActive, CancellationToken cancellationToken)
    {
        var query = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.Id == branchId);

        if (requireActive)
        {
            query = query.Where(branch => branch.IsActive);
        }

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(branch => ids.Contains(branch.Id));
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> SkillExistsAsync(Guid organizationId, Guid skillId, CancellationToken cancellationToken) =>
        dbContext.Skills.AsNoTracking()
            .AnyAsync(skill => skill.OrganizationId == organizationId && skill.Id == skillId, cancellationToken);

    public async Task<Guid?> GetBranchIdAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken)
    {
        var ids = await ScopedProfiles(organizationId, scope)
            .Where(profile => profile.Id == technicianId)
            .Select(profile => (Guid?)profile.BranchId)
            .ToListAsync(cancellationToken);

        return ids.SingleOrDefault();
    }

    public async Task<IReadOnlyList<TechnicianFacts>> ListFactsAsync(
        Guid organizationId,
        BranchScope scope,
        TeamFactsFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = ScopedProfiles(organizationId, scope);

        if (filter.BranchId is { } branchId)
        {
            query = query.Where(profile => profile.BranchId == branchId);
        }

        if (filter.ProfileStatus is { } status)
        {
            query = query.Where(profile => profile.Status == status);
        }

        if (filter.SkillId is { } skillId)
        {
            query = query.Where(profile => dbContext.TechnicianSkills
                .Any(skill => skill.TechnicianId == profile.Id && skill.SkillId == skillId));
        }

        query = filter.AccountLink switch
        {
            TeamAccountFilter.Linked => query.Where(profile => profile.OrganizationUserId != null),
            TeamAccountFilter.NotLinked => query.Where(profile => profile.OrganizationUserId == null),
            _ => query,
        };

        var rows = await Rows(query).ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            rows = [.. rows.Where(row => TeamSearch.Matches(
                filter.Search, row.FirstName, row.LastName, row.Email, row.EmployeeCode, row.Phone))];
        }

        var context = await OrganizationContextAsync(organizationId, cancellationToken);
        var schedules = await LoadSchedulesAsync(organizationId, rows, context, now, cancellationToken);
        var skills = await LoadSkillsAsync([.. rows.Select(row => row.Id)], cancellationToken);

        return [.. rows.Select(row => new TechnicianFacts(
            row.Id,
            row.BranchId,
            row.BranchName,
            row.FirstName,
            row.LastName,
            row.OrganizationUserId is not null,
            skills.TryGetValue(row.Id, out var names) ? [.. names.Select(skill => skill.Name)] : [],
            schedules[row.Id]))];
    }

    public Task<int> CountActiveAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken)
    {
        var query = ScopedProfiles(organizationId, scope).Where(profile => profile.Status == TechnicianStatus.Active);

        if (branchId is { } branch)
        {
            query = query.Where(profile => profile.BranchId == branch);
        }

        return query.CountAsync(cancellationToken);
    }

    public async Task<TechnicianProfileData?> GetProfileAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await Rows(ScopedProfiles(organizationId, scope).Where(profile => profile.Id == technicianId))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : await BuildProfileAsync(organizationId, row, now, cancellationToken);
    }

    public async Task<TechnicianProfileData?> GetOwnProfileAsync(
        Guid organizationId, Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await Rows(dbContext.TechnicianProfiles.AsNoTracking()
                .Where(profile => profile.OrganizationId == organizationId && profile.OrganizationUserId == membershipId))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : await BuildProfileAsync(organizationId, row, now, cancellationToken);
    }

    public async Task<IReadOnlyList<SkillCoverageRow>> GetSkillCoverageAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken)
    {
        var profiles = ScopedProfiles(organizationId, scope).Where(profile => profile.Status == TechnicianStatus.Active);

        if (branchId is { } branch)
        {
            profiles = profiles.Where(profile => profile.BranchId == branch);
        }

        var counts = await (
            from technicianSkill in dbContext.TechnicianSkills.AsNoTracking()
            join profile in profiles on technicianSkill.TechnicianId equals profile.Id
            group technicianSkill by technicianSkill.SkillId into grouped
            select new { SkillId = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(item => item.SkillId, item => item.Count, cancellationToken);

        var skills = await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId && skill.IsActive)
            .Select(skill => new { skill.Id, skill.Name })
            .ToListAsync(cancellationToken);

        return [.. skills.Select(skill => new SkillCoverageRow(
            skill.Id, skill.Name, counts.TryGetValue(skill.Id, out var count) ? count : 0))];
    }

    public async Task<IReadOnlyList<LinkableAccount>> ListLinkableAccountsAsync(
        Guid organizationId, string? search, CancellationToken cancellationToken)
    {
        var query =
            from member in dbContext.OrganizationUsers.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
            join user in dbContext.Users.AsNoTracking() on member.UserId equals user.Id
            where member.OrganizationId == organizationId
                && member.Status == UserStatus.Active
                && LinkableRoles.Contains(role.Code)
                && !dbContext.TechnicianProfiles.Any(profile => profile.OrganizationUserId == member.Id)
            select new { member.Id, user.FirstName, user.LastName, user.Email, RoleName = role.Name };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(item => (item.FirstName + " " + item.LastName).ToLower().Contains(term)
                || item.Email.ToLower().Contains(term));
        }

        var rows = await query
            .OrderBy(item => item.FirstName).ThenBy(item => item.LastName).ThenBy(item => item.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(item => new LinkableAccount(
            item.Id, $"{item.FirstName} {item.LastName}", item.Email, item.RoleName))];
    }

    public async Task<TeamSaveResult> CreateAsync(
        Guid organizationId,
        Guid branchId,
        TeamProfileValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var duplicates = await FindDuplicatesAsync(organizationId, null, values, cancellationToken);

        if (duplicates is not null)
        {
            return new TeamSaveResult(TeamSaveOutcome.Duplicate, Errors: duplicates);
        }

        var profile = TechnicianProfile.Create(
            organizationId, branchId, values.FirstName, values.LastName, values.Email, values.Phone, values.EmployeeCode, values.Notes);

        dbContext.TechnicianProfiles.Add(profile);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            TeamAuditActions.Created,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: branchId,
            ipAddress: clientIp,
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal) { ["branchId"] = branchId })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DuplicateErrors(ex) is { } errors)
        {
            dbContext.ChangeTracker.Clear();

            return new TeamSaveResult(TeamSaveOutcome.Duplicate, Errors: errors);
        }

        return new TeamSaveResult(TeamSaveOutcome.Saved, profile.Id);
    }

    public async Task<TeamSaveResult> UpdateAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid branchId,
        TeamProfileValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profile = await ScopedTrackedProfiles(organizationId, scope)
            .Where(candidate => candidate.Id == technicianId)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return new TeamSaveResult(TeamSaveOutcome.NotFound);
        }

        var changed = new List<string>();

        void Diff(string name, object? before, object? after)
        {
            if (!Equals(before, after))
            {
                changed.Add(name);
            }
        }

        Diff("firstName", profile.FirstName, values.FirstName);
        Diff("lastName", profile.LastName, values.LastName);
        Diff("email", profile.Email, values.Email);
        Diff("phone", profile.Phone, values.Phone);
        Diff("employeeCode", profile.EmployeeCode, values.EmployeeCode);
        Diff("notes", profile.Notes, values.Notes);
        Diff("branchId", profile.BranchId, branchId);

        if (changed.Count == 0)
        {
            return new TeamSaveResult(TeamSaveOutcome.Saved, profile.Id);
        }

        var duplicates = await FindDuplicatesAsync(organizationId, technicianId, values, cancellationToken);

        if (duplicates is not null)
        {
            return new TeamSaveResult(TeamSaveOutcome.Duplicate, Errors: duplicates);
        }

        profile.UpdateDetails(
            branchId, values.FirstName, values.LastName, values.Email, values.Phone, values.EmployeeCode, values.Notes, now);

        // Field names only: email, phone and notes never reach the audit row.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            TeamAuditActions.Updated,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["changedFields"] = changed,
            })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DuplicateErrors(ex) is { } errors)
        {
            dbContext.ChangeTracker.Clear();

            return new TeamSaveResult(TeamSaveOutcome.Duplicate, Errors: errors);
        }

        return new TeamSaveResult(TeamSaveOutcome.Saved, profile.Id);
    }

    public async Task<TeamStatusResult> SetStatusAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        bool activate,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profile = await ScopedTrackedProfiles(organizationId, scope)
            .Where(candidate => candidate.Id == technicianId)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return new TeamStatusResult(TeamStatusOutcome.NotFound);
        }

        var target = activate ? TechnicianStatus.Active : TechnicianStatus.Inactive;

        if (profile.Status == target)
        {
            return new TeamStatusResult(TeamStatusOutcome.NoChange);
        }

        if (activate)
        {
            return await ApplyStatusAsync(profile, target, organizationId, actorUserId, clientIp, now, cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The row lock conflicts with the FOR KEY SHARE lock a new visit assignment takes through its
        // foreign key, so the guard below and the status change cannot interleave with an assignment.
        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM technician_profiles
                WHERE id = {technicianId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        await dbContext.Entry(profile).ReloadAsync(cancellationToken);

        if (profile.Status == TechnicianStatus.Inactive)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new TeamStatusResult(TeamStatusOutcome.NoChange);
        }

        var upcoming = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on assignment.VisitId equals visit.Id
            where assignment.TechnicianId == technicianId
                && assignment.UnassignedAt == null
                && visit.OrganizationId == organizationId
                && UpcomingStatuses.Contains(visit.Status)
                && (visit.ScheduledEnd == null || visit.ScheduledEnd > now)
            select visit.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        if (upcoming > 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new TeamStatusResult(TeamStatusOutcome.HasUpcomingVisits, upcoming);
        }

        var result = await ApplyStatusAsync(profile, target, organizationId, actorUserId, clientIp, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public async Task<TeamLinkOutcome> LinkAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid organizationUserId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profile = await ScopedTrackedProfiles(organizationId, scope)
            .Where(candidate => candidate.Id == technicianId)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return TeamLinkOutcome.NotFound;
        }

        if (profile.OrganizationUserId is not null)
        {
            return TeamLinkOutcome.Rejected;
        }

        // A foreign membership is indistinguishable from an ineligible one.
        var eligible = await (
            from member in dbContext.OrganizationUsers.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
            where member.Id == organizationUserId
                && member.OrganizationId == organizationId
                && member.Status == UserStatus.Active
                && LinkableRoles.Contains(role.Code)
                && !dbContext.TechnicianProfiles.Any(other => other.OrganizationUserId == member.Id)
            select member.Id)
            .AnyAsync(cancellationToken);

        if (!eligible)
        {
            return TeamLinkOutcome.Rejected;
        }

        profile.LinkMembership(organizationUserId, now);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            TeamAuditActions.AccountLinked,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["organizationUserId"] = organizationUserId,
            })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConstraintName(ex) == TechnicianProfileConfiguration.OrgUserIndexName)
        {
            dbContext.ChangeTracker.Clear();

            return TeamLinkOutcome.Rejected;
        }

        return TeamLinkOutcome.Changed;
    }

    public async Task<TeamLinkOutcome> UnlinkAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profile = await ScopedTrackedProfiles(organizationId, scope)
            .Where(candidate => candidate.Id == technicianId)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return TeamLinkOutcome.NotFound;
        }

        var previous = profile.OrganizationUserId;

        if (!profile.UnlinkMembership(now))
        {
            return TeamLinkOutcome.NoChange;
        }

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            TeamAuditActions.AccountUnlinked,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["organizationUserId"] = previous,
            })));

        await dbContext.SaveChangesAsync(cancellationToken);

        return TeamLinkOutcome.Changed;
    }

    private async Task<TeamStatusResult> ApplyStatusAsync(
        TechnicianProfile profile,
        TechnicianStatus target,
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var before = profile.Status;

        profile.ChangeStatus(target, now);

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            target == TechnicianStatus.Active ? TeamAuditActions.Activated : TeamAuditActions.Deactivated,
            TeamAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: profile.Id,
            branchId: profile.BranchId,
            ipAddress: clientIp,
            beforeData: StatusJson(before),
            afterData: StatusJson(target)));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new TeamStatusResult(TeamStatusOutcome.Changed);
    }

    private static string StatusJson(TechnicianStatus status) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = status.ToString().ToLowerInvariant(),
        });

    private IQueryable<TechnicianProfile> ScopedProfiles(Guid organizationId, BranchScope scope) =>
        ScopedTrackedProfiles(organizationId, scope).AsNoTracking();

    private IQueryable<TechnicianProfile> ScopedTrackedProfiles(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.TechnicianProfiles.Where(profile => profile.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(profile => ids.Contains(profile.BranchId));
        }

        return query;
    }

    private IQueryable<ProfileRow> Rows(IQueryable<TechnicianProfile> profiles) =>
        from profile in profiles
        join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
        select new ProfileRow(
            profile.Id,
            profile.BranchId,
            branch.Name,
            branch.Timezone,
            profile.FirstName,
            profile.LastName,
            profile.Email,
            profile.Phone,
            profile.EmployeeCode,
            profile.Notes,
            profile.Status,
            profile.OrganizationUserId);

    private async Task<(string? Timezone, string Prefix)> OrganizationContextAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new { organization.Timezone, organization.WorkOrderPrefix })
            .SingleAsync(cancellationToken);

        return (row.Timezone, row.WorkOrderPrefix);
    }

    private async Task<Dictionary<Guid, List<(string Name, short? Proficiency, bool IsPrimary)>>> LoadSkillsAsync(
        Guid[] ids, CancellationToken cancellationToken)
    {
        var rows = await (
            from technicianSkill in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on technicianSkill.SkillId equals skill.Id
            where ids.Contains(technicianSkill.TechnicianId) && skill.IsActive
            select new
            {
                technicianSkill.TechnicianId,
                skill.Name,
                technicianSkill.Proficiency,
                technicianSkill.IsPrimary,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.TechnicianId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.IsPrimary)
                    .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(row => (row.Name, row.Proficiency, row.IsPrimary))
                    .ToList());
    }

    private Task<Dictionary<Guid, TechnicianSchedule>> LoadSchedulesAsync(
        Guid organizationId,
        IReadOnlyList<ProfileRow> rows,
        (string? Timezone, string Prefix) context,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [.. rows.Select(row => new ScheduleSubject(row.Id, row.Status, row.BranchTimezone))],
            context.Timezone,
            context.Prefix,
            now.AddDays(-9),
            now.AddDays(9),
            onlyActiveExceptions: false,
            cancellationToken);

    private async Task<TechnicianProfileData> BuildProfileAsync(
        Guid organizationId, ProfileRow row, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var context = await OrganizationContextAsync(organizationId, cancellationToken);
        var schedule = (await LoadSchedulesAsync(organizationId, [row], context, now, cancellationToken))[row.Id];
        var skills = await LoadSkillsAsync([row.Id], cancellationToken);

        TechnicianAccountView? account = null;

        if (row.OrganizationUserId is { } membershipId)
        {
            account = await (
                from member in dbContext.OrganizationUsers.AsNoTracking()
                join user in dbContext.Users.AsNoTracking() on member.UserId equals user.Id
                join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
                where member.Id == membershipId && member.OrganizationId == organizationId
                select new TechnicianAccountView(user.Email, role.Name))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var availability = schedule.Slots
            .GroupBy(slot => slot.DayOfWeek)
            .OrderBy(group => (group.Key + 6) % 7)
            .Select(group => new AvailabilityDayView(
                group.Key,
                [.. group.OrderBy(slot => slot.Start)
                    .Select(slot => new AvailabilityWindowView(slot.Start.ToString("HH:mm"), slot.End.ToString("HH:mm")))]))
            .ToList();

        return new TechnicianProfileData(
            row.Id,
            row.FirstName,
            row.LastName,
            row.Email,
            row.Phone,
            row.EmployeeCode,
            row.Notes,
            new TeamOption(row.BranchId, row.BranchName),
            row.Status,
            account,
            skills.TryGetValue(row.Id, out var list)
                ? [.. list.Select(skill => new TechnicianSkillView(skill.Name, skill.Proficiency, skill.IsPrimary))]
                : [],
            availability,
            schedule);
    }

    private async Task<Dictionary<string, string[]>?> FindDuplicatesAsync(
        Guid organizationId, Guid? excludeId, TeamProfileValues values, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var others = dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId && (excludeId == null || profile.Id != excludeId));

        if (values.Email is { } email
            && await others.AnyAsync(profile => profile.Email != null && profile.Email.ToLower() == email, cancellationToken))
        {
            errors[TeamFieldKeys.Email] = [TeamMessages.EmailInUse];
        }

        if (values.EmployeeCode is { } code
            && await others.AnyAsync(profile => profile.EmployeeCode != null && profile.EmployeeCode.ToUpper() == code, cancellationToken))
        {
            errors[TeamFieldKeys.EmployeeCode] = [TeamMessages.EmployeeCodeInUse];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static string? ConstraintName(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName
            : null;

    private static Dictionary<string, string[]>? DuplicateErrors(DbUpdateException ex) =>
        ConstraintName(ex) switch
        {
            TechnicianProfileConfiguration.OrgEmailIndexName => new(StringComparer.Ordinal)
            {
                [TeamFieldKeys.Email] = [TeamMessages.EmailInUse],
            },
            TechnicianProfileConfiguration.EmployeeCodeIndexName => new(StringComparer.Ordinal)
            {
                [TeamFieldKeys.EmployeeCode] = [TeamMessages.EmployeeCodeInUse],
            },
            _ => null,
        };
}
