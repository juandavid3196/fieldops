using FieldOps.Domain.Branches;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class InvitationBranchConfiguration
    : IEntityTypeConfiguration<InvitationBranch>
{
    public void Configure(EntityTypeBuilder<InvitationBranch> builder)
    {
        builder.ToTable("invitation_branches");

        // PRIMARY KEY (invitation_id, branch_id)
        builder.HasKey(invitationBranch => new
        {
            invitationBranch.InvitationId,
            invitationBranch.BranchId,
        });

        builder.HasOne<UserInvitation>()
            .WithMany()
            .HasForeignKey(invitationBranch => invitationBranch.InvitationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(invitationBranch => invitationBranch.BranchId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
