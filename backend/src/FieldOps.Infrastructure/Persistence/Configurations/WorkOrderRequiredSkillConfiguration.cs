using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderRequiredSkillConfiguration
    : IEntityTypeConfiguration<WorkOrderRequiredSkill>
{
    public void Configure(EntityTypeBuilder<WorkOrderRequiredSkill> builder)
    {
        builder.ToTable("work_order_required_skills", table => table.HasCheckConstraint(
            "ck_work_order_required_skills_minimum_proficiency",
            "minimum_proficiency BETWEEN 1 AND 5"));

        // PRIMARY KEY (work_order_id, skill_id)
        builder.HasKey(requiredSkill => new
        {
            requiredSkill.WorkOrderId,
            requiredSkill.SkillId,
        });

        builder.Property(requiredSkill => requiredSkill.MinimumProficiency);

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(requiredSkill => requiredSkill.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Skill>()
            .WithMany()
            .HasForeignKey(requiredSkill => requiredSkill.SkillId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
