using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitEvidenceConfiguration
    : IEntityTypeConfiguration<VisitEvidence>
{
    public void Configure(EntityTypeBuilder<VisitEvidence> builder)
    {
        builder.ToTable("visit_evidence", table =>
        {
            table.HasCheckConstraint(
                "ck_visit_evidence_size_bytes",
                "size_bytes > 0");
            table.HasCheckConstraint(
                "ck_visit_evidence_evidence_type",
                "evidence_type IN ('before','during','after','incident','other')");
        });

        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(evidence => evidence.VisitId)
            .IsRequired();

        // Metadata and storage key only: binary file contents are never
        // stored in the relational model.
        builder.Property(evidence => evidence.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(evidence => evidence.StorageKey)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(evidence => evidence.MimeType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(evidence => evidence.SizeBytes)
            .IsRequired();

        // varchar(30) with CHECK, not a native PostgreSQL enum type:
        // preserved as a lowercase string conversion.
        builder.Property(evidence => evidence.EvidenceType)
            .HasConversion(
                type => type.ToString().ToLowerInvariant(),
                value => Enum.Parse<VisitEvidenceType>(value, ignoreCase: true))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(evidence => evidence.Caption)
            .HasColumnType("text");

        builder.Property(evidence => evidence.UploadedByUserId)
            .IsRequired();

        builder.Property(evidence => evidence.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(evidence => evidence.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(evidence => evidence.UploadedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
