using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>The related-record lists, Activity, Overview visibility and customer options of the customer page (customer-related-records).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerRelatedRecordsTests(CompanySettingsDatabaseFixture database)
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    // FR-02 to FR-05, AC-02 to AC-06: columns, order, search, status, paging, customerTotal and the invoice summary
    // (independent of search, status and page) of the four lists, the BR-01 authorization order and the BR-04 400s.
    [Fact]
    public async Task Lists_ReturnContractRowsOrderSearchStatusPagingAndIndependentSummary()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var customer = await database.SeedCustomerAsync(org, branch.Id, "List Customer");
        var rival = await database.SeedCustomerAsync(org, branch.Id, "Rival Customer");
        var primary = await database.PrimaryPropertyAsync(customer);
        var warehouse = await database.SeedPropertyAsync(org, customer, branch.Id, "Warehouse");
        var plumbing = await database.SeedCategoryAsync(org, "Plumbing");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        // Requests: 12 rows (paging), the first with property and category, the second with a long description only.
        var requests = new List<Guid>();

        for (var number = 1; number <= 12; number++)
        {
            var description = number switch
            {
                1 => "Leaking tap\nsecond line",
                2 => new string('x', 70) + " tail",
                _ => "Filler alpha",
            };
            var status = number switch { 1 => "new", 2 => "quoted", _ => "cancelled" };
            requests.Add(await database.SeedRequestAsync(org, customer, number, description, status, now.AddDays(-number)));
        }

        await database.PlaceRequestAsync(requests[0], branch.Id, warehouse, plumbing);
        var rivalRequest = await database.SeedRequestAsync(org, rival, 90, "Rival request", "new", now);

        // Quotes: awaiting response, draft without version, expired by its valid-until, and a rival quote.
        var (_, sentVersion) = await database.SeedQuoteAsync(org, owner.UserId, customer, requests[0], 1, "sent", "Scope", 1250.50m, now.AddDays(-1), now.AddDays(-1));
        await database.ExpireVersionAsync(sentVersion, today.AddDays(30));
        await database.SeedQuoteAsync(org, owner.UserId, customer, requests[1], 2, "draft", "Draft", 0m, now.AddDays(-2), null, withVersion: false);
        var (_, expiredVersion) = await database.SeedQuoteAsync(org, owner.UserId, customer, requests[2], 3, "sent", "Old scope", 99m, now.AddDays(-3), now.AddDays(-3));
        await database.ExpireVersionAsync(expiredVersion, today.AddDays(-1));
        await database.SeedQuoteAsync(org, owner.UserId, rival, rivalRequest, 4, "sent", "Rival", 5m, now, now);

        // Jobs: completed with a technician, assigned with an upcoming visit, ready to schedule, cancelled and a draft.
        var tina = await database.SeedTechnicianProfileAsync(org, branch.Id, "Tina", "Tech");
        var bob = await database.SeedTechnicianProfileAsync(org, branch.Id, "Bob", "Builder");
        var completed = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, warehouse, Guid.NewGuid(), 1, "completed", "Scope", now.AddDays(-3));
        var assigned = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, primary, Guid.NewGuid(), 2, "scheduled", "Scope", now.AddDays(-2));
        var ready = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, warehouse, Guid.NewGuid(), 3, "ready_to_schedule", "Scope", now.AddDays(-1));
        var cancelled = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, primary, Guid.NewGuid(), 5, "cancelled", "Scope", now.AddDays(-20));
        await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, primary, Guid.NewGuid(), 4, "draft", "Scope", now);
        await database.TitleJobAsync(completed, "Install boiler");
        await database.TitleJobAsync(assigned, "Replace valve");
        await database.TitleJobAsync(ready, "Inspect roof");
        await database.TitleJobAsync(cancelled, "Old job");
        var doneVisit = await database.SeedVisitAsync(org, completed, 1, "completed", now.AddDays(-5), now.AddDays(-5).AddHours(2), now.AddDays(-5).AddHours(1));
        await database.SeedAssignmentAsync(doneVisit, tina, owner.UserId);
        var nextVisit = await database.SeedVisitAsync(org, assigned, 1, "assigned", now.AddDays(3), now.AddDays(3).AddHours(2));
        await database.SeedAssignmentAsync(nextVisit, bob, owner.UserId);
        await database.SeedVisitAsync(org, cancelled, 1, "cancelled", now.AddDays(4), now.AddDays(4).AddHours(1));

        // Invoices: draft, sent, past-due partially paid (overdue), paid, void, and a rival invoice.
        await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, completed, 1, "draft", 900m, 0m, null, null, now.AddDays(-1));
        await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, assigned, 2, "sent", 300m, 0m, today.AddDays(-5), today.AddDays(10), now.AddDays(-2));
        await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, ready, 3, "partially_paid", 500m, 200m, today.AddDays(-40), today.AddDays(-10), now.AddDays(-3));
        await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, cancelled, 4, "paid", 400m, 400m, today.AddDays(-60), today.AddDays(-30), now.AddDays(-4));
        await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, completed, 5, "void", 999m, 0m, today.AddDays(-1), null, now.AddDays(-5));
        var rivalJob = await database.SeedJobAsync(org, owner.UserId, branch.Id, rival, await database.PrimaryPropertyAsync(rival), Guid.NewGuid(), 9, "scheduled", "Rival job", now);
        await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, rival, rivalJob, 6, "sent", 777m, 0m, today, today.AddDays(5), now);

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var accounting = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.AccountingRoleId);

        async Task<JsonNode> Ok(string path)
        {
            var response = await host.SendAsync(HttpMethod.Get, path, cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

            return await CustomerHost.ReadAsync(response);
        }

        string[] Numbers(JsonNode body) => CustomerRelatedRecordsSeed.Strings(body, "items", "number");

        // Requests (BR-05).
        var requestsPath = $"/customers/{customer}/requests";
        var first = await Ok(requestsPath);
        Assert.Equal((12, 12, 1, 10), (first["totalCount"]!.GetValue<int>(), first["customerTotal"]!.GetValue<int>(), first["page"]!.GetValue<int>(), first["items"]!.AsArray().Count));
        Assert.Equal(["REQ-1", "REQ-2", "REQ-3"], Numbers(first)[..3]);
        Assert.Equal(("Plumbing", "Warehouse", "new"), (first["items"]![0]!["service"]!.GetValue<string>(), first["items"]![0]!["propertyName"]!.GetValue<string>(), first["items"]![0]!["status"]!.GetValue<string>()));
        Assert.Equal(new string('x', 60) + "…", first["items"]![1]!["service"]!.GetValue<string>());
        Assert.Null(first["items"]![1]!["propertyName"]);
        Assert.Equal(("Filler alpha", "cancelled"), (first["items"]![2]!["service"]!.GetValue<string>(), first["items"]![2]!["status"]!.GetValue<string>()));
        Assert.DoesNotContain("REQ-90", Numbers(first));
        Assert.Equal(["REQ-1"], Numbers(await Ok($"{requestsPath}?search=PLUMB")));
        Assert.Equal(["REQ-2"], Numbers(await Ok($"{requestsPath}?search=2")));
        Assert.Equal(["REQ-1"], Numbers(await Ok($"{requestsPath}?search=req-1")));
        Assert.Equal(["REQ-1"], Numbers(await Ok($"{requestsPath}?search=warehouse")));
        Assert.Equal(["REQ-1"], Numbers(await Ok($"{requestsPath}?search=leaking")));
        var quoted = await Ok($"{requestsPath}?status=quoted");
        Assert.Equal((1, 12), (quoted["totalCount"]!.GetValue<int>(), quoted["customerTotal"]!.GetValue<int>()));

        // BR-04 paging: page 2 of 10 holds the last two, a page beyond the last is empty, sizes 25 and 50 are accepted.
        Assert.Equal(["REQ-11", "REQ-12"], Numbers(await Ok($"{requestsPath}?page=2")));
        var beyond = await Ok($"{requestsPath}?page=3");
        Assert.Equal((0, 12, 3), (beyond["items"]!.AsArray().Count, beyond["totalCount"]!.GetValue<int>(), beyond["page"]!.GetValue<int>()));
        var wide = await Ok($"{requestsPath}?pageSize=25");
        Assert.Equal((12, 25), (wide["items"]!.AsArray().Count, wide["pageSize"]!.GetValue<int>()));
        Assert.Equal(50, (await Ok($"{requestsPath}?pageSize=50"))["pageSize"]!.GetValue<int>());

        // Quotes (BR-06): shown-version total and expiry, module display status, no rival quotes.
        var quotes = await Ok($"/customers/{customer}/quotes");
        Assert.Equal(["Q-1", "Q-2", "Q-3"], Numbers(quotes));
        Assert.Equal((3, 3, "USD"), (quotes["totalCount"]!.GetValue<int>(), quotes["customerTotal"]!.GetValue<int>(), quotes["currency"]!.GetValue<string>()));
        var quoteRows = quotes["items"]!.AsArray();
        Assert.Equal(("REQ-1", "Plumbing", 1250.50m, today.AddDays(30).ToString("yyyy-MM-dd"), "awaiting_response"), (
            quoteRows[0]!["request"]!["number"]!.GetValue<string>(),
            quoteRows[0]!["request"]!["service"]!.GetValue<string>(),
            quoteRows[0]!["total"]!.GetValue<decimal>(),
            quoteRows[0]!["validUntil"]!.GetValue<string>(),
            quoteRows[0]!["status"]!.GetValue<string>()));
        Assert.Equal(("draft", "expired"), (quoteRows[1]!["status"]!.GetValue<string>(), quoteRows[2]!["status"]!.GetValue<string>()));
        Assert.Null(quoteRows[1]!["validUntil"]);
        Assert.Equal(["Q-3"], Numbers(await Ok($"/customers/{customer}/quotes?status=expired")));
        Assert.Equal(["Q-2"], Numbers(await Ok($"/customers/{customer}/quotes?search=REQ-2")));
        Assert.Equal(["Q-1"], Numbers(await Ok($"/customers/{customer}/quotes?search=plumbing")));
        var drafts = await Ok($"/customers/{customer}/quotes?status=draft");
        Assert.Equal((1, 3), (drafts["totalCount"]!.GetValue<int>(), drafts["customerTotal"]!.GetValue<int>()));

        // Jobs (BR-07): draft excluded, next visit or completion, technician and operational status.
        var jobs = await Ok($"/customers/{customer}/jobs");
        Assert.Equal(["WO-3", "WO-2", "WO-1", "WO-5"], Numbers(jobs));
        Assert.Equal((4, 4), (jobs["totalCount"]!.GetValue<int>(), jobs["customerTotal"]!.GetValue<int>()));
        var jobRows = jobs["items"]!.AsArray();
        Assert.Equal(("Inspect roof", "Warehouse", "ready_to_schedule"), (jobRows[0]!["title"]!.GetValue<string>(), jobRows[0]!["propertyName"]!.GetValue<string>(), jobRows[0]!["status"]!.GetValue<string>()));
        Assert.Null(jobRows[0]!["technician"]);
        Assert.Null(jobRows[0]!["nextVisitAt"]);
        Assert.Equal(("assigned", "Bob Builder", "BB"), (jobRows[1]!["status"]!.GetValue<string>(), jobRows[1]!["technician"]!["name"]!.GetValue<string>(), jobRows[1]!["technician"]!["initials"]!.GetValue<string>()));
        Assert.Equal(now.AddDays(3), jobRows[1]!["nextVisitAt"]!.GetValue<DateTimeOffset>(), Tolerance);
        Assert.Equal(("completed", "Tina Tech"), (jobRows[2]!["status"]!.GetValue<string>(), jobRows[2]!["technician"]!["name"]!.GetValue<string>()));
        Assert.Equal(now.AddDays(-5).AddHours(1), jobRows[2]!["completedAt"]!.GetValue<DateTimeOffset>(), Tolerance);
        Assert.Null(jobRows[2]!["nextVisitAt"]);
        Assert.Equal("cancelled", jobRows[3]!["status"]!.GetValue<string>());
        Assert.Equal(["WO-1"], Numbers(await Ok($"/customers/{customer}/jobs?search=tina")));
        Assert.Equal(["WO-2"], Numbers(await Ok($"/customers/{customer}/jobs?search=VALVE")));
        Assert.Equal(["WO-3", "WO-1"], Numbers(await Ok($"/customers/{customer}/jobs?search=warehouse")));
        Assert.Equal(["WO-2"], Numbers(await Ok($"/customers/{customer}/jobs?search=WO-2")));
        Assert.Equal(["WO-2"], Numbers(await Ok($"/customers/{customer}/jobs?status=assigned")));
        Assert.Empty((await Ok($"/customers/{customer}/jobs?status=needs_correction"))["items"]!.AsArray());

        // Invoices (BR-08): void excluded, display status, issue-date order (nulls first) and the unfiltered summary.
        var invoices = await Ok($"/customers/{customer}/invoices");
        Assert.Equal(["INV-1", "INV-2", "INV-3", "INV-4"], Numbers(invoices));
        Assert.Equal(["draft", "sent", "overdue", "paid"], CustomerRelatedRecordsSeed.Strings(invoices, "items", "status"));
        Assert.Equal(("WO-1", "WO-2", "WO-3", "WO-5", 300m, null), (
            invoices["items"]![0]!["job"]!["number"]!.GetValue<string>(),
            invoices["items"]![1]!["job"]!["number"]!.GetValue<string>(),
            invoices["items"]![2]!["job"]!["number"]!.GetValue<string>(),
            invoices["items"]![3]!["job"]!["number"]!.GetValue<string>(),
            invoices["items"]![2]!["balanceDue"]!.GetValue<decimal>(),
            invoices["items"]![0]!["issueDate"]?.GetValue<string>()));
        Assert.Equal(today.AddDays(-5).ToString("yyyy-MM-dd"), invoices["items"]![1]!["issueDate"]!.GetValue<string>());
        Assert.Equal((600m, 300m, 600m), (
            invoices["summary"]!["outstanding"]!.GetValue<decimal>(),
            invoices["summary"]!["overdue"]!.GetValue<decimal>(),
            invoices["summary"]!["paid"]!.GetValue<decimal>()));
        Assert.Equal((4, "USD"), (invoices["customerTotal"]!.GetValue<int>(), invoices["currency"]!.GetValue<string>()));

        var filtered = await Ok($"/customers/{customer}/invoices?status=paid&search=INV-4&pageSize=10");
        Assert.Equal((1, 4, 600m, 300m, 600m), (
            filtered["totalCount"]!.GetValue<int>(),
            filtered["customerTotal"]!.GetValue<int>(),
            filtered["summary"]!["outstanding"]!.GetValue<decimal>(),
            filtered["summary"]!["overdue"]!.GetValue<decimal>(),
            filtered["summary"]!["paid"]!.GetValue<decimal>()));
        Assert.Equal(["INV-3"], Numbers(await Ok($"/customers/{customer}/invoices?status=overdue")));
        Assert.Equal(["INV-2"], Numbers(await Ok($"/customers/{customer}/invoices?status=sent")));
        Assert.Equal(["INV-3"], Numbers(await Ok($"/customers/{customer}/invoices?search=wo-3&status=overdue")));
        Assert.Equal(["INV-4"], Numbers(await Ok($"/customers/{customer}/invoices?search=WO-5")));
        Assert.Equal(0, (await Ok($"/customers/{customer}/invoices?status=partially_paid"))["totalCount"]!.GetValue<int>());

        // BR-04 400s: every invalid value names its parameter.
        foreach (var (path, key) in new[]
        {
            ($"{requestsPath}?status=draft", "status"),
            ($"{requestsPath}?page=0", "page"),
            ($"{requestsPath}?page=abc", "page"),
            ($"{requestsPath}?pageSize=20", "pageSize"),
            ($"{requestsPath}?search={new string('s', 101)}", "search"),
            ($"/customers/{customer}/quotes?status=converted", "status"),
            ($"/customers/{customer}/jobs?status=draft", "status"),
            ($"/customers/{customer}/invoices?status=void", "status"),
        })
        {
            var response = await host.SendAsync(HttpMethod.Get, path, cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotEmpty(CustomerSeed.Error(await CustomerHost.ReadAsync(response), key));
        }

        // BR-01 order: 401, then 403 (module), then 404 (customer), then 400 (query).
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, requestsPath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, $"/customers/{Guid.NewGuid()}/jobs?page=0", accounting)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/customers/{Guid.NewGuid()}/requests?page=0", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/customers/{customer}/invoices", accounting)).StatusCode);
    }

    // AC-08, AC-09, FR-08, FR-09: a Dispatcher and an Accounting user scoped to branch X see only X records (and
    // null-branch requests) in lists, summaries, events and Overview values; draft jobs and void invoices never appear;
    // the Owner sees both branches; Accounting gets no job data anywhere (BR-14 omission, not zero).
    [Fact]
    public async Task Visibility_ScopesEverySurfaceByBranchAndOmitsUnreadableModules()
    {
        var org = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee");
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var customer = await database.SeedCustomerAsync(org, branchX.Id, "Scoped Customer");
        var primary = await database.PrimaryPropertyAsync(customer);
        var xSite = await database.SeedPropertyAsync(org, customer, branchX.Id, "X site");
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var requestX = await database.SeedRequestAsync(org, customer, 1, "Request X", "new", now.AddDays(-1));
        var requestY = await database.SeedRequestAsync(org, customer, 2, "Request Y", "new", now.AddDays(-2));
        var requestNull = await database.SeedRequestAsync(org, customer, 3, "Request null", "new", now.AddDays(-3));
        await database.PlaceRequestAsync(requestX, branchX.Id, null, null);
        await database.PlaceRequestAsync(requestY, branchY.Id, null, null);
        var (quoteX, versionX) = await database.SeedQuoteAsync(org, owner.UserId, customer, requestX, 1, "sent", "X", 100m, now.AddDays(-1), now);
        var (quoteY, versionY) = await database.SeedQuoteAsync(org, owner.UserId, customer, requestY, 2, "sent", "Y", 200m, now.AddDays(-2), now);
        var (quoteNull, _) = await database.SeedQuoteAsync(org, owner.UserId, customer, requestNull, 3, "sent", "N", 300m, now.AddDays(-3), now);

        var jobX = await database.SeedJobAsync(org, owner.UserId, branchX.Id, customer, xSite, versionX, 1, "scheduled", "Job X", now.AddDays(-4));
        var jobY = await database.SeedJobAsync(org, owner.UserId, branchY.Id, customer, primary, versionY, 2, "completed", "Job Y", now.AddDays(-5));
        var jobDraft = await database.SeedJobAsync(org, owner.UserId, branchX.Id, customer, xSite, Guid.NewGuid(), 3, "draft", "Job draft", now.AddDays(-6));
        var visitX = await database.SeedVisitAsync(org, jobX, 1, "scheduled", now.AddDays(2), now.AddDays(2).AddHours(1));
        await database.SeedVisitAsync(org, jobY, 1, "completed", now.AddDays(-9), now.AddDays(-9).AddHours(1), now.AddDays(-9));
        await database.SeedVisitAsync(org, jobY, 2, "scheduled", now.AddDays(4), now.AddDays(4).AddHours(1));

        var invoiceX = await database.SeedJobInvoiceAsync(org, owner.UserId, branchX.Id, customer, jobX, 1, "sent", 150m, 50m, today, today.AddDays(30), now.AddDays(-1));
        var invoiceY = await database.SeedJobInvoiceAsync(org, owner.UserId, branchY.Id, customer, jobY, 2, "sent", 450m, 50m, today, today.AddDays(30), now.AddDays(-2));
        var invoiceVoid = await database.SeedJobInvoiceAsync(org, owner.UserId, branchX.Id, customer, jobX, 3, "void", 999m, 0m, today, null, now.AddDays(-3));
        var paymentX = await database.SeedPaymentAsync(org, customer, owner.UserId, 1, now.AddDays(-1), (invoiceX, 50m));
        var paymentY = await database.SeedPaymentAsync(org, customer, owner.UserId, 2, now.AddDays(-2), (invoiceY, 50m));
        var paymentMixed = await database.SeedPaymentAsync(org, customer, owner.UserId, 3, now.AddDays(-3), (invoiceX, 30m), (invoiceY, 20m));

        var events = new (string Action, string Entity, Guid Id)[]
        {
            ("service_request.created", "service_request", requestX),
            ("service_request.created", "service_request", requestY),
            ("service_request.created", "service_request", requestNull),
            ("quote.sent", "quote", quoteX),
            ("quote.sent", "quote", quoteY),
            ("quote.sent", "quote", quoteNull),
            ("work_order.created", "work_order", jobX),
            ("work_order.created", "work_order", jobY),
            ("work_order.created", "work_order", jobDraft),
            ("invoice.sent", "invoice", invoiceX),
            ("invoice.sent", "invoice", invoiceY),
            ("invoice.sent", "invoice", invoiceVoid),
            ("payment.recorded", "payment", paymentX),
            ("payment.recorded", "payment", paymentY),
            ("payment.recorded", "payment", paymentMixed),
        };

        for (var i = 0; i < events.Length; i++)
        {
            await database.SeedAuditAsync(org, owner.UserId, events[i].Action, events[i].Entity, events[i].Id, now.AddMinutes(-60 + i));
        }

        await using var host = CustomerHost.Create(database);
        var ownerCookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var dispatcher = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, branchX.Id);
        var accounting = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.AccountingRoleId, branchX.Id);

        async Task<JsonNode> Read(string path, string cookie)
        {
            var response = await host.SendAsync(HttpMethod.Get, path, cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return await CustomerHost.ReadAsync(response);
        }

        string[] Numbers(JsonNode body) => CustomerRelatedRecordsSeed.Strings(body, "items", "number");
        var basePath = $"/customers/{customer}";

        // Lists and counts: X plus null-branch requests for the scoped roles, everything non-draft/non-void for the Owner.
        Assert.Equal(["REQ-1", "REQ-3"], Numbers(await Read($"{basePath}/requests", dispatcher)));
        Assert.Equal(["REQ-1", "REQ-2", "REQ-3"], Numbers(await Read($"{basePath}/requests", ownerCookie)));
        Assert.Equal(2, (await Read($"{basePath}/requests", dispatcher))["customerTotal"]!.GetValue<int>());
        Assert.Equal(["Q-1", "Q-3"], Numbers(await Read($"{basePath}/quotes", dispatcher)));
        Assert.Equal(["Q-1", "Q-2", "Q-3"], Numbers(await Read($"{basePath}/quotes", ownerCookie)));
        Assert.Equal(["WO-1"], Numbers(await Read($"{basePath}/jobs", dispatcher)));
        Assert.Equal(["WO-1", "WO-2"], Numbers(await Read($"{basePath}/jobs", ownerCookie)).OrderBy(number => number).ToArray());

        var scopedInvoices = await Read($"{basePath}/invoices", accounting);
        Assert.Equal(["INV-1"], Numbers(scopedInvoices));
        Assert.Equal((100m, 0m, 50m, 1), (
            scopedInvoices["summary"]!["outstanding"]!.GetValue<decimal>(),
            scopedInvoices["summary"]!["overdue"]!.GetValue<decimal>(),
            scopedInvoices["summary"]!["paid"]!.GetValue<decimal>(),
            scopedInvoices["customerTotal"]!.GetValue<int>()));
        var ownerInvoices = await Read($"{basePath}/invoices", ownerCookie);
        Assert.Equal((500m, 100m, 2), (
            ownerInvoices["summary"]!["outstanding"]!.GetValue<decimal>(),
            ownerInvoices["summary"]!["paid"]!.GetValue<decimal>(),
            ownerInvoices["customerTotal"]!.GetValue<int>()));

        // Activity: events of hidden or draft/void records never appear; a payment keeps only its visible invoices.
        string[] Keys(JsonNode body) => [.. body["items"]!.AsArray().Select(item => $"{item!["action"]!.GetValue<string>()}:{item["subject"]!["number"]?.GetValue<string>()}")];
        var dispatcherEvents = await Read($"{basePath}/activity?period=all", dispatcher);
        Assert.Equal(
            ["invoice.sent:INV-1", "payment.recorded:", "payment.recorded:", "quote.sent:Q-1", "quote.sent:Q-3", "service_request.created:REQ-1", "service_request.created:REQ-3", "work_order.created:WO-1"],
            Keys(dispatcherEvents).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        var payments = dispatcherEvents["items"]!.AsArray().Where(item => item!["type"]!.GetValue<string>() == "payments").ToArray();
        Assert.All(payments, payment => Assert.Equal(["INV-1"], payment!["subject"]!["invoiceNumbers"]!.AsArray().Select(number => number!.GetValue<string>()).ToArray()));
        Assert.Equal(13, (await Read($"{basePath}/activity?period=all", ownerCookie))["items"]!.AsArray().Count);

        var accountingEvents = await Read($"{basePath}/activity?period=all", accounting);
        Assert.DoesNotContain(accountingEvents["items"]!.AsArray(), item => item!["type"]!.GetValue<string>() is "jobs" or "visits");
        Assert.Equal(
            ["customer", "requests", "messages", "quotes", "invoices", "payments"],
            accountingEvents["availableTypes"]!.AsArray().Select(type => type!.GetValue<string>()).ToArray());
        Assert.Equal(7, accountingEvents["items"]!.AsArray().Count);

        foreach (var type in new[] { "jobs", "visits" })
        {
            var rejected = await host.SendAsync(HttpMethod.Get, $"{basePath}/activity?type={type}", accounting);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.NotEmpty(CustomerSeed.Error(await CustomerHost.ReadAsync(rejected), "type"));
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, $"{basePath}/jobs", accounting)).StatusCode);

        // Overview values come from visible records only (BR-14).
        var dispatcherDetail = await Read($"{basePath}/detail", dispatcher);
        Assert.Equal((100m, 1, 50m), (
            dispatcherDetail["outstandingBalance"]!.GetValue<decimal>(),
            dispatcherDetail["summary"]!["totalJobs"]!.GetValue<int>(),
            dispatcherDetail["summary"]!["lifetimeValue"]!.GetValue<decimal>()));
        Assert.Null(dispatcherDetail["summary"]!["lastServiceAt"]);
        var ownerDetail = await Read($"{basePath}/detail", ownerCookie);
        Assert.Equal((500m, 2, 100m), (
            ownerDetail["outstandingBalance"]!.GetValue<decimal>(),
            ownerDetail["summary"]!["totalJobs"]!.GetValue<int>(),
            ownerDetail["summary"]!["lifetimeValue"]!.GetValue<decimal>()));
        Assert.NotNull(ownerDetail["summary"]!["lastServiceAt"]);

        var dispatcherWork = (await Read($"{basePath}/recent-work", dispatcher))["items"]!.AsArray();
        Assert.Equal(
            ["Q-1", "Q-3", "REQ-1", "REQ-3", "WO-1"],
            dispatcherWork.Select(item => item!["number"]!.GetValue<string>()).OrderBy(number => number, StringComparer.Ordinal).ToArray());
        var appointments = (await Read($"{basePath}/upcoming-appointments", dispatcher))["items"]!.AsArray();
        Assert.Equal([visitX], appointments.Select(item => item!["visitId"]!.GetValue<Guid>()).ToArray());
        Assert.Equal(2, (await Read($"{basePath}/upcoming-appointments", ownerCookie))["items"]!.AsArray().Count);

        var scopedProperties = (await Read($"{basePath}/properties", dispatcher))["items"]!.AsArray();
        var ownerProperties = (await Read($"{basePath}/properties", ownerCookie))["items"]!.AsArray();
        JsonNode PropertyOf(JsonArray items, Guid id) => items.Single(item => item!["id"]!.GetValue<Guid>() == id)!;
        Assert.Null(PropertyOf(scopedProperties, primary)["lastService"]);
        Assert.NotNull(PropertyOf(ownerProperties, primary)["lastService"]);
        Assert.NotNull(PropertyOf(scopedProperties, xSite)["nextAppointment"]);

        // Accounting: job-derived values are omitted (null), never zero, and the job rows and card disappear.
        var accountingDetail = await Read($"{basePath}/detail", accounting);
        Assert.Null(accountingDetail["summary"]!["totalJobs"]);
        Assert.Null(accountingDetail["summary"]!["lastServiceAt"]);
        Assert.Equal((100m, 50m), (accountingDetail["outstandingBalance"]!.GetValue<decimal>(), accountingDetail["summary"]!["lifetimeValue"]!.GetValue<decimal>()));
        Assert.Empty((await Read($"{basePath}/upcoming-appointments", accounting))["items"]!.AsArray());
        Assert.DoesNotContain(
            (await Read($"{basePath}/recent-work", accounting))["items"]!.AsArray(),
            item => item!["type"]!.GetValue<string>() == "job");
        Assert.All(
            (await Read($"{basePath}/properties", accounting))["items"]!.AsArray(),
            item =>
            {
                Assert.Null(item!["lastService"]);
                Assert.Null(item["nextAppointment"]);
            });
    }

    // AC-14 to AC-16, FR-07: the whitelist, current-value summaries, links and BR-13 origins; redaction by projection
    // (no before/after data, metadata, IP, note text); newest-first cursor paging in blocks of 20 without duplicates
    // (including events that share one instant); the period windows and the type, period and cursor 400s.
    [Fact]
    public async Task Activity_ReturnsWhitelistedRedactedEventsAndPagesByCursor()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var otherBranch = await database.SeedBranchAsync(other, isMain: true);
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var portalUser = await database.SeedUserAsync();
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Activity Customer");
        var rival = await database.SeedCustomerAsync(org, branch.Id, "Rival Customer");
        var foreign = await database.SeedCustomerAsync(other, otherBranch.Id, "Foreign Customer");
        var primary = await database.PrimaryPropertyAsync(customer);
        var contact = await database.PrimaryContactAsync(customer);
        var plumbing = await database.SeedCategoryAsync(org, "Plumbing");
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var request = await database.SeedRequestAsync(org, customer, 7, "Fix sink", "quoted", now.AddDays(-9));
        await database.PlaceRequestAsync(request, branch.Id, primary, plumbing);
        var (quote, version) = await database.SeedQuoteAsync(org, owner.UserId, customer, request, 4, "sent", "Scope", 1250.50m, now.AddDays(-8), now.AddDays(-8));
        var job = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, primary, version, 11, "completed", "Scope", now.AddDays(-7));
        await database.TitleJobAsync(job, "Fix sink job");
        var visit = await database.SeedVisitAsync(org, job, 2, "scheduled", now.AddDays(1), now.AddDays(1).AddHours(1));
        var invoice = await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, job, 21, "sent", 800m, 0m, today, today.AddDays(30), now.AddDays(-6));
        var secondJob = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, primary, Guid.NewGuid(), 12, "completed", "Second", now.AddDays(-7));
        var secondInvoice = await database.SeedJobInvoiceAsync(org, owner.UserId, branch.Id, customer, secondJob, 22, "sent", 100m, 0m, today, today.AddDays(30), now.AddDays(-6));
        var payment = await database.SeedPaymentAsync(org, customer, owner.UserId, 31, now.AddDays(-5), (secondInvoice, 40m), (invoice, 60m));
        var note = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO customer_notes (id, organization_id, customer_id, author_user_id, note) VALUES (@id, @o, @c, @a, 'Secret note text')",
            ("id", note), ("o", org), ("c", customer), ("a", owner.UserId));
        await database.ExecuteAsync("UPDATE properties SET name = 'Renamed Site' WHERE id = @p", ("p", primary));
        var rivalRequest = await database.SeedRequestAsync(org, rival, 70, "Rival request", "new", now.AddDays(-9));

        const string secret = "{\"email\":\"leak@example.com\",\"note\":\"SECRET-NOTE\",\"status\":\"leaky\"}";
        var minute = 0;

        Task Seed(Guid? actor, string action, string entityType, Guid entityId, string? afterData = null, Guid? org2 = null) =>
            database.SeedAuditAsync(org2 ?? org, actor, action, entityType, entityId, now.AddMinutes(-200 + minute++), secret, afterData ?? secret, secret);

        // Catalog events, one per shape.
        await Seed(owner.UserId, "customer.created", "customer", customer);
        await Seed(owner.UserId, "property.updated", "property", primary);
        await Seed(owner.UserId, "customer_note.created", "customer_note", note);
        await Seed(null, "service_request.created", "service_request", request);
        await Seed(owner.UserId, "service_request.quoted", "service_request", request);
        await Seed(portalUser, "portal.message_sent", "customer_contact", contact);
        await Seed(portalUser, "quote.approved", "quote", quote);
        await Seed(owner.UserId, "quote.sent", "quote", quote);
        await Seed(null, "work_order.created", "work_order", job);
        await Seed(owner.UserId, "work_order.status_changed", "work_order", job, afterData: "{\"status\":\"completed\",\"email\":\"leak@example.com\"}");
        await Seed(owner.UserId, "visit.dispatched", "visit", visit);
        await Seed(owner.UserId, "visit.completed", "visit", visit);
        await Seed(owner.UserId, "invoice.sent", "invoice", invoice);
        await Seed(owner.UserId, "payment.recorded", "payment", payment);

        // Rows that must never appear: not whitelisted, of another customer, or of another organization.
        await Seed(owner.UserId, "visit.paused", "visit", visit);
        await Seed(owner.UserId, "invoice.draft_saved", "invoice", invoice);
        await Seed(owner.UserId, "quote.draft_saved", "quote", quote);
        await Seed(owner.UserId, "customer.imported", "customer", customer);
        await Seed(owner.UserId, "service_request.status_changed", "service_request", request);
        await Seed(owner.UserId, "service_request.created", "service_request", rivalRequest);
        await Seed(owner.UserId, "customer.updated", "customer", rival);
        await Seed(owner.UserId, "customer.updated", "customer", customer, org2: other);
        await Seed(owner.UserId, "customer.updated", "customer", foreign, org2: other);

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var path = $"/customers/{customer}/activity";

        var response = await host.SendAsync(HttpMethod.Get, path + "?period=all", cookie);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(text)!;
        var items = body["items"]!.AsArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(14, items.Count);
        Assert.Null(body["nextCursor"]);
        Assert.Equal(
            ["customer", "requests", "messages", "quotes", "jobs", "visits", "invoices", "payments"],
            body["availableTypes"]!.AsArray().Select(type => type!.GetValue<string>()).ToArray());
        Assert.Equal(("USD", true), (body["currency"]!.GetValue<string>(), body["timezone"] is not null));
        var occurred = items.Select(item => item!["occurredAt"]!.GetValue<DateTimeOffset>()).ToArray();
        Assert.Equal(occurred.OrderByDescending(at => at).ToArray(), occurred);
        Assert.Equal("payment.recorded", items[0]!["action"]!.GetValue<string>());

        JsonNode Event(string action) => items.Single(item => item!["action"]!.GetValue<string>() == action)!;
        Assert.Equal("Renamed Site", Event("property.updated")["subject"]!["propertyName"]!.GetValue<string>());
        Assert.Equal(("REQ-7", "Plumbing", "request", request), (
            Event("service_request.quoted")["subject"]!["number"]!.GetValue<string>(),
            Event("service_request.quoted")["subject"]!["service"]!.GetValue<string>(),
            Event("service_request.quoted")["link"]!["kind"]!.GetValue<string>(),
            Event("service_request.quoted")["link"]!["id"]!.GetValue<Guid>()));
        Assert.Equal(("Pat Contact", null), (Event("portal.message_sent")["subject"]!["contactName"]!.GetValue<string>(), Event("portal.message_sent")["link"]?.GetValue<string>()));
        Assert.Equal(("Q-4", 1250.50m, "quote"), (
            Event("quote.sent")["subject"]!["number"]!.GetValue<string>(),
            Event("quote.sent")["subject"]!["amount"]!.GetValue<decimal>(),
            Event("quote.sent")["link"]!["kind"]!.GetValue<string>()));
        Assert.Equal(("WO-11", "Fix sink job", "completed"), (
            Event("work_order.status_changed")["subject"]!["number"]!.GetValue<string>(),
            Event("work_order.status_changed")["subject"]!["title"]!.GetValue<string>(),
            Event("work_order.status_changed")["subject"]!["newStatus"]!.GetValue<string>()));
        Assert.Null(Event("work_order.created")["subject"]!["newStatus"]);
        Assert.Equal((job, 2, "WO-11"), (
            Event("visit.dispatched")["link"]!["id"]!.GetValue<Guid>(),
            Event("visit.dispatched")["subject"]!["visitNumber"]!.GetValue<int>(),
            Event("visit.dispatched")["subject"]!["number"]!.GetValue<string>()));
        Assert.Equal(now.AddDays(1), Event("visit.dispatched")["subject"]!["scheduledAt"]!.GetValue<DateTimeOffset>(), Tolerance);
        Assert.Null(Event("visit.completed")["subject"]!["scheduledAt"]);
        Assert.Equal(("INV-21", 800m), (Event("invoice.sent")["subject"]!["number"]!.GetValue<string>(), Event("invoice.sent")["subject"]!["amount"]!.GetValue<decimal>()));
        Assert.Equal((100m, "invoice", invoice), (
            Event("payment.recorded")["subject"]!["amount"]!.GetValue<decimal>(),
            Event("payment.recorded")["link"]!["kind"]!.GetValue<string>(),
            Event("payment.recorded")["link"]!["id"]!.GetValue<Guid>()));
        Assert.Equal(["INV-21", "INV-22"], Event("payment.recorded")["subject"]!["invoiceNumbers"]!.AsArray().Select(number => number!.GetValue<string>()).ToArray());

        // BR-13 origins: staff name, public-link or non-member actor as customer portal, no actor as system.
        Assert.Equal(("staff", "Ola Owner"), (Event("customer.created")["origin"]!["kind"]!.GetValue<string>(), Event("customer.created")["origin"]!["actorName"]!.GetValue<string>()));
        Assert.Equal("customer_portal", Event("service_request.created")["origin"]!["kind"]!.GetValue<string>());
        Assert.Equal("customer_portal", Event("quote.approved")["origin"]!["kind"]!.GetValue<string>());
        Assert.Equal("customer_portal", Event("portal.message_sent")["origin"]!["kind"]!.GetValue<string>());
        Assert.Equal("system", Event("work_order.created")["origin"]!["kind"]!.GetValue<string>());
        Assert.Null(Event("work_order.created")["origin"]!["actorName"]);

        // Redaction: nothing of the audit payload, IP, note or contact data reaches the response.
        foreach (var forbidden in new[] { "SECRET-NOTE", "leak@example.com", "leaky", "10.1.2.3", "beforeData", "afterData", "metadata", "ipAddress", "Secret note text", "ada@example.com" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }

        // Type and period filters.
        var payments = JsonNode.Parse(await (await host.SendAsync(HttpMethod.Get, path + "?type=payments&period=all", cookie)).Content.ReadAsStringAsync())!;
        Assert.Equal(["payment.recorded"], payments["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ToArray());
        Assert.Equal(["customer"], JsonNode.Parse(await (await host.SendAsync(HttpMethod.Get, path + "?type=customer&period=all", cookie)).Content.ReadAsStringAsync())!["items"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()).Distinct().ToArray());

        foreach (var (query, key) in new[] { ("type=unknown", "type"), ("type=Jobs", "type"), ("period=last_year", "period"), ("before=not-a-cursor", "before"), ("before=MTIz", "before") })
        {
            var rejected = await host.SendAsync(HttpMethod.Get, $"{path}?{query}", cookie);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.NotEmpty(CustomerSeed.Error(await CustomerHost.ReadAsync(rejected), key));
        }

        // Cursor paging: 45 recent events (in groups of 3 sharing one instant), 3 at 200 days and 2 at 400 days.
        var paged = await database.SeedCustomerAsync(org, branch.Id, "Paged Customer");

        for (var i = 0; i < 45; i++)
        {
            await database.SeedAuditAsync(org, owner.UserId, "customer.updated", "customer", paged, now.AddDays(-80).AddMinutes(i / 3));
        }

        for (var i = 0; i < 3; i++)
        {
            await database.SeedAuditAsync(org, owner.UserId, "customer.updated", "customer", paged, now.AddDays(-200).AddMinutes(i));
        }

        for (var i = 0; i < 2; i++)
        {
            await database.SeedAuditAsync(org, owner.UserId, "customer.updated", "customer", paged, now.AddDays(-400).AddMinutes(i));
        }

        async Task<string> Walk(string query)
        {
            var sizes = new List<int>();
            var ids = new List<long>();
            var times = new List<DateTimeOffset>();
            string? cursor = null;

            do
            {
                var url = $"/customers/{paged}/activity?{query}" + (cursor is null ? string.Empty : $"&before={cursor}");
                var block = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, url, cookie));
                sizes.Add(block["items"]!.AsArray().Count);
                ids.AddRange(block["items"]!.AsArray().Select(item => long.Parse(item!["id"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)));
                times.AddRange(block["items"]!.AsArray().Select(item => item!["occurredAt"]!.GetValue<DateTimeOffset>()));
                cursor = block["nextCursor"]?.GetValue<string>();
            }
            while (cursor is not null);

            return $"{string.Join(',', sizes)}|{ids.Distinct().Count()}|{times.SequenceEqual(times.OrderByDescending(at => at))}";
        }

        Assert.Equal("20,20,5|45|True", await Walk("type=all"));
        Assert.Equal("20,20,8|48|True", await Walk("period=last_12_months"));
        Assert.Equal("20,20,10|50|True", await Walk("period=all"));
        Assert.Equal("0|0|True", await Walk("period=last_30_days"));
    }

    // AC-10, AC-11, FR-09: the role matrix of every new read (Technician 403, Accounting jobs 403, unauthenticated 401,
    // read roles 403 on customer options) and the cross-organization 404 of the customer-scoped read path.
    [Theory]
    [InlineData("owner", 200, 200, 200, true)]
    [InlineData("operations_manager", 200, 200, 200, false)]
    [InlineData("dispatcher", 200, 200, 200, true)]
    [InlineData("accounting", 200, 403, 200, false)]
    [InlineData("viewer", 200, 200, 200, false)]
    [InlineData("technician", 403, 403, 403, false)]
    [InlineData("none", 401, 401, 401, false)]
    public async Task RoleMatrix_AppliesModuleReadPoliciesToListsActivityAndCustomerOptions(
        string role, int requestsStatus, int jobsStatus, int invoicesStatus, bool canUseOptions)
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Matrix Customer");
        await using var host = CustomerHost.Create(database);
        string? cookie = null;

        if (role != "none")
        {
            cookie = await host.CookieAsync(database, org, role switch
            {
                "owner" => CompanySettingsDatabaseFixture.OwnerRoleId,
                "operations_manager" => CompanySettingsDatabaseFixture.OperationsManagerRoleId,
                "dispatcher" => CompanySettingsDatabaseFixture.DispatcherRoleId,
                "accounting" => CompanySettingsDatabaseFixture.AccountingRoleId,
                "viewer" => CompanySettingsDatabaseFixture.ViewerRoleId,
                _ => CompanySettingsDatabaseFixture.TechnicianRoleId,
            });
        }

        async Task<int> Status(string path) => (int)(await host.SendAsync(HttpMethod.Get, path, cookie)).StatusCode;

        Assert.Equal(requestsStatus, await Status($"/customers/{customer}/requests"));
        Assert.Equal(requestsStatus, await Status($"/customers/{customer}/quotes"));
        Assert.Equal(jobsStatus, await Status($"/customers/{customer}/jobs"));
        Assert.Equal(invoicesStatus, await Status($"/customers/{customer}/invoices"));
        Assert.Equal(requestsStatus, await Status($"/customers/{customer}/activity"));
        Assert.Equal(
            role == "none" ? 401 : canUseOptions ? 200 : 403,
            await Status($"/service-requests/customer-options?customerId={customer}"));
    }

    // AC-10: an organization A user reading organization B's customer gets 404 on every customer-scoped read, A's own
    // customer shows no B record, and customer options for B's customer stay empty.
    [Fact]
    public async Task CrossOrganization_ReturnsNotFoundAndNeverMixesRecords()
    {
        var orgA = await database.SeedOrganizationAsync();
        var orgB = await database.SeedOrganizationAsync();
        var branchA = await database.SeedBranchAsync(orgA, isMain: true);
        var branchB = await database.SeedBranchAsync(orgB, isMain: true);
        var memberB = await database.SeedMemberAsync(orgB, CompanySettingsDatabaseFixture.OwnerRoleId, "Bea", "Owner");
        var customerA = await database.SeedCustomerAsync(orgA, branchA.Id, "A Customer");
        var customerB = await database.SeedCustomerAsync(orgB, branchB.Id, "B Customer");
        var now = DateTimeOffset.UtcNow;
        var requestB = await database.SeedRequestAsync(orgB, customerB, 1, "B request", "new", now);
        var propertyB = await database.PrimaryPropertyAsync(customerB);
        var (_, versionB) = await database.SeedQuoteAsync(orgB, memberB.UserId, customerB, requestB, 1, "sent", "B", 10m, now, now);
        var jobB = await database.SeedJobAsync(orgB, memberB.UserId, branchB.Id, customerB, propertyB, versionB, 1, "scheduled", "B job", now);
        var invoiceB = await database.SeedJobInvoiceAsync(orgB, memberB.UserId, branchB.Id, customerB, jobB, 1, "sent", 10m, 0m, DateOnly.FromDateTime(DateTime.UtcNow), null, now);
        await database.SeedAuditAsync(orgB, memberB.UserId, "invoice.sent", "invoice", invoiceB, now);

        await using var host = CustomerHost.Create(database);
        var owner = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.OwnerRoleId);

        foreach (var suffix in new[] { "requests", "quotes", "jobs", "invoices", "activity", "detail", "recent-work", "upcoming-appointments", "properties", "notes" })
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/customers/{customerB}/{suffix}", owner);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{suffix}: {response.StatusCode}");
            Assert.DoesNotContain("B request", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        foreach (var suffix in new[] { "requests", "quotes", "jobs", "invoices", "activity" })
        {
            var own = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customerA}/{suffix}?period=all", owner));
            Assert.Empty(own["items"]!.AsArray());
        }

        var options = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/service-requests/customer-options?customerId={customerB}", owner));
        Assert.Empty(options["items"]!.AsArray());
    }

    // AC-13, FR-06, BR-09: customer-options with customerId returns the one active in-scope customer, nothing for an
    // archived, out-of-scope or foreign customer, and rejects combining it with search or a malformed id.
    [Fact]
    public async Task CustomerOptions_WithCustomerId_ReturnsOnlyTheActiveCustomerInScope()
    {
        var org = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee");
        var active = await database.SeedCustomerAsync(org, branchX.Id, "Active Customer");
        var archived = await database.SeedCustomerAsync(org, branchX.Id, "Archived Customer", active: false);
        var outOfScope = await database.SeedCustomerAsync(org, branchY.Id, "Y Customer");
        var foreignOrg = await database.SeedOrganizationAsync();
        var foreignBranch = await database.SeedBranchAsync(foreignOrg, isMain: true);
        var foreign = await database.SeedCustomerAsync(foreignOrg, foreignBranch.Id, "Foreign Customer");

        await using var host = CustomerHost.Create(database);
        var scoped = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, branchX.Id);
        var owner = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);

        async Task<JsonArray> Options(string query, string cookie)
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/service-requests/customer-options?{query}", cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return (await CustomerHost.ReadAsync(response))["items"]!.AsArray();
        }

        var one = await Options($"customerId={active}", scoped);
        Assert.Equal([active], one.Select(item => item!["id"]!.GetValue<Guid>()).ToArray());
        Assert.Equal("Active Customer", one[0]!["displayName"]!.GetValue<string>());
        Assert.NotEmpty(one[0]!["contacts"]!.AsArray());
        Assert.NotEmpty(one[0]!["properties"]!.AsArray());
        Assert.Empty(await Options($"customerId={archived}", owner));
        Assert.Empty(await Options($"customerId={outOfScope}", scoped));
        Assert.Single(await Options($"customerId={outOfScope}", owner));
        Assert.Empty(await Options($"customerId={foreign}", owner));
        Assert.Empty(await Options($"customerId={Guid.NewGuid()}", owner));
        Assert.True((await Options("search=Customer", owner)).Count >= 2);

        foreach (var query in new[] { $"customerId={active}&search=Active", "customerId=not-a-guid", $"customerId={Guid.Empty}" })
        {
            var rejected = await host.SendAsync(HttpMethod.Get, $"/service-requests/customer-options?{query}", owner);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.NotEmpty(CustomerSeed.Error(await CustomerHost.ReadAsync(rejected), "customerId"));
        }
    }
}
