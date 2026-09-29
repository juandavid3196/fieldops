using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationLogoConfiguration : IEntityTypeConfiguration<OrganizationLogo>
{
    public void Configure(EntityTypeBuilder<OrganizationLogo> builder)
    {
        builder.ToTable("organization_logos");

        // PRIMARY KEY (organization_id): one logo per organization.
        builder.HasKey(logo => logo.OrganizationId);

        builder.Property(logo => logo.OrganizationId)
            .ValueGeneratedNever();

        builder.Property(logo => logo.ContentType)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(logo => logo.Content)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(logo => logo.SizeBytes)
            .IsRequired();

        builder.Property(logo => logo.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(logo => logo.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_organization_logos_content_type",
                "content_type IN ('image/png','image/jpeg','image/svg+xml')");
            table.HasCheckConstraint(
                "ck_organization_logos_size_bytes",
                "size_bytes BETWEEN 1 AND 2097152");
        });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(logo => logo.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
