using FieldOps.Domain.Branches;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("properties");

        builder.HasKey(property => property.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(property => new
        {
            property.OrganizationId,
            property.Id,
        });

        builder.Property(property => property.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(property => property.OrganizationId)
            .IsRequired();

        builder.Property(property => property.CustomerId)
            .IsRequired();

        builder.Property(property => property.BranchId);

        builder.Property(property => property.Name)
            .HasMaxLength(140)
            .IsRequired();

        builder.Property(property => property.AddressLine1)
            .HasMaxLength(180)
            .IsRequired();

        builder.Property(property => property.AddressLine2)
            .HasMaxLength(180);

        builder.Property(property => property.City)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(property => property.StateRegion)
            .HasMaxLength(100);

        builder.Property(property => property.PostalCode)
            .HasMaxLength(30);

        builder.Property(property => property.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired();

        builder.Property(property => property.Latitude)
            .HasPrecision(9, 6);

        builder.Property(property => property.Longitude)
            .HasPrecision(9, 6);

        builder.Property(property => property.AccessInstructions)
            .HasColumnType("text");

        builder.Property(property => property.ServiceNotes)
            .HasColumnType("text");

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(property => property.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(property => property.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(property => property.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE INDEX ix_properties_customer ON properties (organization_id, customer_id)
        builder.HasIndex(property => new { property.OrganizationId, property.CustomerId })
            .HasDatabaseName("ix_properties_customer");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(property => property.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(property => new { property.OrganizationId, property.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(property => property.BranchId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
