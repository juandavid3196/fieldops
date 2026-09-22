using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianSkillConfiguration
    : IEntityTypeConfiguration<TechnicianSkill>
{
    public void Configure(EntityTypeBuilder<TechnicianSkill> builder)
    {
        builder.ToTable("technician_skills", table => table.HasCheckConstraint(
            "ck_technician_skills_proficiency",
            "proficiency BETWEEN 1 AND 5"));

        // PRIMARY KEY (technician_id, skill_id)
        builder.HasKey(technicianSkill => new
        {
            technicianSkill.TechnicianId,
            technicianSkill.SkillId,
        });

        builder.Property(technicianSkill => technicianSkill.Proficiency);

        builder.Property(technicianSkill => technicianSkill.YearsExperience)
            .HasPrecision(4, 1);

        builder.HasOne<TechnicianProfile>()
            .WithMany()
            .HasForeignKey(technicianSkill => technicianSkill.TechnicianId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Skill>()
            .WithMany()
            .HasForeignKey(technicianSkill => technicianSkill.SkillId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
