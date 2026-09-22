using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianExceptionConfiguration
    : IEntityTypeConfiguration<TechnicianException>
{
    public void Configure(EntityTypeBuilder<TechnicianException> builder)
    {
        builder.ToTable("technician_exceptions", table => table.HasCheckConstraint(
            "ck_technician_exceptions_start_end",
            "starts_at < ends_at"));

        builder.HasKey(exception => exception.Id);

        builder.Property(exception => exception.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(exception => exception.TechnicianId)
            .IsRequired();

        builder.Property(exception => exception.StartsAt)
            .IsRequired();

        builder.Property(exception => exception.EndsAt)
            .IsRequired();

        builder.Property(exception => exception.IsAvailable)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(exception => exception.Reason)
            .HasMaxLength(200);

        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(exception => exception.TechnicianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
