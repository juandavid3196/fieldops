using System.Net;
using System.Text.Json;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.Users;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Names of the unique indexes that map to acceptance conflicts (BR-10).</summary>
internal static class AcceptanceConstraintNames
{
    public const string UserEmail = "ix_users_email";

    public const string MembershipPerOrganizationUser = "ix_organization_users_organization_id_user_id";
}

internal sealed class InvitationAcceptanceStore(FieldOpsDbContext dbContext, TimeProvider timeProvider)
    : IInvitationAcceptanceStore
{
    public async Task<InvitationDetailsView?> FindUsableAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var row = await (
            from invitation in dbContext.UserInvitations.AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on invitation.OrganizationId equals organization.Id
            join role in dbContext.Roles.AsNoTracking() on invitation.RoleId equals role.Id
            join inviter in dbContext.Users.AsNoTracking() on invitation.InvitedByUserId equals inviter.Id
            where invitation.TokenHash == tokenHash
                && invitation.AcceptedAt == null
                && invitation.RevokedAt == null
                && invitation.ExpiresAt > now
                && organization.IsActive
            select new
            {
                invitation.Id,
                invitation.OrganizationId,
                OrganizationName = organization.Name,
                InviterName = inviter.FirstName + " " + inviter.LastName,
                invitation.Email,
                invitation.FirstName,
                invitation.LastName,
                RoleCode = role.Code,
                RoleName = role.Name,
                invitation.IsAllBranches,
                invitation.ExpiresAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var branches = row.IsAllBranches
            ? []
            : await (
                from link in dbContext.InvitationBranches.AsNoTracking()
                join branch in dbContext.Branches.AsNoTracking() on link.BranchId equals branch.Id
                where link.InvitationId == row.Id && branch.OrganizationId == row.OrganizationId && branch.IsActive
                orderby branch.Name
                select new InvitationBranchView(branch.Name))
                .ToListAsync(cancellationToken);

        return new InvitationDetailsView(
            row.OrganizationName,
            row.InviterName,
            row.Email,
            row.FirstName,
            row.LastName,
            new InvitationRoleView(row.RoleCode, row.RoleName),
            row.IsAllBranches,
            branches,
            row.ExpiresAt);
    }

    public async Task<InvitationResult<AcceptedInvitation>> AcceptNewUserAsync(
        AcceptNewUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var invitation = await LoadLockedUsableInvitationAsync(request.TokenHash, now, cancellationToken);

        if (invitation is null)
        {
            return InvitationResult<AcceptedInvitation>.Gone();
        }

        // Stored emails are already normalized; UNIQUE (email) backs this check.
        var accountExists = await dbContext.Users.AsNoTracking()
            .AnyAsync(user => user.Email == invitation.Email, cancellationToken);

        if (accountExists)
        {
            return InvitationResult<AcceptedInvitation>.Failed(InvitationConflict.AccountExists);
        }

        var branchIds = await ResolveActiveBranchIdsAsync(invitation, cancellationToken);

        if (branchIds is null)
        {
            return InvitationResult<AcceptedInvitation>.Failed(InvitationConflict.AccessUnavailable);
        }

        var user = User.Create(invitation.Email, request.PasswordHash, request.FirstName, request.LastName);
        user.Activate();
        user.MarkEmailVerified(now);
        user.RecordSignIn(now);
        dbContext.Users.Add(user);

        return await SaveAcceptanceAsync(
            transaction, invitation, user, accountCreated: true, branchIds, request.ClientIp, now, cancellationToken);
    }

    public async Task<InvitationResult<AcceptedInvitation>> AcceptExistingUserAsync(
        AcceptExistingUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var invitation = await LoadLockedUsableInvitationAsync(request.TokenHash, now, cancellationToken);

        if (invitation is null)
        {
            return InvitationResult<AcceptedInvitation>.Gone();
        }

        // The email comes from the database, never from the request.
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == request.UserId, cancellationToken);

        if (user is null
            || user.Status != UserStatus.Active
            || !string.Equals(user.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            return InvitationResult<AcceptedInvitation>.Failed(InvitationConflict.IdentityMismatch);
        }

        var membershipExists = await dbContext.OrganizationUsers.AsNoTracking().AnyAsync(
            membership => membership.OrganizationId == invitation.OrganizationId && membership.UserId == user.Id,
            cancellationToken);

        if (membershipExists)
        {
            return InvitationResult<AcceptedInvitation>.Failed(InvitationConflict.MembershipExists);
        }

        var branchIds = await ResolveActiveBranchIdsAsync(invitation, cancellationToken);

        if (branchIds is null)
        {
            return InvitationResult<AcceptedInvitation>.Failed(InvitationConflict.AccessUnavailable);
        }

        user.RecordSignIn(now);

        return await SaveAcceptanceAsync(
            transaction, invitation, user, accountCreated: false, branchIds, request.ClientIp, now, cancellationToken);
    }

    // BR-10: locks the row by token hash, then re-checks BR-02 under the lock.
    private async Task<UserInvitation?> LoadLockedUsableInvitationAsync(
        string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM user_invitations
                WHERE token_hash = {tokenHash}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count != 1)
        {
            return null;
        }

        var invitationId = ids[0];
        var invitation = await dbContext.UserInvitations.SingleOrDefaultAsync(
            candidate => candidate.Id == invitationId, cancellationToken);

        if (invitation is null || !invitation.IsUsable(now))
        {
            return null;
        }

        var organizationActive = await dbContext.Organizations.AsNoTracking().AnyAsync(
            organization => organization.Id == invitation.OrganizationId && organization.IsActive,
            cancellationToken);

        return organizationActive ? invitation : null;
    }

    /// <summary>
    /// Empty for an all-branches invitation; the still-active invited branches
    /// otherwise; null when a limited invitation has none left (BR-09).
    /// </summary>
    private async Task<IReadOnlyList<Guid>?> ResolveActiveBranchIdsAsync(
        UserInvitation invitation, CancellationToken cancellationToken)
    {
        if (invitation.IsAllBranches)
        {
            return [];
        }

        var ids = await (
            from link in dbContext.InvitationBranches.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on link.BranchId equals branch.Id
            where link.InvitationId == invitation.Id
                && branch.OrganizationId == invitation.OrganizationId
                && branch.IsActive
            select branch.Id)
            .ToListAsync(cancellationToken);

        return ids.Count == 0 ? null : ids;
    }

    private async Task<InvitationResult<AcceptedInvitation>> SaveAcceptanceAsync(
        IDbContextTransaction transaction,
        UserInvitation invitation,
        User user,
        bool accountCreated,
        IReadOnlyList<Guid> branchIds,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.AsNoTracking()
            .Where(candidate => candidate.Id == invitation.RoleId)
            .Select(candidate => new { candidate.Code, candidate.Name })
            .SingleAsync(cancellationToken);

        var organizationName = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == invitation.OrganizationId)
            .Select(organization => organization.Name)
            .SingleAsync(cancellationToken);

        var membership = OrganizationUser.Create(
            invitation.OrganizationId,
            user.Id,
            invitation.RoleId,
            invitation.IsAllBranches,
            now,
            invitation.InvitedByUserId);

        dbContext.OrganizationUsers.Add(membership);

        foreach (var branchId in branchIds)
        {
            dbContext.OrganizationUserBranches.Add(OrganizationUserBranch.Create(membership.Id, branchId));
        }

        invitation.Accept(now);

        var profileLinked = invitation.LinkTeamProfile
            && await TryLinkProfileAsync(invitation, membership.Id, now, cancellationToken);

        dbContext.AuditLogs.Add(AuditLog.Create(
            invitation.OrganizationId,
            UserAuditActions.InvitationAccepted,
            UserAuditActions.InvitationEntity,
            actorUserId: user.Id,
            entityId: invitation.Id,
            ipAddress: clientIp,
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["membershipId"] = membership.Id,
                ["roleCode"] = role.Code,
                ["isAllBranches"] = invitation.IsAllBranches,
                ["branchIds"] = SortedIds(branchIds),
                ["accountCreated"] = accountCreated,
                ["teamProfileLinked"] = profileLinked,
            })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConflictFor(ex) is { } conflict)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            return InvitationResult<AcceptedInvitation>.Failed(conflict);
        }

        await transaction.CommitAsync(cancellationToken);

        return InvitationResult<AcceptedInvitation>.Ok(new AcceptedInvitation(
            new SessionView(
                new SessionUser(user.Id, user.FirstName, user.LastName, user.Email),
                new SessionOrganization(invitation.OrganizationId, organizationName),
                new SessionRole(role.Code, role.Name)),
            membership.Id,
            now));
    }

    // BR-11: exactly one unlinked profile of the organization with this email.
    private async Task<bool> TryLinkProfileAsync(
        UserInvitation invitation, Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var email = invitation.Email;

        var matches = await dbContext.TechnicianProfiles
            .Where(profile => profile.OrganizationId == invitation.OrganizationId
                && profile.OrganizationUserId == null
                && profile.Email != null
                && profile.Email.Trim().ToLower() == email)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count != 1)
        {
            return false;
        }

        matches[0].LinkMembership(membershipId, now);

        return true;
    }

    private static InvitationConflict? ConflictFor(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName switch
            {
                AcceptanceConstraintNames.UserEmail => InvitationConflict.AccountExists,
                AcceptanceConstraintNames.MembershipPerOrganizationUser => InvitationConflict.MembershipExists,
                _ => null,
            }
            : null;

    private static string[] SortedIds(IEnumerable<Guid> ids) =>
        [.. ids.Select(id => id.ToString()).OrderBy(id => id, StringComparer.Ordinal)];
}
