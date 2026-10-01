using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>
/// FR-08, AC-11: the generated migration applied on top of data created before it. Uses its own disposable
/// container (never a local database) so the schema can stop at the previous migration first.
/// </summary>
public class CustomerPropertyMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261001004947_AddCustomerManagement";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsOldestActivePropertyAsPrimaryAndDatabaseRejectsInvalidWrites()
    {
        await using var factory = FieldOpsApiFactory.Create(connectionString: _postgres.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var org = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var otherBranch = Guid.NewGuid();
        var oldest = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await ExecuteAsync("INSERT INTO organizations (id, name) VALUES (@a, 'Org A'), (@b, 'Org B')", ("a", org), ("b", otherOrg));
        await ExecuteAsync(
            "INSERT INTO branches (id, organization_id, name, code) VALUES (@a, @oa, 'Branch A', 'BRA'), (@b, @ob, 'Branch B', 'BRB')",
            ("a", branch), ("oa", org), ("b", otherBranch), ("ob", otherOrg));

        var withArchivedFirst = await SeedCustomerAsync(org, branch);
        var archivedOldest = await SeedPropertyAsync(org, withArchivedFirst, branch, active: false, oldest);
        var expectedFirst = await SeedPropertyAsync(org, withArchivedFirst, branch, active: true, oldest.AddDays(1));
        var laterActive = await SeedPropertyAsync(org, withArchivedFirst, branch, active: true, oldest.AddDays(2));

        var single = await SeedCustomerAsync(org, branch);
        var onlyProperty = await SeedPropertyAsync(org, single, branch, active: true, oldest);

        var archivedOnly = await SeedCustomerAsync(org, branch);
        var archivedProperty = await SeedPropertyAsync(org, archivedOnly, branch, active: false, oldest);

        var tied = await SeedCustomerAsync(org, branch);
        var tieLow = await SeedPropertyAsync(org, tied, branch, active: true, oldest, Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
        var tieHigh = await SeedPropertyAsync(org, tied, branch, active: true, oldest, Guid.Parse("00000000-0000-0000-0000-0000000000a2"));

        await dbContext.Database.MigrateAsync();

        // Each pre-existing customer's oldest active property (created_at, then id) is its primary one, and only it.
        Assert.Equal(
            new[] { expectedFirst, onlyProperty, tieLow }.Order().ToArray(),
            (await QueryGuidsAsync("SELECT id FROM properties WHERE is_primary")).Order().ToArray());
        Assert.Equal(
            new[] { archivedOldest, laterActive, archivedProperty, tieHigh }.Order().ToArray(),
            (await QueryGuidsAsync("SELECT id FROM properties WHERE NOT is_primary")).Order().ToArray());

        // The database rejects a second primary, an archived primary and a branch of another organization.
        var second = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE properties SET is_primary = true WHERE id = @p", ("p", laterActive)));
        Assert.Equal(("23505", "ux_properties_customer_primary"), (second.SqlState, second.ConstraintName));

        var archivedPrimary = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE properties SET is_primary = true WHERE id = @p", ("p", archivedProperty)));
        Assert.Equal(("23514", "ck_properties_primary_active"), (archivedPrimary.SqlState, archivedPrimary.ConstraintName));

        var foreignBranch = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE properties SET branch_id = @b WHERE id = @p", ("b", otherBranch), ("p", onlyProperty)));
        Assert.Equal(("23503", "fk_properties_branches_organization_id_branch_id"), (foreignBranch.SqlState, foreignBranch.ConstraintName));

        // Nothing changed and the notes index is the descending one.
        Assert.Equal(3L, await ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE is_primary"));
        Assert.Contains("created_at DESC", await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_customer_notes_customer'"), StringComparison.Ordinal);
    }

    private async Task<Guid> SeedCustomerAsync(Guid org, Guid branch)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            "INSERT INTO customers (id, organization_id, type, branch_id, display_name) VALUES (@id, @o, 'person', @b, @name)",
            ("id", id), ("o", org), ("b", branch), ("name", $"Customer {id:N}"));

        return id;
    }

    private async Task<Guid> SeedPropertyAsync(
        Guid org, Guid customer, Guid branch, bool active, DateTimeOffset createdAt, Guid? id = null)
    {
        var propertyId = id ?? Guid.NewGuid();

        await ExecuteAsync(
            "INSERT INTO properties (id, organization_id, customer_id, branch_id, name, address_line1, city, country_code, is_active, created_at) VALUES (@id, @o, @c, @b, 'Site', '1 Main St', 'Austin', 'US', @active, @created)",
            ("id", propertyId), ("o", org), ("c", customer), ("b", branch), ("active", active), ("created", createdAt));

        return propertyId;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid[]> QueryGuidsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();

        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }
}
