using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderChecklistTemplateConfiguration
    : IEntityTypeConfiguration<WorkOrderChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<WorkOrderChecklistTemplate> builder)
    {
        builder.ToTable("work_order_checklist_templates");

        builder.HasKey(template => template.Id);

        builder.Property(template => template.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(template => template.WorkOrderId)
            .IsRequired();

        builder.Property(template => template.Label)
            .HasMaxLength(240)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(template => template.IsRequired)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(template => template.SortOrder)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(template => template.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
