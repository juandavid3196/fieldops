using System.Data.Common;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Authorization, tenant isolation and query efficiency of the quotes workspace (quotes-management AC-01, AC-02, AC-11, AC-23).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuotesWorkspaceSecurityTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short Operations = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private static async Task<string> ProblemBodyAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");

        return body.ToJsonString();
    }

    private static string[] BranchNames(JsonNode options) =>
        [.. options["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>())];

    // AC-01, AC-02, AC-11: role 403s and flags, the Dispatcher scope over items, counts, metrics and ready requests,
    // another organization, and one identical 404 for an unknown, foreign, inactive and out-of-scope branch.
    [Fact]
    public async Task Workspace_RolesScopeAndForeignBranch_AreAuthorizedAndIsolated()
    {
        var world = await database.SeedWorkspaceWorldAsync();
        var foreign = await database.SeedWorkspaceWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var (dispatcherA, _) = await host.SignInAsync(database, world.Org, Dispatcher, "Dee", "Alpha", world.BranchA);
        var (operations, _) = await host.SignInAsync(database, world.Org, Operations);
        var (accounting, _) = await host.SignInAsync(database, world.Org, Accounting);
        var (viewer, _) = await host.SignInAsync(database, world.Org, Viewer);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, Owner);
        var inactive = await database.SeedBranchAsync(world.Org, "Closed Branch", isActive: false);

        var inA = await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 55m, requestStatus: "ready_for_quote");
        var inB = await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 77m, requestBranch: world.BranchB);
        var noBranch = await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 11m, noRequestBranch: true);
        await database.SeedWorkspaceQuoteAsync(foreign, foreignMember.UserId, "draft", currentVersion: 0, total: 9000m);
        await database.SeedRequestAsync(world, status: "ready_for_quote", branch: world.BranchB);
        await database.SeedRequestAsync(world, status: "ready_for_quote", branch: null);
        await database.SeedRequestAsync(foreign, status: "ready_for_quote", branch: foreign.BranchA);

        // AC-01: Technician gets 403 on all three endpoints before any read (even with a malformed branch).
        var (technician, _) = await host.SignInAsync(database, world.Org, Technician);

        foreach (var path in new[] { "/quotes", "/quotes/summary", "/quotes/options", $"/quotes/summary?branchId={Guid.NewGuid()}", "/quotes?branchId=bad" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, technician)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, "/quotes/options", null)).StatusCode);

        foreach (var (cookie, manage, manageOrders, readOrders) in new[]
        {
            (owner, true, true, true),
            (dispatcherA, true, true, true),
            (operations, false, true, true),
            (accounting, false, false, false),
            (viewer, false, false, true),
        })
        {
            var options = await QuotesWorkspaceTests.GetAsync(host, cookie, "/quotes/options");
            Assert.Equal(
                (manage, manageOrders, readOrders),
                (options["canManage"]!.GetValue<bool>(), options["canManageWorkOrders"]!.GetValue<bool>(), options["canReadWorkOrders"]!.GetValue<bool>()));
            Assert.Equal("USD", options["currency"]!.GetValue<string>());
            Assert.Equal("UTC", options["organizationTimezone"]!.GetValue<string>());
            await QuotesWorkspaceTests.GetAsync(host, cookie, "/quotes");
            await QuotesWorkspaceTests.GetAsync(host, cookie, "/quotes/summary");
        }

        // Options: active in-scope branches by name; another organization never leaks.
        Assert.Equal(["Alpha Branch", "Bravo Branch"], BranchNames(await QuotesWorkspaceTests.GetAsync(host, owner, "/quotes/options")));
        Assert.Equal(["Alpha Branch"], BranchNames(await QuotesWorkspaceTests.GetAsync(host, dispatcherA, "/quotes/options")));
        Assert.Equal(2, (await QuotesWorkspaceTests.GetAsync(host, foreignOwner, "/quotes/options"))["branches"]!.AsArray().Count);

        // BR-02: the Dispatcher of branch A sees its branch and the null-branch request under All, never branch B.
        var scoped = await QuotesWorkspaceTests.GetAsync(host, dispatcherA, "/quotes");
        Assert.Equal(
            new[] { inA.Id, noBranch.Id }.Order(),
            scoped["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).Order());
        Assert.Equal([2, 2, 0, 0, 0], QuotesWorkspaceTests.Counts(scoped));

        var scopedSummary = await QuotesWorkspaceTests.GetAsync(host, dispatcherA, "/quotes/summary");
        Assert.Equal((2, 66m), (scopedSummary["drafts"]!["count"]!.GetValue<int>(), scopedSummary["drafts"]!["amount"]!.GetValue<decimal>()));
        Assert.Equal(2, scopedSummary["readyRequests"]!.GetValue<int>());

        var selected = await QuotesWorkspaceTests.GetAsync(host, dispatcherA, $"/quotes?branchId={world.BranchA}");
        Assert.Equal([inA.Id], selected["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));
        var selectedSummary = await QuotesWorkspaceTests.GetAsync(host, dispatcherA, $"/quotes/summary?branchId={world.BranchA}");
        Assert.Equal((1, 55m), (selectedSummary["drafts"]!["count"]!.GetValue<int>(), selectedSummary["drafts"]!["amount"]!.GetValue<decimal>()));
        Assert.Equal(1, selectedSummary["readyRequests"]!.GetValue<int>());

        var ownerAll = await QuotesWorkspaceTests.GetAsync(host, owner, "/quotes");
        Assert.Equal(
            new[] { inA.Id, inB.Id, noBranch.Id }.Order(),
            ownerAll["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).Order());
        Assert.Equal(3, (await QuotesWorkspaceTests.GetAsync(host, owner, "/quotes/summary"))["readyRequests"]!.GetValue<int>());
        Assert.Equal(1, (await QuotesWorkspaceTests.GetAsync(host, foreignOwner, "/quotes"))["total"]!.GetValue<int>());
        Assert.Equal(1, (await QuotesWorkspaceTests.GetAsync(host, foreignOwner, "/quotes/summary"))["readyRequests"]!.GetValue<int>());

        // A foreign, inactive, out-of-scope and unknown branch are one identical 404 for the list and the summary.
        var unknown = await host.SendAsync(HttpMethod.Get, $"/quotes?branchId={Guid.NewGuid()}", dispatcherA);
        var unknownBody = await ProblemBodyAsync(unknown);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        foreach (var (cookie, branch) in new[]
        {
            (dispatcherA, world.BranchB),
            (dispatcherA, foreign.BranchA),
            (owner, foreign.BranchA),
            (owner, inactive.Id),
        })
        {
            foreach (var path in new[] { "/quotes", "/quotes/summary" })
            {
                var response = await host.SendAsync(HttpMethod.Get, $"{path}?branchId={branch}", cookie);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(unknownBody, await ProblemBodyAsync(response));
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
        }

        // The literal routes never shadow the id route.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/quotes/{inA.Id}", owner)).StatusCode);
    }

    // AC-23: the same number of commands for a page of 1, 10 and 50 over 120 quotes with versions, responses and work orders.
    [Fact]
    public async Task ListAndSummary_PageSizeAndRowCount_DoNotChangeTheCommandCount()
    {
        var world = await database.SeedWorkspaceWorldAsync();
        var counter = new CommandCounter();
        await using var session = SessionTestHost.Create(database.ConnectionString);
        using var client = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(counter)))));
        var member = await database.SeedMemberAsync(world.Org, Owner, "Sam", "Staff");
        var cookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);

        await database.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO service_requests (id, organization_id, branch_id, request_number, customer_id, property_id, category_id, catalog_item_id, guest_name, description, status)
            SELECT gen_random_uuid(), @org, @branch, 100 + g, @customer, @property, @category, @service, 'Guest ' || g, 'Bulk request', CAST('quoted' AS request_status)
            FROM generate_series(1, 120) g;
            INSERT INTO quotes (id, organization_id, request_id, customer_id, property_id, quote_number, status, current_version_no, created_by_user_id)
            SELECT gen_random_uuid(), @org, r.id, @customer, @property, r.request_number,
                CAST(CASE r.request_number % 3 WHEN 0 THEN 'approved' WHEN 1 THEN 'sent' ELSE 'clarification_requested' END AS quote_status), 2, @user
            FROM service_requests r WHERE r.organization_id = @org AND r.request_number > 100;
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, valid_until, sent_at, is_immutable, created_by_user_id)
            SELECT gen_random_uuid(), q.organization_id, q.id, 2, 'Scope', 10, 0, 0, 10 + q.quote_number, 'USD', current_date + 3, now(), true, @user
            FROM quotes q WHERE q.organization_id = @org;
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, is_immutable, created_by_user_id)
            SELECT gen_random_uuid(), q.organization_id, q.id, 3, 'Scope', 1, 0, 0, 1, 'USD', false, @user
            FROM quotes q WHERE q.organization_id = @org AND q.quote_number % 2 = 0;
            INSERT INTO quote_responses (id, organization_id, quote_version_id, response, responder_name, subtotal, discount_total, tax_total, total)
            SELECT gen_random_uuid(), q.organization_id, v.id, 'approved', 'Carla', 10, 0, 0, 12
            FROM quotes q JOIN quote_versions v ON v.quote_id = q.id AND v.version_no = 2 WHERE q.organization_id = @org AND q.status = 'approved';
            UPDATE quotes q SET approved_version_id = v.id FROM quote_versions v WHERE v.quote_id = q.id AND v.version_no = 2 AND q.status = 'approved' AND q.organization_id = @org;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id, status, scope_snapshot, created_by_user_id)
            SELECT gen_random_uuid(), q.organization_id, @branch, q.quote_number, q.approved_version_id, @customer, @property, 'Job', @category, CAST('scheduled' AS work_order_status), 'Scope', @user
            FROM quotes q WHERE q.organization_id = @org AND q.approved_version_id IS NOT NULL;
            SET session_replication_role = DEFAULT;
            """,
            ("org", world.Org),
            ("branch", world.BranchA),
            ("customer", world.Customer),
            ("property", world.Property),
            ("category", world.Category),
            ("service", world.Service),
            ("user", member.UserId));

        async Task<(int Commands, JsonNode Body)> Measure(string path)
        {
            counter.Reset();
            var response = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Get, path, null, cookie);
            var body = await DispatchSeed.ReadAsync(response);

            return (counter.Count, body);
        }

        var (small, smallPage) = await Measure("/quotes?pageSize=10");
        var (large, largePage) = await Measure("/quotes?pageSize=50");
        var (single, _) = await Measure("/quotes?pageSize=1");
        var (sorted, _) = await Measure("/quotes?pageSize=50&sort=total&tab=sent");
        var (searched, _) = await Measure("/quotes?pageSize=10&sort=expiring&q=guest&date=30d&status=approved");
        var (summaryCount, _) = await Measure("/quotes/summary");

        Assert.Equal(10, smallPage["items"]!.AsArray().Count);
        Assert.Equal(50, largePage["items"]!.AsArray().Count);
        Assert.Equal(120, largePage["total"]!.GetValue<int>());
        Assert.Equal(small, large);
        Assert.Equal(small, single);
        Assert.Equal(small, sorted);
        Assert.InRange(searched, 1, 12);
        Assert.InRange(large, 1, 12);
        Assert.InRange(summaryCount, 1, 12);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Volatile.Write(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);

            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);

            return ValueTask.FromResult(result);
        }
    }
}
