using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteResponseConfiguration : IEntityTypeConfiguration<QuoteResponse>
{
    public void Configure(EntityTypeBuilder<QuoteResponse> builder)
    {
        builder.ToTable("quote_responses", table =>
        {
            table.HasCheckConstraint(
                "ck_quote_responses_response",
                "response IN ('approved','rejected','clarification_requested')");

            // Customer-quote-approval BR-25: the server totals exist exactly on an approval.
            table.HasCheckConstraint(
                "ck_quote_responses_totals",
                "(response = 'approved') = (subtotal IS NOT NULL AND discount_total IS NOT NULL AND tax_total IS NOT NULL AND total IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_quote_responses_totals_null",
                "response = 'approved' OR (subtotal IS NULL AND discount_total IS NULL AND tax_total IS NULL AND total IS NULL)");
            table.HasCheckConstraint("ck_quote_responses_subtotal", "subtotal >= 0");
            table.HasCheckConstraint("ck_quote_responses_discount_total", "discount_total >= 0");
            table.HasCheckConstraint("ck_quote_responses_tax_total", "tax_total >= 0");
            table.HasCheckConstraint("ck_quote_responses_total", "total >= 0");
        });

        builder.HasKey(response => response.Id);

        // UNIQUE (organization_id, quote_version_id, id): target of the selected optional lines.
        builder.HasAlternateKey(response => new
        {
            response.OrganizationId,
            response.QuoteVersionId,
            response.Id,
        });

        builder.Property(response => response.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(response => response.OrganizationId)
            .IsRequired();

        builder.Property(response => response.QuoteVersionId)
            .IsRequired();

        // PostgreSQL enum quote_status, mapped in FieldOpsDbContext; the check
        // constraint above narrows it to the three response-only labels.
        builder.Property(response => response.Response)
            .IsRequired();

        builder.Property(response => response.ResponderName)
            .HasMaxLength(180)
            .IsRequired();

        builder.Property(response => response.ResponderContactId);

        builder.Property(response => response.Comment)
            .HasColumnType("text");

        builder.Property(response => response.RespondedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(response => response.IpAddress)
            .HasColumnType("inet");

        builder.Property(response => response.Subtotal)
            .HasPrecision(14, 2);

        builder.Property(response => response.DiscountTotal)
            .HasPrecision(14, 2);

        builder.Property(response => response.TaxTotal)
            .HasPrecision(14, 2);

        builder.Property(response => response.Total)
            .HasPrecision(14, 2);

        // CREATE UNIQUE INDEX ux_quote_responses_final ON quote_responses (quote_version_id) WHERE response IN ('approved','rejected')
        builder.HasIndex(response => response.QuoteVersionId, "ux_quote_responses_final")
            .IsUnique()
            .HasDatabaseName("ux_quote_responses_final")
            .HasFilter("response IN ('approved','rejected')");

        // CREATE UNIQUE INDEX ux_quote_responses_clarification ON quote_responses (quote_version_id) WHERE response = 'clarification_requested'
        builder.HasIndex(response => response.QuoteVersionId, "ux_quote_responses_clarification")
            .IsUnique()
            .HasDatabaseName("ux_quote_responses_clarification")
            .HasFilter("response = 'clarification_requested'");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(response => response.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id)
        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(response => new { response.OrganizationId, response.QuoteVersionId })
            .HasPrincipalKey(version => new { version.OrganizationId, version.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, responder_contact_id) REFERENCES customer_contacts (organization_id, id)
        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(response => new { response.OrganizationId, response.ResponderContactId })
            .HasPrincipalKey(contact => new { contact.OrganizationId, contact.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
