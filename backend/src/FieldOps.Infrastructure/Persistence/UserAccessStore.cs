using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Users;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class UserAccessStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IUserAccessStore
{
    private sealed class RowProjection
    {
        public Guid Id { get; init; }

        public bool IsInvitation { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string RoleCode { get; init; } = string.Empty;

        public string RoleName { get; init; } = string.Empty;

        public bool IsAllBranches { get; init; }

        public string Status { get; init; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; init; }

        public DateTimeOffset? LastLoginAt { get; init; }

        public string SortKey { get; init; } = string.Empty;
    }

    public async Task<UserListPageView> ListAsync(
        Guid organizationId, Guid currentMembershipId, UserListFilter filter, CancellationToken cancellationToken)
    {
        var includeMembers = filter.Status is null or UserRowStatuses.Active or UserRowStatuses.Suspended;
        var includeInvitations = filter.Status is null or UserRowStatuses.PendingInvitation;
        var search = filter.Search?.ToLowerInvariant();
        var roleCode = filter.RoleCode;
        var branchId = filter.BranchId;
        var status = filter.Status;

        var members = MemberRows(organizationId);

        if (status is UserRowStatuses.Active or UserRowStatuses.Suspended)
        {
            members = members.Where(row => row.Status == status);
        }

        if (roleCode is not null)
        {
            members = members.Where(row => row.RoleCode == roleCode);
        }

        if (branchId is { } memberBranch)
        {
            members = members.Where(row => row.IsAllBranches
                || dbContext.OrganizationUserBranches.Any(link =>
                    link.OrganizationUserId == row.Id && link.BranchId == memberBranch));
        }

        if (search is not null)
        {
            members = members.Where(row => row.SortKey.ToLower().Contains(search) || row.Email.ToLower().Contains(search));
        }

        var invitations = InvitationRows(organizationId);

        if (roleCode is not null)
        {
            invitations = invitations.Where(row => row.RoleCode == roleCode);
        }

        if (branchId is { } invitationBranch)
        {
            invitations = invitations.Where(row => row.IsAllBranches
                || dbContext.InvitationBranches.Any(link =>
                    link.InvitationId == row.Id && link.BranchId == invitationBranch));
        }

        if (search is not null)
        {
            invitations = invitations.Where(row =>
                row.SortKey.ToLower().Contains(search) || row.Email.ToLower().Contains(search));
        }

        IQueryable<RowProjection> combined = (includeMembers, includeInvitations) switch
        {
            (true, true) => members.Concat(invitations),
            (true, false) => members,
            _ => invitations,
        };

        var total = await combined.CountAsync(cancellationToken);

        var ordered = filter.Descending
            ? combined.OrderByDescending(row => row.SortKey.ToLower())
                .ThenByDescending(row => row.Email)
                .ThenBy(row => row.Id)
            : combined.OrderBy(row => row.SortKey.ToLower())
                .ThenBy(row => row.Email)
                .ThenBy(row => row.Id);

        var page = await ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = await HydrateAsync(organizationId, currentMembershipId, page, cancellationToken);

        return new UserListPageView(items, filter.Page, filter.PageSize, total);
    }

    public async Task<UserSummaryView> GetSummaryAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var active = await dbContext.OrganizationUsers.AsNoTracking().CountAsync(
            member => member.OrganizationId == organizationId && member.Status == UserStatus.Active,
            cancellationToken);

        var suspended = await dbContext.OrganizationUsers.AsNoTracking().CountAsync(
            member => member.OrganizationId == organizationId && member.Status == UserStatus.Suspended,
            cancellationToken);

        var pending = await dbContext.UserInvitations.AsNoTracking().CountAsync(
            invitation => invitation.OrganizationId == organizationId
                && invitation.AcceptedAt == null
                && invitation.RevokedAt == null,
            cancellationToken);

        var owners = await CountActiveOwnersAsync(organizationId, cancellationToken);

        return new UserSummaryView(active, pending, suspended, owners);
    }

    public async Task<UserResult<UserRowView>> CreateInvitationAsync(
        CreateInvitationRequest request,
        Func<InvitationDeliveryContext, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId;
        var role = await GetRoleByCodeAsync(request.Access.RoleCode, cancellationToken);

        if (!await BranchesAreValidAsync(organizationId, request.Access.BranchIds, cancellationToken))
        {
            return UserResult<UserRowView>.Invalid("branchIds", UserMessages.InvalidBranch);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var email = request.Email;

        var isMember = await (
            from membership in dbContext.OrganizationUsers.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.OrganizationId == organizationId
                && (membership.Status == UserStatus.Active || membership.Status == UserStatus.Suspended)
                && user.Email.ToLower() == email
            select membership.Id)
            .AnyAsync(cancellationToken);

        if (isMember)
        {
            return UserResult<UserRowView>.Conflict(UserMessages.AlreadyMember, "email");
        }

        var hasOpenInvitation = await dbContext.UserInvitations.AsNoTracking().AnyAsync(
            invitation => invitation.OrganizationId == organizationId
                && invitation.Email == email
                && invitation.AcceptedAt == null
                && invitation.RevokedAt == null,
            cancellationToken);

        if (hasOpenInvitation)
        {
            return UserResult<UserRowView>.Conflict(UserMessages.PendingInvitationExists, "email");
        }

        var invitation = UserInvitation.Create(
            organizationId,
            email,
            request.FirstName,
            request.LastName,
            role.Id,
            request.Access.IsAllBranches,
            request.LinkTeamProfile,
            request.TokenHash,
            request.ActorUserId,
            request.ExpiresAt);

        dbContext.UserInvitations.Add(invitation);

        foreach (var branchId in request.Access.BranchIds)
        {
            dbContext.InvitationBranches.Add(InvitationBranch.Create(invitation.Id, branchId));
        }

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            UserAuditActions.Invited,
            UserAuditActions.InvitationEntity,
            actorUserId: request.ActorUserId,
            entityId: invitation.Id,
            ipAddress: request.ClientIp,
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["roleCode"] = request.Access.RoleCode,
                ["isAllBranches"] = request.Access.IsAllBranches,
                ["branchIds"] = SortedIds(request.Access.BranchIds),
                ["linkTeamProfile"] = request.LinkTeamProfile,
                ["expiresAt"] = request.ExpiresAt,
            })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsConstraintViolation(ex, UserInvitationConfiguration.OpenEmailIndexName))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return UserResult<UserRowView>.Conflict(UserMessages.PendingInvitationExists, "email");
        }

        var context = await BuildDeliveryContextAsync(
            organizationId, request.ActorUserId, invitation.Email, invitation.FirstName, role.Name, invitation.ExpiresAt,
            cancellationToken);

        if (!await TryDeliverAsync(transaction, beforeCommit, context, cancellationToken))
        {
            return UserResult<UserRowView>.DeliveryFailed();
        }

        var row = await GetRowAsync(organizationId, Guid.Empty, invitation.Id, isInvitation: true, cancellationToken);

        return UserResult<UserRowView>.Ok(row!);
    }

    public async Task<UserResult<UserRowView>> ResendInvitationAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        string newTokenHash,
        DateTimeOffset expiresAt,
        Func<InvitationDeliveryContext, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var invitation = await LoadLockedInvitationAsync(organizationId, invitationId, cancellationToken);

        if (invitation is null)
        {
            return UserResult<UserRowView>.NotFound();
        }

        if (!invitation.IsOpen)
        {
            return UserResult<UserRowView>.Conflict(UserMessages.NotPending);
        }

        invitation.Resend(newTokenHash, expiresAt);

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            UserAuditActions.InvitationResent,
            UserAuditActions.InvitationEntity,
            actorUserId: actorUserId,
            entityId: invitation.Id,
            ipAddress: clientIp,
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?> { ["expiresAt"] = expiresAt })));

        await dbContext.SaveChangesAsync(cancellationToken);

        var roleName = await dbContext.Roles.AsNoTracking()
            .Where(role => role.Id == invitation.RoleId)
            .Select(role => role.Name)
            .SingleAsync(cancellationToken);

        var context = await BuildDeliveryContextAsync(
            organizationId, actorUserId, invitation.Email, invitation.FirstName, roleName, invitation.ExpiresAt,
            cancellationToken);

        if (!await TryDeliverAsync(transaction, beforeCommit, context, cancellationToken))
        {
            return UserResult<UserRowView>.DeliveryFailed();
        }

        var row = await GetRowAsync(organizationId, Guid.Empty, invitation.Id, isInvitation: true, cancellationToken);

        return UserResult<UserRowView>.Ok(row!);
    }

    public async Task<UserResult<NoValue>> RevokeInvitationAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var invitation = await LoadLockedInvitationAsync(organizationId, invitationId, cancellationToken);

        if (invitation is null)
        {
            return UserResult<NoValue>.NotFound();
        }

        if (!invitation.IsOpen)
        {
            return UserResult<NoValue>.Conflict(UserMessages.NotPending);
        }

        invitation.Revoke(now);

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            UserAuditActions.InvitationRevoked,
            UserAuditActions.InvitationEntity,
            actorUserId: actorUserId,
            entityId: invitation.Id,
            ipAddress: clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserResult<NoValue>.NoOp();
    }

    public async Task<UserResult<UserRowView>> UpdateInvitationAccessAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        AccessSpec access,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var invitation = await LoadLockedInvitationAsync(organizationId, invitationId, cancellationToken);

        if (invitation is null)
        {
            return UserResult<UserRowView>.NotFound();
        }

        if (!invitation.IsOpen)
        {
            return UserResult<UserRowView>.Conflict(UserMessages.NotPending);
        }

        var role = await GetRoleByCodeAsync(access.RoleCode, cancellationToken);

        if (!await BranchesAreValidAsync(organizationId, access.BranchIds, cancellationToken))
        {
            return UserResult<UserRowView>.Invalid("branchIds", UserMessages.InvalidBranch);
        }

        var links = await dbContext.InvitationBranches
            .Where(link => link.InvitationId == invitation.Id)
            .ToListAsync(cancellationToken);

        var currentIds = links.Select(link => link.BranchId).ToList();
        var oldRoleCode = await GetRoleCodeAsync(invitation.RoleId, cancellationToken);
        var before = AccessJson(oldRoleCode, invitation.IsAllBranches, currentIds);

        if (invitation.RoleId == role.Id
            && invitation.IsAllBranches == access.IsAllBranches
            && SameSet(currentIds, access.BranchIds))
        {
            return UserResult<UserRowView>.Ok(
                (await GetRowAsync(organizationId, Guid.Empty, invitation.Id, isInvitation: true, cancellationToken))!);
        }

        invitation.UpdateAccess(role.Id, access.IsAllBranches);

        foreach (var link in links.Where(link => !access.BranchIds.Contains(link.BranchId)))
        {
            dbContext.InvitationBranches.Remove(link);
        }

        foreach (var branchId in access.BranchIds.Where(id => !currentIds.Contains(id)))
        {
            dbContext.InvitationBranches.Add(InvitationBranch.Create(invitation.Id, branchId));
        }

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            UserAuditActions.AccessUpdated,
            UserAuditActions.InvitationEntity,
            actorUserId: actorUserId,
            entityId: invitation.Id,
            ipAddress: clientIp,
            beforeData: before,
            afterData: AccessJson(access.RoleCode, access.IsAllBranches, access.BranchIds)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserResult<UserRowView>.Ok(
            (await GetRowAsync(organizationId, Guid.Empty, invitation.Id, isInvitation: true, cancellationToken))!);
    }

    public async Task<UserResult<UserRowView>> UpdateMemberAccessAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid actorMembershipId,
        IPAddress? clientIp,
        Guid membershipId,
        AccessSpec access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var ownerRoleId = await LockOwnersAndTargetAsync(organizationId, membershipId, cancellationToken);
        var target = await LoadMemberAsync(organizationId, membershipId, cancellationToken);

        if (target is null)
        {
            return UserResult<UserRowView>.NotFound();
        }

        var role = await GetRoleByCodeAsync(access.RoleCode, cancellationToken);

        if (!await BranchesAreValidAsync(organizationId, access.BranchIds, cancellationToken))
        {
            return UserResult<UserRowView>.Invalid("branchIds", UserMessages.InvalidBranch);
        }

        var links = await dbContext.OrganizationUserBranches
            .Where(link => link.OrganizationUserId == target.Id)
            .ToListAsync(cancellationToken);

        var currentIds = links.Select(link => link.BranchId).ToList();

        if (target.RoleId == role.Id
            && target.IsAllBranches == access.IsAllBranches
            && SameSet(currentIds, access.BranchIds))
        {
            return UserResult<UserRowView>.Ok(
                (await GetRowAsync(organizationId, actorMembershipId, target.Id, isInvitation: false, cancellationToken))!);
        }

        if (target.Status == UserStatus.Active
            && target.RoleId == ownerRoleId
            && role.Id != ownerRoleId
            && await CountActiveOwnersAsync(organizationId, cancellationToken) <= 1)
        {
            return UserResult<UserRowView>.Conflict(UserMessages.LastOwner);
        }

        var oldRoleCode = await GetRoleCodeAsync(target.RoleId, cancellationToken);
        var before = AccessJson(oldRoleCode, target.IsAllBranches, currentIds);

        target.ChangeAccess(role.Id, access.IsAllBranches, now);

        foreach (var link in links.Where(link => !access.BranchIds.Contains(link.BranchId)))
        {
            dbContext.OrganizationUserBranches.Remove(link);
        }

        foreach (var branchId in access.BranchIds.Where(id => !currentIds.Contains(id)))
        {
            dbContext.OrganizationUserBranches.Add(OrganizationUserBranch.Create(target.Id, branchId));
        }

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            UserAuditActions.AccessUpdated,
            UserAuditActions.MemberEntity,
            actorUserId: actorUserId,
            entityId: target.Id,
            ipAddress: clientIp,
            beforeData: before,
            afterData: AccessJson(access.RoleCode, access.IsAllBranches, access.BranchIds)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserResult<UserRowView>.Ok(
            (await GetRowAsync(organizationId, actorMembershipId, target.Id, isInvitation: false, cancellationToken))!);
    }

    public async Task<UserResult<NoValue>> SuspendMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid actorMembershipId,
        IPAddress? clientIp,
        Guid membershipId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var ownerRoleId = await LockOwnersAndTargetAsync(organizationId, membershipId, cancellationToken);
        var target = await LoadMemberAsync(organizationId, membershipId, cancellationToken);

        if (target is null)
        {
            return UserResult<NoValue>.NotFound();
        }

        if (target.Status == UserStatus.Suspended)
        {
            return UserResult<NoValue>.NoOp();
        }

        if (target.Id == actorMembershipId)
        {
            return UserResult<NoValue>.Conflict(UserMessages.SelfSuspend);
        }

        if (target.RoleId == ownerRoleId && await CountActiveOwnersAsync(organizationId, cancellationToken) <= 1)
        {
            return UserResult<NoValue>.Conflict(UserMessages.LastOwner);
        }

        target.Suspend(now);
        AddStatusAudit(organizationId, actorUserId, clientIp, target.Id, UserAuditActions.Suspended, "active", "suspended");

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserResult<NoValue>.NoOp();
    }

    public async Task<UserResult<NoValue>> ReactivateMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid membershipId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await LoadMemberAsync(organizationId, membershipId, cancellationToken);

        if (target is null)
        {
            return UserResult<NoValue>.NotFound();
        }

        if (!target.Reactivate(now))
        {
            return UserResult<NoValue>.NoOp();
        }

        AddStatusAudit(organizationId, actorUserId, clientIp, target.Id, UserAuditActions.Reactivated, "suspended", "active");

        await dbContext.SaveChangesAsync(cancellationToken);

        return UserResult<NoValue>.NoOp();
    }

    private IQueryable<RowProjection> MemberRows(Guid organizationId) =>
        from membership in dbContext.OrganizationUsers.AsNoTracking()
        join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
        join role in dbContext.Roles.AsNoTracking() on membership.RoleId equals role.Id
        where membership.OrganizationId == organizationId
            && (membership.Status == UserStatus.Active || membership.Status == UserStatus.Suspended)
        select new RowProjection
        {
            Id = membership.Id,
            IsInvitation = false,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            RoleCode = role.Code,
            RoleName = role.Name,
            IsAllBranches = membership.IsAllBranches || role.Code == PermissionCatalog.Owner,
            Status = membership.Status == UserStatus.Active ? UserRowStatuses.Active : UserRowStatuses.Suspended,
            ExpiresAt = membership.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            SortKey = user.FirstName + " " + user.LastName,
        };

    // LastLoginAt reads accepted_at, which is always null for an open invitation:
    // a typed null keeps the UNION column types aligned.
    private IQueryable<RowProjection> InvitationRows(Guid organizationId) =>
        from invitation in dbContext.UserInvitations.AsNoTracking()
        join role in dbContext.Roles.AsNoTracking() on invitation.RoleId equals role.Id
        where invitation.OrganizationId == organizationId
            && invitation.AcceptedAt == null
            && invitation.RevokedAt == null
        select new RowProjection
        {
            Id = invitation.Id,
            IsInvitation = true,
            FirstName = invitation.FirstName,
            LastName = invitation.LastName,
            Email = invitation.Email,
            RoleCode = role.Code,
            RoleName = role.Name,
            IsAllBranches = invitation.IsAllBranches,
            Status = UserRowStatuses.PendingInvitation,
            ExpiresAt = invitation.ExpiresAt,
            LastLoginAt = invitation.AcceptedAt,
            SortKey = invitation.FirstName + " " + invitation.LastName,
        };

    private async Task<UserRowView?> GetRowAsync(
        Guid organizationId, Guid currentMembershipId, Guid id, bool isInvitation, CancellationToken cancellationToken)
    {
        var rows = isInvitation
            ? await InvitationRows(organizationId).Where(row => row.Id == id).ToListAsync(cancellationToken)
            : await MemberRows(organizationId).Where(row => row.Id == id).ToListAsync(cancellationToken);

        return (await HydrateAsync(organizationId, currentMembershipId, rows, cancellationToken)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<UserRowView>> HydrateAsync(
        Guid organizationId,
        Guid currentMembershipId,
        List<RowProjection> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow();
        var memberIds = rows.Where(row => !row.IsInvitation).Select(row => row.Id).ToList();
        var invitationIds = rows.Where(row => row.IsInvitation).Select(row => row.Id).ToList();

        var memberBranches = await (
            from link in dbContext.OrganizationUserBranches.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on link.BranchId equals branch.Id
            where memberIds.Contains(link.OrganizationUserId) && branch.OrganizationId == organizationId
            select new { Owner = link.OrganizationUserId, branch.Id, branch.Name })
            .ToListAsync(cancellationToken);

        var invitationBranches = await (
            from link in dbContext.InvitationBranches.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on link.BranchId equals branch.Id
            where invitationIds.Contains(link.InvitationId) && branch.OrganizationId == organizationId
            select new { Owner = link.InvitationId, branch.Id, branch.Name })
            .ToListAsync(cancellationToken);

        var profiles = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId
                && profile.OrganizationUserId != null
                && memberIds.Contains(profile.OrganizationUserId.Value))
            .OrderBy(profile => profile.CreatedAt)
            .Select(profile => new { Owner = profile.OrganizationUserId!.Value, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken);

        var activeOwners = await CountActiveOwnersAsync(organizationId, cancellationToken);

        var branchesByOwner = memberBranches.Concat(invitationBranches)
            .GroupBy(link => link.Owner)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BranchRefView>)[.. group
                    .OrderBy(link => link.Name, StringComparer.Ordinal)
                    .Select(link => new BranchRefView(link.Id, link.Name))]);

        return [.. rows.Select(row =>
        {
            var applicable = PermissionCatalog.Find(row.RoleCode)?.HasTeamProfile == true;
            var profile = applicable && !row.IsInvitation ? profiles.FirstOrDefault(p => p.Owner == row.Id) : null;
            var isActiveOwner = !row.IsInvitation
                && row.Status == UserRowStatuses.Active
                && row.RoleCode == PermissionCatalog.Owner;

            return new UserRowView(
                row.Id,
                row.IsInvitation ? UserRowKinds.Invitation : UserRowKinds.Member,
                row.FirstName,
                row.LastName,
                row.Email,
                row.RoleCode,
                row.RoleName,
                row.IsAllBranches,
                row.IsAllBranches ? [] : branchesByOwner.GetValueOrDefault(row.Id, []),
                new TeamProfileView(applicable, profile is null ? null : $"{profile.FirstName} {profile.LastName}"),
                row.Status,
                row.IsInvitation && row.ExpiresAt <= now,
                row.IsInvitation ? null : row.LastLoginAt,
                !row.IsInvitation && row.Id == currentMembershipId,
                isActiveOwner && activeOwners == 1);
        })];
    }

    private Task<int> CountActiveOwnersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (from membership in dbContext.OrganizationUsers.AsNoTracking()
         join role in dbContext.Roles.AsNoTracking() on membership.RoleId equals role.Id
         where membership.OrganizationId == organizationId
            && membership.Status == UserStatus.Active
            && role.Code == PermissionCatalog.Owner
         select membership.Id)
        .CountAsync(cancellationToken);

    // BR-15: locks the organization's Owner memberships and the target in one
    // deterministic (ORDER BY id) statement so concurrent suspend/downgrade
    // calls serialize and cannot leave zero active Owners.
    private async Task<short> LockOwnersAndTargetAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var ownerRoleId = await dbContext.Roles.AsNoTracking()
            .Where(role => role.Code == PermissionCatalog.Owner)
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);

        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM organization_users
                WHERE organization_id = {organizationId} AND (role_id = {ownerRoleId} OR id = {membershipId})
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return ownerRoleId;
    }

    private Task<OrganizationUser?> LoadMemberAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken) =>
        dbContext.OrganizationUsers.SingleOrDefaultAsync(
            member => member.OrganizationId == organizationId
                && member.Id == membershipId
                && (member.Status == UserStatus.Active || member.Status == UserStatus.Suspended),
            cancellationToken);

    private async Task<UserInvitation?> LoadLockedInvitationAsync(
        Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM user_invitations
                WHERE organization_id = {organizationId} AND id = {invitationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return await dbContext.UserInvitations.SingleOrDefaultAsync(
            invitation => invitation.OrganizationId == organizationId && invitation.Id == invitationId,
            cancellationToken);
    }

    private async Task<(short Id, string Name)> GetRoleByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.AsNoTracking()
            .Where(candidate => candidate.Code == code)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .SingleAsync(cancellationToken);

        return (role.Id, role.Name);
    }

    private Task<string> GetRoleCodeAsync(short roleId, CancellationToken cancellationToken) =>
        dbContext.Roles.AsNoTracking()
            .Where(role => role.Id == roleId)
            .Select(role => role.Code)
            .SingleAsync(cancellationToken);

    private async Task<bool> BranchesAreValidAsync(
        Guid organizationId, IReadOnlyList<Guid> branchIds, CancellationToken cancellationToken)
    {
        if (branchIds.Count == 0)
        {
            return true;
        }

        var found = await dbContext.Branches.AsNoTracking().CountAsync(
            branch => branch.OrganizationId == organizationId && branch.IsActive && branchIds.Contains(branch.Id),
            cancellationToken);

        return found == branchIds.Count;
    }

    private async Task<InvitationDeliveryContext> BuildDeliveryContextAsync(
        Guid organizationId,
        Guid actorUserId,
        string email,
        string firstName,
        string roleName,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var organizationName = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Name)
            .SingleAsync(cancellationToken);

        var inviter = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .SingleAsync(cancellationToken);

        return new InvitationDeliveryContext(email, firstName, organizationName, inviter, roleName, expiresAt);
    }

    // The delivery call sits inside the transaction: a failure rolls the
    // whole mutation back (BR-11). Cancellation is never swallowed and the
    // exception message is never logged.
    private static async Task<bool> TryDeliverAsync(
        IDbContextTransaction transaction,
        Func<InvitationDeliveryContext, CancellationToken, Task> beforeCommit,
        InvitationDeliveryContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await beforeCommit(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private void AddStatusAudit(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid membershipId,
        string action,
        string before,
        string after) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            action,
            UserAuditActions.MemberEntity,
            actorUserId: actorUserId,
            entityId: membershipId,
            ipAddress: clientIp,
            beforeData: JsonSerializer.Serialize(new Dictionary<string, object?> { ["status"] = before }),
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?> { ["status"] = after })));

    private static string AccessJson(string roleCode, bool isAllBranches, IEnumerable<Guid> branchIds) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["roleCode"] = roleCode,
            ["isAllBranches"] = isAllBranches,
            ["branchIds"] = SortedIds(branchIds),
        });

    private static string[] SortedIds(IEnumerable<Guid> ids) =>
        [.. ids.Select(id => id.ToString()).OrderBy(id => id, StringComparer.Ordinal)];

    private static bool SameSet(IReadOnlyCollection<Guid> current, IReadOnlyCollection<Guid> target) =>
        current.Count == target.Count && current.All(target.Contains);

    private static bool IsConstraintViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } postgres
        && postgres.ConstraintName == constraintName;
}
