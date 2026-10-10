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

        // SA-11 (invoices-payments-management)
        builder.Property(organization => organization.PaymentPrefix)
            .HasMaxLength(20)
            .HasDefaultValue("PAY")
            .IsRequired();

        builder.Property(organization => organization.NextPaymentNumber)
            .HasDefaultValue(1L)
            .IsRequired();

        // public_slug varchar(60) NOT NULL (no default): written once at
        // registration; the migration adds it nullable, backfills, then sets NOT NULL.
        builder.Property(organization => organization.PublicSlug)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(organization => organization.RequestPrefix)
            .HasMaxLength(20)
            .HasDefaultValue("REQ")
            .IsRequired();

        builder.Property(organization => organization.NextRequestNumber)
            .HasDefaultValue(1L)
            .IsRequired();

        builder.HasIndex(organization => organization.PublicSlug)
            .HasDatabaseName("ux_organizations_public_slug")
            .IsUnique();

        builder.Property(organization => organization.RequireCustomerSignature)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(organization => organization.Website)
            .HasMaxLength(255);

        builder.Property(organization => organization.AddressLine1)
            .HasMaxLength(180);

        builder.Property(organization => organization.City)
            .HasMaxLength(100);

        builder.Property(organization => organization.StateRegion)
            .HasMaxLength(100);

        builder.Property(organization => organization.PostalCode)
            .HasMaxLength(30);

        builder.Property(organization => organization.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.Property(organization => organization.PricesIncludeTax)
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

        // SA-18 (customer-invoice-payments)
        builder.Property(organization => organization.StripeAccountId)
            .HasMaxLength(255);

        builder.Property(organization => organization.BankName)
            .HasMaxLength(120);

        builder.Property(organization => organization.BankAccountNumberCiphertext)
            .HasColumnType("bytea");

        builder.Property(organization => organization.BankAccountLast4)
            .HasMaxLength(4);

        builder.Property(organization => organization.BankRoutingNumber)
            .HasMaxLength(9)
            .IsFixedLength();

        builder.Property(organization => organization.BankDetailsUpdatedAt);

        // Concurrency token (BR-07): every write sets it explicitly, and the
        // store compares the client's submitted value against it before
        // mutating, then relies on this token to catch a race between that
        // check and SaveChangesAsync. Pure model annotation: no DDL change.
        builder.Property(organization => organization.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsConcurrencyToken()
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_organizations_default_tax_rate",
                "default_tax_rate BETWEEN 0 AND 100");

            table.HasCheckConstraint(
                "ck_organizations_public_slug",
                "public_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(public_slug) BETWEEN 1 AND 60");

            // SA-18: the five bank columns are all null or all set.
            table.HasCheckConstraint(
                "ck_organizations_bank_details_all_or_none",
                "(bank_name IS NULL) = (bank_account_number_ciphertext IS NULL) AND (bank_name IS NULL) = (bank_account_last4 IS NULL) "
                + "AND (bank_name IS NULL) = (bank_routing_number IS NULL) AND (bank_name IS NULL) = (bank_details_updated_at IS NULL)");
        });
    }
}
