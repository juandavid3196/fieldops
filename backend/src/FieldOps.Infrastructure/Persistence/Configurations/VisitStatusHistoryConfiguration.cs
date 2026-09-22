using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitStatusHistoryConfiguration
    : IEntityTypeConfiguration<VisitStatusHistory>
{
    public void Configure(EntityTypeBuilder<VisitStatusHistory> builder)
    {
        builder.ToTable("visit_status_history");

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(history => history.VisitId)
            .IsRequired();

        // PostgreSQL enum visit_status, mapped in FieldOpsDbContext.
        builder.Property(history => history.FromStatus);

        builder.Property(history => history.ToStatus)
            .IsRequired();

        builder.Property(history => history.ChangedByUserId);

        builder.Property(history => history.Reason)
            .HasColumnType("text");

        builder.Property(history => history.ChangedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(history => history.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(history => history.ChangedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
