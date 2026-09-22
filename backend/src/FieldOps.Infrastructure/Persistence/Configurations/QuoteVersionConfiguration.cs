using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteVersionConfiguration : IEntityTypeConfiguration<QuoteVersion>
{
    public void Configure(EntityTypeBuilder<QuoteVersion> builder)
    {
        builder.ToTable("quote_versions", table =>
        {
            table.HasCheckConstraint(
                "ck_quote_versions_version_no",
                "version_no > 0");
            table.HasCheckConstraint(
                "ck_quote_versions_subtotal",
                "subtotal >= 0");
            table.HasCheckConstraint(
                "ck_quote_versions_tax_total",
                "tax_total >= 0");
            table.HasCheckConstraint(
                "ck_quote_versions_total",
                "total >= 0");
        });

        builder.HasKey(version => version.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(version => new
        {
            version.OrganizationId,
            version.Id,
        });

        builder.Property(version => version.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(version => version.OrganizationId)
            .IsRequired();

        builder.Property(version => version.QuoteId)
            .IsRequired();

        builder.Property(version => version.VersionNo)
            .IsRequired();

        builder.Property(version => version.Scope)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(version => version.CustomerNotes)
            .HasColumnType("text");

        builder.Property(version => version.InternalNotes)
            .HasColumnType("text");

        builder.Property(version => version.Subtotal)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(version => version.TaxTotal)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(version => version.Total)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(version => version.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(version => version.ValidUntil)
            .HasColumnType("date");

        builder.Property(version => version.SentAt);

        builder.Property(version => version.IsImmutable)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(version => version.CreatedByUserId)
            .IsRequired();

        builder.Property(version => version.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (quote_id, version_no)
        builder.HasIndex(version => new { version.QuoteId, version.VersionNo })
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(version => version.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, quote_id) REFERENCES quotes (organization_id, id)
        builder.HasOne<Quote>()
            .WithMany()
            .HasForeignKey(version => new { version.OrganizationId, version.QuoteId })
            .HasPrincipalKey(quote => new { quote.OrganizationId, quote.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(version => version.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
