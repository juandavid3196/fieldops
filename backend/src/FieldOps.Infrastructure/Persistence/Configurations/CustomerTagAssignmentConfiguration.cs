using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerTagAssignmentConfiguration : IEntityTypeConfiguration<CustomerTagAssignment>
{
    public void Configure(EntityTypeBuilder<CustomerTagAssignment> builder)
    {
        builder.ToTable("customer_tag_assignments");

        // PRIMARY KEY (customer_id, tag_id)
        builder.HasKey(assignment => new { assignment.CustomerId, assignment.TagId });

        builder.Property(assignment => assignment.OrganizationId)
            .IsRequired();

        builder.Property(assignment => assignment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE INDEX ix_customer_tag_assignments_org_tag ON customer_tag_assignments (organization_id, tag_id)
        builder.HasIndex(assignment => new { assignment.OrganizationId, assignment.TagId })
            .HasDatabaseName("ix_customer_tag_assignments_org_tag");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(assignment => assignment.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.OrganizationId, assignment.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, tag_id) REFERENCES customer_tags (organization_id, id)
        builder.HasOne<CustomerTag>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.OrganizationId, assignment.TagId })
            .HasPrincipalKey(tag => new { tag.OrganizationId, tag.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
