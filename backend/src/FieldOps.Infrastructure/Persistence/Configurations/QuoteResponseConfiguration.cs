using FieldOps.Domain.Customers;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteResponseConfiguration : IEntityTypeConfiguration<QuoteResponse>
{
    public void Configure(EntityTypeBuilder<QuoteResponse> builder)
    {
        builder.ToTable("quote_responses", table => table.HasCheckConstraint(
            "ck_quote_responses_response",
            "response IN ('approved','rejected','clarification_requested')"));

        builder.HasKey(response => response.Id);

        builder.Property(response => response.Id)
            .HasDefaultValueSql("gen_random_uuid()");

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

        // No organization_id column in the relational model: tenant isolation
        // for this table is derived through quote_version_id -> quotes.
        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(response => response.QuoteVersionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(response => response.ResponderContactId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
