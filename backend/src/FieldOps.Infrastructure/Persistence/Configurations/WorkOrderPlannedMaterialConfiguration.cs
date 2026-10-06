using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderPlannedMaterialConfiguration
    : IEntityTypeConfiguration<WorkOrderPlannedMaterial>
{
    public void Configure(EntityTypeBuilder<WorkOrderPlannedMaterial> builder)
    {
        builder.ToTable("work_order_planned_materials", table =>
        {
            table.HasCheckConstraint(
                "ck_work_order_planned_materials_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_work_order_planned_materials_source",
                "source IN ('truck_stock','warehouse','to_purchase')");
        });

        builder.HasKey(material => material.Id);

        builder.Property(material => material.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(material => material.OrganizationId)
            .IsRequired();

        builder.Property(material => material.WorkOrderId)
            .IsRequired();

        builder.Property(material => material.QuoteLineId);

        builder.Property(material => material.CatalogItemId);

        builder.Property(material => material.Description)
            .HasMaxLength(240)
            .IsRequired();

        builder.Property(material => material.Quantity)
            .HasPrecision(12, 3)
            .IsRequired();

        builder.Property(material => material.Unit)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(material => material.Source)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(material => material.SortOrder)
            .HasDefaultValue(0)
            .IsRequired();

        // CREATE INDEX ON work_order_planned_materials (work_order_id)
        builder.HasIndex(material => material.WorkOrderId);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(material => material.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id) ON DELETE CASCADE
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(material => new { material.OrganizationId, material.WorkOrderId })
            .HasPrincipalKey(workOrder => new { workOrder.OrganizationId, workOrder.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items (organization_id, id)
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(material => new { material.OrganizationId, material.CatalogItemId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // quote_line_id uuid REFERENCES quote_lines (id): version and selection are enforced by the application.
        builder.HasOne<QuoteLine>()
            .WithMany()
            .HasForeignKey(material => material.QuoteLineId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
