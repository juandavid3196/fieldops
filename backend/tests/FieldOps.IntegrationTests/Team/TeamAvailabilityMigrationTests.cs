using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.Team;

/// <summary>
/// team-availability-skills AC-25, BR-22: the migration on top of existing data in its own disposable container (never a
/// local database): profiles with personal rows keep them, the others inherit, and the exception reason accepts 250 characters.
/// </summary>
public class TeamAvailabilityMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261020215611_AddGlobalSearchUnaccent";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsCustomSchedulesAndWidensTheReason()
    {
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var withRows = Guid.NewGuid();
        var withoutRows = Guid.NewGuid();
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "fieldops_team" }.ConnectionString;

        await ExecuteAsync(_postgres.GetConnectionString(), "CREATE DATABASE fieldops_team");
        await MigrateAsync(connection, PreviousMigration);
        await ExecuteAsync(connection, "INSERT INTO organizations (id, name, public_slug) VALUES (@o, 'Org', 'org-' || replace(CAST(@o AS text), '-', ''))", ("o", org));
        await ExecuteAsync(connection, "INSERT INTO branches (id, organization_id, name, code, is_main) VALUES (@b, @o, 'Main', 'MAIN', true)", ("b", branch), ("o", org));
        await ExecuteAsync(
            connection,
            "INSERT INTO technician_profiles (id, organization_id, branch_id, first_name, last_name) VALUES (@a, @o, @b, 'With', 'Rows'), (@c, @o, @b, 'Without', 'Rows')",
            ("a", withRows), ("c", withoutRows), ("o", org), ("b", branch));
        await ExecuteAsync(
            connection,
            "INSERT INTO technician_weekly_availability (technician_id, day_of_week, start_time, end_time) VALUES (@a, 1, '09:00', '17:00')",
            ("a", withRows));

        await MigrateAsync(connection, null);

        Assert.False(await ScalarAsync<bool>(connection, "SELECT uses_branch_hours FROM technician_profiles WHERE id = @a", ("a", withRows)));
        Assert.True(await ScalarAsync<bool>(connection, "SELECT uses_branch_hours FROM technician_profiles WHERE id = @a", ("a", withoutRows)));
        Assert.Equal(
            250,
            await ScalarAsync<int>(connection, "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'technician_exceptions' AND column_name = 'reason'"));
        await ExecuteAsync(
            connection,
            "INSERT INTO technician_exceptions (technician_id, starts_at, ends_at, reason) VALUES (@a, now(), now() + interval '1 day', @r)",
            ("a", withRows), ("r", new string('r', 250)));
    }

    private static async Task MigrateAsync(string connectionString, string? targetMigration)
    {
        await using var factory = FieldOpsApiFactory.Create(connectionString: connectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        await dbContext.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }
}
