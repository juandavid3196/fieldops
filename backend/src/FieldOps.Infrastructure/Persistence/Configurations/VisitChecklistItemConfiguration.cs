using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class VisitChecklistItemConfiguration
    : IEntityTypeConfiguration<VisitChecklistItem>
{
    public void Configure(EntityTypeBuilder<VisitChecklistItem> builder)
    {
        builder.ToTable("visit_checklist_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(item => item.VisitId)
            .IsRequired();

        builder.Property(item => item.TemplateItemId);

        builder.Property(item => item.Label)
            .HasMaxLength(240)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(item => item.IsRequired)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(item => item.IsCompleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(item => item.CompletedByUserId);

        builder.Property(item => item.CompletedAt);

        builder.Property(item => item.Notes)
            .HasColumnType("text");

        builder.Property(item => item.SortOrder)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(item => item.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<WorkOrderChecklistTemplate>()
            .WithMany()
            .HasForeignKey(item => item.TemplateItemId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.CompletedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
