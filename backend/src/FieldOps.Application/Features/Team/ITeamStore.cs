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

    // Skills and availability page (profile, exceptions, catalog).

    /// <summary>
    /// The page data of an in-scope profile, or null when it is not visible. When <paramref name="ownMembershipId"/>
    /// is set (technician callers) only the profile linked to that membership is visible and the scope is ignored.
    /// </summary>
    Task<SkillsAvailabilityData?> GetSkillsAvailabilityAsync(
        Guid organizationId,
        BranchScope scope,
        Guid? ownMembershipId,
        Guid technicianId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>The time zone id of an in-scope profile (BR-03), or null when it is not visible.</summary>
    Task<string?> GetZoneIdAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic replace of weekly rows, breaks and active-skill assignments under a profile row lock (BR-11).
    /// An unchanged save writes nothing.
    /// </summary>
    Task<SkillsSaveResult> SaveSkillsAvailabilityAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        SkillsAvailabilityValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ExceptionResult> CreateExceptionAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        ExceptionWrite write,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ExceptionResult> UpdateExceptionAsync(
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
        CancellationToken cancellationToken);

    Task<ExceptionResult> SetExceptionActiveAsync(
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
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SkillView>> ListSkillsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<SkillResult> CreateSkillAsync(
        Guid organizationId,
        SkillValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    Task<SkillResult> UpdateSkillAsync(
        Guid organizationId,
        Guid skillId,
        SkillValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>Same-status calls are a no-op without audit; a missing or foreign skill is <c>NotFound</c>.</summary>
    Task<SkillResult> SetSkillActiveAsync(
        Guid organizationId,
        Guid skillId,
        bool active,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    // Team availability and skills tabs (team-availability-skills). Every method here sees active in-scope profiles only.

    /// <summary>The time zone id of an active in-scope profile, or null when it is not visible.</summary>
    Task<string?> GetActiveProfileZoneIdAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken);

    Task<string?> GetOrganizationTimezoneAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Active in-scope profiles matching the filter with effective weekly slots and active exceptions near the range.</summary>
    Task<IReadOnlyList<AvailabilityGridProfile>> ListAvailabilityGridAsync(
        Guid organizationId,
        BranchScope scope,
        AvailabilityGridFilter filter,
        DateTimeOffset low,
        DateTimeOffset high,
        CancellationToken cancellationToken);

    /// <summary>Active organization skills and the active in-scope profiles matching the filter with their active-skill ids.</summary>
    Task<SkillMatrixData> GetSkillMatrixAsync(
        Guid organizationId, BranchScope scope, AvailabilityGridFilter filter, CancellationToken cancellationToken);

    Task<TechnicianAvailabilityData?> GetTechnicianAvailabilityAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>BR-10: replaces the weekly rows under a profile row lock; an unchanged custom schedule writes nothing.</summary>
    Task<AvailabilityWriteResult> SaveAvailabilityAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        IReadOnlyList<AvailabilityDayValues> days,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>BR-12: deletes the weekly rows and inherits the branch hours; already inheriting is a no-op.</summary>
    Task<AvailabilityWriteResult> UseBranchHoursAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<TimeOffResult> CreateTimeOffAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        TimeOffWrite write,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<TimeOffResult> RetireTimeOffAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        Guid timeOffId,
        DateTimeOffset version,
        string zoneId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SkillAssignmentsData?> GetSkillAssignmentsAsync(
        Guid organizationId, BranchScope scope, Guid technicianId, CancellationToken cancellationToken);

    /// <summary>BR-21: ids are validated before the lock; the active-skill set is replaced atomically.</summary>
    Task<AvailabilityWriteResult> SaveSkillAssignmentsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset version,
        IReadOnlyList<Guid> skillIds,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
