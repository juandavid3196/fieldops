using FieldOps.Domain.Catalog;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitMaterialConfiguration
    : IEntityTypeConfiguration<VisitMaterial>
{
    public void Configure(EntityTypeBuilder<VisitMaterial> builder)
    {
        builder.ToTable("visit_materials", table => table.HasCheckConstraint(
            "ck_visit_materials_quantity",
            "quantity > 0"));

        builder.HasKey(material => material.Id);

        builder.Property(material => material.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(material => material.VisitId)
            .IsRequired();

        builder.Property(material => material.CatalogItemId);

        // Snapshotted independently from CatalogItem: kept even if the
        // catalog item's description, unit or cost changes later.
        builder.Property(material => material.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(material => material.Quantity)
            .HasPrecision(12, 3)
            .IsRequired();

        builder.Property(material => material.Unit)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(material => material.UnitCost)
            .HasPrecision(14, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(material => material.Billable)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(material => material.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(material => material.CatalogItemId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
