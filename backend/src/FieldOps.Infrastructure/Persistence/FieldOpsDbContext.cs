using FieldOps.Domain.Branches;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed class FieldOpsDbContext(
    DbContextOptions<FieldOpsDbContext> options)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();

    public DbSet<OrganizationUserBranch> OrganizationUserBranches =>
        Set<OrganizationUserBranch>();

    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    public DbSet<InvitationBranch> InvitationBranches =>
        Set<InvitationBranch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Declares the enum with labels in enum declaration order.
        modelBuilder.HasPostgresEnum<UserStatus>(name: "user_status");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FieldOpsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
