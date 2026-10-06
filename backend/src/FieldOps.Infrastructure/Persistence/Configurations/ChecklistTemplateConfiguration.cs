using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ChecklistTemplateConfiguration : IEntityTypeConfiguration<ChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplate> builder)
    {
        builder.ToTable("checklist_templates");

        builder.HasKey(template => template.Id);

        // UNIQUE (organization_id, id): target of the composite foreign key of the items.
        builder.HasAlternateKey(template => new
        {
            template.OrganizationId,
            template.Id,
        });

        builder.Property(template => template.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(template => template.OrganizationId)
            .IsRequired();

        builder.Property(template => template.ServiceCategoryId);

        builder.Property(template => template.Name)
            .HasMaxLength(120)
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(template => template.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(template => template.CreatedByUserId)
            .IsRequired();

        builder.Property(template => template.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(template => template.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE INDEX ux_checklist_templates_org_name ON checklist_templates (organization_id, lower(name)) is
        // created by the migration with SQL: EF Core cannot express an expression index in the model.

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(template => template.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, service_category_id) REFERENCES service_categories (organization_id, id)
        builder.HasOne<ServiceCategory>()
            .WithMany()
            .HasForeignKey(template => new { template.OrganizationId, template.ServiceCategoryId })
            .HasPrincipalKey(category => new { category.OrganizationId, category.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(template => template.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
