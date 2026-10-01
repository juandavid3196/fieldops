using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianExceptionConfiguration
    : IEntityTypeConfiguration<TechnicianException>
{
    public void Configure(EntityTypeBuilder<TechnicianException> builder)
    {
        builder.ToTable("technician_exceptions", table =>
        {
            table.HasCheckConstraint("ck_technician_exceptions_start_end", "starts_at < ends_at");
            table.HasCheckConstraint("ck_technician_exceptions_status", "status IN ('active','cancelled')");
        });

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

        builder.Property(exception => exception.Status)
            .HasMaxLength(20)
            .HasDefaultValue(TechnicianExceptionStatus.Active)
            .IsRequired();

        builder.Property(exception => exception.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // The exception version; truncated to microseconds by the writer so it round-trips through PostgreSQL.
        builder.Property(exception => exception.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(exception => exception.TechnicianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
