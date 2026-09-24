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

    public Task<SessionView?> FindActiveSessionAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
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
                && organization.IsActive
            select new SessionView(
                new SessionUser(user.Id, user.FirstName, user.LastName, user.Email),
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
}
