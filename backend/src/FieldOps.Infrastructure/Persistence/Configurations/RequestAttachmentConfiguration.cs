using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class RequestAttachmentConfiguration
    : IEntityTypeConfiguration<RequestAttachment>
{
    public void Configure(EntityTypeBuilder<RequestAttachment> builder)
    {
        builder.ToTable("request_attachments", table =>
        {
            table.HasCheckConstraint(
                "ck_request_attachments_size_bytes",
                "size_bytes > 0 AND size_bytes <= 10485760");

            table.HasCheckConstraint(
                "ck_request_attachments_mime_type",
                "mime_type IN ('image/jpeg', 'image/png', 'application/pdf')");

            table.HasCheckConstraint(
                "ck_request_attachments_content_or_storage",
                "content IS NOT NULL OR storage_key IS NOT NULL");
        });

        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(attachment => attachment.OrganizationId)
            .IsRequired();

        builder.Property(attachment => attachment.RequestId)
            .IsRequired();

        builder.Property(attachment => attachment.FileName)
            .HasMaxLength(255)
            .IsRequired();

        // Nullable since public uploads store their content inline.
        builder.Property(attachment => attachment.StorageKey)
            .HasColumnType("text");

        builder.Property(attachment => attachment.Content)
            .HasColumnType("bytea");

        builder.Property(attachment => attachment.MimeType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(attachment => attachment.SizeBytes)
            .IsRequired();

        builder.Property(attachment => attachment.UploadedByUserId);

        builder.Property(attachment => attachment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(attachment => attachment.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id)
        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(attachment => new { attachment.OrganizationId, attachment.RequestId })
            .HasPrincipalKey(request => new { request.OrganizationId, request.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(attachment => attachment.UploadedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
