using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianBreakConfiguration
    : IEntityTypeConfiguration<TechnicianBreak>
{
    public void Configure(EntityTypeBuilder<TechnicianBreak> builder)
    {
        builder.ToTable("technician_breaks", table => table.HasCheckConstraint(
            "ck_technician_breaks_start_end",
            "start_time < end_time"));

        builder.HasKey(technicianBreak => technicianBreak.Id);

        builder.Property(technicianBreak => technicianBreak.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(technicianBreak => technicianBreak.AvailabilityId)
            .IsRequired();

        builder.Property(technicianBreak => technicianBreak.StartTime)
            .IsRequired();

        builder.Property(technicianBreak => technicianBreak.EndTime)
            .IsRequired();

        builder.HasOne<TechnicianWeeklyAvailability>()
            .WithMany()
            .HasForeignKey(technicianBreak => technicianBreak.AvailabilityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
