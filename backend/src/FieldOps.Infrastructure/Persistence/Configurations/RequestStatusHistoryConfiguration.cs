using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class RequestStatusHistoryConfiguration
    : IEntityTypeConfiguration<RequestStatusHistory>
{
    public void Configure(EntityTypeBuilder<RequestStatusHistory> builder)
    {
        builder.ToTable("request_status_history");

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(history => history.OrganizationId)
            .IsRequired();

        builder.Property(history => history.RequestId)
            .IsRequired();

        // PostgreSQL enum request_status, mapped in FieldOpsDbContext.
        builder.Property(history => history.FromStatus);

        builder.Property(history => history.ToStatus)
            .IsRequired();

        builder.Property(history => history.ChangedByUserId);

        builder.Property(history => history.Reason)
            .HasColumnType("text");

        builder.Property(history => history.ChangedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(history => history.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id)
        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(history => new { history.OrganizationId, history.RequestId })
            .HasPrincipalKey(request => new { request.OrganizationId, request.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(history => history.ChangedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
