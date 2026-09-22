using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationUserConfiguration
    : IEntityTypeConfiguration<OrganizationUser>
{
    public void Configure(EntityTypeBuilder<OrganizationUser> builder)
    {
        builder.ToTable("organization_users");

        builder.HasKey(organizationUser => organizationUser.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(organizationUser => new
        {
            organizationUser.OrganizationId,
            organizationUser.Id,
        });

        builder.Property(organizationUser => organizationUser.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(organizationUser => organizationUser.OrganizationId)
            .IsRequired();

        builder.Property(organizationUser => organizationUser.UserId)
            .IsRequired();

        builder.Property(organizationUser => organizationUser.RoleId)
            .IsRequired();

        // PostgreSQL enum user_status, mapped in FieldOpsDbContext.
        builder.Property(organizationUser => organizationUser.Status)
            .IsRequired();

        builder.Property(organizationUser => organizationUser.IsAllBranches)
            .IsRequired();

        builder.Property(organizationUser => organizationUser.InvitedByUserId);

        builder.Property(organizationUser => organizationUser.JoinedAt);

        builder.Property(organizationUser => organizationUser.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(organizationUser => organizationUser.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, user_id)
        builder.HasIndex(organizationUser => new
        {
            organizationUser.OrganizationId,
            organizationUser.UserId,
        })
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(organizationUser => organizationUser.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(organizationUser => organizationUser.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(organizationUser => organizationUser.RoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(organizationUser => organizationUser.InvitedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
