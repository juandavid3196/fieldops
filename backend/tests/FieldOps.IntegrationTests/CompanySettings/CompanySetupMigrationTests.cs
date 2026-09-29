using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// The BR-14 migration and backfill (AC-18), run against its own disposable
/// container so the shared fixture's fully migrated database is untouched.
/// </summary>
public class CompanySetupMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260922170641_AddNotificationsAndAudit";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsOldestActiveBranchAsMainEnforcesUniqueMainAndRegistrationCreatesMain()
    {
        var connectionString = ConnectionFor("fieldops_backfill");
        await CreateDatabaseAsync("fieldops_backfill");
        await MigrateAsync(connectionString, PreviousMigration);

        var singleOrg = Guid.NewGuid();
        var singleBranch = Guid.NewGuid();
        var threeOrg = Guid.NewGuid();
        var inactiveOldest = Guid.NewGuid();
        var tiedLow = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        var tiedHigh = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
        var t0 = DateTimeOffset.UtcNow.AddDays(-10);
        var t1 = DateTimeOffset.UtcNow.AddDays(-5);

        await ExecuteAsync(connectionString, "INSERT INTO organizations (id, name) VALUES (@a, 'One'), (@b, 'Three')",
            ("a", singleOrg), ("b", threeOrg));
        await InsertBranchAsync(connectionString, singleBranch, singleOrg, "S1", true, t1);
        await InsertBranchAsync(connectionString, inactiveOldest, threeOrg, "OLD", false, t0);
        await InsertBranchAsync(connectionString, tiedHigh, threeOrg, "HIGH", true, t1);
        await InsertBranchAsync(connectionString, tiedLow, threeOrg, "LOW", true, t1);

        var updatedBefore = await ScalarAsync<DateTime>(
            connectionString, "SELECT updated_at FROM branches WHERE id = @id", ("id", tiedHigh));

        await MigrateAsync(connectionString, null);

        Assert.Equal(new[] { singleBranch }, await MainIdsAsync(connectionString, singleOrg));
        Assert.Equal(new[] { tiedLow }, await MainIdsAsync(connectionString, threeOrg));
        Assert.Equal(
            4L,
            await ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM branches WHERE uses_company_billing AND cardinality(service_postal_codes) = 0"));
        Assert.Equal(
            updatedBefore,
            await ScalarAsync<DateTime>(
                connectionString, "SELECT updated_at FROM branches WHERE id = @id", ("id", tiedHigh)));
        Assert.False(await ScalarAsync<bool>(
            connectionString, "SELECT is_active FROM branches WHERE id = @id", ("id", inactiveOldest)));

        var duplicateMain = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "UPDATE branches SET is_main = true WHERE id = @id",
            ("id", tiedHigh)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateMain.SqlState);
        Assert.Equal("ux_branches_org_main", duplicateMain.ConstraintName);

        var inactiveMain = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "UPDATE branches SET is_main = true WHERE id = @id", ("id", inactiveOldest)));
        Assert.Equal("ck_branches_main_active", inactiveMain.ConstraintName);

        await using var api = FieldOpsApiFactory.Create(connectionString: connectionString);
        using var client = api.CreateClient();
        var registration = await OrganizationRegistrationApi.PostAsync(
            client, OrganizationRegistrationApi.ValidBody(branchCode: "FIRST"));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var organizationId = (await registration.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("organizationId").GetGuid();
        Assert.True(await ScalarAsync<bool>(
            connectionString,
            "SELECT is_main FROM branches WHERE organization_id = @o",
            ("o", organizationId)));
    }

    [Fact]
    public async Task Migration_OrganizationWithoutActiveBranch_FailsAndRollsBack()
    {
        var connectionString = ConnectionFor("fieldops_no_active");
        await CreateDatabaseAsync("fieldops_no_active");
        await MigrateAsync(connectionString, PreviousMigration);

        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        await ExecuteAsync(connectionString, "INSERT INTO organizations (id, name) VALUES (@a, 'NoActive')", ("a", org));
        await InsertBranchAsync(connectionString, branch, org, "OFF", false, DateTimeOffset.UtcNow.AddDays(-1));

        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(connectionString, null));

        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'branches' AND column_name = 'is_main'"));
        Assert.False(await ScalarAsync<bool>(
            connectionString, "SELECT is_active FROM branches WHERE id = @id", ("id", branch)));
    }

    private string ConnectionFor(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task CreateDatabaseAsync(string database)
    {
        await ExecuteAsync(_postgres.GetConnectionString(), $"CREATE DATABASE {database}");
    }

    private static async Task MigrateAsync(string connectionString, string? targetMigration)
    {
        await using var factory = FieldOpsApiFactory.Create(connectionString: connectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    private static Task InsertBranchAsync(
        string connectionString, Guid id, Guid organizationId, string code, bool isActive, DateTimeOffset createdAt) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO branches (id, organization_id, name, code, is_active, created_at)
            VALUES (@id, @o, @code, @code, @active, @created)
            """,
            ("id", id), ("o", organizationId), ("code", code), ("active", isActive), ("created", createdAt));

    private static async Task<Guid[]> MainIdsAsync(string connectionString, Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM branches WHERE organization_id = @o AND is_main", connection);
        command.Parameters.AddWithValue("o", organizationId);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();

        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    private static async Task ExecuteAsync(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
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

    private static async Task<T> ScalarAsync<T>(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
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
