using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteResponseOptionalLineConfiguration : IEntityTypeConfiguration<QuoteResponseOptionalLine>
{
    public void Configure(EntityTypeBuilder<QuoteResponseOptionalLine> builder)
    {
        builder.ToTable("quote_response_optional_lines");

        // PRIMARY KEY (quote_response_id, quote_line_id)
        builder.HasKey(line => new { line.QuoteResponseId, line.QuoteLineId });

        builder.Property(line => line.OrganizationId)
            .IsRequired();

        builder.Property(line => line.QuoteVersionId)
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(line => line.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, quote_version_id, quote_response_id)
        //   REFERENCES quote_responses (organization_id, quote_version_id, id) ON DELETE CASCADE
        builder.HasOne<QuoteResponse>()
            .WithMany()
            .HasForeignKey(line => new { line.OrganizationId, line.QuoteVersionId, line.QuoteResponseId })
            .HasPrincipalKey(response => new { response.OrganizationId, response.QuoteVersionId, response.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // FOREIGN KEY (organization_id, quote_version_id, quote_line_id)
        //   REFERENCES quote_lines (organization_id, quote_version_id, id)
        builder.HasOne<QuoteLine>()
            .WithMany()
            .HasForeignKey(line => new { line.OrganizationId, line.QuoteVersionId, line.QuoteLineId })
            .HasPrincipalKey(quoteLine => new { quoteLine.OrganizationId, quoteLine.QuoteVersionId, quoteLine.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
