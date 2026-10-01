using FieldOps.Domain.Branches;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(customer => customer.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(customer => new
        {
            customer.OrganizationId,
            customer.Id,
        });

        builder.Property(customer => customer.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(customer => customer.OrganizationId)
            .IsRequired();

        builder.Property(customer => customer.BranchId)
            .IsRequired();

        // PostgreSQL enum customer_type, mapped in FieldOpsDbContext.
        builder.Property(customer => customer.Type)
            .IsRequired();

        builder.Property(customer => customer.DisplayName)
            .HasMaxLength(180)
            .IsRequired();

        builder.Property(customer => customer.LegalName)
            .HasMaxLength(200);

        builder.Property(customer => customer.TaxId)
            .HasMaxLength(60);

        builder.Property(customer => customer.PrimaryEmail)
            .HasMaxLength(254);

        builder.Property(customer => customer.PrimaryPhone)
            .HasMaxLength(40);

        builder.Property(customer => customer.BillingAddress)
            .HasColumnType("jsonb");

        builder.Property(customer => customer.Notes)
            .HasColumnType("text");

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(customer => customer.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(customer => customer.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(customer => customer.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE INDEX ix_customers_org_name ON customers (organization_id, display_name)
        builder.HasIndex(customer => new { customer.OrganizationId, customer.DisplayName })
            .HasDatabaseName("ix_customers_org_name");

        // CREATE INDEX ix_customers_org_branch ON customers (organization_id, branch_id)
        builder.HasIndex(customer => new { customer.OrganizationId, customer.BranchId })
            .HasDatabaseName("ix_customers_org_branch");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(customer => customer.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // branch_id uuid NOT NULL REFERENCES branches (id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(customer => customer.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(customer => new { customer.OrganizationId, customer.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
