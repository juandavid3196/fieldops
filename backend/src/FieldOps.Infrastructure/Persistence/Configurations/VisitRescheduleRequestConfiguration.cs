using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitRescheduleRequestConfiguration : IEntityTypeConfiguration<VisitRescheduleRequest>
{
    public const string PendingIndexName = "ux_visit_reschedule_requests_pending";

    public void Configure(EntityTypeBuilder<VisitRescheduleRequest> builder)
    {
        builder.ToTable(
            "visit_reschedule_requests",
            table => table.HasCheckConstraint(
                "ck_visit_reschedule_requests_time_window",
                "time_window IN ('morning','afternoon','evening','any')"));

        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(request => request.OrganizationId).IsRequired();

        builder.Property(request => request.VisitId).IsRequired();

        builder.Property(request => request.ContactId).IsRequired();

        builder.Property(request => request.OriginalScheduledStart).IsRequired();

        builder.Property(request => request.PreferredDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(request => request.TimeWindow)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(request => request.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(request => request.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE UNIQUE INDEX ux_visit_reschedule_requests_pending ON visit_reschedule_requests (visit_id, original_scheduled_start)
        builder.HasIndex(request => new { request.VisitId, request.OriginalScheduledStart })
            .IsUnique()
            .HasDatabaseName(PendingIndexName);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(request => request.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, visit_id) REFERENCES visits (organization_id, id)
        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(request => new { request.OrganizationId, request.VisitId })
            .HasPrincipalKey(visit => new { visit.OrganizationId, visit.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts (organization_id, id)
        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(request => new { request.OrganizationId, request.ContactId })
            .HasPrincipalKey(contact => new { contact.OrganizationId, contact.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
