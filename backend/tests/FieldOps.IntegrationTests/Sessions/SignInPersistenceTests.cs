using System.Net;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SignInPersistenceTests(SessionsDatabaseFixture database)
{
    // Microsecond precision, as stored by timestamptz.
    private static readonly DateTimeOffset SignInTime = new(2026, 9, 24, 12, 34, 56, 789, 123, TimeSpan.Zero);

    [Fact]
    public async Task PostSessions_ValidCredentials_SetsLastLoginAtToSignInTime()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(SignInTime));

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await database.QueryAsync(
            "SELECT last_login_at, updated_at FROM users WHERE id = @id",
            ("id", account.UserId)));
        Assert.Equal(SignInTime, ToOffset(row["last_login_at"]));
        Assert.Equal(SignInTime, ToOffset(row["updated_at"]));
    }

    [Fact]
    public async Task PostSessions_ValidCredentials_InsertsOneAuditRowWithBr13Values()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var before = await CountAuditRowsAsync();

        var response = await SessionApi.SignInAsync(
            host.Client,
            account.Email,
            SessionsDatabaseFixture.Password,
            clientIp: "203.0.113.25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before + 1, await CountAuditRowsAsync());

        var row = Assert.Single(await database.QueryAsync(
            """
            SELECT organization_id, actor_user_id, action, entity_type, entity_id, branch_id,
                   before_data::text AS before_data, after_data::text AS after_data,
                   metadata::text AS metadata, host(ip_address) AS ip_address
            FROM audit_logs WHERE actor_user_id = @id
            """,
            ("id", account.UserId)));
        Assert.Equal(account.OrganizationId, row["organization_id"]);
        Assert.Equal(account.UserId, row["actor_user_id"]);
        Assert.Equal("auth.signed_in", row["action"]);
        Assert.Equal("user", row["entity_type"]);
        Assert.Equal(account.UserId, row["entity_id"]);
        Assert.Null(row["branch_id"]);
        Assert.Null(row["before_data"]);
        Assert.Null(row["after_data"]);
        Assert.Equal("{}", row["metadata"]);
        Assert.Equal("203.0.113.25", row["ip_address"]);
    }

    [Fact]
    public async Task PostSessions_AuditInsertFails_Returns500WithoutCookieOrChanges()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);

        // Test-only trigger in the disposable container, scoped to this user.
        var trigger = $"fail_audit_{account.UserId:N}";
        await database.ExecuteAsync(
            $"""
            CREATE FUNCTION {trigger}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'audit insert rejected by test'; END $$;
            CREATE TRIGGER {trigger} BEFORE INSERT ON audit_logs FOR EACH ROW
            WHEN (NEW.actor_user_id = '{account.UserId}') EXECUTE FUNCTION {trigger}();
            """);

        try
        {
            var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            SessionApi.AssertNoSessionCookie(response);
            Assert.DoesNotContain("audit insert rejected", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await database.ExecuteAsync(
                $"DROP TRIGGER {trigger} ON audit_logs; DROP FUNCTION {trigger}();");
        }

        Assert.Null(await database.ScalarAsync<DateTime?>(
            "SELECT last_login_at FROM users WHERE id = @id",
            ("id", account.UserId)));
        Assert.Equal(0L, await CountAuditRowsAsync(account.UserId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostSessions_FailedSignIn_AddsNoAuditRow(bool knownEmail)
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var before = await CountAuditRowsAsync();

        var response = await SessionApi.SignInAsync(
            host.Client,
            knownEmail ? account.Email : SessionsDatabaseFixture.NewEmail(),
            "wrong password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await CountAuditRowsAsync());
        Assert.Equal(0L, await CountAuditRowsAsync(account.UserId));
        Assert.Null(await database.ScalarAsync<DateTime?>(
            "SELECT last_login_at FROM users WHERE id = @id",
            ("id", account.UserId)));
    }

    private async Task<long> CountAuditRowsAsync(Guid? userId = null) =>
        userId is null
            ? await database.ScalarAsync<long>("SELECT count(*) FROM audit_logs")
            : await database.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE actor_user_id = @id OR entity_id = @id",
                ("id", userId.Value));

    private static DateTimeOffset ToOffset(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException($"Unexpected value {value}"),
    };
}
