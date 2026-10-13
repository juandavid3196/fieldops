using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerContactConfiguration
    : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("customer_contacts", table =>
        {
            table.HasCheckConstraint(
                "ck_customer_contacts_preferred_channel",
                "prefers_email OR prefers_sms");

            // SA-19: the link user and the link time are set together.
            table.HasCheckConstraint(
                "ck_customer_contacts_portal_link",
                "(portal_user_id IS NULL) = (portal_linked_at IS NULL)");
        });

        builder.HasKey(contact => contact.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(contact => new
        {
            contact.OrganizationId,
            contact.Id,
        });

        builder.Property(contact => contact.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(contact => contact.OrganizationId)
            .IsRequired();

        builder.Property(contact => contact.CustomerId)
            .IsRequired();

        builder.Property(contact => contact.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(contact => contact.LastName)
            .HasMaxLength(100);

        builder.Property(contact => contact.Email)
            .HasMaxLength(254);

        builder.Property(contact => contact.Phone)
            .HasMaxLength(40);

        builder.Property(contact => contact.Title)
            .HasMaxLength(100);

        builder.Property(contact => contact.IsPrimary)
            .HasDefaultValue(false)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(contact => contact.PrefersEmail)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(contact => contact.PrefersSms)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(contact => contact.PortalUserId);

        builder.Property(contact => contact.PortalLinkedAt);

        builder.Property(contact => contact.PortalUpdatesSeenAt);

        // CREATE UNIQUE INDEX ux_customer_contacts_org_portal_user ON customer_contacts (organization_id, portal_user_id) WHERE portal_user_id IS NOT NULL
        builder.HasIndex(contact => new { contact.OrganizationId, contact.PortalUserId })
            .IsUnique()
            .HasFilter("portal_user_id IS NOT NULL")
            .HasDatabaseName("ux_customer_contacts_org_portal_user");

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(contact => contact.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(contact => contact.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(contact => contact.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE INDEX ix_contacts_org_email ON customer_contacts (organization_id, email)
        builder.HasIndex(contact => new { contact.OrganizationId, contact.Email })
            .HasDatabaseName("ix_contacts_org_email");

        // CREATE INDEX ix_contacts_org_phone ON customer_contacts (organization_id, phone)
        builder.HasIndex(contact => new { contact.OrganizationId, contact.Phone })
            .HasDatabaseName("ix_contacts_org_phone");

        // CREATE UNIQUE INDEX ux_customer_contacts_primary ON customer_contacts (customer_id) WHERE is_primary
        builder.HasIndex(contact => contact.CustomerId)
            .IsUnique()
            .HasFilter("is_primary")
            .HasDatabaseName("ux_customer_contacts_primary");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(contact => contact.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(contact => new { contact.OrganizationId, contact.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(contact => contact.PortalUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
