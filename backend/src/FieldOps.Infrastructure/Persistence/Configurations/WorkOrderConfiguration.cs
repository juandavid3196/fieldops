using FieldOps.Domain.Branches;
using FieldOps.Domain.Catalog;
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
        builder.ToTable("work_orders", table =>
        {
            table.HasCheckConstraint(
                "ck_work_orders_priority",
                "priority BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "ck_work_orders_job_type",
                "job_type IN ('one_time','recurring')");
            table.HasCheckConstraint(
                "ck_work_orders_estimated_duration_minutes",
                "estimated_duration_minutes BETWEEN 30 AND 720 AND estimated_duration_minutes % 30 = 0");
            table.HasCheckConstraint(
                "ck_work_orders_recurrence_frequency",
                "recurrence_frequency IN ('weekly','biweekly','monthly','quarterly')");
            table.HasCheckConstraint(
                "ck_work_orders_recurrence_count",
                "recurrence_count BETWEEN 2 AND 24");
            table.HasCheckConstraint(
                "ck_work_orders_recurrence_job_type",
                "(job_type = 'recurring') = (recurrence_frequency IS NOT NULL AND recurrence_count IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_work_orders_recurrence_pair",
                "(recurrence_frequency IS NULL) = (recurrence_count IS NULL)");
            table.HasCheckConstraint(
                "ck_work_orders_preferred_window",
                "(preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end)");
            // SA-06 (completed-jobs-review): the follow-up time and user are set and cleared together.
            table.HasCheckConstraint(
                "ck_work_orders_billing_follow_up_pair",
                "(billing_follow_up_at IS NULL) = (billing_follow_up_by_user_id IS NULL)");
        });

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

        builder.Property(workOrder => workOrder.Title)
            .HasMaxLength(160)
            .IsRequired();

        // varchar + CHECK, not a PostgreSQL enum: one_time | recurring.
        builder.Property(workOrder => workOrder.JobType)
            .HasMaxLength(20)
            .HasDefaultValue("one_time")
            .IsRequired();

        builder.Property(workOrder => workOrder.ServiceCategoryId)
            .IsRequired();

        builder.Property(workOrder => workOrder.EstimatedDurationMinutes);

        builder.Property(workOrder => workOrder.RecurrenceFrequency)
            .HasMaxLength(20);

        builder.Property(workOrder => workOrder.RecurrenceCount);

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(workOrder => workOrder.NotifyCustomerWhenScheduled)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(workOrder => workOrder.SendTechnicianDetails)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(workOrder => workOrder.SendArrivalReminder)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(workOrder => workOrder.ScopeSnapshot)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(workOrder => workOrder.InternalInstructions)
            .HasColumnType("text");

        builder.Property(workOrder => workOrder.PreferredStart);

        builder.Property(workOrder => workOrder.PreferredEnd);

        builder.Property(workOrder => workOrder.BillingReviewNote)
            .HasMaxLength(500);

        builder.Property(workOrder => workOrder.BillingFollowUpAt);

        builder.Property(workOrder => workOrder.BillingFollowUpByUserId);

        builder.Property(workOrder => workOrder.CreatedByUserId)
            .IsRequired();

        builder.Property(workOrder => workOrder.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Optimistic concurrency token of the draft (create-work-order BR-16).
        builder.Property(workOrder => workOrder.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsConcurrencyToken()
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

        // CREATE INDEX ix_work_orders_org_created ON work_orders (organization_id, created_at DESC)
        builder.HasIndex(workOrder => new { workOrder.OrganizationId, workOrder.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_work_orders_org_created");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(workOrder => new { workOrder.OrganizationId, workOrder.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id)
        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(workOrder => new { workOrder.OrganizationId, workOrder.QuoteVersionId })
            .HasPrincipalKey(version => new { version.OrganizationId, version.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, service_category_id) REFERENCES service_categories (organization_id, id)
        builder.HasOne<ServiceCategory>()
            .WithMany()
            .HasForeignKey(workOrder => new { workOrder.OrganizationId, workOrder.ServiceCategoryId })
            .HasPrincipalKey(category => new { category.OrganizationId, category.Id })
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

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.BillingFollowUpByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
