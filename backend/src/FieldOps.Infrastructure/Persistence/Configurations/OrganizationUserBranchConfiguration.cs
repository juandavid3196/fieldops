using FieldOps.Domain.Branches;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationUserBranchConfiguration
    : IEntityTypeConfiguration<OrganizationUserBranch>
{
    public void Configure(EntityTypeBuilder<OrganizationUserBranch> builder)
    {
        builder.ToTable("organization_user_branches");

        // PRIMARY KEY (organization_user_id, branch_id)
        builder.HasKey(organizationUserBranch => new
        {
            organizationUserBranch.OrganizationUserId,
            organizationUserBranch.BranchId,
        });

        builder.HasOne<OrganizationUser>()
            .WithMany()
            .HasForeignKey(organizationUserBranch =>
                organizationUserBranch.OrganizationUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(organizationUserBranch =>
                organizationUserBranch.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
