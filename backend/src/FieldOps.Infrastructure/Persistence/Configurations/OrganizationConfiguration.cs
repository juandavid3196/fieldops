using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration
    : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(organization => organization.Id);

        builder.Property(organization => organization.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(organization => organization.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(organization => organization.LegalName)
            .HasMaxLength(200);

        builder.Property(organization => organization.TaxId)
            .HasMaxLength(60);

        builder.Property(organization => organization.Email)
            .HasMaxLength(254);

        builder.Property(organization => organization.Phone)
            .HasMaxLength(40);

        builder.Property(organization => organization.Timezone)
            .HasMaxLength(80)
            .HasDefaultValue("UTC")
            .IsRequired();

        builder.Property(organization => organization.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("USD")
            .IsRequired();

        // numeric(7,4) with CHECK (default_tax_rate BETWEEN 0 AND 100).
        builder.Property(organization => organization.DefaultTaxRate)
            .HasPrecision(7, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(organization => organization.QuotePrefix)
            .HasMaxLength(20)
            .HasDefaultValue("Q")
            .IsRequired();

        builder.Property(organization => organization.WorkOrderPrefix)
            .HasMaxLength(20)
            .HasDefaultValue("WO")
            .IsRequired();

        builder.Property(organization => organization.InvoicePrefix)
            .HasMaxLength(20)
            .HasDefaultValue("INV")
            .IsRequired();

        builder.Property(organization => organization.NextQuoteNumber)
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(organization => organization.NextWorkOrderNumber)
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(organization => organization.NextInvoiceNumber)
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(organization => organization.RequireCustomerSignature)
            .HasDefaultValue(false)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(organization => organization.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(organization => organization.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(organization => organization.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_organizations_default_tax_rate",
            "default_tax_rate BETWEEN 0 AND 100"));
    }
}
