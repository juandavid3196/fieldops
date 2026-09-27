using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;

namespace FieldOps.UnitTests.Organizations;

public class OrganizationUserTests
{
    private static readonly DateTimeOffset JoinedAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ForRegistration_SetsActiveOwnerMembership()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var membership = OrganizationUser.Create(
            organizationId, userId, roleId: 1, isAllBranches: true, joinedAt: JoinedAt);

        Assert.Equal(UserStatus.Active, membership.Status);
        Assert.True(membership.IsAllBranches);
        Assert.Equal(JoinedAt, membership.JoinedAt);
        Assert.Null(membership.InvitedByUserId);
    }

    [Fact]
    public void Create_WithoutIsAllBranchesOrJoinedAt_DefaultsToFalseAndNull()
    {
        var membership = OrganizationUser.Create(Guid.NewGuid(), Guid.NewGuid(), roleId: 1);

        Assert.False(membership.IsAllBranches);
        Assert.Null(membership.JoinedAt);
    }
}
