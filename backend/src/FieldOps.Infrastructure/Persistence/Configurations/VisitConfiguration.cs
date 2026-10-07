using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visits", table =>
        {
            table.HasCheckConstraint(
                "ck_visits_visit_number",
                "visit_number > 0");
            table.HasCheckConstraint(
                "ck_visits_pause_seconds",
                "pause_seconds >= 0");
            table.HasCheckConstraint(
                "ck_visits_schedule_range",
                "scheduled_end IS NULL OR scheduled_start IS NULL OR scheduled_start < scheduled_end");
            table.HasCheckConstraint(
                "ck_visits_schedule_pair",
                "(scheduled_start IS NULL) = (scheduled_end IS NULL)");
            table.HasCheckConstraint(
                "ck_visits_preferred_range",
                "(preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end)");
            table.HasCheckConstraint(
                "ck_visits_arrival_window_pair",
                "(arrival_window_start IS NULL) = (arrival_window_end IS NULL)");
            table.HasCheckConstraint(
                "ck_visits_arrival_window_range",
                "arrival_window_start IS NULL OR (scheduled_start IS NOT NULL AND arrival_window_start <= scheduled_start AND scheduled_start <= arrival_window_end)");
        });

        builder.HasKey(visit => visit.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(visit => new
        {
            visit.OrganizationId,
            visit.Id,
        });

        builder.Property(visit => visit.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(visit => visit.OrganizationId)
            .IsRequired();

        builder.Property(visit => visit.WorkOrderId)
            .IsRequired();

        builder.Property(visit => visit.VisitNumber)
            .IsRequired();

        // PostgreSQL enum visit_status, mapped in FieldOpsDbContext.
        builder.Property(visit => visit.Status)
            .IsRequired();

        builder.Property(visit => visit.ScheduledStart);

        builder.Property(visit => visit.ScheduledEnd);

        builder.Property(visit => visit.PreferredStart);

        builder.Property(visit => visit.PreferredEnd);

        builder.Property(visit => visit.ArrivalWindowStart);

        builder.Property(visit => visit.ArrivalWindowEnd);

        builder.Property(visit => visit.DispatchNote)
            .HasMaxLength(1000);

        builder.Property(visit => visit.ActualStartedAt);

        builder.Property(visit => visit.ActualCompletedAt);

        builder.Property(visit => visit.PauseSeconds)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(visit => visit.CompletionSummary)
            .HasColumnType("text");

        builder.Property(visit => visit.CompletionWithoutSignatureReason)
            .HasColumnType("text");

        builder.Property(visit => visit.ReviewNotes)
            .HasColumnType("text");

        builder.Property(visit => visit.ReviewedByUserId);

        builder.Property(visit => visit.ReviewedAt);

        builder.Property(visit => visit.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(visit => visit.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (work_order_id, visit_number)
        builder.HasIndex(visit => new { visit.WorkOrderId, visit.VisitNumber })
            .IsUnique();

        // CREATE INDEX ix_visits_schedule ON visits (organization_id, scheduled_start, status)
        builder.HasIndex(visit => new
        {
            visit.OrganizationId,
            visit.ScheduledStart,
            visit.Status,
        })
            .HasDatabaseName("ix_visits_schedule");

        // CREATE INDEX ix_visits_org_status_preferred ON visits (organization_id, status, preferred_start)
        builder.HasIndex(visit => new
        {
            visit.OrganizationId,
            visit.Status,
            visit.PreferredStart,
        })
            .HasDatabaseName("ix_visits_org_status_preferred");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(visit => visit.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id)
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(visit => new { visit.OrganizationId, visit.WorkOrderId })
            .HasPrincipalKey(workOrder => new { workOrder.OrganizationId, workOrder.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(visit => visit.ReviewedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
