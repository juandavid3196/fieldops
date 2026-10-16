using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;
using static FieldOps.IntegrationTests.AuditLog.AuditSeed;

namespace FieldOps.IntegrationTests.AuditLog;

/// <summary>The audit-log list, summary and filter options (audit-trail AC-01 to AC-05, AC-24).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AuditLogListTests(CompanySettingsDatabaseFixture database)
{
    // AC-01, AC-05, AC-24: today in the organization time zone (near midnight UTC), every filter, row content and sources.
    [Fact]
    public async Task List_DefaultsFiltersAndLocalDay_FollowTheOrganizationTimeZone()
    {
        var world = await database.SeedWorldAsync(Bogota);
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, Owner, "Olivia", "Owner");
        var dana = await database.SeedMemberAsync(world.Org, Dispatcher, "Dana", "Dispatcher");
        var portal = await database.SeedMemberAsync(foreign.Org, Owner, "Pat", "Portal");
        await database.ClearAuditAsync(world.Org);
        var request = await database.SeedRequestAsync(world);
        var start = LocalStart(Bogota);

        // One minute before local midnight is yesterday locally even when it is the same UTC date as the rows of today.
        var yesterdayLate = await database.InsertAsync(world.Org, "customer.updated", "customer", start.AddMinutes(-1), entity: world.Customer);
        var a = await database.InsertAsync(world.Org, "auth.signed_in", "user", start.AddMinutes(1), actor: ownerMember.UserId, entity: ownerMember.UserId, ip: "10.9.8.7");
        var b = await database.InsertAsync(
            world.Org, "service_request.status_changed", "service_request", start.AddMinutes(2), actor: dana.UserId, entity: request.Id, branch: world.BranchA,
            before: """{"status":"new"}""", after: """{"status":"quoted"}""");
        var c = await database.InsertAsync(world.Org, "service_request.created", "service_request", start.AddMinutes(3), entity: request.Id);
        var d = await database.InsertAsync(world.Org, "payment_intent.succeeded", "invoice_payment_attempt", start.AddMinutes(4), entity: Guid.NewGuid(), branch: world.BranchA);
        var e = await database.InsertAsync(world.Org, "foo.bar_baz", "widget", start.AddMinutes(5), actor: dana.UserId, entity: Guid.NewGuid());
        var f = await database.InsertAsync(world.Org, "portal.message_sent", "customer_contact", start.AddMinutes(6), actor: portal.UserId, entity: world.Contact);
        var tomorrow = await database.InsertAsync(world.Org, "auth.signed_in", "user", start.AddDays(1).AddMinutes(1), actor: ownerMember.UserId);

        var page = await GetAsync(host, owner, "/audit-log");
        Assert.Equal([f, e, d, c, b, a], Ids(page));
        Assert.Equal((6, 1, 25, Bogota), (page["total"]!.GetValue<int>(), page["page"]!.GetValue<int>(), page["pageSize"]!.GetValue<int>(), page["timeZone"]!.GetValue<string>()));

        // BR-06: row content per case.
        var rowA = Item(page, a);
        Assert.Equal(($"AUD-{a}", "Olivia Owner", "Signed in", "User account", "—", "Signed in", "Web app", "10.9.8.7", "low"),
            (rowA["eventId"]!.GetValue<string>(), rowA["actor"]!["name"]!.GetValue<string>(), rowA["action"]!["label"]!.GetValue<string>(),
                rowA["entity"]!["label"]!.GetValue<string>(), rowA["reference"]!.GetValue<string>(), rowA["summary"]!.GetValue<string>(),
                rowA["source"]!.GetValue<string>(), rowA["ipAddress"]!.GetValue<string>(), rowA["impact"]!.GetValue<string>()));
        Assert.Null(rowA["branch"]);

        var rowB = Item(page, b);
        Assert.Equal(("Dana Dispatcher", dana.UserId, "Status changed", "Request", $"REQ-{request.Number}", "Status: new → quoted", "Web app", "medium"),
            (rowB["actor"]!["name"]!.GetValue<string>(), rowB["actor"]!["id"]!.GetValue<Guid>(), rowB["action"]!["label"]!.GetValue<string>(),
                rowB["entity"]!["label"]!.GetValue<string>(), rowB["reference"]!.GetValue<string>(), rowB["summary"]!.GetValue<string>(),
                rowB["source"]!.GetValue<string>(), rowB["impact"]!.GetValue<string>()));
        Assert.Equal(("Alpha Branch", world.BranchA), (rowB["branch"]!["name"]!.GetValue<string>(), rowB["branch"]!["id"]!.GetValue<Guid>()));
        Assert.Null(rowB["ipAddress"]);
        Assert.Equal(request.Id, rowB["entity"]!["id"]!.GetValue<Guid>());

        var rowC = Item(page, c);
        Assert.Null(rowC["actor"]!["id"]);
        Assert.Equal(("Customer", "Customer", "Request created"),
            (rowC["actor"]!["name"]!.GetValue<string>(), rowC["source"]!.GetValue<string>(), rowC["summary"]!.GetValue<string>()));

        var rowD = Item(page, d);
        Assert.Equal(("System", "System", "—"), (rowD["actor"]!["name"]!.GetValue<string>(), rowD["source"]!.GetValue<string>(), rowD["reference"]!.GetValue<string>()));

        var rowE = Item(page, e);
        Assert.Equal(("foo.bar_baz", "Foo bar baz", "widget", "Widget", "low"),
            (rowE["action"]!["code"]!.GetValue<string>(), rowE["action"]!["label"]!.GetValue<string>(), rowE["entity"]!["type"]!.GetValue<string>(),
                rowE["entity"]!["label"]!.GetValue<string>(), rowE["impact"]!.GetValue<string>()));

        var rowF = Item(page, f);
        Assert.Equal(("Pat Portal", "Customer"), (rowF["actor"]!["name"]!.GetValue<string>(), rowF["source"]!.GetValue<string>()));
        Assert.StartsWith(
            await database.ScalarAsync<string>("SELECT first_name FROM customer_contacts WHERE id = @c", ("c", world.Contact)),
            rowF["reference"]!.GetValue<string>(),
            StringComparison.Ordinal);

        // Local days: yesterday and tomorrow hold exactly their own rows, and the range includes both ends.
        var yesterday = LocalDay(Bogota, -1);
        Assert.Equal([yesterdayLate], Ids(await GetAsync(host, owner, $"/audit-log?from={yesterday}&to={yesterday}")));
        var tomorrowDay = LocalDay(Bogota, 1);
        Assert.Equal([tomorrow], Ids(await GetAsync(host, owner, $"/audit-log?from={tomorrowDay}&to={tomorrowDay}")));
        Assert.Equal([tomorrow, f, e, d, c, b, a, yesterdayLate], Ids(await GetAsync(host, owner, $"/audit-log?from={yesterday}&to={tomorrowDay}")));

        // Every filter, alone and combined (AND).
        Assert.Equal([e, b], Ids(await GetAsync(host, owner, $"/audit-log?actorId={dana.UserId}")));
        Assert.Equal([d, c], Ids(await GetAsync(host, owner, "/audit-log?actorId=system")));
        Assert.Equal([b], Ids(await GetAsync(host, owner, "/audit-log?action=service_request.status_changed")));
        Assert.Equal([c, b], Ids(await GetAsync(host, owner, $"/audit-log?entityType=service_request&entityId={request.Id}")));
        Assert.Equal([d, b], Ids(await GetAsync(host, owner, $"/audit-log?branchId={world.BranchA}")));
        Assert.Empty(Ids(await GetAsync(host, owner, $"/audit-log?branchId={world.BranchB}")));
        Assert.Equal([b], Ids(await GetAsync(host, owner, "/audit-log?impact=medium")));
        Assert.Equal([f, e, d, c, a], Ids(await GetAsync(host, owner, "/audit-log?impact=low")));
        Assert.Empty(Ids(await GetAsync(host, owner, "/audit-log?impact=high")));
        var combined = await GetAsync(
            host, owner,
            $"/audit-log?actorId={dana.UserId}&action=service_request.status_changed&entityType=service_request&entityId={request.Id}&branchId={world.BranchA}&impact=medium&q=dana");
        Assert.Equal([b], Ids(combined));
        Assert.Equal(1, combined["total"]!.GetValue<int>());
        Assert.Empty(Ids(await GetAsync(host, owner, $"/audit-log?actorId={dana.UserId}&impact=high")));
    }

    // AC-02, AC-03: order and ties, page slices, a page beyond the last, and every validation error.
    [Fact]
    public async Task List_SortPagingAndValidation_FollowBr05()
    {
        var world = await database.SeedWorldAsync(Bogota);
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var foreignMember = await database.SeedMemberAsync(foreign.Org, Owner, "Fay", "Foreign");
        await database.ClearAuditAsync(world.Org);
        var start = LocalStart(Bogota);
        string[] codes = ["payment.recorded", "quote.sent", "auth.signed_in"];
        int[] ranks = [2, 1, 0];
        var rows = new List<(long Id, int Rank, DateTimeOffset At)>();

        for (var i = 1; i <= 60; i++)
        {
            // Rows 59 and 60 share their instant, so the id breaks the tie.
            var at = start.AddMinutes(i == 60 ? 59 : i);
            rows.Add((await database.InsertAsync(world.Org, codes[i % 3], "user", at, actor: member.UserId), ranks[i % 3], at));
        }

        long[] Order(IEnumerable<(long Id, int Rank, DateTimeOffset At)> source) => [.. source.Select(row => row.Id)];
        var newest = Order(rows.OrderByDescending(row => row.At).ThenByDescending(row => row.Id));

        var all = await GetAsync(host, owner, "/audit-log?pageSize=100");
        Assert.Equal(newest, Ids(all));
        Assert.Equal(60, all["total"]!.GetValue<int>());
        Assert.Equal(newest.Take(25), Ids(await GetAsync(host, owner, "/audit-log")));
        Assert.Equal(newest.Skip(25).Take(25), Ids(await GetAsync(host, owner, "/audit-log?page=2")));
        Assert.Equal(newest.Skip(50).Take(25), Ids(await GetAsync(host, owner, "/audit-log?page=3&pageSize=25")));
        Assert.Equal(newest.Take(50), Ids(await GetAsync(host, owner, "/audit-log?pageSize=50")));
        Assert.Equal(newest.Skip(50), Ids(await GetAsync(host, owner, "/audit-log?pageSize=50&page=2")));

        var past = await GetAsync(host, owner, "/audit-log?page=4&pageSize=25");
        Assert.Empty(Ids(past));
        Assert.Equal((60, 4), (past["total"]!.GetValue<int>(), past["page"]!.GetValue<int>()));

        Assert.Equal(Order(rows.OrderByDescending(row => row.Rank).ThenByDescending(row => row.At).ThenByDescending(row => row.Id)),
            Ids(await GetAsync(host, owner, "/audit-log?sort=impact&pageSize=100")));
        Assert.Equal(Order(rows.OrderBy(row => row.Rank).ThenByDescending(row => row.At).ThenByDescending(row => row.Id)),
            Ids(await GetAsync(host, owner, "/audit-log?sort=impact&direction=asc&pageSize=100")));
        Assert.Equal(Order(rows.OrderBy(row => row.At).ThenByDescending(row => row.Id)),
            Ids(await GetAsync(host, owner, "/audit-log?sort=occurredAt&direction=asc&pageSize=100")));

        // BR-03, BR-05: one 400 per invalid value, keyed by parameter.
        foreach (var (query, key) in new[]
        {
            ("from=2026-13-01", "from"),
            ("to=not-a-date", "to"),
            ("from=2026-02-01&to=2026-01-01", "from"),
            ("from=2025-01-01&to=2026-06-01", "to"),
            ($"q={new string('x', 101)}", "q"),
            ("action=nope.nope", "action"),
            ("entityType=nope", "entityType"),
            ("impact=extreme", "impact"),
            ("sort=name", "sort"),
            ("direction=up", "direction"),
            ("actorId=not-a-guid", "actorId"),
            ($"actorId={Guid.NewGuid()}", "actorId"),
            ($"actorId={foreignMember.UserId}", "actorId"),
            ($"entityId={Guid.NewGuid()}", "entityId"),
            ("entityType=customer&entityId=bad", "entityId"),
            ("page=0", "page"),
            ("page=x", "page"),
            ("pageSize=30", "pageSize"),
        })
        {
            Assert.Equal([key], ErrorKeys(await GetAsync(host, owner, "/audit-log?" + query, HttpStatusCode.BadRequest)));
        }

        // The widest span (366 days) and a 100 character search are valid.
        await GetAsync(host, owner, $"/audit-log?from={LocalDay(Bogota, -365)}&to={LocalDay(Bogota)}&q={new string('x', 100)}");

        // BR-03: a foreign, unknown, malformed or out-of-scope branch is one identical 404 with its code.
        var foreignBranch = foreign.BranchA;

        foreach (var branch in new[] { foreignBranch.ToString(), Guid.NewGuid().ToString(), "x" })
        {
            var response = await host.SendAsync(HttpMethod.Get, "/audit-log?branchId=" + branch, owner);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("branch_not_found", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        }
    }

    // AC-04, FR-03: the four metrics of today only (ignoring filters) and the filter options.
    [Fact]
    public async Task SummaryAndFilters_CountTodayOnlyAndListTheOptions()
    {
        var world = await database.SeedWorldAsync(Bogota);
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, Owner, "Olivia", "Owner");
        var dana = await database.SeedMemberAsync(world.Org, Dispatcher, "Dana", "Dispatcher");
        var suspended = await database.SeedMemberAsync(world.Org, Viewer, "Abe", "Away", status: "suspended");
        await database.SeedMemberAsync(foreign.Org, Owner, "Zed", "Foreign");
        await database.SeedBranchAsync(world.Org, "Closed Branch", isActive: false);
        await database.ClearAuditAsync(world.Org);
        var start = LocalStart(Bogota);

        await database.InsertAsync(world.Org, "auth.signed_in", "user", start.AddMinutes(1), actor: ownerMember.UserId);
        await database.InsertAsync(world.Org, "auth.signed_in", "user", start.AddMinutes(2), actor: ownerMember.UserId);
        await database.InsertAsync(world.Org, "user.suspended", "organization_user", start.AddMinutes(3), actor: dana.UserId);
        await database.InsertAsync(world.Org, "payment.recorded", "payment", start.AddMinutes(4), actor: ownerMember.UserId);
        await database.InsertAsync(world.Org, "quote.expired", "quote", start.AddMinutes(5));
        await database.InsertAsync(world.Org, "user.access_updated", "organization_user", start.AddMinutes(-1), actor: dana.UserId);
        await database.InsertAsync(world.Org, "customer.archived", "customer", start.AddDays(1).AddMinutes(1), actor: dana.UserId);
        await database.InsertAsync(foreign.Org, "payment.recorded", "payment", start.AddMinutes(10));

        var summary = await GetAsync(host, owner, "/audit-log/summary?q=zzz&actorId=system&impact=low&from=2020-01-01");
        Assert.Equal((5, 2, 2, 3, Bogota),
            (summary["eventsToday"]!.GetValue<int>(), summary["activeUsersToday"]!.GetValue<int>(), summary["highImpactToday"]!.GetValue<int>(),
                summary["accessChangesToday"]!.GetValue<int>(), summary["timeZone"]!.GetValue<string>()));

        var filters = await GetAsync(host, owner, "/audit-log/filters");
        var actions = filters["actions"]!.AsArray().Select(option => (Code: option!["code"]!.GetValue<string>(), Label: option["label"]!.GetValue<string>())).ToArray();
        Assert.Equal(AuditCatalog.Codes.Order(), actions.Select(option => option.Code).Order());
        Assert.Equal(actions.Select(option => option.Label).Order(StringComparer.OrdinalIgnoreCase), actions.Select(option => option.Label));
        Assert.Contains(("payment.recorded", "Payment recorded"), actions);
        var entityTypes = filters["entityTypes"]!.AsArray().Select(option => option!["code"]!.GetValue<string>()).ToArray();
        Assert.Equal(AuditCatalog.EntityTypes.Order(), entityTypes.Order());

        var users = filters["users"]!.AsArray().Select(user => (Id: user!["id"]!.GetValue<string>(), Name: user["name"]!.GetValue<string>())).ToArray();
        Assert.Equal(
            ["Abe Away", "Dana Dispatcher", "Olivia Owner", "System"],
            users.Select(user => user.Name));
        Assert.Equal(["system"], users.Where(user => user.Name == "System").Select(user => user.Id));
        Assert.Contains((suspended.UserId.ToString(), "Abe Away"), users);

        var branches = filters["branches"]!.AsArray().Select(branch => (branch!["name"]!.GetValue<string>(), branch["isActive"]!.GetValue<bool>())).ToArray();
        Assert.Equal([("Alpha Branch", true), ("Bravo Branch", true), ("Closed Branch", false)], branches);
    }
}
