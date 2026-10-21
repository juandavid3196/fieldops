using FieldOps.Application.Authentication;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class AuthenticationStore(FieldOpsDbContext dbContext) : IAuthenticationStore
{
    // Stored emails are already normalized, so an exact match uses UNIQUE (email).
    public Task<User?> FindUserByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(
            user => user.Email == normalizedEmail,
            cancellationToken);

    public Task<DateTimeOffset?> FindPhotoUpdatedAtAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserPhotos.AsNoTracking()
            .Where(photo => photo.UserId == userId)
            .Select(photo => (DateTimeOffset?)photo.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MembershipCandidate>> GetMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await (
            from membership in dbContext.OrganizationUsers.AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            join role in dbContext.Roles.AsNoTracking()
                on membership.RoleId equals role.Id
            where membership.UserId == userId
            select new MembershipCandidate(
                membership.Id,
                organization.Id,
                organization.Name,
                organization.IsActive,
                membership.Status,
                role.Code,
                role.Name,
                membership.JoinedAt,
                membership.CreatedAt))
            .ToListAsync(cancellationToken);

    // A password reset revokes earlier sessions (BR-15). signedInAt comes from a
    // cookie claim truncated to milliseconds and PasswordChangedAt is stored
    // truncated to milliseconds too (PasswordResetStore), so a later sign-in
    // never compares lower.
    public Task<SessionView?> FindActiveSessionAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
        DateTimeOffset signedInAt,
        CancellationToken cancellationToken) =>
        (
            from membership in dbContext.OrganizationUsers.AsNoTracking()
            join user in dbContext.Users.AsNoTracking()
                on membership.UserId equals user.Id
            join organization in dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            join role in dbContext.Roles.AsNoTracking()
                on membership.RoleId equals role.Id
            where membership.Id == membershipId
                && membership.UserId == userId
                && membership.OrganizationId == organizationId
                && membership.Status == UserStatus.Active
                && user.Status == UserStatus.Active
                && (user.PasswordChangedAt == null || user.PasswordChangedAt <= signedInAt)
                && organization.IsActive
            select new SessionView(
                new SessionUser(
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.Email,
                    dbContext.UserPhotos
                        .Where(photo => photo.UserId == user.Id)
                        .Select(photo => (DateTimeOffset?)photo.UpdatedAt)
                        .FirstOrDefault(),
                    user.PreferredLanguage),
                new SessionOrganization(organization.Id, organization.Name),
                new SessionRole(role.Code, role.Name)))
            .SingleOrDefaultAsync(cancellationToken);

    // One SaveChangesAsync call: EF Core wraps the user update and the audit
    // insert in a single transaction, so both are written or neither is.
    public async Task SaveSignInAsync(
        User user,
        AuditLog auditLog,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordSignedOutAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
