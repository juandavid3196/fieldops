using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerPortalInvitationConfiguration : IEntityTypeConfiguration<CustomerPortalInvitation>
{
    public const string OpenContactIndexName = "ux_customer_portal_invitations_open";

    public void Configure(EntityTypeBuilder<CustomerPortalInvitation> builder)
    {
        builder.ToTable(
            "customer_portal_invitations",
            table => table.HasCheckConstraint(
                "ck_customer_portal_invitations_expires_after_created",
                "expires_at > created_at"));

        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(invitation => invitation.OrganizationId).IsRequired();

        builder.Property(invitation => invitation.ContactId).IsRequired();

        builder.Property(invitation => invitation.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(invitation => invitation.TokenHash)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(invitation => invitation.InvitedByUserId).IsRequired();

        builder.Property(invitation => invitation.ExpiresAt).IsRequired();

        builder.Property(invitation => invitation.AcceptedAt);

        builder.Property(invitation => invitation.RevokedAt);

        builder.Property(invitation => invitation.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (token_hash)
        builder.HasIndex(invitation => invitation.TokenHash)
            .IsUnique();

        // CREATE UNIQUE INDEX ux_customer_portal_invitations_open ON customer_portal_invitations (contact_id) WHERE accepted_at IS NULL AND revoked_at IS NULL
        builder.HasIndex(invitation => invitation.ContactId)
            .IsUnique()
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL")
            .HasDatabaseName(OpenContactIndexName);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(invitation => invitation.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts (organization_id, id)
        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(invitation => new { invitation.OrganizationId, invitation.ContactId })
            .HasPrincipalKey(contact => new { contact.OrganizationId, contact.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(invitation => invitation.InvitedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
