using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration
    : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(organization => organization.Id);

        builder.Property(organization => organization.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(organization => organization.LegalName)
            .HasMaxLength(200);

        builder.Property(organization => organization.TaxId)
            .HasMaxLength(60);

        builder.Property(organization => organization.Timezone)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(organization => organization.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(organization => organization.IsActive)
            .IsRequired();

        builder.Property(organization => organization.CreatedAt)
            .IsRequired();

        builder.Property(organization => organization.UpdatedAt)
            .IsRequired();
    }
}
