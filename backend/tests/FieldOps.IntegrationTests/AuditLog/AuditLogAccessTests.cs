using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;
using static FieldOps.IntegrationTests.AuditLog.AuditSeed;

namespace FieldOps.IntegrationTests.AuditLog;

/// <summary>Role access, read-only methods, Accounting visibility, branch scope and organization isolation (audit-trail AC-08 to AC-12).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AuditLogAccessTests(CompanySettingsDatabaseFixture database)
{
    // AC-08, AC-12: every endpoint per role, then PUT, PATCH and DELETE answer 405 (before authorization) and change nothing.
    [Fact]
    public async Task Endpoints_RoleMatrixAndReadOnlyMethods_FollowBr09AndBr17()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var roles = new (short Role, bool Allowed)[]
        {
            (Owner, true), (Operations, true), (Accounting, true), (Dispatcher, false), (Technician, false), (Viewer, false),
        };
        var cookies = new List<(string Cookie, bool Allowed)>();

        foreach (var (role, allowed) in roles)
        {
            cookies.Add(((await host.SignInAsync(database, world.Org, role)).Cookie, allowed));
        }

        await database.ClearAuditAsync(world.Org);
        var id = await database.InsertAsync(world.Org, "payment.recorded", "payment", LocalStart("UTC").AddMinutes(1), branch: world.BranchA, after: """{"amount":"10.00"}""");

        foreach (var (cookie, allowed) in cookies)
        {
            foreach (var path in new[] { "/audit-log", "/audit-log/summary", "/audit-log/filters", $"/audit-log/{id}", "/audit-log/export" })
            {
                var response = await host.SendAsync(HttpMethod.Get, path, cookie);
                Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        foreach (var path in new[] { "/audit-log", "/audit-log/summary", $"/audit-log/{id}", "/audit-log/export" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, path, null)).StatusCode);
        }

        var before = await database.ScalarAsync<string>("SELECT action || '|' || after_data::text FROM audit_logs WHERE id = @i", ("i", id));
        var rows = await database.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE organization_id = @o", ("o", world.Org));

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            foreach (var path in new[] { "/audit-log", $"/audit-log/{id}" })
            {
                foreach (var cookie in new string?[] { cookies[0].Cookie, cookies[3].Cookie, null })
                {
                    var response = await host.SendAsync(method, path, cookie, new JsonObject { ["action"] = "x" });
                    Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
                    Assert.Contains("GET", response.Content.Headers.Allow);
                }
            }
        }

        Assert.Equal(before, await database.ScalarAsync<string>("SELECT action || '|' || after_data::text FROM audit_logs WHERE id = @i", ("i", id)));
        Assert.Equal(rows, await database.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE organization_id = @o", ("o", world.Org)));
    }

    // AC-09, AC-10, AC-11: Accounting sees financial events only inside its branch scope; another organization is invisible everywhere.
    [Fact]
    public async Task Visibility_AccountingFinancialOnlyBranchScopeAndCrossTenant_HideEverythingElse()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, Owner);
        var (limited, _) = await host.SignInAsync(database, world.Org, Accounting, "Lia", "Limited", world.BranchA);
        var (accountingAll, _) = await host.SignInAsync(database, world.Org, Accounting, "Alex", "All");
        var foreignOwner = await database.SeedMemberAsync(foreign.Org, Owner, "Fay", "Foreign");
        await database.ClearAuditAsync(world.Org);
        var at = LocalStart("UTC").AddMinutes(1);

        Task<long> RowAsync(string action, string type, Guid? branch, string metadata = "{}") =>
            database.InsertAsync(world.Org, action, type, at, entity: Guid.NewGuid(), branch: branch, metadata: metadata);

        var f1 = await RowAsync("invoice.sent", "invoice", world.BranchA);
        var f2 = await RowAsync("payment.recorded", "payment", world.BranchB);
        var f3 = await RowAsync("invoice.created", "invoice", null);
        var n1 = await RowAsync("quote.sent", "quote", world.BranchA);
        var f4 = await RowAsync("work_order.billing_note_updated", "work_order", world.BranchA);
        var f5 = await RowAsync("export.generated", "export", world.BranchA, """{"reportType":"invoices","filters":{},"rowCount":0}""");
        var n2 = await RowAsync("export.generated", "export", world.BranchA, """{"reportType":"catalog_items","filters":{},"rowCount":0}""");
        var n3 = await RowAsync("customer.updated", "customer", world.BranchA);
        var other = await database.InsertAsync(foreign.Org, "invoice.sent", "invoice", at, branch: foreign.BranchA);

        // The list: Accounting limited to branch A, Accounting on every branch, and the Owner.
        var limitedPage = await GetAsync(host, limited, "/audit-log");
        Assert.Equal(new[] { f1, f4, f5 }.Order(), Ids(limitedPage).Order());
        Assert.Equal(3, limitedPage["total"]!.GetValue<int>());
        Assert.Equal(new[] { f1, f2, f3, f4, f5 }.Order(), Ids(await GetAsync(host, accountingAll, "/audit-log")).Order());
        Assert.Equal(new[] { f1, f2, f3, n1, f4, f5, n2, n3 }.Order(), Ids(await GetAsync(host, owner, "/audit-log")).Order());

        // Summary and filter options follow the same visibility.
        Assert.Equal(3, (await GetAsync(host, limited, "/audit-log/summary"))["eventsToday"]!.GetValue<int>());
        Assert.Equal(8, (await GetAsync(host, owner, "/audit-log/summary"))["eventsToday"]!.GetValue<int>());
        var filters = await GetAsync(host, limited, "/audit-log/filters");
        var codes = filters["actions"]!.AsArray().Select(option => option!["code"]!.GetValue<string>()).ToArray();
        Assert.Contains("payment.recorded", codes);
        Assert.Contains("work_order.billing_note_updated", codes);
        Assert.Contains("export.generated", codes);
        Assert.DoesNotContain("quote.sent", codes);
        Assert.DoesNotContain("customer.updated", codes);
        Assert.Equal(["Alpha Branch"], filters["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>()));
        Assert.DoesNotContain("customer", filters["entityTypes"]!.AsArray().Select(option => option!["code"]!.GetValue<string>()));

        // Detail: a non-financial event, another branch, a null-branch event and another organization are the same 404.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/audit-log/{f1}", limited)).StatusCode);

        foreach (var hidden in new[] { n1, n2, n3, f2, f3, other })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/audit-log/{hidden}", limited)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/audit-log/{f2}", accountingAll)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/audit-log/{n1}", accountingAll)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/audit-log/{n1}", owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/audit-log/{other}", owner)).StatusCode);

        // Filters: a non-financial action is a 400 for Accounting only; another branch is a 404 with its code.
        Assert.Equal(["action"], ErrorKeys(await GetAsync(host, limited, "/audit-log?action=quote.sent", HttpStatusCode.BadRequest)));
        Assert.Equal([n1], Ids(await GetAsync(host, owner, "/audit-log?action=quote.sent")));
        Assert.Equal([f1], Ids(await GetAsync(host, limited, "/audit-log?action=invoice.sent")));

        foreach (var (cookie, branch) in new[] { (limited, world.BranchB), (owner, foreign.BranchA) })
        {
            var denied = await host.SendAsync(HttpMethod.Get, $"/audit-log?branchId={branch}", cookie);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal("branch_not_found", JsonNode.Parse(await denied.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        }

        Assert.Equal(["actorId"], ErrorKeys(await GetAsync(host, owner, $"/audit-log?actorId={foreignOwner.UserId}", HttpStatusCode.BadRequest)));

        // Export: only the visible rows, never another organization's.
        var limitedCsv = AuditLogContentTests.Records(await TextAsync(host, limited, "/audit-log/export"));
        Assert.Equal(new[] { $"AUD-{f1}", $"AUD-{f4}", $"AUD-{f5}" }.Order(), limitedCsv.Skip(1).Select(record => record[0]).Order());
        var ownerCsv = await TextAsync(host, owner, "/audit-log/export");
        Assert.DoesNotContain($"AUD-{other}", ownerCsv, StringComparison.Ordinal);
        // The 8 events of the arrangement plus the export row the Accounting export recorded (the Owner sees every report type).
        Assert.Equal(9, AuditLogContentTests.Records(ownerCsv).Count - 1);
    }
}
