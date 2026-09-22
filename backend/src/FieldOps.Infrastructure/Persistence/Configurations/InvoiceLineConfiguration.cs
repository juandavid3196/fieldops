using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines", table =>
        {
            table.HasCheckConstraint(
                "ck_invoice_lines_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_invoice_lines_unit_price",
                "unit_price >= 0");
        });

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(line => line.InvoiceId)
            .IsRequired();

        builder.Property(line => line.SourceQuoteLineId);

        builder.Property(line => line.SourceVisitMaterialId);

        // Snapshotted independently from its source: kept even if the
        // source QuoteLine or VisitMaterial changes or is removed later.
        builder.Property(line => line.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(line => line.Quantity)
            .HasPrecision(12, 3)
            .IsRequired();

        builder.Property(line => line.Unit)
            .HasMaxLength(40)
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

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(line => line.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<QuoteLine>()
            .WithMany()
            .HasForeignKey(line => line.SourceQuoteLineId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<VisitMaterial>()
            .WithMany()
            .HasForeignKey(line => line.SourceVisitMaterialId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
