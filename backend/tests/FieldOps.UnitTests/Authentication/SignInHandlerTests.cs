using System.Net;
using FieldOps.Application.Authentication;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;

namespace FieldOps.UnitTests.Authentication;

public class SignInHandlerTests
{
    private const string Email = "user@example.com";

    private const string Password = "correct-password";

    private const string StoredHash = "stored-hash";

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakePasswordHasher _hasher = new();

    private readonly FakeThrottle _throttle = new();

    private readonly FakeStore _store = new();

    private readonly SignInHandler _handler;

    public SignInHandlerTests()
    {
        _handler = new SignInHandler(
            new SignInCommandValidator(),
            _throttle,
            _hasher,
            _store,
            new MutableTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_VerifiesDummyHashOnceAndReturnsInvalidCredentials()
    {
        var result = await _handler.HandleAsync(Command(Email, "any-password"), CancellationToken.None);

        var failed = Assert.IsType<SignInResult.InvalidCredentials>(result);
        Assert.Equal(SignInFailureCategory.UnknownEmail, failed.Category);
        Assert.Null(failed.UserId);
        var verification = Assert.Single(_hasher.Verifications);
        Assert.Equal(FakePasswordHasher.Dummy, verification.StoredHash);
        Assert.Equal("any-password", verification.Password);
        Assert.Equal([Email], _throttle.Failures);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_RecordsSignInAuditAndResetsThrottle()
    {
        var user = AddUser(UserStatus.Active);
        var membership = AddMembership(user);
        var clientIp = IPAddress.Parse("198.51.100.4");

        var result = await _handler.HandleAsync(
            Command(" User@Example.COM ", Password, clientIp),
            CancellationToken.None);

        var succeeded = Assert.IsType<SignInResult.Succeeded>(result);
        Assert.Equal(membership.MembershipId, succeeded.MembershipId);
        Assert.Equal(Now, succeeded.SignedInAt);
        Assert.Equal(user.Id, succeeded.Session.User.Id);
        Assert.Equal(membership.OrganizationId, succeeded.Session.Organization.Id);
        Assert.Equal(membership.OrganizationName, succeeded.Session.Organization.Name);
        Assert.Equal(new SessionRole("owner", "Owner"), succeeded.Session.Role);

        Assert.Equal(Now, user.LastLoginAt);
        var (savedUser, audit) = Assert.Single(_store.Saved);
        Assert.Same(user, savedUser);
        Assert.Equal("auth.signed_in", audit.Action);
        Assert.Equal("user", audit.EntityType);
        Assert.Equal(user.Id, audit.EntityId);
        Assert.Equal(user.Id, audit.ActorUserId);
        Assert.Equal(membership.OrganizationId, audit.OrganizationId);
        Assert.Equal(clientIp, audit.IpAddress);
        Assert.Null(audit.BranchId);
        Assert.Null(audit.BeforeData);
        Assert.Null(audit.AfterData);
        Assert.Equal("{}", audit.Metadata);

        Assert.Equal([Email], _throttle.Resets);
        Assert.Empty(_throttle.Failures);
        Assert.Equal(Email, _store.LookedUpEmail);
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Disabled)]
    public async Task HandleAsync_WrongPasswordForUserNotActive_ReportsPasswordMismatchBeforeStatus(UserStatus status)
    {
        var user = AddUser(status);
        AddMembership(user);

        var result = await _handler.HandleAsync(Command(Email, "wrong-password"), CancellationToken.None);

        var failed = Assert.IsType<SignInResult.InvalidCredentials>(result);
        Assert.Equal(SignInFailureCategory.PasswordMismatch, failed.Category);
        Assert.Equal(user.Id, failed.UserId);
        Assert.Equal(0, _store.MembershipQueries);
        Assert.Equal([Email], _throttle.Failures);
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Disabled)]
    public async Task HandleAsync_CorrectPasswordForUserNotActive_ReturnsUserNotActive(UserStatus status)
    {
        var user = AddUser(status);
        AddMembership(user);

        var result = await _handler.HandleAsync(Command(Email, Password), CancellationToken.None);

        var failed = Assert.IsType<SignInResult.InvalidCredentials>(result);
        Assert.Equal(SignInFailureCategory.UserNotActive, failed.Category);
        Assert.Empty(_store.Saved);
        Assert.Null(user.LastLoginAt);
        Assert.Equal([Email], _throttle.Failures);
    }

    [Fact]
    public async Task HandleAsync_NoEligibleMembership_ReturnsNoEligibleMembership()
    {
        var user = AddUser(UserStatus.Active);
        AddMembership(user, status: UserStatus.Suspended);
        AddMembership(user, organizationIsActive: false);

        var result = await _handler.HandleAsync(Command(Email, Password), CancellationToken.None);

        var failed = Assert.IsType<SignInResult.InvalidCredentials>(result);
        Assert.Equal(SignInFailureCategory.NoEligibleMembership, failed.Category);
        Assert.Empty(_store.Saved);
        Assert.Equal([Email], _throttle.Failures);
    }

    [Fact]
    public async Task HandleAsync_ThrottledEmail_ReturnsThrottledWithoutVerifyingPassword()
    {
        AddUser(UserStatus.Active);
        _throttle.RetryAfter = TimeSpan.FromSeconds(90);

        var result = await _handler.HandleAsync(Command(Email, Password), CancellationToken.None);

        var throttled = Assert.IsType<SignInResult.Throttled>(result);
        Assert.Equal(TimeSpan.FromSeconds(90), throttled.RetryAfter);
        Assert.Empty(_hasher.Verifications);
        Assert.Null(_store.LookedUpEmail);
        Assert.Empty(_throttle.Failures);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task HandleAsync_ThrottledUnknownEmail_ReturnsThrottledWithoutVerifyingPassword()
    {
        _throttle.RetryAfter = TimeSpan.FromSeconds(90);

        var result = await _handler.HandleAsync(Command(Email, "any"), CancellationToken.None);

        Assert.IsType<SignInResult.Throttled>(result);
        Assert.Empty(_hasher.Verifications);
    }

    [Fact]
    public async Task HandleAsync_InvalidInput_ReturnsErrorsWithoutTouchingThrottleOrStore()
    {
        _throttle.RetryAfter = TimeSpan.FromSeconds(90);

        var result = await _handler.HandleAsync(Command("", ""), CancellationToken.None);

        var invalid = Assert.IsType<SignInResult.Invalid>(result);
        Assert.Equal(["Enter your email address."], invalid.Errors["email"]);
        Assert.Equal(["Enter your password."], invalid.Errors["password"]);
        Assert.Equal(0, _throttle.Checks);
        Assert.Empty(_throttle.Failures);
        Assert.Null(_store.LookedUpEmail);
        Assert.Empty(_hasher.Verifications);
    }

    [Fact]
    public async Task HandleAsync_SaveFails_PropagatesWithoutResettingOrCountingFailure()
    {
        var user = AddUser(UserStatus.Active);
        AddMembership(user);
        _store.FailOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command(Email, Password), CancellationToken.None));

        Assert.Empty(_throttle.Resets);
        Assert.Empty(_throttle.Failures);
    }

    private static SignInCommand Command(string email, string password, IPAddress? clientIp = null) =>
        new(email, password, RememberMe: false, clientIp);

    private User AddUser(UserStatus status)
    {
        var user = TestUsers.Create(status, Email, StoredHash);
        _store.User = user;
        return user;
    }

    private MembershipCandidate AddMembership(
        User user,
        UserStatus status = UserStatus.Active,
        bool organizationIsActive = true)
    {
        var membership = new MembershipCandidate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Acme Services",
            organizationIsActive,
            status,
            "owner",
            "Owner",
            JoinedAt: Now.AddDays(-30),
            CreatedAt: Now.AddDays(-30));

        _store.Memberships.Add(membership);
        return membership;
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public const string Dummy = "dummy-hash";

        public List<(string Password, string StoredHash)> Verifications { get; } = [];

        public string DummyHash => Dummy;

        public string Hash(string password) => throw new NotSupportedException();

        public bool Verify(string password, string storedHash)
        {
            Verifications.Add((password, storedHash));
            return storedHash == StoredHash && password == Password;
        }
    }

    private sealed class FakeThrottle : ISignInThrottle
    {
        public TimeSpan? RetryAfter { get; set; }

        public int Checks { get; private set; }

        public List<string> Failures { get; } = [];

        public List<string> Resets { get; } = [];

        public TimeSpan? GetRetryAfter(string normalizedEmail)
        {
            Checks++;
            return RetryAfter;
        }

        public void RecordFailure(string normalizedEmail) => Failures.Add(normalizedEmail);

        public void Reset(string normalizedEmail) => Resets.Add(normalizedEmail);
    }

    private sealed class FakeStore : IAuthenticationStore
    {
        public User? User { get; set; }

        public List<MembershipCandidate> Memberships { get; } = [];

        public List<(User User, AuditLog AuditLog)> Saved { get; } = [];

        public string? LookedUpEmail { get; private set; }

        public int MembershipQueries { get; private set; }

        public bool FailOnSave { get; set; }

        public Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            LookedUpEmail = normalizedEmail;
            return Task.FromResult(User?.Email == normalizedEmail ? User : null);
        }

        public Task<IReadOnlyList<MembershipCandidate>> GetMembershipsAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            MembershipQueries++;
            return Task.FromResult<IReadOnlyList<MembershipCandidate>>(Memberships);
        }

        public Task<SessionView?> FindActiveSessionAsync(
            Guid userId,
            Guid organizationId,
            Guid membershipId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveSignInAsync(User user, AuditLog auditLog, CancellationToken cancellationToken)
        {
            if (FailOnSave)
            {
                throw new InvalidOperationException("Save failed.");
            }

            Saved.Add((user, auditLog));
            return Task.CompletedTask;
        }
    }
}
