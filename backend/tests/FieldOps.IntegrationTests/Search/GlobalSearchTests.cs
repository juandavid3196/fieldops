using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using static FieldOps.IntegrationTests.CompanySettings.CompanySettingsDatabaseFixture;
using static FieldOps.IntegrationTests.Search.GlobalSearchSeed;

namespace FieldOps.IntegrationTests.Search;

/// <summary>The global search endpoint (global-search BR-01 to BR-09; AC-02, AC-03, AC-07, AC-10 to AC-15, AC-21).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class GlobalSearchTests(CompanySettingsDatabaseFixture database)
{
    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static Guid[] Ids(JsonNode body) => [.. Keys(body).Select(key => key.Id)];

    // AC-12: each type matches only through its BR-04 fields, ignoring case and accents; wildcards are literal.
    [Fact]
    public async Task Search_BusinessFields_MatchIgnoringCaseAccentsWithLiteralWildcards()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var (owner, member) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var org = world.Org;
        var zeta = await database.SeedCategoryAsync(org, "Zeta Roofing");

        var display = await database.CustomerAsync(org, world.BranchA, "Zorblax Displayname");
        var legal = await database.CustomerAsync(org, world.BranchA, "Plain One", legal: "Quuxington Legal Inc");
        var email = await database.CustomerAsync(org, world.BranchA, "Plain Two", email: "frobnicate.person@example.test");
        var phone = await database.CustomerAsync(org, world.BranchA, "Plain Three", phone: "+1 (555) 019-8877");
        await database.CustomerAsync(org, world.BranchA, "Plain Four", notes: "hiddennotesword", taxId: "TAXHIDDEN99");
        var accent = await database.CustomerAsync(org, world.BranchA, "José Martínez");
        var wild = await database.CustomerAsync(org, world.BranchA, "50% Off_Co");
        await database.CustomerAsync(org, world.BranchA, "500 Offxco");
        var slash = await database.CustomerAsync(org, world.BranchA, "Back\\Slash Ltd");
        var guest = await database.RequestAsync(
            org, world.BranchA, 1, guest: "Waldo Gómez", category: world.Category, item: world.Service, description: "needlexyz description");
        var roofing = await database.RequestAsync(org, world.BranchA, 2, category: zeta);
        var carlaRequest = await database.RequestAsync(org, world.BranchA, 3, customer: world.Customer);
        var order = await database.WorkOrderAsync(org, world.BranchA, world.Customer, 1, "Replace boiler Quibble", member.UserId);
        var invoice = await database.InvoiceAsync(org, world.BranchA, world.Customer, 1, member.UserId);

        (string Query, Guid[] Expected)[] cases =
        [
            ("ZORBLAX", [display]),
            ("quuxington", [legal]),
            ("FROBNICATE.PERSON", [email]),
            ("555) 019", [phone]),
            ("hiddennotesword", []),
            ("taxhidden99", []),
            ("needlexyz", []),
            ("jose martinez", [accent]),
            ("JOSÉ MARTÍNEZ", [accent]),
            ("martinez", [accent]),
            ("50% off_co", [wild]),
            ("0%", [wild]),
            ("_c", [wild]),
            ("%%", []),
            ("back\\slash", [slash]),
            ("zeta roofing", [roofing]),
            ("waldo", [guest]),
            ("GOMEZ", [guest]),
            ("drain cleaning", [guest]),
            ("plumbing", [guest]),
            ("quibble", [order]),
            ("carla", [world.Customer, carlaRequest, order, invoice]),
        ];

        foreach (var (query, expected) in cases)
        {
            var body = await host.SearchAsync(owner, query);

            Assert.True(
                expected.Order().SequenceEqual(Ids(body).Order()), $"Query '{query}' returned {Ids(body).Length} items.");
        }

        // BR-01: unknown parameters (type, field, organization) have no effect.
        var plain = await host.SearchAsync(owner, "carla");
        var extra = await host.SearchAsync(owner, "carla", $"&type=invoice&field=notes&organizationId={Guid.NewGuid()}&branchId={world.BranchB}");
        Assert.Equal(Ids(plain), Ids(extra));
    }

    // AC-14: BR-08 content per type including historical rows; branchName only with two active branches.
    [Fact]
    public async Task Search_ItemContent_FollowsBr08ForEveryTypeAndBranchNameNeedsTwoBranches()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var (owner, member) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var org = world.Org;
        await database.ExecuteAsync("UPDATE organizations SET invoice_prefix = 'BILL' WHERE id = @o", ("o", org));

        var person = await database.CustomerAsync(org, world.BranchA, "Kestrel Person");
        var corp = await database.CustomerAsync(org, world.BranchB, "Kestrel Corp", type: "company", active: false);
        var r1 = await database.RequestAsync(org, world.BranchA, 7, guest: "Kestrel Guest", category: world.Category, item: world.Service, status: "cancelled");
        var r2 = await database.RequestAsync(org, null, 8, guest: "Kestrel Two", customer: world.Customer, category: world.Category);
        var r3 = await database.RequestAsync(org, world.BranchB, 9, guest: "Kestrel Three");
        var q1 = await database.QuoteAsync(org, null, r1, null, 21, member.UserId);
        var q2 = await database.QuoteAsync(org, world.BranchB, r2, person, 22, member.UserId, status: "approved");
        var order = await database.WorkOrderAsync(org, world.BranchA, world.Customer, 31, "Kestrel boiler", member.UserId, status: "ready_to_schedule");
        var invoice = await database.InvoiceAsync(org, world.BranchB, person, 41, member.UserId, total: 485m, due: new DateOnly(2026, 3, 5));

        var body = await host.SearchAsync(owner, "kestrel");
        var items = body["items"]!.AsArray().ToDictionary(item => item!["entityId"]!.GetValue<Guid>(), item => item!);

        void Check(
            Guid id, string type, string? number, string title, string? subtitle, string status, Guid? branch, string? branchName)
        {
            var item = items[id];
            Assert.Equal(
                (type, number, title, subtitle, status, branch, branchName),
                (Text(item["entityType"]),
                 item["displayNumber"]?.GetValue<string>(),
                 Text(item["title"]),
                 item["subtitle"]?.GetValue<string>(),
                 Text(item["status"]),
                 item["branchId"]?.GetValue<Guid>(),
                 item["branchName"]?.GetValue<string>()));
            Assert.Equal(TimeSpan.Zero, item["updatedAt"]!.GetValue<DateTimeOffset>().Offset);
        }

        Assert.Equal("kestrel", Text(body["query"]));
        Assert.Equal(9, items.Count);
        Assert.False(body["hasMore"]!.GetValue<bool>());
        Check(person, "customer", null, "Kestrel Person", "Residential customer", "active", world.BranchA, "Alpha Branch");
        Check(corp, "customer", null, "Kestrel Corp", "Commercial customer", "inactive", world.BranchB, "Bravo Branch");
        Check(r1, "service_request", "REQ-7", "Drain cleaning", "Kestrel Guest", "cancelled", world.BranchA, "Alpha Branch");
        Check(r2, "service_request", "REQ-8", "Plumbing", "Carla Customer", "new", null, null);
        Check(r3, "service_request", "REQ-9", "Service request", "Kestrel Three", "new", world.BranchB, "Bravo Branch");
        Check(q1, "quote", "Q-21", "Kestrel Guest", "REQ-7", "sent", world.BranchA, "Alpha Branch");
        Check(q2, "quote", "Q-22", "Kestrel Person", "REQ-8", "approved", world.BranchB, "Bravo Branch");
        Check(order, "work_order", "WO-31", "Kestrel boiler", "Carla Customer", "ready_to_schedule", world.BranchA, "Alpha Branch");
        Check(invoice, "invoice", "BILL-41", "$485.00", "Kestrel Person · Due Mar 5", "void", world.BranchB, "Bravo Branch");

        // One active branch: the branch id stays, the name is hidden.
        var solo = await database.SeedWorldAsync();
        var (soloOwner, _) = await host.SignInAsync(database, solo.Org, OwnerRoleId, "Sole", "Owner");
        await database.ExecuteAsync("UPDATE branches SET is_active = false WHERE id = @b", ("b", solo.BranchB));
        var single = await database.CustomerAsync(solo.Org, solo.BranchA, "Kestrel Solo");

        var soloItem = (await host.SearchAsync(soloOwner, "kestrel"))["items"]!.AsArray().Single()!;
        Assert.Equal((single, solo.BranchA), (soloItem["entityId"]!.GetValue<Guid>(), soloItem["branchId"]!.GetValue<Guid>()));
        Assert.Null(soloItem["branchName"]);
    }

    // AC-03: exact number first (with or without prefix or #), then prefix, then partial; ties by updatedAt descending.
    [Fact]
    public async Task Search_NumbersAndText_AreRankedExactPrefixPartialThenNewest()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var (owner, member) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var org = world.Org;
        var now = DateTimeOffset.UtcNow;

        var plaza = await database.CustomerAsync(org, world.BranchA, "Unit 88 Plaza", updated: now);
        var oak = await database.CustomerAsync(org, world.BranchA, "88 Oak Street", updated: now.AddDays(-2));
        var request = await database.RequestAsync(org, world.BranchA, 88, updated: now.AddDays(-10));
        var other = await database.RequestAsync(org, world.BranchA, 5000, guest: "Nobody");
        var quote = await database.QuoteAsync(org, world.BranchA, other, null, 188, member.UserId, updated: now.AddDays(-8));
        var order = await database.WorkOrderAsync(org, world.BranchA, world.Customer, 880, "Boiler", member.UserId, updated: now.AddDays(-9));
        var invoice = await database.InvoiceAsync(org, world.BranchA, world.Customer, 88, member.UserId, updated: now.AddDays(-1));

        Assert.Equal([invoice, request, oak, order, plaza, quote], Ids(await host.SearchAsync(owner, "88")));
        // The hash only means a number: customer names do not contain it.
        Assert.Equal([invoice, request, order, quote], Ids(await host.SearchAsync(owner, "#88")));

        Assert.Equal([request], Ids(await host.SearchAsync(owner, "REQ-88")));
        Assert.Equal([invoice], Ids(await host.SearchAsync(owner, "inv-88")));
        Assert.Equal([order], Ids(await host.SearchAsync(owner, "wo880")));
    }

    // AC-13: at most 5 per type, at most `limit` in total, global order, and hasMore from either cap.
    [Fact]
    public async Task Search_PerTypeAndTotalCaps_KeepGlobalOrderAndSignalHasMore()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var (owner, member) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var org = world.Org;
        var now = DateTimeOffset.UtcNow;
        var expected = new List<Guid>();

        // Eight per type, interleaved so the newest rows alternate between the types.
        for (var index = 1; index <= 8; index++)
        {
            var customer = await database.CustomerAsync(org, world.BranchA, $"Zed Customer {index:00}", updated: now.AddMinutes(-3 * index));
            var request = await database.RequestAsync(org, world.BranchA, 100 + index, guest: $"Zed Guest {index:00}", updated: now.AddMinutes((-3 * index) - 1));
            var order = await database.WorkOrderAsync(org, world.BranchA, world.Customer, 200 + index, $"Zed Job {index:00}", member.UserId, updated: now.AddMinutes((-3 * index) - 2));

            if (index <= 5)
            {
                expected.AddRange([customer, request, order]);
            }
        }

        var full = await host.SearchAsync(owner, "zed");
        Assert.Equal(expected, Ids(full));
        Assert.True(full["hasMore"]!.GetValue<bool>());
        Assert.Equal(expected.Take(7), Ids(await host.SearchAsync(owner, "zed", "&limit=7")));
        Assert.Single(Ids(await host.SearchAsync(owner, "zed", "&limit=1")));

        var single = await host.SearchAsync(owner, "zed job 03");
        Assert.Single(Ids(single));
        Assert.False(single["hasMore"]!.GetValue<bool>());

        // Twelve matches, none of the types over its cap: only the total cap sets hasMore.
        for (var index = 1; index <= 4; index++)
        {
            await database.CustomerAsync(org, world.BranchA, $"Yak Customer {index}");
            await database.RequestAsync(org, world.BranchA, 300 + index, guest: $"Yak Guest {index}");
            await database.WorkOrderAsync(org, world.BranchA, world.Customer, 400 + index, $"Yak Job {index}", member.UserId);
        }

        var capped = await host.SearchAsync(owner, "yak", "&limit=10");
        Assert.Equal((10, true), (Ids(capped).Length, capped["hasMore"]!.GetValue<bool>()));
        var uncapped = await host.SearchAsync(owner, "yak", "&limit=12");
        Assert.Equal((12, false), (Ids(uncapped).Length, uncapped["hasMore"]!.GetValue<bool>()));
    }

    // AC-11: validation errors name the field and never echo the query; session is required.
    [Fact]
    public async Task Search_InvalidInput_ReturnsFieldErrorsWithoutEchoingTheQuery()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var (owner, _) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var tooLong = "Lengthy" + new string('x', 100);

        (string Query, string Extra, string Field)[] cases =
        [
            ("a", string.Empty, "q"),
            (" a ", string.Empty, "q"),
            (tooLong, string.Empty, "q"),
            ("ab", "&limit=0", "limit"),
            ("ab", "&limit=21", "limit"),
            ("ab", "&limit=abc", "limit"),
            ("ab", "&limit=1.5", "limit"),
        ];

        foreach (var (query, extra, field) in cases)
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/search/global?q={Uri.EscapeDataString(query)}{extra}", owner);
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(field, JsonNode.Parse(text)!["errors"]!.AsObject().Select(pair => pair.Key));
            Assert.DoesNotContain("Lengthy", text, StringComparison.Ordinal);
        }

        var missing = await host.SendAsync(HttpMethod.Get, "/search/global", owner);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var ok = await host.SendAsync(HttpMethod.Get, "/search/global?q=%20ab%20&limit=20", owner);
        Assert.Equal(("no-store", "ab"), (ok.Headers.CacheControl?.ToString(), Text((await RequestsHost.ReadAsync(ok))["query"])));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, "/search/global?q=ab", null)).StatusCode);
    }

    // AC-10: a search writes nothing and the query text reaches no log entry or error body.
    [Fact]
    public async Task Search_DistinctiveText_WritesNothingAndIsNeverLogged()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var (owner, _) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var (technician, _) = await host.SignInAsync(database, world.Org, TechnicianRoleId, "Tess", "Tech");
        await database.CustomerAsync(world.Org, world.BranchA, "Distinctive Quokka");
        var auditBefore = await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o", ("o", world.Org));
        var rowsBefore = await RowsAsync(world.Org);

        Assert.Single(Ids(await host.SearchAsync(owner, "quokka")));
        var failed = await host.SendAsync(HttpMethod.Get, $"/search/global?q=quokka{new string('z', 100)}", owner);
        var denied = await host.SendAsync(HttpMethod.Get, "/search/global?q=quokka", technician);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.Forbidden), (failed.StatusCode, denied.StatusCode));
        Assert.DoesNotContain("quokka", await failed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quokka", await denied.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(auditBefore, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(rowsBefore, await RowsAsync(world.Org));
        Assert.DoesNotContain(
            host.Logs.Entries,
            entry => entry.Message.Contains("quokka", StringComparison.OrdinalIgnoreCase)
                || (entry.Exception?.ToString().Contains("quokka", StringComparison.OrdinalIgnoreCase) ?? false)
                || entry.Properties.Values.Any(value => value?.ToString()?.Contains("quokka", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.Contains(host.Logs.Entries, entry => entry.Message.Contains("Global search finished", StringComparison.Ordinal));
    }

    // AC-15: the number of database commands does not depend on the number of matching rows.
    [Fact]
    public async Task Search_TenOrFourHundredMatchesPerType_RunsTheSameNumberOfCommands()
    {
        // A host that also captures the EF command log (the shared test host captures FieldOps categories only).
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(logs);
            logging.AddFilter<CapturingLoggerProvider>("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        }));
        using var client = SessionApi.CreateClient(factory);
        var small = await database.SeedWorldAsync();
        var large = await database.SeedWorldAsync();
        var smallMember = await database.SeedMemberAsync(small.Org, OwnerRoleId, "Small", "Owner");
        var largeMember = await database.SeedMemberAsync(large.Org, OwnerRoleId, "Large", "Owner");
        var smallOwner = await CompanySettingsApi.SignInCookieAsync(client, smallMember.Email);
        var largeOwner = await CompanySettingsApi.SignInCookieAsync(client, largeMember.Email);
        await BulkAsync(small, smallMember.UserId, 10);
        await BulkAsync(large, largeMember.UserId, 400);

        int Commands() => logs.Entries.Count(entry =>
            entry.Category == "Microsoft.EntityFrameworkCore.Database.Command"
            && entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal));

        async Task<(int Commands, JsonNode Body)> RunAsync(string cookie)
        {
            var before = Commands();
            var response = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Get, "/search/global?q=mongoose", null, cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return (Commands() - before, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
        }

        var (smallCommands, smallBody) = await RunAsync(smallOwner);
        var (largeCommands, largeBody) = await RunAsync(largeOwner);

        Assert.True(largeCommands > 0, "The EF command log was not captured.");
        Assert.Equal(smallCommands, largeCommands);
        Assert.InRange(largeCommands, 1, 14);
        Assert.Equal((15, 15), (Ids(smallBody).Length, Ids(largeBody).Length));

        // Parameter values (the query text) are not logged: sensitive data logging stays off.
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("mongoose", StringComparison.OrdinalIgnoreCase));
    }

    // AC-02, AC-07: organization isolation, branch scope with null branches, all-branches callers, role types and hasMore.
    [Fact]
    public async Task Search_OrganizationRoleAndBranchScope_HidesWhatTheCallerMayNotReadWithoutAffectingHasMore()
    {
        await using var host = RequestsHost.Create(database);
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var (owner, member) = await host.SignInAsync(database, world.Org, OwnerRoleId, "Olive", "Owner");
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, OwnerRoleId, "Fred", "Foreign");
        var org = world.Org;

        var cA = await database.CustomerAsync(org, world.BranchA, "Orion Alpha");
        var cB = await database.CustomerAsync(org, world.BranchB, "Orion Bravo");
        var rA = await database.RequestAsync(org, world.BranchA, 1, guest: "Orion RA");
        var rB = await database.RequestAsync(org, world.BranchB, 2, guest: "Orion RB");
        var rN = await database.RequestAsync(org, null, 3, guest: "Orion RN");
        var hiddenQuote = await database.QuoteAsync(org, null, rB, null, 1, member.UserId);
        var nullQuote = await database.QuoteAsync(org, null, rN, null, 2, member.UserId);
        var ownBranchQuote = await database.QuoteAsync(org, world.BranchA, rB, null, 3, member.UserId, status: "cancelled");
        var wA = await database.WorkOrderAsync(org, world.BranchA, cA, 1, "Orion Job A", member.UserId);
        var iA = await database.InvoiceAsync(org, world.BranchA, cA, 1, member.UserId);
        var iB = await database.InvoiceAsync(org, world.BranchB, cB, 2, member.UserId);
        var branchBJobs = new List<Guid>();

        for (var index = 1; index <= 9; index++)
        {
            branchBJobs.Add(await database.WorkOrderAsync(org, world.BranchB, cB, 1 + index, $"Orion Job B{index}", member.UserId));
        }

        var foreignCustomer = await database.CustomerAsync(foreign.Org, foreign.BranchA, "Orion Foreign");
        var foreignIds = new[]
        {
            foreignCustomer,
            await database.RequestAsync(foreign.Org, foreign.BranchA, 1, guest: "Orion Foreign Request"),
            await database.WorkOrderAsync(foreign.Org, foreign.BranchA, foreignCustomer, 1, "Orion Foreign Job", foreignMember.UserId),
            await database.InvoiceAsync(foreign.Org, foreign.BranchA, foreignCustomer, 1, foreignMember.UserId),
        };

        async Task<(Guid[] Ids, string[] Types, bool HasMore)> SearchAs(short role, params Guid[] branches)
        {
            var (cookie, _) = await host.SignInAsync(database, org, role, "Role", "Caller", branches);
            var body = await host.SearchAsync(cookie, "orion");

            return (Ids(body), [.. Keys(body).Select(key => key.Type)], body["hasMore"]!.GetValue<bool>());
        }

        var all = await SearchAs(OwnerRoleId);
        var viewer = await SearchAs(ViewerRoleId);
        var scoped = await SearchAs(DispatcherRoleId, world.BranchA);
        var accounting = await SearchAs(AccountingRoleId);
        var accountingScoped = await SearchAs(AccountingRoleId, world.BranchA);

        // The owner sees everything of the organization (five jobs at most) and nothing of the other one.
        Guid[] visibleEverywhere = [cA, cB, rA, rB, rN, hiddenQuote, nullQuote, ownBranchQuote, iA, iB];
        Assert.Equal(visibleEverywhere.Order(), all.Ids.Where(id => !branchBJobs.Contains(id) && id != wA).Order());
        Assert.Equal(5, all.Types.Count(type => type == "work_order"));
        Assert.True(all.HasMore);
        Assert.Equal((15, true), (viewer.Ids.Length, viewer.HasMore));
        Assert.Empty(foreignIds.Intersect(all.Ids).Concat(foreignIds.Intersect(viewer.Ids)).Concat(foreignIds.Intersect(scoped.Ids)));

        // Branch A only: branch A and null-branch entities (a quote falls back to its request's branch); hidden jobs do not set hasMore.
        Assert.Equal(new[] { cA, rA, rN, nullQuote, ownBranchQuote, wA, iA }.Order(), scoped.Ids.Order());
        Assert.False(scoped.HasMore);
        Assert.Equal(new[] { cA, rA, rN, nullQuote, ownBranchQuote, iA }.Order(), accountingScoped.Ids.Order());

        // Accounting (all branches) never finds jobs, and the ten hidden jobs do not set hasMore.
        Assert.Equal(visibleEverywhere.Order(), accounting.Ids.Order());
        Assert.DoesNotContain("work_order", accounting.Types);
        Assert.False(accounting.HasMore);

        // A technician has no global search; the other organization sees only itself.
        var (technician, _) = await host.SignInAsync(database, org, TechnicianRoleId, "Tess", "Tech");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/search/global?q=orion", technician)).StatusCode);
        Assert.Equal(foreignIds.Order(), Ids(await host.SearchAsync(foreignOwner, "orion")).Order());
    }

    private Task<string> RowsAsync(Guid org) =>
        database.ScalarAsync<string>(
            """
            SELECT (SELECT COUNT(*) FROM customers WHERE organization_id = @o) || '|' || (SELECT COUNT(*) FROM service_requests WHERE organization_id = @o)
                || '|' || (SELECT COUNT(*) FROM notifications WHERE organization_id = @o) || '|' || (SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o)
            """,
            ("o", org));

    private Task BulkAsync(RequestWorld world, Guid user, int count) =>
        database.ExecuteAsync(
            """
            INSERT INTO customers (id, organization_id, type, branch_id, display_name)
            SELECT gen_random_uuid(), @org, CAST('person' AS customer_type), @branch, 'Mongoose Customer ' || g FROM generate_series(1, @count) g;
            INSERT INTO service_requests (id, organization_id, branch_id, request_number, guest_name, description, status)
            SELECT gen_random_uuid(), @org, @branch, 1000 + g, 'Mongoose Guest ' || g, 'Routine', CAST('new' AS request_status) FROM generate_series(1, @count) g;
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id, scope_snapshot, created_by_user_id, status)
            SELECT gen_random_uuid(), @org, @branch, 2000 + g, gen_random_uuid(), @customer, gen_random_uuid(), 'Mongoose Job ' || g, gen_random_uuid(), 'Scope', @user, CAST('scheduled' AS work_order_status) FROM generate_series(1, @count) g;
            SET session_replication_role = DEFAULT;
            """,
            ("org", world.Org),
            ("branch", world.BranchA),
            ("customer", world.Customer),
            ("user", user),
            ("count", count));
}
