using FieldOps.Application.Authentication;
using FieldOps.Domain.Users;

namespace FieldOps.UnitTests.Authentication;

public class ActiveOrganizationSelectorTests
{
    private static readonly DateTimeOffset CreatedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Select_TwoActiveMemberships_ReturnsEarliestJoined()
    {
        var organizationA = Membership(joinedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var organizationB = Membership(joinedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var selected = ActiveOrganizationSelector.Select([organizationA, organizationB]);

        Assert.Equal(organizationB, selected);
    }

    [Fact]
    public void Select_EarlierMembershipInInactiveOrganization_ReturnsActiveOrganization()
    {
        var inactiveB = Membership(
            joinedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            organizationIsActive: false);
        var activeA = Membership(joinedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var selected = ActiveOrganizationSelector.Select([inactiveB, activeA]);

        Assert.Equal(activeA, selected);
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Disabled)]
    public void Select_EarlierMembershipNotActive_IsSkipped(UserStatus status)
    {
        var notActive = Membership(
            joinedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            status: status);
        var active = Membership(joinedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(active, ActiveOrganizationSelector.Select([notActive, active]));
    }

    [Fact]
    public void Select_NullJoinedAt_SortsLast()
    {
        var neverJoined = Membership(joinedAt: null, createdAt: CreatedAt.AddYears(-5));
        var joined = Membership(joinedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(joined, ActiveOrganizationSelector.Select([neverJoined, joined]));
    }

    [Fact]
    public void Select_SameJoinedAt_ReturnsEarliestCreated()
    {
        var joinedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = Membership(joinedAt: joinedAt, createdAt: CreatedAt.AddDays(1));
        var earlier = Membership(joinedAt: joinedAt, createdAt: CreatedAt);

        Assert.Equal(earlier, ActiveOrganizationSelector.Select([later, earlier]));
    }

    [Fact]
    public void Select_SameJoinedAndCreated_ReturnsLowestId()
    {
        var high = Membership(
            joinedAt: null,
            membershipId: Guid.Parse("ffffffff-0000-0000-0000-000000000000"));
        var low = Membership(
            joinedAt: null,
            membershipId: Guid.Parse("0fffffff-ffff-ffff-ffff-ffffffffffff"));

        Assert.Equal(low, ActiveOrganizationSelector.Select([high, low]));
    }

    [Fact]
    public void Select_NoEligibleMembership_ReturnsNull()
    {
        Assert.Null(ActiveOrganizationSelector.Select([]));
        Assert.Null(ActiveOrganizationSelector.Select(
        [
            Membership(joinedAt: null, status: UserStatus.Suspended),
            Membership(joinedAt: null, organizationIsActive: false),
        ]));
    }

    private static MembershipCandidate Membership(
        DateTimeOffset? joinedAt,
        bool organizationIsActive = true,
        UserStatus status = UserStatus.Active,
        DateTimeOffset? createdAt = null,
        Guid? membershipId = null) =>
        new(
            membershipId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "Organization",
            organizationIsActive,
            status,
            "owner",
            "Owner",
            joinedAt,
            createdAt ?? CreatedAt);
}
