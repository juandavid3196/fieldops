using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class OrganizationRegistrationStore(FieldOpsDbContext dbContext)
    : IOrganizationRegistrationStore
{
    private const string OwnerRoleCode = "owner";

    // Stored emails are already normalized, so an exact match uses UNIQUE (email).
    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

    public async Task<short?> FindOwnerRoleIdAsync(CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(role => role.Code == OwnerRoleCode, cancellationToken);

        return role?.Id;
    }

    // One SaveChangesAsync call: EF Core wraps all six inserts in a single
    // implicit transaction, so either every row is written or none is
    // (FR-04, FR-09, AC-19).
    public async Task SaveRegistrationAsync(
        Organization organization,
        Branch branch,
        User user,
        OrganizationUser membership,
        OrganizationUserBranch membershipBranch,
        AuditLog auditLog,
        CancellationToken cancellationToken)
    {
        dbContext.Organizations.Add(organization);
        dbContext.Branches.Add(branch);
        dbContext.Users.Add(user);
        dbContext.OrganizationUsers.Add(membership);
        dbContext.OrganizationUserBranches.Add(membershipBranch);
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_users_email",
        })
        {
            throw new DuplicateEmailException(user.Email, ex);
        }
    }
}
