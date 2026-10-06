using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class AssessmentAttachmentConfiguration
    : IEntityTypeConfiguration<AssessmentAttachment>
{
    public void Configure(EntityTypeBuilder<AssessmentAttachment> builder)
    {
        builder.ToTable("assessment_attachments", table =>
        {
            table.HasCheckConstraint(
                "ck_assessment_attachments_size_bytes",
                "size_bytes > 0 AND size_bytes <= 10485760");

            table.HasCheckConstraint(
                "ck_assessment_attachments_mime_type",
                "mime_type IN ('image/jpeg', 'image/png')");

            table.HasCheckConstraint(
                "ck_assessment_attachments_content_or_storage",
                "content IS NOT NULL OR storage_key IS NOT NULL");
        });

        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(attachment => attachment.OrganizationId)
            .IsRequired();

        builder.Property(attachment => attachment.AssessmentId)
            .IsRequired();

        builder.Property(attachment => attachment.FileName)
            .HasMaxLength(255)
            .IsRequired();

        // Nullable since photos store their content inline (quote-builder BR-02).
        builder.Property(attachment => attachment.StorageKey)
            .HasColumnType("text");

        builder.Property(attachment => attachment.Content)
            .HasColumnType("bytea");

        builder.Property(attachment => attachment.MimeType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(attachment => attachment.SizeBytes)
            .IsRequired();

        builder.Property(attachment => attachment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(attachment => attachment.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Assessment>()
            .WithMany()
            .HasForeignKey(attachment => attachment.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // FOREIGN KEY (organization_id, assessment_id) REFERENCES assessments (organization_id, id) ON DELETE CASCADE
        builder.HasOne<Assessment>()
            .WithMany()
            .HasForeignKey(attachment => new { attachment.OrganizationId, attachment.AssessmentId })
            .HasPrincipalKey(assessment => new { assessment.OrganizationId, assessment.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
