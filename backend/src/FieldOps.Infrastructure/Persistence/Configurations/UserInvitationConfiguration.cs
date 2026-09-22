using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class UserInvitationConfiguration
    : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable("user_invitations");

        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(invitation => invitation.OrganizationId)
            .IsRequired();

        builder.Property(invitation => invitation.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(invitation => invitation.RoleId)
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
