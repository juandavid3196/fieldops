using FieldOps.Domain.Branches;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes");

        builder.HasKey(quote => quote.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(quote => new
        {
            quote.OrganizationId,
            quote.Id,
        });

        builder.Property(quote => quote.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(quote => quote.OrganizationId)
            .IsRequired();

        builder.Property(quote => quote.BranchId);

        builder.Property(quote => quote.RequestId)
            .IsRequired();

        builder.Property(quote => quote.CustomerId);

        builder.Property(quote => quote.PropertyId);

        builder.Property(quote => quote.QuoteNumber)
            .IsRequired();

        // PostgreSQL enum quote_status, mapped in FieldOpsDbContext.
        builder.Property(quote => quote.Status)
            .IsRequired();

        builder.Property(quote => quote.CurrentVersionNo)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(quote => quote.ApprovedVersionId);

        builder.Property(quote => quote.CreatedByUserId)
            .IsRequired();

        builder.Property(quote => quote.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(quote => quote.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, quote_number)
        builder.HasIndex(quote => new { quote.OrganizationId, quote.QuoteNumber })
            .IsUnique();

        // CREATE INDEX ix_quotes_status ON quotes (organization_id, status, created_at DESC)
        builder.HasIndex(quote => new
        {
            quote.OrganizationId,
            quote.Status,
            quote.CreatedAt,
        })
            .HasDatabaseName("ix_quotes_status")
            .IsDescending(false, false, true);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(quote => quote.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(quote => quote.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id)
        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(quote => new { quote.OrganizationId, quote.RequestId })
            .HasPrincipalKey(request => new { request.OrganizationId, request.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(quote => new { quote.OrganizationId, quote.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, property_id) REFERENCES properties (organization_id, id)
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(quote => new { quote.OrganizationId, quote.PropertyId })
            .HasPrincipalKey(property => new { property.OrganizationId, property.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(quote => quote.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // ALTER TABLE quotes ADD CONSTRAINT fk_quotes_approved_version
        //   FOREIGN KEY (approved_version_id) REFERENCES quote_versions (id);
        // Added after both tables exist to resolve the Quote <-> QuoteVersion cycle.
        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(quote => quote.ApprovedVersionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
