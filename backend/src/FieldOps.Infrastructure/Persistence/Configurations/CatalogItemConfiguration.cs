using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CatalogItemConfiguration
    : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("catalog_items", table =>
        {
            table.HasCheckConstraint(
                "ck_catalog_items_unit_cost",
                "unit_cost >= 0");
            table.HasCheckConstraint(
                "ck_catalog_items_unit_price",
                "unit_price >= 0");
            table.HasCheckConstraint(
                "ck_catalog_items_tax_rate",
                "tax_rate BETWEEN 0 AND 100");
        });

        builder.HasKey(item => item.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(item => new
        {
            item.OrganizationId,
            item.Id,
        });

        builder.Property(item => item.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(item => item.OrganizationId)
            .IsRequired();

        builder.Property(item => item.CategoryId);

        // PostgreSQL enum catalog_item_type, mapped in FieldOpsDbContext.
        builder.Property(item => item.Type)
            .IsRequired();

        builder.Property(item => item.Sku)
            .HasMaxLength(60);

        builder.Property(item => item.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(item => item.Description)
            .HasColumnType("text");

        builder.Property(item => item.Unit)
            .HasMaxLength(40)
            .HasDefaultValue("unit")
            .IsRequired();

        builder.Property(item => item.UnitCost)
            .HasPrecision(14, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(item => item.UnitPrice)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(item => item.TaxRate)
            .HasPrecision(7, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(item => item.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(item => item.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(item => item.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, sku)
        builder.HasIndex(item => new { item.OrganizationId, item.Sku })
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, category_id) REFERENCES service_categories (organization_id, id)
        builder.HasOne<ServiceCategory>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, item.CategoryId })
            .HasPrincipalKey(category => new { category.OrganizationId, category.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
