using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.Users;

/// <summary>
/// AC-22: the BR-16 migration, run against its own disposable container so
/// the shared fixture's migrated database is untouched.
/// </summary>
public class UsersInvitationMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260928220538_AddCompanySetupCompletion";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsNamesAddsColumnsCheckAndPartialUniqueIndexOnOpenEmail()
    {
        var connectionString = _postgres.GetConnectionString();
        await MigrateAsync(connectionString, PreviousMigration);

        var org = Guid.NewGuid();
        var user = Guid.NewGuid();
        var existing = Guid.NewGuid();
        await ExecuteAsync(connectionString, "INSERT INTO organizations (id, name) VALUES (@o, 'Migrated')", ("o", org));
        await ExecuteAsync(
            connectionString,
            "INSERT INTO users (id, email, password_hash, first_name, last_name, status) VALUES (@u, 'm@example.com', 'x', 'M', 'U', 'active')",
            ("u", user));
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO user_invitations (id, organization_id, email, role_id, token_hash, invited_by_user_id, expires_at)
            VALUES (@i, @o, 'old@example.com', 1, 'old-hash', @u, now() + interval '7 days')
            """,
            ("i", existing), ("o", org), ("u", user));

        await MigrateAsync(connectionString, null);

        // Existing rows are backfilled with empty names and false flags; the defaults are gone.
        Assert.Equal("", await ScalarAsync<string>(connectionString, "SELECT first_name FROM user_invitations WHERE id = @i", ("i", existing)));
        Assert.Equal("", await ScalarAsync<string>(connectionString, "SELECT last_name FROM user_invitations WHERE id = @i", ("i", existing)));
        Assert.False(await ScalarAsync<bool>(connectionString, "SELECT is_all_branches FROM user_invitations WHERE id = @i", ("i", existing)));
        Assert.False(await ScalarAsync<bool>(connectionString, "SELECT link_team_profile FROM user_invitations WHERE id = @i", ("i", existing)));
        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'user_invitations' AND column_name IN ('first_name','last_name') AND column_default IS NOT NULL"));
        Assert.Equal(
            2L,
            await ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'user_invitations' AND column_name IN ('is_all_branches','link_team_profile') AND column_default = 'false' AND is_nullable = 'NO'"));
        Assert.Equal(
            "character varying",
            await ScalarAsync<string>(
                connectionString,
                "SELECT data_type FROM information_schema.columns WHERE table_name = 'user_invitations' AND column_name = 'first_name'"));
        Assert.Equal(
            100,
            await ScalarAsync<int>(
                connectionString,
                "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'user_invitations' AND column_name = 'last_name'"));

        // The named CHECK and the partial unique index exist per the amended schema.
        Assert.Equal(
            "CHECK ((expires_at > created_at))",
            await ScalarAsync<string>(
                connectionString,
                "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_user_invitations_expires_after_created'"));
        var index = await ScalarAsync<string>(
            connectionString, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_user_invitations_open_email'");
        Assert.Contains("UNIQUE", index, StringComparison.Ordinal);
        Assert.Contains("(organization_id, email)", index, StringComparison.Ordinal);
        Assert.Contains("accepted_at IS NULL", index, StringComparison.Ordinal);
        Assert.Contains("revoked_at IS NULL", index, StringComparison.Ordinal);

        // A second open invitation for the same organization and email violates the index...
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertInvitationAsync(
            connectionString, org, user, "old@example.com", "hash-2", revoked: false));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("ux_user_invitations_open_email", duplicate.ConstraintName);

        // ...but not once the first is revoked, and not across organizations.
        await ExecuteAsync(connectionString, "UPDATE user_invitations SET revoked_at = now() WHERE id = @i", ("i", existing));
        await InsertInvitationAsync(connectionString, org, user, "old@example.com", "hash-3", revoked: false);
        var otherOrg = Guid.NewGuid();
        await ExecuteAsync(connectionString, "INSERT INTO organizations (id, name, public_slug) VALUES (@o, 'Other', 'other-org')", ("o", otherOrg));
        await InsertInvitationAsync(connectionString, otherOrg, user, "old@example.com", "hash-4", revoked: false);

        // expires_at must be after created_at.
        var check = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            """
            INSERT INTO user_invitations (organization_id, email, first_name, last_name, role_id, token_hash, invited_by_user_id, expires_at, created_at)
            VALUES (@o, 'late@example.com', 'L', 'E', 1, 'hash-5', @u, now() - interval '1 day', now())
            """,
            ("o", org), ("u", user)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, check.SqlState);
        Assert.Equal("ck_user_invitations_expires_after_created", check.ConstraintName);
    }

    private static Task InsertInvitationAsync(
        string connectionString, Guid org, Guid user, string email, string hash, bool revoked) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO user_invitations (organization_id, email, first_name, last_name, role_id, token_hash, invited_by_user_id, expires_at)
            VALUES (@o, @e, 'F', 'L', 1, @h, @u, now() + interval '7 days')
            """,
            ("o", org), ("e", email), ("h", hash), ("u", user));

    private static async Task MigrateAsync(string connectionString, string? targetMigration)
    {
        await using var factory = FieldOpsApiFactory.Create(connectionString: connectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }
}
