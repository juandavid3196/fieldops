using FieldOps.Domain.Branches;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");

        builder.HasKey(branch => branch.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(branch => new
        {
            branch.OrganizationId,
            branch.Id,
        });

        builder.Property(branch => branch.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(branch => branch.OrganizationId)
            .IsRequired();

        builder.Property(branch => branch.Name)
            .HasMaxLength(140)
            .IsRequired();

        builder.Property(branch => branch.Code)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(branch => branch.Email)
            .HasMaxLength(254);

        builder.Property(branch => branch.Phone)
            .HasMaxLength(40);

        builder.Property(branch => branch.AddressLine1)
            .HasMaxLength(180);

        builder.Property(branch => branch.AddressLine2)
            .HasMaxLength(180);

        builder.Property(branch => branch.City)
            .HasMaxLength(100);

        builder.Property(branch => branch.StateRegion)
            .HasMaxLength(100);

        builder.Property(branch => branch.PostalCode)
            .HasMaxLength(30);

        builder.Property(branch => branch.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.Property(branch => branch.Timezone)
            .HasMaxLength(80);

        builder.Property(branch => branch.BusinessHours)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        // The sentinel keeps an explicit false from being replaced by the
        // database default (true) on insert.
        builder.Property(branch => branch.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(branch => branch.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(branch => branch.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, code)
        builder.HasIndex(branch => new { branch.OrganizationId, branch.Code })
            .IsUnique();

        // CREATE INDEX ix_branches_org_active ON branches (organization_id, is_active)
        builder.HasIndex(branch => new { branch.OrganizationId, branch.IsActive })
            .HasDatabaseName("ix_branches_org_active");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(branch => branch.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
