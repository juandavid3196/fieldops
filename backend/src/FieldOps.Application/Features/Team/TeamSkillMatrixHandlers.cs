using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Team;

/// <summary>GET /team/skills-matrix (FR-08, BR-17, BR-18). Read policy; scope per branch.</summary>
public sealed class GetSkillsMatrixHandler(ITeamStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<TeamResult<SkillsMatrixPage>> HandleAsync(
        Guid organizationId, Guid membershipId, SkillsMatrixQuery query, CancellationToken cancellationToken)
    {
        var parsed = TeamAvailabilityRules.ParseMatrix(query, out var errors);

        if (parsed is null)
        {
            return TeamResult<SkillsMatrixPage>.Invalid(errors);
        }

        var (search, branchId, skillId, coverage, descending, page) = parsed.Value;
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (branchId is { } branch
            && !await store.IsBranchAllowedAsync(organizationId, scope, branch, requireActive: true, cancellationToken))
        {
            return TeamResult<SkillsMatrixPage>.Invalid(TeamFieldKeys.BranchId, TeamMessages.BranchNotAllowed);
        }

        var data = await store.GetSkillMatrixAsync(
            organizationId, scope, new AvailabilityGridFilter(search, branchId), cancellationToken);

        if (skillId is { } skill && data.Skills.All(item => item.Id != skill))
        {
            return TeamResult<SkillsMatrixPage>.Invalid(TeamFieldKeys.SkillId, TeamMessages.SkillInvalid);
        }

        var filtered = data.Technicians
            .Where(item => skillId is not { } wanted || item.ActiveSkillIds.Contains(wanted))
            .Where(item => coverage switch
            {
                "with" => item.ActiveSkillIds.Count > 0,
                "without" => item.ActiveSkillIds.Count == 0,
                _ => true,
            });

        var ordered = (descending
            ? filtered.OrderByDescending(item => item.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(item => item.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id)
            : filtered.OrderBy(item => item.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id))
            .ToList();

        var items = ordered
            .Skip((page - 1) * TeamQueryParser.PageSize)
            .Take(TeamQueryParser.PageSize)
            .Select(item => new SkillMatrixRowView(
                item.Id, item.FirstName, item.LastName, item.ColorHex, item.Branch, item.ActiveSkillIds, item.ActiveSkillIds.Count))
            .ToList();

        return TeamResult<SkillsMatrixPage>.Ok(new SkillsMatrixPage(data.Skills, items, ordered.Count, page, TeamQueryParser.PageSize));
    }
}

/// <summary>Skill assignment drawer (BR-20, BR-21). A hidden profile is 404 before any validation.</summary>
public sealed class TechnicianSkillAssignmentsHandler(ITeamStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<SkillAssignmentsView>> GetAsync(
        Guid organizationId, Guid membershipId, Guid technicianId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await ReadAsync(organizationId, scope, technicianId, cancellationToken);
    }

    public async Task<TeamResult<SkillAssignmentsView>> SaveAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        SaveSkillAssignmentsInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetActiveProfileZoneIdAsync(organizationId, scope, technicianId, cancellationToken) is null)
        {
            return TeamResult<SkillAssignmentsView>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var version = TeamAvailabilityRules.ParseUpdatedAt(input.UpdatedAt);

        if (version is null)
        {
            errors[TeamAvailabilityKeys.UpdatedAt] = [FieldOps.Application.Validation.UpdatedAtValidation.InvalidMessage];
        }

        var ids = TeamAvailabilityRules.ValidateSkillIds(input.SkillIds, errors);

        if (ids is null || version is null || errors.Count > 0)
        {
            return TeamResult<SkillAssignmentsView>.Invalid(errors);
        }

        var now = SkillsAvailabilityRules.TruncateToMicroseconds(timeProvider.GetUtcNow());
        var result = await store.SaveSkillAssignmentsAsync(
            organizationId, scope, technicianId, version.Value, ids, actorUserId, clientIp, now, cancellationToken);

        switch (result.Outcome)
        {
            case AvailabilityWriteOutcome.NotFound:
                return TeamResult<SkillAssignmentsView>.NotFound();
            case AvailabilityWriteOutcome.Conflict:
                return TeamResult<SkillAssignmentsView>.Conflict(
                    TeamAvailabilityMessages.ProfileStale, TeamAvailabilityConflictCodes.Stale);
            case AvailabilityWriteOutcome.InvalidSkills:
                return TeamResult<SkillAssignmentsView>.Invalid(
                    TeamAvailabilityKeys.SkillIds, TeamAvailabilityMessages.SkillNotActive);
        }

        return await ReadAsync(organizationId, scope, technicianId, cancellationToken);
    }

    private async Task<TeamResult<SkillAssignmentsView>> ReadAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken)
    {
        var data = await store.GetSkillAssignmentsAsync(organizationId, scope, technicianId, cancellationToken);

        return data is null
            ? TeamResult<SkillAssignmentsView>.NotFound()
            : TeamResult<SkillAssignmentsView>.Ok(new SkillAssignmentsView(
                data.Id, data.FirstName, data.LastName, SkillsAvailabilityRules.FormatVersion(data.UpdatedAt), data.SkillIds));
    }
}
