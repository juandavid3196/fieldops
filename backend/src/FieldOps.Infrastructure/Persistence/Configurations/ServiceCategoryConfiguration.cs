using FieldOps.Domain.Catalog;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ServiceCategoryConfiguration
    : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("service_categories");

        builder.HasKey(category => category.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(category => new
        {
            category.OrganizationId,
            category.Id,
        });

        builder.Property(category => category.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(category => category.OrganizationId)
            .IsRequired();

        builder.Property(category => category.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.Description)
            .HasColumnType("text");

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(category => category.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        // UNIQUE (organization_id, name)
        builder.HasIndex(category => new { category.OrganizationId, category.Name })
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(category => category.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
