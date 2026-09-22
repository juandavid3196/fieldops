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
        builder.ToTable("customer_contacts");

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

        builder.Property(contact => contact.PortalUserId);

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
