using FieldOps.Domain.Branches;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ServiceRequestConfiguration
    : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("service_requests", table => table.HasCheckConstraint(
            "ck_service_requests_preferred_range",
            "preferred_end IS NULL OR preferred_start IS NULL OR preferred_start < preferred_end"));

        builder.HasKey(request => request.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(request => new
        {
            request.OrganizationId,
            request.Id,
        });

        builder.Property(request => request.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(request => request.OrganizationId)
            .IsRequired();

        builder.Property(request => request.BranchId);

        builder.Property(request => request.RequestNumber)
            .IsRequired();

        builder.Property(request => request.CustomerId);

        builder.Property(request => request.ContactId);

        builder.Property(request => request.PropertyId);

        builder.Property(request => request.CategoryId);

        builder.Property(request => request.GuestName)
            .HasMaxLength(180);

        builder.Property(request => request.GuestEmail)
            .HasMaxLength(254);

        builder.Property(request => request.GuestPhone)
            .HasMaxLength(40);

        builder.Property(request => request.ServiceAddress)
            .HasColumnType("jsonb");

        builder.Property(request => request.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(request => request.PreferredStart);

        builder.Property(request => request.PreferredEnd);

        // PostgreSQL enum request_status, mapped in FieldOpsDbContext.
        builder.Property(request => request.Status)
            .IsRequired();

        builder.Property(request => request.Source)
            .HasMaxLength(30)
            .HasDefaultValue("public_form")
            .IsRequired();

        builder.Property(request => request.AssignedDispatcherUserId);

        builder.Property(request => request.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(request => request.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(request => request.CancelledAt);

        // UNIQUE (organization_id, request_number)
        builder.HasIndex(request => new { request.OrganizationId, request.RequestNumber })
            .IsUnique();

        // CREATE INDEX ix_requests_pipeline
        //   ON service_requests (organization_id, status, created_at DESC)
        builder.HasIndex(request => new
        {
            request.OrganizationId,
            request.Status,
            request.CreatedAt,
        })
            .HasDatabaseName("ix_requests_pipeline")
            .IsDescending(false, false, true);

        // CREATE INDEX ix_requests_customer ON service_requests (organization_id, customer_id)
        builder.HasIndex(request => new { request.OrganizationId, request.CustomerId })
            .HasDatabaseName("ix_requests_customer");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(request => request.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(request => request.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(request => new { request.OrganizationId, request.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts (organization_id, id)
        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(request => new { request.OrganizationId, request.ContactId })
            .HasPrincipalKey(contact => new { contact.OrganizationId, contact.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, property_id) REFERENCES properties (organization_id, id)
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(request => new { request.OrganizationId, request.PropertyId })
            .HasPrincipalKey(property => new { property.OrganizationId, property.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, category_id) REFERENCES service_categories (organization_id, id)
        builder.HasOne<ServiceCategory>()
            .WithMany()
            .HasForeignKey(request => new { request.OrganizationId, request.CategoryId })
            .HasPrincipalKey(category => new { category.OrganizationId, category.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.AssignedDispatcherUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
