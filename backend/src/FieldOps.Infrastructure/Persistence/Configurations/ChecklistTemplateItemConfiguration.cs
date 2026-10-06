using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ChecklistTemplateItemConfiguration : IEntityTypeConfiguration<ChecklistTemplateItem>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplateItem> builder)
    {
        builder.ToTable("checklist_template_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(item => item.OrganizationId)
            .IsRequired();

        builder.Property(item => item.TemplateId)
            .IsRequired();

        builder.Property(item => item.Label)
            .HasMaxLength(240)
            .IsRequired();

        builder.Property(item => item.SortOrder)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, template_id) REFERENCES checklist_templates (organization_id, id) ON DELETE CASCADE
        builder.HasOne<ChecklistTemplate>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, item.TemplateId })
            .HasPrincipalKey(template => new { template.OrganizationId, template.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
