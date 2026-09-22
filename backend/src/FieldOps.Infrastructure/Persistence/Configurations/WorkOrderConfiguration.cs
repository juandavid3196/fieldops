using FieldOps.Domain.Branches;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders", table => table.HasCheckConstraint(
            "ck_work_orders_priority",
            "priority BETWEEN 1 AND 5"));

        builder.HasKey(workOrder => workOrder.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(workOrder => new
        {
            workOrder.OrganizationId,
            workOrder.Id,
        });

        builder.Property(workOrder => workOrder.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(workOrder => workOrder.OrganizationId)
            .IsRequired();

        builder.Property(workOrder => workOrder.BranchId)
            .IsRequired();

        builder.Property(workOrder => workOrder.WorkOrderNumber)
            .IsRequired();

        builder.Property(workOrder => workOrder.QuoteVersionId)
            .IsRequired();

        builder.Property(workOrder => workOrder.CustomerId)
            .IsRequired();

        builder.Property(workOrder => workOrder.PropertyId)
            .IsRequired();

        // PostgreSQL enum work_order_status, mapped in FieldOpsDbContext.
        builder.Property(workOrder => workOrder.Status)
            .IsRequired();

        builder.Property(workOrder => workOrder.Priority)
            .HasDefaultValue((short)3)
            .IsRequired();

        builder.Property(workOrder => workOrder.ScopeSnapshot)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(workOrder => workOrder.InternalInstructions)
            .HasColumnType("text");

        builder.Property(workOrder => workOrder.PreferredStart);

        builder.Property(workOrder => workOrder.PreferredEnd);

        builder.Property(workOrder => workOrder.CreatedByUserId)
            .IsRequired();

        builder.Property(workOrder => workOrder.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(workOrder => workOrder.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, work_order_number)
        builder.HasIndex(workOrder => new { workOrder.OrganizationId, workOrder.WorkOrderNumber })
            .IsUnique();

        // quote_version_id uuid NOT NULL UNIQUE: enforces one WorkOrder per
        // approved QuoteVersion.
        builder.HasIndex(workOrder => workOrder.QuoteVersionId)
            .IsUnique();

        // CREATE INDEX ix_work_orders_status ON work_orders (organization_id, branch_id, status)
        builder.HasIndex(workOrder => new
        {
            workOrder.OrganizationId,
            workOrder.BranchId,
            workOrder.Status,
        })
            .HasDatabaseName("ix_work_orders_status");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.QuoteVersionId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(workOrder => new { workOrder.OrganizationId, workOrder.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, property_id) REFERENCES properties (organization_id, id)
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(workOrder => new { workOrder.OrganizationId, workOrder.PropertyId })
            .HasPrincipalKey(property => new { property.OrganizationId, property.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
