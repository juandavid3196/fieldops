using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitAssignmentConfiguration
    : IEntityTypeConfiguration<VisitAssignment>
{
    public void Configure(EntityTypeBuilder<VisitAssignment> builder)
    {
        builder.ToTable("visit_assignments");

        builder.HasKey(assignment => assignment.Id);

        builder.Property(assignment => assignment.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(assignment => assignment.VisitId)
            .IsRequired();

        builder.Property(assignment => assignment.TechnicianId)
            .IsRequired();

        builder.Property(assignment => assignment.AssignedByUserId)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(assignment => assignment.IsPrimary)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(assignment => assignment.AssignedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(assignment => assignment.UnassignedAt);

        // UNIQUE (visit_id, technician_id, unassigned_at), as declared in the
        // relational model. PostgreSQL treats NULLs as distinct in unique
        // constraints, so this alone does not prevent two simultaneously
        // active (unassigned_at IS NULL) assignments of the same technician
        // to the same visit; see the partial unique index below.
        builder.HasIndex(assignment => new
        {
            assignment.VisitId,
            assignment.TechnicianId,
            assignment.UnassignedAt,
        })
            .IsUnique();

        // Prevents duplicate active assignments: at most one row per
        // (visit_id, technician_id) may have a NULL unassigned_at.
        builder.HasIndex(assignment => new
        {
            assignment.VisitId,
            assignment.TechnicianId,
        })
            .HasDatabaseName("ix_visit_assignments_active_technician_unique")
            .IsUnique()
            .HasFilter("unassigned_at IS NULL");

        // CREATE INDEX ix_assignments_technician
        //   ON visit_assignments (technician_id, assigned_at) WHERE unassigned_at IS NULL
        builder.HasIndex(assignment => new
        {
            assignment.TechnicianId,
            assignment.AssignedAt,
        })
            .HasDatabaseName("ix_assignments_technician")
            .HasFilter("unassigned_at IS NULL");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(assignment => assignment.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(assignment => assignment.TechnicianId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(assignment => assignment.AssignedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
