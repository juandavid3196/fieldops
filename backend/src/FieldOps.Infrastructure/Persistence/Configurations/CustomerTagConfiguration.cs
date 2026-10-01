using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerTagConfiguration : IEntityTypeConfiguration<CustomerTag>
{
    public const string OrgNormalizedNameIndexName = "ux_customer_tags_org_normalized_name";

    public void Configure(EntityTypeBuilder<CustomerTag> builder)
    {
        builder.ToTable("customer_tags");

        builder.HasKey(tag => tag.Id);

        // UNIQUE (organization_id, id): target of the assignment composite foreign key.
        builder.HasAlternateKey(tag => new
        {
            tag.OrganizationId,
            tag.Id,
        });

        builder.Property(tag => tag.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(tag => tag.OrganizationId)
            .IsRequired();

        builder.Property(tag => tag.Name)
            .HasMaxLength(CustomerTag.NameMaxLength)
            .IsRequired();

        builder.Property(tag => tag.NormalizedName)
            .HasMaxLength(CustomerTag.NameMaxLength)
            .IsRequired();

        builder.Property(tag => tag.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, normalized_name)
        builder.HasIndex(tag => new { tag.OrganizationId, tag.NormalizedName })
            .IsUnique()
            .HasDatabaseName(OrgNormalizedNameIndexName);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(tag => tag.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
