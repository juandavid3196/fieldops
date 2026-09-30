using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CatalogItemImageConfiguration : IEntityTypeConfiguration<CatalogItemImage>
{
    public void Configure(EntityTypeBuilder<CatalogItemImage> builder)
    {
        builder.ToTable("catalog_item_images", table =>
        {
            table.HasCheckConstraint(
                "ck_catalog_item_images_content_type",
                "content_type IN ('image/png','image/jpeg')");
            table.HasCheckConstraint(
                "ck_catalog_item_images_size_bytes",
                "size_bytes BETWEEN 1 AND 5242880");
        });

        // PRIMARY KEY (catalog_item_id): one image per item.
        builder.HasKey(image => image.CatalogItemId);

        builder.Property(image => image.CatalogItemId)
            .ValueGeneratedNever();

        builder.Property(image => image.OrganizationId)
            .IsRequired();

        builder.Property(image => image.ContentType)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(image => image.Content)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(image => image.SizeBytes)
            .IsRequired();

        builder.Property(image => image.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(image => image.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(image => image.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items (organization_id, id) ON DELETE CASCADE
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(image => new { image.OrganizationId, image.CatalogItemId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
