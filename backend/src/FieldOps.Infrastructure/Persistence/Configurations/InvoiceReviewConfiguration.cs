using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

/// <summary>SA-18 (customer-invoice-payments): <c>invoice_reviews</c>, one immutable review per work order.</summary>
internal sealed class InvoiceReviewConfiguration : IEntityTypeConfiguration<InvoiceReview>
{
    public void Configure(EntityTypeBuilder<InvoiceReview> builder)
    {
        builder.ToTable("invoice_reviews", table => table.HasCheckConstraint(
            "ck_invoice_reviews_rating",
            "rating BETWEEN 1 AND 5"));

        builder.HasKey(review => review.Id);

        builder.Property(review => review.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(review => review.OrganizationId)
            .IsRequired();

        builder.Property(review => review.WorkOrderId)
            .IsRequired();

        builder.Property(review => review.InvoiceId)
            .IsRequired();

        builder.Property(review => review.TechnicianId);

        builder.Property(review => review.Rating)
            .IsRequired();

        builder.Property(review => review.Comment)
            .HasMaxLength(InvoiceReview.MaxCommentLength);

        builder.Property(review => review.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (work_order_id): the concurrency backstop of one review per work order (BR-22).
        builder.HasIndex(review => review.WorkOrderId)
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(review => review.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id)
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(review => new { review.OrganizationId, review.WorkOrderId })
            .HasPrincipalKey(order => new { order.OrganizationId, order.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, invoice_id) REFERENCES invoices (organization_id, id)
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(review => new { review.OrganizationId, review.InvoiceId })
            .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, technician_id) REFERENCES technician_profiles (organization_id, id)
        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(review => new { review.OrganizationId, review.TechnicianId })
            .HasPrincipalKey(technician => new { technician.OrganizationId, technician.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
