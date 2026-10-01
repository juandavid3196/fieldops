using FieldOps.Domain.Organizations;
using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public const string NormalizedNameIndexName = "ux_skills_org_normalized_name";

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

        // UNIQUE INDEX ux_skills_org_normalized_name ON skills (organization_id, lower(name)) is created by the
        // migration with SQL: EF Core cannot express an expression index in the model.

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(skill => skill.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
