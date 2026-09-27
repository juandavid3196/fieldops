using FieldOps.Domain.Users;
using FieldOps.UnitTests.Authentication;

namespace FieldOps.UnitTests.Users;

public class UserTests
{
    private static readonly DateTimeOffset SignedInAt = new(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RecordSignIn_ActiveUser_SetsLastLoginAtAndUpdatedAt()
    {
        var user = TestUsers.Create(UserStatus.Active);

        user.RecordSignIn(SignedInAt);

        Assert.Equal(SignedInAt, user.LastLoginAt);
        Assert.Equal(SignedInAt, user.UpdatedAt);
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Disabled)]
    public void RecordSignIn_UserNotActive_ThrowsAndKeepsLastLoginAt(UserStatus status)
    {
        var user = TestUsers.Create(status);

        Assert.Throws<InvalidOperationException>(() => user.RecordSignIn(SignedInAt));
        Assert.Null(user.LastLoginAt);
    }

    [Fact]
    public void RecordSignIn_DefaultTime_Throws()
    {
        var user = TestUsers.Create(UserStatus.Active);

        Assert.Throws<ArgumentException>(() => user.RecordSignIn(default));
    }

    [Fact]
    public void Create_WithPhone_TrimsPhone()
    {
        var user = User.Create("owner@acme.com", "hash", "Ada", "Lovelace", " +1 555 111 2222 ");

        Assert.Equal("+1 555 111 2222", user.Phone);
    }

    [Fact]
    public void Create_WithoutPhone_LeavesItNull()
    {
        var user = User.Create("owner@acme.com", "hash", "Ada", "Lovelace");

        Assert.Null(user.Phone);
    }

    [Fact]
    public void Activate_PendingUser_SetsActiveAndLeavesEmailVerifiedAtNull()
    {
        var user = User.Create("owner@acme.com", "hash", "Ada", "Lovelace");

        user.Activate();

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.EmailVerifiedAt);
    }

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Disabled)]
    public void Activate_NotPending_Throws(UserStatus status)
    {
        var user = TestUsers.Create(status);

        Assert.Throws<InvalidOperationException>(() => user.Activate());
    }
}
