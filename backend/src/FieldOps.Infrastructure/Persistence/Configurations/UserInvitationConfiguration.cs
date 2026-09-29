using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class UserInvitationConfiguration
    : IEntityTypeConfiguration<UserInvitation>
{
    public const string OpenEmailIndexName = "ux_user_invitations_open_email";

    public const string ExpiresAfterCreatedConstraintName = "ck_user_invitations_expires_after_created";

    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable(
            "user_invitations",
            table => table.HasCheckConstraint(
                ExpiresAfterCreatedConstraintName,
                "expires_at > created_at"));

        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(invitation => invitation.OrganizationId)
            .IsRequired();

        builder.Property(invitation => invitation.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(invitation => invitation.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(invitation => invitation.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(invitation => invitation.RoleId)
            .IsRequired();

        builder.Property(invitation => invitation.IsAllBranches)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(invitation => invitation.LinkTeamProfile)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(invitation => invitation.TokenHash)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(invitation => invitation.InvitedByUserId)
            .IsRequired();

        builder.Property(invitation => invitation.ExpiresAt)
            .IsRequired();

        builder.Property(invitation => invitation.AcceptedAt);

        builder.Property(invitation => invitation.RevokedAt);

        builder.Property(invitation => invitation.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (token_hash)
        builder.HasIndex(invitation => invitation.TokenHash)
            .IsUnique();

        // At most one open invitation per organization and email (BR-16).
        builder.HasIndex(invitation => new
        {
            invitation.OrganizationId,
            invitation.Email,
        })
            .IsUnique()
            .HasDatabaseName(OpenEmailIndexName)
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(invitation => invitation.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(invitation => invitation.RoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(invitation => invitation.InvitedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
