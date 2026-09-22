using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class RequestMessageConfiguration
    : IEntityTypeConfiguration<RequestMessage>
{
    public void Configure(EntityTypeBuilder<RequestMessage> builder)
    {
        builder.ToTable("request_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(message => message.OrganizationId)
            .IsRequired();

        builder.Property(message => message.RequestId)
            .IsRequired();

        builder.Property(message => message.AuthorUserId);

        builder.Property(message => message.AuthorContactId);

        // PostgreSQL enum message_visibility, mapped in FieldOpsDbContext.
        builder.Property(message => message.Visibility)
            .IsRequired();

        builder.Property(message => message.Body)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(message => message.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id)
        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(message => new { message.OrganizationId, message.RequestId })
            .HasPrincipalKey(request => new { request.OrganizationId, request.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(message => message.AuthorUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // author_contact_id REFERENCES customer_contacts (id): single-column,
        // not tenant-composite, exactly as defined in the relational model.
        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(message => message.AuthorContactId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
