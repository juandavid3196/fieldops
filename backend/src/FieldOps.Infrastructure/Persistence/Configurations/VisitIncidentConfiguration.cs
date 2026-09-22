using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitIncidentConfiguration
    : IEntityTypeConfiguration<VisitIncident>
{
    public void Configure(EntityTypeBuilder<VisitIncident> builder)
    {
        builder.ToTable("visit_incidents");

        builder.HasKey(incident => incident.Id);

        builder.Property(incident => incident.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(incident => incident.VisitId)
            .IsRequired();

        builder.Property(incident => incident.Type)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(incident => incident.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(incident => incident.AdditionalWorkRequested)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(incident => incident.CreatedByUserId)
            .IsRequired();

        builder.Property(incident => incident.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(incident => incident.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(incident => incident.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
