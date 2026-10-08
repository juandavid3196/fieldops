using System.Globalization;
using System.Net;
using FieldOps.Domain.Invoices;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.BillingReview;

/// <summary>Generation rollback and the CSV export (completed-jobs-review AC-16, AC-17).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BillingReviewRollbackExportTests(CompanySettingsDatabaseFixture database)
{
    private const string Timezone = "America/Chicago";

    [Fact]
    public async Task GenerateFailureRollsBackEverythingAndExportEscapesAndCapsTheCsv()
    {
        await VerifyRollbackAsync();
        await VerifyExportAsync();
    }

    // AC-16: a failure after the counter moved and before the commit leaves no trace, counter included.
    private async Task VerifyRollbackAsync()
    {
        var world = await database.SeedWorldAsync();
        var interceptor = new FailOnInvoiceLineInterceptor();
        await using var session = SessionTestHost.Create(database.ConnectionString);
        using var client = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(interceptor)))));
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sam", "Staff");
        var cookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);
        var job = await database.SeedJobAsync(world, member.UserId);
        var counter = await database.NextInvoiceNumberAsync(world.Org);
        var before = await database.OrderStateAsync(job.Order);
        var path = $"{BillingSeed.Detail(job.Order)}/invoice";
        var body = BillingSeed.InvoiceBody(BillingSeed.Today("UTC"), note: "rollback note").ToJsonString();

        interceptor.Armed = true;
        var failed = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, path, body, cookie);
        interceptor.Armed = false;

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(before, await database.OrderStateAsync(job.Order));
        Assert.Equal(counter, await database.NextInvoiceNumberAsync(world.Org));
        Assert.Equal(
            (0, 0, 0),
            ((int)await database.InvoiceCountAsync(world.Org),
                (int)await database.CountAsync("invoice_lines", "invoice_id IN (SELECT id FROM invoices WHERE organization_id = @o)", ("o", world.Org)),
                (int)await database.CountAsync("visit_status_history", "to_status = 'approved' AND visit_id = @v", ("v", job.Visit))));

        var retried = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, path, body, cookie);
        var created = await BillingSeed.ReadAsync(retried, HttpStatusCode.Created);
        Assert.Equal($"INV-{counter}", created["invoice"]!["number"]!.GetValue<string>());
        Assert.Equal(counter + 1, await database.NextInvoiceNumberAsync(world.Org));
    }

    // AC-17: columns, filename, filters, sort, escaping and the 5,000 row cap.
    private async Task VerifyExportAsync()
    {
        var world = await database.SeedWorldAsync(Timezone);
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "One");
        var comma = await database.SeedCustomerAsync(world.Org, world.BranchA, "Smith, John");
        var dash = await database.SeedCustomerAsync(world.Org, world.BranchA, "-Dash Co");
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        older = new DateTimeOffset(older.Year, older.Month, older.Day, older.Hour, older.Minute, 0, TimeSpan.Zero);
        var newer = older.AddDays(1);

        var formula = await database.SeedJobAsync(
            world,
            member.UserId,
            new JobSpec { Title = "=HYPERLINK(\"http://x\",\"a\")", Customer = comma, CompletedAt = older, Technician = tech, WorkSeconds = 4 * 3600 });
        var normal = await database.SeedJobAsync(
            world, member.UserId, new JobSpec { Title = "Normal job", Customer = dash, CompletedAt = newer, Branch = world.BranchB });
        var invoiced = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Invoiced job", CompletedAt = newer });
        await database.SeedBillingInvoiceAsync(world, member.UserId, invoiced, "draft");
        await database.ExecuteAsync(
            "UPDATE work_orders SET billing_review_note = 'SECRET NOTE', billing_follow_up_at = now(), billing_follow_up_by_user_id = @u WHERE id = @o",
            ("u", member.UserId),
            ("o", formula.Order));

        string Local(DateTimeOffset instant) =>
            TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(Timezone)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var export = await host.SendAsync(HttpMethod.Get, "/billing-review/export", owner);
        var csv = await export.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", export.Content.Headers.ContentType?.ToString());
        Assert.Equal($"completed-jobs-review-{BillingSeed.Today(Timezone)}.csv", export.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("no-store", export.Headers.CacheControl?.ToString());
        Assert.Equal(
            [
                "Work order,Customer,Job,Branch,Technician,Completed at,Approved total,Currency,Labor variance,Material variance,Ready,Follow-up",
                $"WO-{normal.Number},'-Dash Co,Normal job,Bravo Branch,,{Local(newer)},221.65,USD,None,No,Yes,No",
                $"WO-{formula.Number},\"Smith, John\",\"'=HYPERLINK(\"\"http://x\"\",\"\"a\"\")\",Alpha Branch,Tina One,{Local(older)},221.65,USD,Over,No,No,Yes",
            ],
            csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("SECRET NOTE", csv, StringComparison.Ordinal);

        var labor = await (await host.SendAsync(HttpMethod.Get, "/billing-review/export?variance=labor", owner)).Content.ReadAsStringAsync();
        Assert.Equal(2, labor.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("Over", labor, StringComparison.Ordinal);
        var ready = await (await host.SendAsync(HttpMethod.Get, "/billing-review/export?tab=ready&page=9", owner)).Content.ReadAsStringAsync();
        Assert.Equal(2, ready.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("Normal job", ready, StringComparison.Ordinal);
        await BillingSeed.ProblemAsync(
            await host.SendAsync(HttpMethod.Get, "/billing-review/export?tab=nope", owner), HttpStatusCode.BadRequest, errorKey: "tab");

        // More than 5,000 matching jobs is refused; narrowing the filters exports again.
        var big = await database.SeedWorldAsync();
        var (bigOwner, bigMember) = await host.SignInAsync(database, big.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        await database.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO quotes (id, organization_id, branch_id, request_id, customer_id, property_id, quote_number, status, current_version_no, created_by_user_id)
            SELECT md5(@salt || 'q' || g)::uuid, @org, @branch, gen_random_uuid(), @customer, @property, g, CAST('approved' AS quote_status), 1, @user FROM generate_series(1, 5001) g;
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, is_immutable, created_by_user_id)
            SELECT md5(@salt || 'v' || g)::uuid, @org, md5(@salt || 'q' || g)::uuid, 1, 'Scope', 100, 0, 0, 100, 'USD', true, @user FROM generate_series(1, 5001) g;
            INSERT INTO quote_responses (id, organization_id, quote_version_id, response, responder_name, subtotal, discount_total, tax_total, total)
            SELECT gen_random_uuid(), @org, md5(@salt || 'v' || g)::uuid, CAST('approved' AS quote_status), 'Carla', 100, 0, 0, 100 FROM generate_series(1, 5001) g;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id, status, scope_snapshot, created_by_user_id)
            SELECT md5(@salt || 'w' || g)::uuid, @org, @branch, g, md5(@salt || 'v' || g)::uuid, @customer, @property, 'Bulk ' || lpad(g::text, 4, '0'),
                @category, CAST('completed' AS work_order_status), 'Scope', @user FROM generate_series(1, 5001) g;
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, actual_completed_at)
            SELECT gen_random_uuid(), @org, md5(@salt || 'w' || g)::uuid, 1, CAST('completed' AS visit_status), @at FROM generate_series(1, 5001) g;
            SET session_replication_role = DEFAULT;
            """,
            ("salt", big.Org.ToString("N")),
            ("org", big.Org),
            ("branch", big.BranchA),
            ("customer", big.Customer),
            ("property", big.Property),
            ("category", big.Category),
            ("user", bigMember.UserId),
            ("at", DateTimeOffset.UtcNow.AddDays(-3)));

        var tooLarge = await BillingSeed.ProblemAsync(
            await host.SendAsync(HttpMethod.Get, "/billing-review/export", bigOwner), HttpStatusCode.Conflict, "export_too_large");
        Assert.Equal("Narrow the filters to export 5,000 jobs or fewer.", tooLarge["title"]!.GetValue<string>());

        var narrowed = await host.SendAsync(HttpMethod.Get, "/billing-review/export?search=Bulk%204999", bigOwner);
        Assert.Equal(HttpStatusCode.OK, narrowed.StatusCode);
        Assert.Equal(2, (await narrowed.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>Test-only fault seam: fails the save that inserts invoice lines, after the invoice counter already moved.</summary>
    private sealed class FailOnInvoiceLineInterceptor : SaveChangesInterceptor
    {
        public volatile bool Armed;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Check(eventData);

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(eventData);

            return ValueTask.FromResult(result);
        }

        private void Check(DbContextEventData eventData)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<InvoiceLine>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("Injected failure while saving the invoice lines.");
            }
        }
    }
}
