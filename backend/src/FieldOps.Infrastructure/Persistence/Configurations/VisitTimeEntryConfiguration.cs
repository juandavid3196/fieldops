using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitTimeEntryConfiguration
    : IEntityTypeConfiguration<VisitTimeEntry>
{
    public void Configure(EntityTypeBuilder<VisitTimeEntry> builder)
    {
        builder.ToTable("visit_time_entries", table =>
        {
            table.HasCheckConstraint(
                "ck_visit_time_entries_entry_type",
                "entry_type IN ('work','pause','travel')");
            table.HasCheckConstraint(
                "ck_visit_time_entries_start_end",
                "ended_at IS NULL OR started_at < ended_at");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(entry => entry.VisitId)
            .IsRequired();

        builder.Property(entry => entry.TechnicianId)
            .IsRequired();

        builder.Property(entry => entry.StartedAt)
            .IsRequired();

        builder.Property(entry => entry.EndedAt);

        // varchar(20) with CHECK, not a native PostgreSQL enum type:
        // preserved as a lowercase string conversion.
        builder.Property(entry => entry.EntryType)
            .HasConversion(
                type => type.ToString().ToLowerInvariant(),
                value => Enum.Parse<VisitTimeEntryType>(value, ignoreCase: true))
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(entry => entry.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(entry => entry.TechnicianId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
