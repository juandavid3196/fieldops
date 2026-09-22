using FieldOps.Domain.Organizations;
using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skills");

        builder.HasKey(skill => skill.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(skill => new
        {
            skill.OrganizationId,
            skill.Id,
        });

        builder.Property(skill => skill.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(skill => skill.OrganizationId)
            .IsRequired();

        builder.Property(skill => skill.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(skill => skill.Description)
            .HasColumnType("text");

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(skill => skill.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        // UNIQUE (organization_id, name)
        builder.HasIndex(skill => new { skill.OrganizationId, skill.Name })
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(skill => skill.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
