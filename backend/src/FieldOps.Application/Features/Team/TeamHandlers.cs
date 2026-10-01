using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Domain.Technicians;

namespace FieldOps.Application.Features.Team;

internal static class TeamMapping
{
    public static string ProfileStatusText(TechnicianStatus status) =>
        status switch
        {
            TechnicianStatus.Inactive => "inactive",
            TechnicianStatus.Suspended => "suspended",
            _ => "active",
        };

    public static TechnicianDetail ToDetail(
        TechnicianProfileData data, TeamPeriod period, DateTimeOffset now, bool includeNotes)
    {
        var derivation = TechnicianAvailabilityCalculator.Derive(data.Schedule, period, now);

        return new TechnicianDetail(
            data.Id,
            data.FirstName,
            data.LastName,
            data.Email,
            data.Phone,
            data.EmployeeCode,
            includeNotes ? data.Notes : null,
            data.Branch,
            ProfileStatusText(data.Status),
            data.Account,
            data.Skills,
            data.Availability,
            new TechnicianTodayView(
                derivation.TodayStatus,
                derivation.CurrentJobLabel is null ? null : new CurrentJobView(derivation.CurrentJobLabel),
                derivation.Workload.Jobs,
                derivation.Workload.Percent,
                derivation.Workload.State,
                derivation.NextAvailable),
            data.Schedule.ZoneId);
    }

    /// <summary>Name order for lists: "first last", case-insensitive, id as tie-breaker.</summary>
    public static IOrderedEnumerable<TechnicianFacts> OrderByName(IEnumerable<TechnicianFacts> facts, bool descending) =>
        descending
            ? facts.OrderByDescending(fact => fact.FullName.ToLowerInvariant(), StringComparer.Ordinal).ThenByDescending(fact => fact.Id)
            : facts.OrderBy(fact => fact.FullName.ToLowerInvariant(), StringComparer.Ordinal).ThenBy(fact => fact.Id);

    /// <summary>Resolves an optional branch filter against the caller's scope (BR-02).</summary>
    public static async Task<(Guid? BranchId, bool Valid)> ResolveBranchFilterAsync(
        ITeamStore store, Guid organizationId, BranchScope scope, string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (null, true);
        }

        if (!TeamQueryParser.TryParseGuid(text, out var branchId)
            || !await store.IsBranchAllowedAsync(organizationId, scope, branchId, requireActive: false, cancellationToken))
        {
            return (null, false);
        }

        return (branchId, true);
    }
}

/// <summary>GET /team/options (BR-01, BR-02).</summary>
public sealed class GetTeamOptionsHandler(ITeamStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<TeamOptions> HandleAsync(Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetOptionsAsync(organizationId, scope, cancellationToken);
    }
}

/// <summary>GET /team/metrics (FR-02, BR-08, BR-14).</summary>
public sealed class GetTeamMetricsHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TeamMetrics>> HandleAsync(
        Guid organizationId, Guid membershipId, string? branchIdText, string? periodText, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!TeamQueryParser.TryParsePeriod(periodText, out var period))
        {
            errors[TeamFieldKeys.Period] = [TeamMessages.QueryInvalid];
        }

        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var (branchId, branchValid) = await TeamMapping.ResolveBranchFilterAsync(
            store, organizationId, scope, branchIdText, cancellationToken);

        if (!branchValid)
        {
            errors[TeamFieldKeys.BranchId] = [TeamMessages.BranchNotAllowed];
        }

        if (errors.Count > 0)
        {
            return TeamResult<TeamMetrics>.Invalid(errors);
        }

        var now = timeProvider.GetUtcNow();
        var facts = await store.ListFactsAsync(
            organizationId,
            scope,
            new TeamFactsFilter(branchId, null, null, TeamAccountFilter.All, TechnicianStatus.Active),
            now,
            cancellationToken);

        var rows = facts
            .Select(fact => (Fact: fact, Derived: TechnicianAvailabilityCalculator.Derive(fact.Schedule, period, now)))
            .ToList();

        var atCapacity = rows
            .Where(row => row.Derived.Workload.AtCapacity)
            .OrderByDescending(row => row.Derived.Workload.State == WorkloadStates.NoAvailability)
            .ThenByDescending(row => row.Derived.Workload.Percent ?? 0)
            .ThenBy(row => row.Fact.FullName.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(row => row.Fact.Id)
            .ToList();

        var unlinked = TeamMapping.OrderByName(facts.Where(fact => !fact.IsLinked), descending: false).ToList();

        CapacityAlert? capacityAlert = atCapacity.Count == 0
            ? null
            : new CapacityAlert(
                atCapacity[0].Fact.Id,
                atCapacity[0].Fact.FullName,
                atCapacity[0].Derived.Workload.Percent,
                atCapacity[0].Derived.Workload.State == WorkloadStates.NoAvailability,
                atCapacity.Count - 1);

        UnlinkedAlert? unlinkedAlert = unlinked.Count == 0
            ? null
            : new UnlinkedAlert(unlinked[0].Id, unlinked[0].FullName, unlinked.Count - 1);

        return TeamResult<TeamMetrics>.Ok(new TeamMetrics(
            rows.Count,
            rows.Count(row => row.Derived.TodayStatus == TodayStatuses.Available),
            rows.Count(row => row.Derived.TodayStatus == TodayStatuses.OnJob),
            atCapacity.Count,
            unlinked.Count,
            capacityAlert,
            unlinkedAlert));
    }
}

/// <summary>GET /team/technicians (FR-03, FR-04, BR-09 to BR-11).</summary>
public sealed class ListTechniciansHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TechnicianListPage>> HandleAsync(
        Guid organizationId, Guid membershipId, TeamListQuery query, CancellationToken cancellationToken)
    {
        var filter = TeamQueryParser.Parse(query, out var errors);
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (filter?.BranchId is { } branchId
            && !await store.IsBranchAllowedAsync(organizationId, scope, branchId, requireActive: false, cancellationToken))
        {
            errors[TeamFieldKeys.BranchId] = [TeamMessages.BranchNotAllowed];
            filter = null;
        }

        if (filter?.SkillId is { } skillId && !await store.SkillExistsAsync(organizationId, skillId, cancellationToken))
        {
            errors[TeamFieldKeys.SkillId] = [TeamMessages.SkillInvalid];
            filter = null;
        }

        if (filter is null || errors.Count > 0)
        {
            return TeamResult<TechnicianListPage>.Invalid(errors);
        }

        var now = timeProvider.GetUtcNow();
        var profileStatus = filter.Status switch
        {
            TeamStatusFilter.Inactive => TechnicianStatus.Inactive,
            TeamStatusFilter.Suspended => TechnicianStatus.Suspended,
            _ => TechnicianStatus.Active,
        };

        var facts = await store.ListFactsAsync(
            organizationId,
            scope,
            new TeamFactsFilter(filter.BranchId, filter.SkillId, filter.Search, filter.AccountLink, profileStatus),
            now,
            cancellationToken);

        var rows = facts
            .Select(fact => (Fact: fact, Derived: TechnicianAvailabilityCalculator.Derive(fact.Schedule, filter.Period, now)))
            .Where(row => Matches(filter.Status, row.Derived.TodayStatus))
            .ToList();

        var ordered = TeamMapping.OrderByName(rows.Select(row => row.Fact), filter.Descending).ToList();
        var byId = rows.ToDictionary(row => row.Fact.Id);

        var items = ordered
            .Skip((filter.Page - 1) * TeamQueryParser.PageSize)
            .Take(TeamQueryParser.PageSize)
            .Select(fact =>
            {
                var derived = byId[fact.Id].Derived;

                return new TechnicianListItem(
                    fact.Id,
                    fact.FullName,
                    fact.BranchName,
                    fact.SkillNames,
                    fact.IsLinked,
                    TeamMapping.ProfileStatusText(fact.Schedule.Status),
                    derived.TodayStatus,
                    derived.Workload.Jobs,
                    derived.Workload.Percent,
                    derived.Workload.State,
                    derived.Workload.AtCapacity,
                    derived.NextAvailable,
                    fact.Schedule.ZoneId);
            })
            .ToList();

        var teamMembers = await store.CountActiveAsync(organizationId, scope, filter.BranchId, cancellationToken);

        return TeamResult<TechnicianListPage>.Ok(new TechnicianListPage(
            items, ordered.Count, teamMembers, filter.Page, TeamQueryParser.PageSize));
    }

    private static bool Matches(TeamStatusFilter filter, string todayStatus) =>
        filter switch
        {
            TeamStatusFilter.AllActive => todayStatus is not (TodayStatuses.Inactive or TodayStatuses.Suspended),
            TeamStatusFilter.Available => todayStatus == TodayStatuses.Available,
            TeamStatusFilter.OnJob => todayStatus == TodayStatuses.OnJob,
            TeamStatusFilter.Break => todayStatus == TodayStatuses.Break,
            TeamStatusFilter.TimeOff => todayStatus == TodayStatuses.TimeOff,
            TeamStatusFilter.Off => todayStatus == TodayStatuses.Off,
            TeamStatusFilter.Inactive => todayStatus == TodayStatuses.Inactive,
            _ => todayStatus == TodayStatuses.Suspended,
        };
}

/// <summary>GET /team/technicians/{id} (FR-05, BR-13). Notes only for mutators.</summary>
public sealed class GetTechnicianHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TechnicianDetail>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        string? periodText,
        bool includeNotes,
        CancellationToken cancellationToken)
    {
        if (!TeamQueryParser.TryParsePeriod(periodText, out var period))
        {
            return TeamResult<TechnicianDetail>.Invalid(TeamFieldKeys.Period, TeamMessages.QueryInvalid);
        }

        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var data = await store.GetProfileAsync(organizationId, scope, technicianId, now, cancellationToken);

        return data is null
            ? TeamResult<TechnicianDetail>.NotFound()
            : TeamResult<TechnicianDetail>.Ok(TeamMapping.ToDetail(data, period, now, includeNotes));
    }
}

/// <summary>GET /team/me (FR-10, BR-20): the caller's own linked profile, regardless of branch scope; no notes.</summary>
public sealed class GetOwnTechnicianHandler(ITeamStore store, TimeProvider timeProvider)
{
    public async Task<TeamResult<TechnicianDetail>> HandleAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var data = await store.GetOwnProfileAsync(organizationId, membershipId, now, cancellationToken);

        return data is null
            ? TeamResult<TechnicianDetail>.NotFound()
            : TeamResult<TechnicianDetail>.Ok(TeamMapping.ToDetail(data, TeamPeriod.Today, now, includeNotes: false));
    }
}

/// <summary>POST /team/technicians (FR-06, BR-15, BR-16).</summary>
public sealed class CreateTechnicianHandler(ITeamStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<TeamResult<Guid>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid actorUserId,
        IPAddress? clientIp,
        TeamProfileInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var values = TeamRules.Validate(input, out var errors);

        Guid? branchId = null;

        if (!TeamQueryParser.TryParseGuid(input.BranchId, out var parsed)
            || !await store.IsBranchAllowedAsync(organizationId, scope, parsed, requireActive: true, cancellationToken))
        {
            errors[TeamFieldKeys.BranchId] = [TeamMessages.BranchNotAllowed];
        }
        else
        {
            branchId = parsed;
        }

        if (values is null || branchId is null || errors.Count > 0)
        {
            return TeamResult<Guid>.Invalid(errors);
        }

        var result = await store.CreateAsync(organizationId, branchId.Value, values, actorUserId, clientIp, cancellationToken);

        return result.Outcome == TeamSaveOutcome.Saved
            ? TeamResult<Guid>.Ok(result.Id)
            : TeamResult<Guid>.Invalid(result.Errors ?? errors);
    }
}

/// <summary>PUT /team/technicians/{id} (FR-06). The profile is resolved first, so a hidden id is 404 whatever the body is.</summary>
public sealed class UpdateTechnicianHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TechnicianDetail>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        TeamProfileInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetBranchIdAsync(organizationId, scope, technicianId, cancellationToken) is not { } currentBranch)
        {
            return TeamResult<TechnicianDetail>.NotFound();
        }

        var values = TeamRules.Validate(input, out var errors);

        Guid? branchId = null;

        // An unchanged home branch is kept even when it has since been deactivated.
        if (!TeamQueryParser.TryParseGuid(input.BranchId, out var parsed)
            || (parsed != currentBranch
                && !await store.IsBranchAllowedAsync(organizationId, scope, parsed, requireActive: true, cancellationToken)))
        {
            errors[TeamFieldKeys.BranchId] = [TeamMessages.BranchNotAllowed];
        }
        else
        {
            branchId = parsed;
        }

        if (values is null || branchId is null || errors.Count > 0)
        {
            return TeamResult<TechnicianDetail>.Invalid(errors);
        }

        var now = timeProvider.GetUtcNow();
        var result = await store.UpdateAsync(
            organizationId, scope, technicianId, branchId.Value, values, actorUserId, clientIp, now, cancellationToken);

        if (result.Outcome == TeamSaveOutcome.NotFound)
        {
            return TeamResult<TechnicianDetail>.NotFound();
        }

        if (result.Outcome == TeamSaveOutcome.Duplicate)
        {
            return TeamResult<TechnicianDetail>.Invalid(result.Errors ?? errors);
        }

        var data = await store.GetProfileAsync(organizationId, scope, technicianId, now, cancellationToken);

        return data is null
            ? TeamResult<TechnicianDetail>.NotFound()
            : TeamResult<TechnicianDetail>.Ok(TeamMapping.ToDetail(data, TeamPeriod.Today, now, includeNotes: true));
    }
}

/// <summary>POST /team/technicians/{id}/activate and /deactivate (FR-07, BR-17).</summary>
public sealed class SetTechnicianStatusHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TeamNoValue>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        bool activate,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var result = await store.SetStatusAsync(
            organizationId, scope, technicianId, activate, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return result.Outcome switch
        {
            TeamStatusOutcome.NotFound => TeamResult<TeamNoValue>.NotFound(),
            TeamStatusOutcome.HasUpcomingVisits => TeamResult<TeamNoValue>.Conflict(
                TeamMessages.UpcomingVisits, result.UpcomingVisitCount),
            _ => TeamResult<TeamNoValue>.NoOp(),
        };
    }
}

/// <summary>GET /team/linkable-accounts (FR-08, BR-18).</summary>
public sealed class ListLinkableAccountsHandler(ITeamStore store)
{
    public async Task<TeamResult<IReadOnlyList<LinkableAccount>>> HandleAsync(
        Guid organizationId, string? search, CancellationToken cancellationToken)
    {
        var term = search?.Trim();

        if (term is { Length: > TeamQueryParser.SearchMaxLength })
        {
            return TeamResult<IReadOnlyList<LinkableAccount>>.Invalid(TeamFieldKeys.Search, TeamMessages.SearchTooLong);
        }

        return TeamResult<IReadOnlyList<LinkableAccount>>.Ok(
            await store.ListLinkableAccountsAsync(organizationId, string.IsNullOrEmpty(term) ? null : term, cancellationToken));
    }
}

/// <summary>PUT and DELETE /team/technicians/{id}/account-link (FR-08, BR-18).</summary>
public sealed class ChangeTechnicianAccountLinkHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<TeamNoValue>> LinkAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid organizationUserId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var outcome = await store.LinkAsync(
            organizationId, scope, technicianId, organizationUserId, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return outcome switch
        {
            TeamLinkOutcome.NotFound => TeamResult<TeamNoValue>.NotFound(),
            TeamLinkOutcome.Rejected => TeamResult<TeamNoValue>.Conflict(TeamMessages.CannotLink),
            _ => TeamResult<TeamNoValue>.NoOp(),
        };
    }

    public async Task<TeamResult<TeamNoValue>> UnlinkAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var outcome = await store.UnlinkAsync(
            organizationId, scope, technicianId, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return outcome == TeamLinkOutcome.NotFound ? TeamResult<TeamNoValue>.NotFound() : TeamResult<TeamNoValue>.NoOp();
    }
}

/// <summary>GET /team/skill-coverage (FR-09, BR-19).</summary>
public sealed class GetSkillCoverageHandler(ITeamStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<TeamResult<IReadOnlyList<SkillCoverage>>> HandleAsync(
        Guid organizationId, Guid membershipId, string? branchIdText, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var (branchId, valid) = await TeamMapping.ResolveBranchFilterAsync(
            store, organizationId, scope, branchIdText, cancellationToken);

        if (!valid)
        {
            return TeamResult<IReadOnlyList<SkillCoverage>>.Invalid(TeamFieldKeys.BranchId, TeamMessages.BranchNotAllowed);
        }

        var rows = await store.GetSkillCoverageAsync(organizationId, scope, branchId, cancellationToken);

        IReadOnlyList<SkillCoverage> coverage = [.. rows
            .OrderByDescending(row => row.TechnicianCount)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => new SkillCoverage(row.SkillId, row.Name, row.TechnicianCount, Health(row.TechnicianCount)))];

        return TeamResult<IReadOnlyList<SkillCoverage>>.Ok(coverage);
    }

    /// <summary>BR-19: Healthy at 5 or more, Watch at 4, Low at 3 or fewer.</summary>
    public static string Health(int count) =>
        count >= 5 ? "healthy" : count == 4 ? "watch" : "low";
}
