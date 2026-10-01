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
/// AC-23, BR-24: the generated migration applied on top of existing data, in its own disposable container (never a
/// local database): existing exceptions become active, the index is created, and case-duplicate skills fail it.
/// </summary>
public class SkillsAvailabilityMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261001162526_AddTechnicianAccountLinkAndPrimarySkill";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_AddsExceptionStatusAndCaseInsensitiveSkillIndexAndFailsOnCaseDuplicates()
    {
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var tech = Guid.NewGuid();
        var exception = Guid.NewGuid();

        var good = ConnectionFor("fieldops_good");
        await CreateDatabaseAsync("fieldops_good");
        await MigrateAsync(good, PreviousMigration);
        await SeedBaseAsync(good, org, branch, tech);
        await ExecuteAsync(
            good,
            "INSERT INTO technician_exceptions (id, technician_id, starts_at, ends_at, is_available, reason) VALUES (@e, @t, now(), now() + interval '1 hour', false, 'Legacy')",
            ("e", exception), ("t", tech));
        await ExecuteAsync(good, "INSERT INTO skills (id, organization_id, name) VALUES (gen_random_uuid(), @o, 'HVAC'), (gen_random_uuid(), @o, 'Plumbing')", ("o", org));

        await MigrateAsync(good, null);

        Assert.Equal("active", await ScalarAsync<string>(good, "SELECT status FROM technician_exceptions WHERE id = @e", ("e", exception)));
        Assert.True(await ScalarAsync<bool>(good, "SELECT created_at IS NOT NULL AND updated_at IS NOT NULL FROM technician_exceptions WHERE id = @e", ("e", exception)));
        Assert.Equal(
            "CREATE UNIQUE INDEX ux_skills_org_normalized_name ON public.skills USING btree (organization_id, lower((name)::text))",
            await ScalarAsync<string>(good, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_skills_org_normalized_name'"));
        Assert.Equal(
            0,
            await ScalarAsync<long>(good, "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_skills_organization_id_name'"));

        var invalidStatus = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            good, "UPDATE technician_exceptions SET status = 'deleted' WHERE id = @e", ("e", exception)));
        Assert.Equal("ck_technician_exceptions_status", invalidStatus.ConstraintName);

        var caseDuplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            good, "INSERT INTO skills (id, organization_id, name) VALUES (gen_random_uuid(), @o, 'hvac')", ("o", org)));
        Assert.Equal("ux_skills_org_normalized_name", caseDuplicate.ConstraintName);

        // Case-duplicate names make the migration fail instead of deduplicating them.
        var bad = ConnectionFor("fieldops_bad");
        await CreateDatabaseAsync("fieldops_bad");
        await MigrateAsync(bad, PreviousMigration);
        await ExecuteAsync(bad, "INSERT INTO organizations (id, name) VALUES (@o, 'Dup Org')", ("o", org));
        await ExecuteAsync(bad, "INSERT INTO skills (id, organization_id, name) VALUES (gen_random_uuid(), @o, 'HVAC'), (gen_random_uuid(), @o, 'hvac')", ("o", org));

        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(bad, null));

        Assert.Equal(2, await ScalarAsync<long>(bad, "SELECT COUNT(*) FROM skills WHERE organization_id = @o", ("o", org)));
        Assert.Equal(
            0,
            await ScalarAsync<long>(bad, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'technician_exceptions' AND column_name = 'status'"));
    }

    private static async Task SeedBaseAsync(string connectionString, Guid org, Guid branch, Guid tech)
    {
        await ExecuteAsync(connectionString, "INSERT INTO organizations (id, name) VALUES (@o, 'Org')", ("o", org));
        await ExecuteAsync(
            connectionString,
            "INSERT INTO branches (id, organization_id, name, code, is_main) VALUES (@b, @o, 'Main', 'MAIN', true)",
            ("b", branch), ("o", org));
        await ExecuteAsync(
            connectionString,
            "INSERT INTO technician_profiles (id, organization_id, branch_id, first_name, last_name) VALUES (@t, @o, @b, 'Mig', 'Rated')",
            ("t", tech), ("o", org), ("b", branch));
    }

    private string ConnectionFor(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    private Task CreateDatabaseAsync(string database) =>
        ExecuteAsync(_postgres.GetConnectionString(), $"CREATE DATABASE {database}");

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
