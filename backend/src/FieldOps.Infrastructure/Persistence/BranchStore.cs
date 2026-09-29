using System.Net;
using System.Text.Json;
using FieldOps.Application.Auditing;
using FieldOps.Application.Features.Branches;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class BranchStore(FieldOpsDbContext dbContext) : IBranchStore
{
    private const string BranchCodeUniqueIndex = "ix_branches_organization_id_code";

    public async Task<IReadOnlyList<BranchListItemView>> ListAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(organizationId, membershipId, cancellationToken);

        if (!access.MembershipExists)
        {
            return [];
        }

        var query = ScopedBranches(organizationId, membershipId, access.CanAccessAll);

        return await query
            .OrderBy(branch => branch.Name)
            .Select(branch => new BranchListItemView(
                branch.Id,
                branch.Name,
                branch.Code,
                branch.AddressLine1,
                branch.AddressLine2,
                branch.City,
                branch.StateRegion,
                branch.PostalCode,
                branch.CountryCode,
                branch.Timezone,
                branch.IsActive,
                branch.IsMain,
                dbContext.TechnicianProfiles.Count(technician =>
                    technician.OrganizationId == organizationId
                    && technician.BranchId == branch.Id
                    && technician.Status == TechnicianStatus.Active)))
            .ToListAsync(cancellationToken);
    }

    public async Task<BranchDetailView?> GetDetailAsync(
        Guid organizationId, Guid membershipId, Guid branchId, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(organizationId, membershipId, cancellationToken);

        if (!access.MembershipExists)
        {
            return null;
        }

        var query = ScopedBranches(organizationId, membershipId, access.CanAccessAll);

        return await query
            .Where(branch => branch.Id == branchId)
            .Select(branch => new BranchDetailView(
                branch.Id,
                branch.Name,
                branch.Code,
                branch.Email,
                branch.Phone,
                branch.AddressLine1,
                branch.AddressLine2,
                branch.City,
                branch.StateRegion,
                branch.PostalCode,
                branch.CountryCode,
                branch.Timezone,
                branch.BusinessHours,
                branch.IsActive,
                branch.IsMain,
                branch.ServicePostalCodes,
                branch.UsesCompanyBilling,
                branch.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Branch?> GetForManageAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken) =>
        dbContext.Branches.SingleOrDefaultAsync(
            branch => branch.OrganizationId == organizationId && branch.Id == branchId, cancellationToken);

    public Task<int> CountAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Branches.CountAsync(branch => branch.OrganizationId == organizationId, cancellationToken);

    public Task<bool> CodeExistsAsync(
        Guid organizationId, string code, Guid? excludeBranchId, CancellationToken cancellationToken) =>
        dbContext.Branches.AnyAsync(
            branch => branch.OrganizationId == organizationId
                && branch.Code == code
                && (excludeBranchId == null || branch.Id != excludeBranchId),
            cancellationToken);

    // One SaveChangesAsync call: the branch insert and its branch.created
    // audit row are written together, or neither is (FR-07).
    public async Task CreateAsync(Branch branch, AuditLog auditLog, CancellationToken cancellationToken)
    {
        dbContext.Branches.Add(branch);
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsBranchCodeUniqueViolation(ex))
        {
            throw new DuplicateBranchCodeException(branch.OrganizationId, branch.Code, ex);
        }
    }

    // UpdatedAt is a concurrency token (BR-07): a row changed since it was
    // loaded makes this throw DbUpdateConcurrencyException instead of
    // overwriting it. The unique index race (BR-04) is also possible on an
    // update that changes the code.
    public async Task<bool> TrySaveUpdateAsync(Branch branch, AuditLog auditLog, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException ex) when (IsBranchCodeUniqueViolation(ex))
        {
            throw new DuplicateBranchCodeException(branch.OrganizationId, branch.Code, ex);
        }
    }

    // BR-06: a plain unlocked read handles the common cases (not found,
    // already inactive) without opening a transaction. Only the actual
    // deactivation of an active branch opens the row-locking transaction
    // that makes the last-active check safe under concurrent callers.
    public async Task<DeactivateBranchOutcome> DeactivateAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var branch = await dbContext.Branches.SingleOrDefaultAsync(
            b => b.OrganizationId == organizationId && b.Id == branchId, cancellationToken);

        if (branch is null)
        {
            return DeactivateBranchOutcome.NotFound;
        }

        return await DeactivateLoadedAsync(
            branch, actorUserId, clientIp, now, allowConcurrencyRetry: true, cancellationToken);
    }

    // UpdatedAt is a concurrency token (BR-07) on every save of this row, not
    // just PUT. A concurrent PUT can commit a new UpdatedAt between the
    // unlocked read above and this method's own SaveChangesAsync (taken
    // inside the BR-06 lock transaction), which throws
    // DbUpdateConcurrencyException. Neither DeactivateBranchOutcome nor the
    // spec's API contract defines a distinct "stale" case for this endpoint,
    // so a losing race is resolved by rolling back, reloading the row's
    // now-current state and re-evaluating from there once (BR-08: already in
    // the target state resolves to NoOp; still active re-enters the BR-06
    // guard against the fresh state).
    private async Task<DeactivateBranchOutcome> DeactivateLoadedAsync(
        Branch branch,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        bool allowConcurrencyRetry,
        CancellationToken cancellationToken)
    {
        if (!branch.IsActive)
        {
            return DeactivateBranchOutcome.NoOp;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Raw SQL: EF has no declarative row-lock API. ORDER BY id keeps the
        // lock order deterministic across concurrent callers to avoid
        // deadlocks. The count is taken in C# because Postgres rejects
        // count(*) combined with FOR UPDATE.
        var lockedActiveIds = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM branches
                WHERE organization_id = {branch.OrganizationId} AND is_active = true
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        // The tracked row was read before the lock: reload it now that the
        // lock is held, so IsActive/IsMain reflect any change that committed
        // while this call was waiting (BR-11).
        await dbContext.Entry(branch).ReloadAsync(cancellationToken);

        if (!branch.IsActive || !lockedActiveIds.Contains(branch.Id))
        {
            // A concurrent duplicate call deactivated this exact branch while
            // this call's FOR UPDATE was blocked behind it: branch.Id is
            // absent from the locked active set purely because it is no
            // longer active, not because it is the organization's last one.
            // BR-08 requires this to resolve as an idempotent NoOp, not a
            // BR-06 LastActiveConflict.
            await transaction.RollbackAsync(cancellationToken);
            return DeactivateBranchOutcome.NoOp;
        }

        // BR-11: the main branch is checked before the last-active rule.
        if (branch.IsMain)
        {
            await transaction.RollbackAsync(cancellationToken);
            return DeactivateBranchOutcome.MainBranchConflict;
        }

        if (lockedActiveIds.Count <= 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return DeactivateBranchOutcome.LastActiveConflict;
        }

        branch.Deactivate(now);

        var (before, after) = AuditFieldDiff.ForStateChange(before: true, after: false);

        var auditLog = AuditLog.Create(
            branch.OrganizationId,
            "branch.deactivated",
            "branch",
            actorUserId: actorUserId,
            entityId: branch.Id,
            branchId: branch.Id,
            ipAddress: clientIp,
            beforeData: before,
            afterData: after);
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return DeactivateBranchOutcome.Changed;
        }
        catch (DbUpdateConcurrencyException) when (allowConcurrencyRetry)
        {
            dbContext.AuditLogs.Remove(auditLog);
            await transaction.RollbackAsync(cancellationToken);
            await dbContext.Entry(branch).ReloadAsync(cancellationToken);

            return await DeactivateLoadedAsync(
                branch, actorUserId, clientIp, now, allowConcurrencyRetry: false, cancellationToken);
        }
    }

    // BR-11: takes the same lock as deactivation (the organization's active
    // rows, ORDER BY id FOR UPDATE) so set-main and deactivate serialize. The
    // previous main is cleared with a set-based UPDATE and the target is set
    // by the tracked SaveChanges: never both in one statement, so the partial
    // unique index ux_branches_org_main is never violated mid-transaction.
    public async Task<SetMainBranchOutcome> SetMainAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var branch = await dbContext.Branches.SingleOrDefaultAsync(
            b => b.OrganizationId == organizationId && b.Id == branchId, cancellationToken);

        if (branch is null)
        {
            return SetMainBranchOutcome.NotFound;
        }

        return await SetMainLoadedAsync(
            branch, actorUserId, clientIp, now, allowConcurrencyRetry: true, cancellationToken);
    }

    private async Task<SetMainBranchOutcome> SetMainLoadedAsync(
        Branch branch,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        bool allowConcurrencyRetry,
        CancellationToken cancellationToken)
    {
        var organizationId = branch.OrganizationId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockedActiveIds = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM branches
                WHERE organization_id = {organizationId} AND is_active = true
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        await dbContext.Entry(branch).ReloadAsync(cancellationToken);

        if (!branch.IsActive || !lockedActiveIds.Contains(branch.Id))
        {
            await transaction.RollbackAsync(cancellationToken);
            return SetMainBranchOutcome.InactiveConflict;
        }

        if (branch.IsMain)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SetMainBranchOutcome.NoOp;
        }

        var targetId = branch.Id;

        var previousMainId = await dbContext.Branches.AsNoTracking()
            .Where(b => b.OrganizationId == organizationId && b.IsMain && b.Id != targetId)
            .Select(b => (Guid?)b.Id)
            .SingleOrDefaultAsync(cancellationToken);

        await dbContext.Branches
            .Where(b => b.OrganizationId == organizationId && b.IsMain && b.Id != targetId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.IsMain, false)
                    .SetProperty(b => b.UpdatedAt, now),
                cancellationToken);

        branch.SetMain(now);

        var (before, after) = AuditFieldDiff.ForSetAsMain();

        var auditLog = AuditLog.Create(
            organizationId,
            SetMainBranchHandler.SetAsMainAuditAction,
            "branch",
            actorUserId: actorUserId,
            entityId: branch.Id,
            branchId: branch.Id,
            ipAddress: clientIp,
            beforeData: before,
            afterData: after,
            metadata: JsonSerializer.Serialize(
                new Dictionary<string, object?> { ["previousMainBranchId"] = previousMainId }));
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return SetMainBranchOutcome.Changed;
        }
        catch (DbUpdateConcurrencyException) when (allowConcurrencyRetry)
        {
            dbContext.AuditLogs.Remove(auditLog);
            await transaction.RollbackAsync(cancellationToken);
            await dbContext.Entry(branch).ReloadAsync(cancellationToken);

            return await SetMainLoadedAsync(
                branch, actorUserId, clientIp, now, allowConcurrencyRetry: false, cancellationToken);
        }
    }

    public async Task<ReactivateBranchOutcome> ReactivateAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var branch = await dbContext.Branches.SingleOrDefaultAsync(
            b => b.OrganizationId == organizationId && b.Id == branchId, cancellationToken);

        if (branch is null)
        {
            return ReactivateBranchOutcome.NotFound;
        }

        return await ReactivateLoadedAsync(
            branch, actorUserId, clientIp, now, allowConcurrencyRetry: true, cancellationToken);
    }

    // Same UpdatedAt concurrency-token race as DeactivateLoadedAsync above,
    // resolved the same way: reload the row's now-current state and
    // re-evaluate once rather than letting DbUpdateConcurrencyException
    // surface as an undocumented 500 (BR-08 NoOp when already active).
    private async Task<ReactivateBranchOutcome> ReactivateLoadedAsync(
        Branch branch,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        bool allowConcurrencyRetry,
        CancellationToken cancellationToken)
    {
        if (branch.IsActive)
        {
            return ReactivateBranchOutcome.NoOp;
        }

        branch.Reactivate(now);

        var (before, after) = AuditFieldDiff.ForStateChange(before: false, after: true);

        var auditLog = AuditLog.Create(
            branch.OrganizationId,
            "branch.reactivated",
            "branch",
            actorUserId: actorUserId,
            entityId: branch.Id,
            branchId: branch.Id,
            ipAddress: clientIp,
            beforeData: before,
            afterData: after);
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return ReactivateBranchOutcome.Changed;
        }
        catch (DbUpdateConcurrencyException) when (allowConcurrencyRetry)
        {
            dbContext.AuditLogs.Remove(auditLog);
            await dbContext.Entry(branch).ReloadAsync(cancellationToken);

            return await ReactivateLoadedAsync(
                branch, actorUserId, clientIp, now, allowConcurrencyRetry: false, cancellationToken);
        }
    }

    // BR-11: an owner or a membership with is_all_branches = true accesses
    // every branch of the organization; otherwise only branches linked via
    // organization_user_branches. Re-derived from membershipId on every
    // call rather than added to the session contract.
    private async Task<(bool CanAccessAll, bool MembershipExists)> ResolveAccessAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await (
            from organizationUser in dbContext.OrganizationUsers.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on organizationUser.RoleId equals role.Id
            where organizationUser.Id == membershipId && organizationUser.OrganizationId == organizationId
            select new { organizationUser.IsAllBranches, RoleCode = role.Code })
            .SingleOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return (false, false);
        }

        var canAccessAll = string.Equals(membership.RoleCode, "owner", StringComparison.Ordinal)
            || membership.IsAllBranches;

        return (canAccessAll, true);
    }

    private IQueryable<Branch> ScopedBranches(Guid organizationId, Guid membershipId, bool canAccessAll)
    {
        var query = dbContext.Branches.AsNoTracking().Where(branch => branch.OrganizationId == organizationId);

        if (!canAccessAll)
        {
            query = query.Where(branch => dbContext.OrganizationUserBranches
                .Any(link => link.OrganizationUserId == membershipId && link.BranchId == branch.Id));
        }

        return query;
    }

    private static bool IsBranchCodeUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: BranchCodeUniqueIndex,
        };
}
