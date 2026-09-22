using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> builder)
    {
        builder.ToTable("assessments", table => table.HasCheckConstraint(
            "ck_assessments_schedule_range",
            "scheduled_start < scheduled_end"));

        builder.HasKey(assessment => assessment.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(assessment => new
        {
            assessment.OrganizationId,
            assessment.Id,
        });

        builder.Property(assessment => assessment.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(assessment => assessment.OrganizationId)
            .IsRequired();

        builder.Property(assessment => assessment.RequestId)
            .IsRequired();

        builder.Property(assessment => assessment.TechnicianId);

        builder.Property(assessment => assessment.ScheduledStart)
            .IsRequired();

        builder.Property(assessment => assessment.ScheduledEnd)
            .IsRequired();

        // PostgreSQL enum assessment_status, mapped in FieldOpsDbContext.
        builder.Property(assessment => assessment.Status)
            .IsRequired();

        builder.Property(assessment => assessment.Diagnosis)
            .HasColumnType("text");

        builder.Property(assessment => assessment.RecommendedScope)
            .HasColumnType("text");

        builder.Property(assessment => assessment.InternalNotes)
            .HasColumnType("text");

        builder.Property(assessment => assessment.CompletedAt);

        builder.Property(assessment => assessment.CreatedByUserId)
            .IsRequired();

        builder.Property(assessment => assessment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(assessment => assessment.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE INDEX ix_assessments_schedule
        //   ON assessments (organization_id, scheduled_start, status)
        builder.HasIndex(assessment => new
        {
            assessment.OrganizationId,
            assessment.ScheduledStart,
            assessment.Status,
        })
            .HasDatabaseName("ix_assessments_schedule");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(assessment => assessment.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id)
        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(assessment => new { assessment.OrganizationId, assessment.RequestId })
            .HasPrincipalKey(request => new { request.OrganizationId, request.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // technician_id REFERENCES technician_profiles (id): single-column,
        // not tenant-composite, exactly as defined in the relational model.
        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(assessment => assessment.TechnicianId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(assessment => assessment.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
