using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>
/// AC-25: the BR-22 migration applied on top of existing work orders, visits and assignments, in its own disposable
/// container (never a local database): visit #1 takes the work order window, legacy duplicate primaries are reduced
/// to one, and the new checks and partial unique indexes then hold.
/// </summary>
public class DispatchMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261006165531_AddWorkOrderCreation";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsVisitOneWindow_ReducesLegacyPrimaries_AndEnforcesTheNewConstraints()
    {
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var user = Guid.NewGuid();
        var tech = Guid.NewGuid();
        var other = Guid.NewGuid();
        var withWindow = Guid.NewGuid();
        var withoutWindow = Guid.NewGuid();
        var visitWithWindow = Guid.NewGuid();
        var visitWithoutWindow = Guid.NewGuid();
        var visitTwo = Guid.NewGuid();
        var window = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(8);

        await MigrateAsync(PreviousMigration);

        // Foreign keys to unrelated parents are skipped for this throwaway container only.
        await ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO organizations (id, name, public_slug) VALUES (@org, 'Org', 'org-' || replace(CAST(@org AS text), '-', ''));
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id,
                status, scope_snapshot, preferred_start, preferred_end, created_by_user_id)
            VALUES (@w1, @org, @branch, 1, gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'With window', gen_random_uuid(), 'ready_to_schedule', 'Scope', @start, @end, @user),
                   (@w2, @org, @branch, 2, gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'Without window', gen_random_uuid(), 'ready_to_schedule', 'Scope', NULL, NULL, @user);
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status)
            VALUES (@v1, @org, @w1, 1, 'unscheduled'), (@v2, @org, @w2, 1, 'unscheduled'), (@v3, @org, @w1, 2, 'unscheduled');
            INSERT INTO visit_assignments (id, visit_id, technician_id, assigned_by_user_id, assigned_at)
            VALUES (gen_random_uuid(), @v1, @t1, @user, now() - interval '2 hours'), (gen_random_uuid(), @v1, @t2, @user, now() - interval '1 hour');
            """,
            ("org", org),
            ("branch", branch),
            ("user", user),
            ("w1", withWindow),
            ("w2", withoutWindow),
            ("v1", visitWithWindow),
            ("v2", visitWithoutWindow),
            ("v3", visitTwo),
            ("t1", tech),
            ("t2", other),
            ("start", window),
            ("end", window.AddHours(3)));

        await MigrateAsync(null);

        Assert.Equal(
            "true|true",
            await ScalarAsync<string>(
                "SELECT (preferred_start = @start)::text || '|' || (preferred_end = @end)::text FROM visits WHERE id = @v",
                ("v", visitWithWindow),
                ("start", window),
                ("end", window.AddHours(3))));
        Assert.Equal(
            2L,
            await ScalarAsync<long>(
                "SELECT COUNT(*) FROM visits WHERE preferred_start IS NULL AND preferred_end IS NULL AND id IN (@a, @b)",
                ("a", visitWithoutWindow),
                ("b", visitTwo)));
        Assert.Equal(
            1L,
            await ScalarAsync<long>("SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v AND unassigned_at IS NULL AND is_primary", ("v", visitWithWindow)));
        Assert.Equal(
            tech,
            await ScalarAsync<Guid>("SELECT technician_id FROM visit_assignments WHERE visit_id = @v AND is_primary", ("v", visitWithWindow)));
        Assert.Equal(
            0L,
            await ScalarAsync<long>("SELECT COUNT(*) FROM pg_constraint WHERE conrelid = 'visit_assignments'::regclass AND contype = 'u'"));

        var secondPrimary = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "UPDATE visit_assignments SET is_primary = true WHERE visit_id = @v AND technician_id = @t", ("v", visitWithWindow), ("t", other)));
        Assert.Equal("ux_visit_assignments_primary", secondPrimary.ConstraintName);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "SET session_replication_role = replica; INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary) VALUES (@v, @t, @u, false)",
            ("v", visitWithWindow),
            ("t", tech),
            ("u", user)));
        Assert.Equal("ux_visit_assignments_active", duplicate.ConstraintName);

        foreach (var (sql, constraint) in new[]
        {
            ("UPDATE visits SET scheduled_start = now() WHERE id = @v", "ck_visits_schedule_pair"),
            ("UPDATE visits SET preferred_start = now(), preferred_end = NULL WHERE id = @v", "ck_visits_preferred_range"),
            ("UPDATE visits SET arrival_window_start = now(), arrival_window_end = now() WHERE id = @v", "ck_visits_arrival_window_range"),
            ("UPDATE visits SET scheduled_start = now(), scheduled_end = now() + interval '1 hour', arrival_window_start = now() WHERE id = @v", "ck_visits_arrival_window_pair"),
        })
        {
            var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, ("v", visitWithoutWindow)));
            Assert.Equal(constraint, violation.ConstraintName);
        }
    }

    private async Task MigrateAsync(string? targetMigration)
    {
        await using var factory = FieldOpsApiFactory.Create(connectionString: _postgres.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
        await dbContext.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }
}
