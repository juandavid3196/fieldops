using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.ToTable("quote_lines", table =>
        {
            table.HasCheckConstraint(
                "ck_quote_lines_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_quote_lines_unit_price",
                "unit_price >= 0");
        });

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(line => line.QuoteVersionId)
            .IsRequired();

        builder.Property(line => line.CatalogItemId);

        // PostgreSQL enum catalog_item_type, mapped in FieldOpsDbContext.
        builder.Property(line => line.LineType)
            .IsRequired();

        // Snapshotted independently from CatalogItem: quote lines keep their
        // own description, quantity, prices, costs and totals so later
        // catalog changes never alter an already-created quote line.
        builder.Property(line => line.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(line => line.Quantity)
            .HasPrecision(12, 3)
            .IsRequired();

        builder.Property(line => line.Unit)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(line => line.UnitCost)
            .HasPrecision(14, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(line => line.UnitPrice)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(line => line.TaxRate)
            .HasPrecision(7, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(line => line.LineSubtotal)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(line => line.LineTax)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(line => line.LineTotal)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(line => line.SortOrder)
            .HasDefaultValue(0)
            .IsRequired();

        // No organization_id column in the relational model: tenant isolation
        // for this table is derived through quote_version_id -> quotes.
        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(line => line.QuoteVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(line => line.CatalogItemId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
