using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianWeeklyAvailabilityConfiguration
    : IEntityTypeConfiguration<TechnicianWeeklyAvailability>
{
    public void Configure(EntityTypeBuilder<TechnicianWeeklyAvailability> builder)
    {
        builder.ToTable("technician_weekly_availability", table =>
        {
            table.HasCheckConstraint(
                "ck_technician_weekly_availability_day_of_week",
                "day_of_week BETWEEN 0 AND 6");
            table.HasCheckConstraint(
                "ck_technician_weekly_availability_capacity_percent",
                "capacity_percent BETWEEN 1 AND 100");
            table.HasCheckConstraint(
                "ck_technician_weekly_availability_start_end",
                "start_time < end_time");
        });

        builder.HasKey(availability => availability.Id);

        builder.Property(availability => availability.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(availability => availability.TechnicianId)
            .IsRequired();

        builder.Property(availability => availability.DayOfWeek)
            .IsRequired();

        builder.Property(availability => availability.StartTime)
            .IsRequired();

        builder.Property(availability => availability.EndTime)
            .IsRequired();

        builder.Property(availability => availability.CapacityPercent)
            .HasDefaultValue((short)100)
            .IsRequired();

        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(availability => availability.TechnicianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
