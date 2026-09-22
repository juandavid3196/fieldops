using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class AssessmentAttachmentConfiguration
    : IEntityTypeConfiguration<AssessmentAttachment>
{
    public void Configure(EntityTypeBuilder<AssessmentAttachment> builder)
    {
        builder.ToTable("assessment_attachments");

        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(attachment => attachment.AssessmentId)
            .IsRequired();

        builder.Property(attachment => attachment.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(attachment => attachment.StorageKey)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(attachment => attachment.MimeType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(attachment => attachment.SizeBytes)
            .IsRequired();

        builder.Property(attachment => attachment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // No organization_id column in the relational model: tenant isolation
        // for this table is derived through assessment_id -> assessments.
        builder.HasOne<Assessment>()
            .WithMany()
            .HasForeignKey(attachment => attachment.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
