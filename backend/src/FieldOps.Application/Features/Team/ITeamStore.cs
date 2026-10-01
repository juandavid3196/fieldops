using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Team;

/// <summary>
/// Persistence for technician profiles. Every query filters by the session organization first and then by the
/// caller's branch scope; a profile outside either is reported as not found. Reads are batched: no per-row queries.
/// </summary>
public interface ITeamStore
{
    /// <summary>Active in-scope branches and active skills, by name.</summary>
    Task<TeamOptions> GetOptionsAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken);

    /// <summary>True when the branch exists in the organization and the scope, and (for writes) is active.</summary>
    Task<bool> IsBranchAllowedAsync(
        Guid organizationId, BranchScope scope, Guid branchId, bool requireActive, CancellationToken cancellationToken);

    Task<bool> SkillExistsAsync(Guid organizationId, Guid skillId, CancellationToken cancellationToken);

    /// <summary>The home branch of an in-scope profile, or null when it is not visible.</summary>
    Task<Guid?> GetBranchIdAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken);

    /// <summary>In-scope profiles matching the filter with the inputs of the derived status, in one batch.</summary>
    Task<IReadOnlyList<TechnicianFacts>> ListFactsAsync(
        Guid organizationId,
        BranchScope scope,
        TeamFactsFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Active in-scope profiles, optionally in one branch.</summary>
    Task<int> CountActiveAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken);

    Task<TechnicianProfileData?> GetProfileAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The profile linked to the membership, whatever the branch scope.</summary>
    Task<TechnicianProfileData?> GetOwnProfileAsync(
        Guid organizationId, Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkillCoverageRow>> GetSkillCoverageAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinkableAccount>> ListLinkableAccountsAsync(
        Guid organizationId, string? search, CancellationToken cancellationToken);

    /// <summary>Adds the active, unlinked profile and its audit row in one save; duplicates are reported, not thrown.</summary>
    Task<TeamSaveResult> CreateAsync(
        Guid organizationId,
        Guid branchId,
        TeamProfileValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>Updates an in-scope profile; a no-op writes nothing.</summary>
    Task<TeamSaveResult> UpdateAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid branchId,
        TeamProfileValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Activates or deactivates. Deactivation re-checks upcoming assigned visits under a row lock in the
    /// same transaction as the update (BR-17).
    /// </summary>
    Task<TeamStatusResult> SetStatusAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        bool activate,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Links an eligible membership (BR-18); <c>Rejected</c> covers ineligible, foreign, taken and a race.</summary>
    Task<TeamLinkOutcome> LinkAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid organizationUserId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<TeamLinkOutcome> UnlinkAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
