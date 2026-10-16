using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Reports;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.BusinessOverview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.Team;
using static FieldOps.IntegrationTests.Reports.ReportsSeed;

namespace FieldOps.IntegrationTests.Reports;

/// <summary>The reports workspace API (reports-workspace AC-01 to AC-18). Seeds the business-overview history so every report reconciles with it.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class ReportsTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;

    private const short Operations = CompanySettingsDatabaseFixture.OperationsManagerRoleId;

    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;

    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;

    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;

    // AC-01, AC-02: the role x report matrix on options, data, export and history; an unknown key; denials write no audit row.
    [Fact]
    public async Task Matrix_RoleByReport_ExposesOnlyTheCatalogOfTheRole_AndDenialsWriteNoAudit()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var operational = new[] { "quote_conversion", "jobs_on_time", "technician_utilization" };
        var financial = new[] { "job_profitability", "invoice_aging" };
        var cases = new (short Role, string[] Visible)[]
        {
            (Owner, Keys), (Operations, Keys), (Dispatcher, operational), (Accounting, financial), (Viewer, []), (Technician, []),
        };
        var before = await database.ExportCountAsync(seed.World.Org);
        var exports = 0;

        foreach (var (role, visible) in cases)
        {
            var (cookie, _) = await database.SignInAsync(host, seed.World.Org, role);
            var options = await host.GetAsync("/reports/options", cookie);
            var history = await host.GetAsync("/reports/exports", cookie);

            if (visible.Length == 0)
            {
                Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (options.StatusCode, history.StatusCode));
            }
            else
            {
                var body = await BillingSeed.ReadAsync(options);
                Assert.Equal("no-store", options.Headers.CacheControl?.ToString());
                Assert.Equal(Keys.Where(visible.Contains).ToArray(), body["reports"]!.AsArray().Select(report => Text(report!["key"])).ToArray());
                Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            }

            foreach (var key in Keys)
            {
                var expected = visible.Contains(key) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;

                Assert.Equal(expected, (await host.GetAsync(Path(key), cookie)).StatusCode);
                Assert.Equal(expected, (await host.GetAsync(ExportPath(key, "csv"), cookie)).StatusCode);
                exports += expected == HttpStatusCode.OK ? 1 : 0;
            }

            var unknown = await host.GetAsync(Path("unknown_report"), cookie);
            Assert.Equal(visible.Length == 0 ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, unknown.StatusCode);
        }

        var owner = await BillingSeed.ReadAsync(await host.GetAsync("/reports/options", (await database.SignInAsync(host, seed.World.Org, Owner)).Cookie));
        Assert.Equal(
            ["Billing & payments", "Billing & payments", "Sales & quotes", "Operations", "Operations"],
            owner["reports"]!.AsArray().Select(report => Text(report!["group"])).ToArray());
        Assert.Equal(("America/Chicago", "USD", 40.0m), (Text(owner["timezone"]), Text(owner["currency"]), Dec(owner["marginTarget"])));
        Assert.Equal(before + exports, await database.ExportCountAsync(seed.World.Org));
    }

    // AC-03: tenant isolation, an inactive branch, branch-limited members and identical 404s.
    [Fact]
    public async Task Isolation_ForeignOutOfScopeAndInactiveBranches_AreIdenticalNotFoundOrScoped()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var world = seed.World;
        var foreignTech = await database.SeedTechAsync(seed.Foreign.Org, seed.Foreign.BranchA, "Fay", "Foreign");
        var branchBTech = await database.SeedTechAsync(world.Org, world.BranchB, "Bea", "Bravo");
        var (owner, _) = await database.SignInAsync(host, world.Org, Owner);
        var (dispatcher, _) = await database.SignInAsync(host, world.Org, Dispatcher, world.BranchA);
        var (accounting, _) = await database.SignInAsync(host, world.Org, Accounting, world.BranchA);

        // The owner has every branch, the inactive one included and labelled; a client organization id is ignored.
        var options = await BillingSeed.ReadAsync(await host.GetAsync("/reports/options", owner));
        var branches = options["branches"]!.AsArray();
        Assert.Equal(3, branches.Count);
        Assert.Contains(branches, branch => Text(branch!["name"]) == "Gamma, Inc" && !branch["isActive"]!.GetValue<bool>());
        var aging = await host.OkAsync(Path("invoice_aging", $"&organizationId={seed.Foreign.Org}"), owner);
        Assert.Equal(650m, Value(aging, "outstanding"));
        Assert.Contains(Items(aging), row => Text(row["branch"]) == "Gamma, Inc (Inactive)");
        var inactive = await host.OkAsync(Path("job_profitability", $"&branchId={seed.InactiveBranch}"), owner);
        Assert.Equal(1m, Value(inactive, "completedJobs"));

        // Foreign branch, out-of-scope branch and unknown branch are the same 404; the same for technicians.
        foreach (var key in Keys)
        {
            var unknown = await BodyWithoutTraceAsync(await host.GetAsync(Path(key, $"&branchId={Guid.NewGuid()}"), owner));
            Assert.StartsWith("404 ", unknown, StringComparison.Ordinal);
            Assert.Equal(unknown, await BodyWithoutTraceAsync(await host.GetAsync(Path(key, $"&branchId={seed.Foreign.BranchA}"), owner)));
            Assert.Equal(unknown, await BodyWithoutTraceAsync(await host.GetAsync(ExportPath(key, "csv", $"&branchId={seed.Foreign.BranchA}"), owner)));
        }

        var unknownTech = await BodyWithoutTraceAsync(await host.GetAsync(Path("technician_utilization", $"&technicianId={Guid.NewGuid()}"), owner));
        Assert.Contains("technician_not_found", unknownTech, StringComparison.Ordinal);
        Assert.Equal(unknownTech, await BodyWithoutTraceAsync(await host.GetAsync(Path("technician_utilization", $"&technicianId={foreignTech}"), owner)));
        Assert.Equal(unknownTech, await BodyWithoutTraceAsync(await host.GetAsync(Path("jobs_on_time", $"&technicianId={foreignTech}"), owner)));

        // A branch-limited dispatcher: All is branch A only, branch B and its technician are the same 404 as a foreign one.
        var all = await host.OkAsync(Path("quote_conversion"), dispatcher);
        var one = await host.OkAsync(Path("quote_conversion", $"&branchId={world.BranchA}"), dispatcher);
        Assert.Equal((Value(one, "quotesSent"), Value(one, "quotesAccepted")), (Value(all, "quotesSent"), Value(all, "quotesAccepted")));
        Assert.True(Value(all, "quotesAccepted") < 7m);
        Assert.Equal(unknownTech, await BodyWithoutTraceAsync(await host.GetAsync(Path("technician_utilization", $"&technicianId={branchBTech}"), dispatcher)));
        Assert.StartsWith("404 ", await BodyWithoutTraceAsync(await host.GetAsync(Path("jobs_on_time", $"&branchId={world.BranchB}"), dispatcher)), StringComparison.Ordinal);
        var team = await BillingSeed.ReadAsync(await host.GetAsync("/reports/options", dispatcher));
        Assert.Equal(["Alpha Branch"], team["branches"]!.AsArray().Select(branch => Text(branch!["name"])).ToArray());
        Assert.DoesNotContain(team["technicians"]!.AsArray(), technician => technician!["branchId"]!.GetValue<Guid>() == world.BranchB);

        // A branch-limited accountant sees only branch A money in aging, profitability and CSV.
        var limitedAging = await host.OkAsync(Path("invoice_aging"), accounting);
        Assert.Equal((300m, 100m), (Value(limitedAging, "outstanding"), Value(limitedAging, "overdue")));
        var limitedProfit = await host.OkAsync(Path("job_profitability"), accounting);
        Assert.Equal(280m, Value(limitedProfit, "revenue"));
        Assert.All(Items(limitedProfit), row => Assert.Equal("Alpha Branch", Text(row["branch"])));
        var csv = await (await host.GetAsync(ExportPath("invoice_aging", "csv"), accounting)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Gamma", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo Branch", csv, StringComparison.Ordinal);
    }

    // AC-04, AC-06, AC-07: job profitability reconciles with Business Overview; Not invoiced, filters, chart, links by role.
    [Fact]
    public async Task JobProfitability_ByCompletion_ReconcilesWithBusinessOverview()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var (owner, _) = await database.SignInAsync(host, seed.World.Org, Owner);
        var (accounting, _) = await database.SignInAsync(host, seed.World.Org, Accounting);
        var overview = await host.OkAsync(BusinessOverviewSeed.Overview(), owner);

        var report = await host.OkAsync(Path("job_profitability"), owner);
        Assert.Equal(("job_profitability", HubSeed.Day(-29), HubSeed.Day(0), "day"), (Text(report["reportKey"]), Text(report["from"]), Text(report["to"]), Text(report["granularity"])));
        Assert.Equal((decimal)overview["kpis"]!["jobsCompleted"]!["current"]!.GetValue<int>(), Value(report, "completedJobs"));
        Assert.Equal((6m, 2m), (Value(report, "completedJobs"), Value(report, "notInvoiced")));
        Assert.Equal((630m, 305m, 325m, 51.6m), (Value(report, "revenue"), Value(report, "estimatedCost"), Value(report, "estimatedGrossProfit"), Value(report, "estimatedGrossMargin")));
        Assert.Equal((100m, 0m, 100m, 1m), (Previous(report, "revenue"), Previous(report, "estimatedCost"), Previous(report, "estimatedGrossProfit"), Previous(report, "completedJobs")));

        // Totals cover the whole set, the not-invoiced rows show nothing and add nothing, and sit last for the money keys.
        Assert.Equal((630m, 305m, 325m, 51.6m), (Dec(report["totals"]!["revenue"]), Dec(report["totals"]!["estimatedCost"]), Dec(report["totals"]!["grossProfit"]), Dec(report["totals"]!["margin"])));
        var paged = await host.OkAsync(Path("job_profitability", "&pageSize=10&sort=revenue&direction=asc"), owner);
        Assert.Equal(6, paged["total"]!.GetValue<int>());
        var ascending = Items(paged);
        Assert.Equal(new[] { "Not invoiced", "Not invoiced" }, ascending.TakeLast(2).Select(row => Text(row["invoicing"])).ToArray());
        Assert.All(ascending.TakeLast(2), row => Assert.Null(row["revenue"]));
        Assert.Equal(new[] { 50m, 80m, 200m, 300m }, ascending.Take(4).Select(row => Dec(row["revenue"])).ToArray());
        var descending = Items(await host.OkAsync(Path("job_profitability", "&sort=revenue&direction=desc"), owner));
        Assert.Equal(new[] { 300m, 200m, 80m, 50m }, descending.Take(4).Select(row => Dec(row["revenue"])).ToArray());
        Assert.Null(descending[^1]["revenue"]);
        Assert.True(ascending.First(row => row["revenue"] is not null && Dec(row["revenue"]) == 300m)["belowTarget"]!.GetValue<bool>());

        // Chart: invoiced categories by revenue; filters by status and category.
        var chart = report["chart"]!;
        Assert.Equal("category_bars", Text(chart["kind"]));
        Assert.Equal(["Plumbing", "=Drains"], chart["points"]!.AsArray().Select(point => Text(point!["label"])).ToArray());
        Assert.Equal(550m, Dec(chart["points"]![0]!["values"]!["revenue"]));
        var completed = Value(await host.OkAsync(Path("job_profitability", "&status=completed"), owner), "completedJobs");
        var approved = Value(await host.OkAsync(Path("job_profitability", "&status=approved_for_billing"), owner), "completedJobs");
        Assert.Equal(6m, completed + approved);
        var drains = await host.OkAsync(Path("job_profitability", $"&categoryId={seed.Drains}"), owner);
        Assert.Equal((1m, 80m), (Value(drains, "completedJobs"), Value(drains, "revenue")));

        // Links: the owner opens jobs, the accountant (no Jobs module) only invoices and customers.
        var ownerRow = Items(report).First(row => row["invoiced"]!.GetValue<bool>());
        Assert.NotNull(ownerRow["links"]!["job"]);
        Assert.NotNull(ownerRow["links"]!["invoice"]);
        var accountantRow = Items(await host.OkAsync(Path("job_profitability"), accounting)).First(row => row["invoiced"]!.GetValue<bool>());
        Assert.Null(accountantRow["links"]!["job"]);
        Assert.NotNull(accountantRow["links"]!["invoice"]);
        Assert.NotNull(accountantRow["links"]!["customer"]);
    }

    // AC-08: invoice aging as of today reconciles with the hub and ignores the period.
    [Fact]
    public async Task InvoiceAging_AsOfToday_ReconcilesWithTheHubAndIgnoresThePeriod()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var (owner, _) = await database.SignInAsync(host, seed.World.Org, Owner);
        var overview = await host.OkAsync(BusinessOverviewSeed.Overview(), owner);
        var cash = overview["cashFlow"]!;

        var report = await host.OkAsync(Path("invoice_aging"), owner);
        Assert.Equal((HubSeed.Day(0), true, true, true), (Text(report["asOf"]), report["from"] is null, report["previousFrom"] is null, report["granularity"] is null));
        Assert.Equal((Dec(cash["outstanding"]), Dec(cash["overdue"]), 3m), (Value(report, "outstanding"), Value(report, "overdue"), Value(report, "overdueInvoices")));
        Assert.Equal(
            (Dec(cash["aging"]!["current"]), Dec(cash["aging"]!["days1To30"]), Dec(cash["aging"]!["days31To60"]), Dec(cash["aging"]!["days60Plus"])),
            (Value(report, "current"), Value(report, "days1To30"), Value(report, "days31To60"), Value(report, "days60Plus")));
        Assert.All(report["summary"]!.AsArray(), metric => Assert.Null(metric!["previous"]));
        Assert.Equal((650m, 4), (Dec(report["totals"]!["balanceDue"]), report["total"]!.GetValue<int>()));
        Assert.Equal(["overdue", "overdue", "overdue", "sent"], Items(report).Select(row => Text(row["status"])).ToArray());
        Assert.Equal(new int?[] { 35, 29, 10, null }, Items(report).Select(row => row["daysOverdue"]?.GetValue<int>()).ToArray());

        // Another period changes nothing; the filters narrow rows, summary and totals.
        var other = await host.OkAsync($"/reports/invoice_aging?from={HubSeed.Day(-5)}&to={HubSeed.Day(-1)}", owner);
        Assert.Equal(Value(report, "outstanding"), Value(other, "outstanding"));
        var bucket = await host.OkAsync(Path("invoice_aging", "&bucket=1_30"), owner);
        Assert.Equal((2, 350m, 350m), (bucket["total"]!.GetValue<int>(), Value(bucket, "outstanding"), Dec(bucket["totals"]!["balanceDue"])));
        var current = await host.OkAsync(Path("invoice_aging", "&status=sent"), owner);
        Assert.Equal((1, 200m), (current["total"]!.GetValue<int>(), Value(current, "outstanding")));
        var one = await host.OkAsync(Path("invoice_aging", $"&branchId={seed.World.BranchA}"), owner);
        Assert.Equal((300m, 100m), (Value(one, "outstanding"), Value(one, "overdue")));
    }

    // AC-09: quote conversion is event based, reconciles with Business Overview, and does not sum other currencies.
    [Fact]
    public async Task QuoteConversion_SentAndAcceptedEvents_ReconcileAndExcludeOtherCurrencies()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var (owner, _) = await database.SignInAsync(host, seed.World.Org, Owner);
        var overview = await host.OkAsync(BusinessOverviewSeed.Overview(), owner);
        var conversion = overview["kpis"]!["quoteConversion"]!;

        var report = await host.OkAsync(Path("quote_conversion"), owner);
        Assert.Equal(
            ((decimal)conversion["sent"]!.GetValue<int>(), (decimal)conversion["accepted"]!.GetValue<int>(), (decimal)conversion["percent"]!.GetValue<int>()),
            (Value(report, "quotesSent"), Value(report, "quotesAccepted"), Value(report, "conversion")));
        Assert.True(Value(report, "conversion") > 100m);
        Assert.Equal(1m, Previous(report, "quotesSent"));
        Assert.Equal((7, 7m), (report["total"]!.GetValue<int>(), Dec(report["totals"]!["acceptedAt"])));
        Assert.Null(report["notes"]!["otherCurrencyCount"]);

        var sentOnly = await host.OkAsync(Path("quote_conversion", "&event=sent"), owner);
        Assert.Equal(3, sentOnly["total"]!.GetValue<int>());
        Assert.All(Items(sentOnly), row => Assert.True(row["sentInPeriod"]!.GetValue<bool>()));
        Assert.Equal(7, (await host.OkAsync(Path("quote_conversion", "&event=accepted"), owner))["total"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync(Path("quote_conversion", "&status=approved"), owner)).StatusCode);

        // Branch scope matches Business Overview BR-06, and a quote in another currency is counted but not summed.
        var branch = await host.OkAsync(BusinessOverviewSeed.Overview(branch: seed.World.BranchA.ToString()), owner);
        var scoped = await host.OkAsync(Path("quote_conversion", $"&branchId={seed.World.BranchA}"), owner);
        Assert.Equal(
            ((decimal)branch["kpis"]!["quoteConversion"]!["sent"]!.GetValue<int>(), (decimal)branch["kpis"]!["quoteConversion"]!["accepted"]!.GetValue<int>()),
            (Value(scoped, "quotesSent"), Value(scoped, "quotesAccepted")));

        var sentValue = Value(report, "sentValue");
        await database.ExecuteAsync(
            "UPDATE quote_versions SET currency = 'EUR' WHERE id = (SELECT quote_version_id FROM work_orders WHERE id = (SELECT work_order_id FROM invoices WHERE organization_id = @o AND total = 300 AND status = 'sent'))",
            ("o", seed.World.Org));
        var euro = await host.OkAsync(Path("quote_conversion"), owner);
        Assert.Equal(1, euro["notes"]!["otherCurrencyCount"]!.GetValue<int>());
        Assert.True(Value(euro, "sentValue") < sentValue);
        Assert.Equal((Value(report, "quotesSent"), Value(report, "quotesAccepted")), (Value(euro, "quotesSent"), Value(euro, "quotesAccepted")));
    }

    // AC-10: scheduled visits, on-time arrival equal to Business Overview and the separate Jobs completed metric.
    [Fact]
    public async Task JobsOnTime_VisitsInThePeriod_ReconcileWithBusinessOverview()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var (owner, _) = await database.SignInAsync(host, seed.World.Org, Owner);
        var overview = await host.OkAsync(BusinessOverviewSeed.Overview(), owner);

        var report = await host.OkAsync(Path("jobs_on_time"), owner);
        Assert.Equal((decimal)overview["branchTotals"]!["onTimePercent"]!.GetValue<int>(), Value(report, "onTimeArrival"));
        Assert.Equal((decimal)overview["kpis"]!["jobsCompleted"]!["current"]!.GetValue<int>(), Value(report, "jobsCompleted"));
        var items = Items(report);
        Assert.Equal((1, 1), (items.Count(row => Text(row["onTime"]) == "on_time"), items.Count(row => Text(row["onTime"]) == "late")));
        Assert.Equal((decimal)items.Count, Value(report, "scheduledVisits"));
        Assert.Equal(items.Count, (int)Dec(report["totals"]!["rows"]));
        Assert.Equal("stacked_series", Text(report["chart"]!["kind"]));

        // Filters: on-time result and status group narrow rows and the summary; Jobs completed ignores them.
        var late = await host.OkAsync(Path("jobs_on_time", "&onTime=late"), owner);
        Assert.Equal((1, 0m, Value(report, "jobsCompleted")), (late["total"]!.GetValue<int>(), Value(late, "onTimeArrival")!.Value, Value(late, "jobsCompleted")));
        var completed = await host.OkAsync(Path("jobs_on_time", "&status=completed"), owner);
        Assert.All(Items(completed), row => Assert.Equal("completed", Text(row["status"])));
        Assert.Equal(Value(completed, "scheduledVisits"), Value(completed, "completedVisits"));
        var scheduled = await host.OkAsync(Path("jobs_on_time", "&status=scheduled"), owner);
        Assert.Equal(0, scheduled["total"]!.GetValue<int>());
        var drains = await host.OkAsync(Path("jobs_on_time", $"&categoryId={seed.Drains}"), owner);
        Assert.Equal(1m, Value(drains, "jobsCompleted"));
    }

    // AC-11: technician utilization reconciles with Business Overview BR-17; inactive technicians are excluded.
    [Fact]
    public async Task TechnicianUtilization_CapacityAndDelivery_ReconcileWithBusinessOverview()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var (owner, _) = await database.SignInAsync(host, seed.World.Org, Owner);
        var overview = await host.OkAsync(BusinessOverviewSeed.Overview(), owner);
        var team = overview["team"]!;

        var report = await host.OkAsync(Path("technician_utilization"), owner);
        Assert.Equal(
            ((decimal)team["technicians"]!.GetValue<int>(), (decimal)team["utilizationPercent"]!.GetValue<int>(), 3m),
            (Value(report, "technicians"), Value(report, "utilization"), Value(report, "overtimeHours")));
        Assert.Equal((240m, 15m), (Value(report, "availableHours"), Value(report, "scheduledHours")));
        Assert.Null(Previous(report, "technicians"));
        Assert.Equal(1m, Previous(report, "overtimeHours"));
        var row = Assert.Single(Items(report));
        Assert.Equal(("Tina One", 14400, 900, 6, 180), (Text(row["technician"]), row["availableMinutes"]!.GetValue<int>(), row["scheduledMinutes"]!.GetValue<int>(), row["utilization"]!.GetValue<int>(), row["overtimeMinutes"]!.GetValue<int>()));
        Assert.Equal((14400, 900, 180), (report["totals"]!["availableMinutes"]!.GetValue<int>(), report["totals"]!["scheduledMinutes"]!.GetValue<int>(), report["totals"]!["overtimeMinutes"]!.GetValue<int>()));
        Assert.Equal("technician_bars", Text(report["chart"]!["kind"]));
        Assert.Equal(6m, Dec(report["chart"]!["points"]![0]!["values"]!["utilization"]));
        Assert.NotNull(row["links"]!["technician"]);

        // The inactive technician is in scope but not a row; the technician and branch filters narrow the rows.
        var options = await BillingSeed.ReadAsync(await host.GetAsync("/reports/options", owner));
        var ivan = options["technicians"]!.AsArray().Single(technician => Text(technician!["name"]) == "Ivan Out")!;
        Assert.False(ivan["isActive"]!.GetValue<bool>());
        var none = await host.OkAsync(Path("technician_utilization", $"&technicianId={ivan["id"]!.GetValue<Guid>()}"), owner);
        Assert.Equal((0, 0m), (none["total"]!.GetValue<int>(), Value(none, "technicians")!.Value));
        Assert.Null(Value(none, "utilization"));
        var branchB = await host.OkAsync(Path("technician_utilization", $"&branchId={seed.World.BranchB}"), owner);
        Assert.Equal(0, branchB["total"]!.GetValue<int>());
    }

    // AC-13, AC-14: allowlisted sorts in both directions, paging with the real total and unchanged totals, validation errors.
    [Fact]
    public async Task SortPagingAndValidation_PerReport_FollowTheAllowlistsAndErrors()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var (owner, _) = await database.SignInAsync(host, seed.World.Org, Owner);
        var sorts = new Dictionary<string, string[]>
        {
            ["job_profitability"] = ["completedAt", "job", "customer", "category", "technician", "revenue", "estimatedCost", "grossProfit", "margin", "branch"],
            ["invoice_aging"] = ["daysOverdue", "dueDate", "issueDate", "invoice", "customer", "total", "balanceDue", "branch"],
            ["quote_conversion"] = ["firstSentAt", "acceptedAt", "quote", "customer", "total", "status", "branch"],
            ["jobs_on_time"] = ["scheduledStart", "job", "customer", "technician", "status", "onTime", "variance", "branch"],
            ["technician_utilization"] = ["utilization", "technician", "branch", "availableMinutes", "scheduledMinutes", "overtimeMinutes", "completedVisits", "onTime"],
        };

        foreach (var (key, columns) in sorts)
        {
            var baseline = await host.OkAsync(Path(key), owner);

            foreach (var column in columns)
            {
                var asc = Items(await host.OkAsync(Path(key, $"&sort={column}&direction=asc&pageSize=50"), owner));
                var desc = Items(await host.OkAsync(Path(key, $"&sort={column}&direction=desc&pageSize=50"), owner));
                Assert.Equal(baseline["total"]!.GetValue<int>(), asc.Count);
                Assert.Equal(asc.Select(row => row["id"]!.GetValue<Guid>()).Order().ToArray(), desc.Select(row => row["id"]!.GetValue<Guid>()).Order().ToArray());
            }

            // Pages slice the same set; a page past the last is empty with the real total and the same totals.
            var total = baseline["total"]!.GetValue<int>();
            var pageTwo = await host.OkAsync(Path(key, "&page=2&pageSize=10"), owner);
            Assert.Equal((total, 2, 10, Math.Max(0, Math.Min(10, total - 10))), (pageTwo["total"]!.GetValue<int>(), pageTwo["page"]!.GetValue<int>(), pageTwo["pageSize"]!.GetValue<int>(), pageTwo["items"]!.AsArray().Count));
            var past = await host.OkAsync(Path(key, "&page=9&pageSize=10"), owner);
            Assert.Equal((total, 0), (past["total"]!.GetValue<int>(), past["items"]!.AsArray().Count));
            Assert.True(JsonNode.DeepEquals(baseline["totals"], past["totals"]));
            Assert.True(JsonNode.DeepEquals(baseline["summary"], past["summary"]));

            // Ties and missing values are stable: the same request twice returns the same order.
            var first = Items(await host.OkAsync(Path(key, $"&sort={columns[^1]}&direction=desc"), owner));
            var second = Items(await host.OkAsync(Path(key, $"&sort={columns[^1]}&direction=desc"), owner));
            Assert.Equal(first.Select(row => row["id"]!.GetValue<Guid>()), second.Select(row => row["id"]!.GetValue<Guid>()));

            foreach (var (query, errorKey) in new[]
            {
                ("&sort=bogus", "sort"), ("&direction=sideways", "direction"), ("&page=0", "page"), ("&page=x", "page"), ("&pageSize=30", "pageSize"),
                ("&status=bogus", "status"), ("&bucket=bogus", "bucket"), ("&event=bogus", "event"), ("&onTime=bogus", "onTime"),
                ($"&categoryId={Guid.NewGuid()}", "categoryId"), ("&technicianId=nope", "technicianId"), ("&branchId=nope", "branchId"),
            })
            {
                await BillingSeed.ProblemAsync(await host.GetAsync(Path(key, query), owner), HttpStatusCode.BadRequest, errorKey: errorKey);
            }

            foreach (var (path, errorKey) in new[]
            {
                ($"/reports/{key}?from=2026-13-01&to={HubSeed.Day(0)}", "from"),
                ($"/reports/{key}?from={HubSeed.Day(-5)}&to=tomorrow", "to"),
                ($"/reports/{key}?from={HubSeed.Day(-1)}&to={HubSeed.Day(-2)}", "to"),
                ($"/reports/{key}?from={HubSeed.Day(0)}&to={HubSeed.Day(1)}", "to"),
                ($"/reports/{key}?from={HubSeed.Day(-366)}&to={HubSeed.Day(0)}", "to"),
                ($"/reports/{key}", "from"),
                (ExportPath(key, "xls"), "format"),
                (ExportPath(key, "csv", "&compare=maybe"), "compare"),
            })
            {
                await BillingSeed.ProblemAsync(await host.GetAsync(path, owner), HttpStatusCode.BadRequest, errorKey: errorKey);
            }

            Assert.Equal(HttpStatusCode.OK, (await host.GetAsync($"/reports/{key}?from={HubSeed.Day(-365)}&to={HubSeed.Day(0)}", owner)).StatusCode);
        }

        await BillingSeed.ProblemAsync(await host.GetAsync(Path("job_profitability", $"&categoryId={seed.Foreign.Category}"), owner), HttpStatusCode.BadRequest, errorKey: "categoryId");
    }

    // AC-15 to AC-17: CSV and PDF content, limits, escaping and the export.generated audit, including a failing insert.
    [Fact]
    public async Task Exports_CsvAndPdf_AreAuditedOnlyAfterSuccess()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database, new ReportExportLimits(Csv: 6, Pdf: 2));
        var org = seed.World.Org;
        var (owner, _) = await database.SignInAsync(host, org, Owner);
        await database.ExecuteAsync("DELETE FROM audit_logs WHERE organization_id = @o", ("o", org));

        // CSV: name, headers, the two blocks, the totals row, the "=Drains" formula neutralized and no contact data.
        var response = await host.GetAsync(ExportPath("job_profitability", "csv", "&sort=revenue&direction=desc&compare=false"), owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal($"job-profitability-{HubSeed.Day(-29)}-to-{HubSeed.Day(0)}.csv", response.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        var text = await response.Content.ReadAsStringAsync();
        var rows = BusinessOverviewSeed.ParseCsv(text);
        Assert.Equal(["Section", "Job", "Completed", "Customer", "Service", "Technician", "Revenue", "Est. cost", "Est. gross profit", "Est. margin", "Branch", "Invoicing", "Currency"], rows[0]);
        Assert.Equal(["Rows", "Totals"], new[] { rows[1][0], rows[7][0] });
        Assert.Equal("300.00", rows[1][6]);
        Assert.Equal(["Totals", "", "", "", "", "", "630.00", "305.00", "325.00", "51.6", "", "", "USD"], rows[7]);
        Assert.Contains(rows, row => row.Length > 4 && row[4] == "'=Drains");
        Assert.Equal([""], rows[8]);
        Assert.Equal(["Section", "Metric", "Current", "Previous", "Unit"], rows[9]);
        Assert.Contains(rows, row => row[0] == "Summary" && row[1] == "Revenue" && row[2] == "630.00" && row[3] == "" && row[4] == "USD");
        Assert.DoesNotContain("@example.com", text, StringComparison.Ordinal);
        Assert.Equal(1, await database.ExportCountAsync(org));

        // With comparison on the Previous column is filled; aging uses the as-of file name.
        var compared = BusinessOverviewSeed.ParseCsv(await (await host.GetAsync(ExportPath("job_profitability", "csv"), owner)).Content.ReadAsStringAsync());
        Assert.Contains(compared, row => row[0] == "Summary" && row[1] == "Revenue" && row[3] == "100.00");
        var aging = await host.GetAsync(ExportPath("invoice_aging", "csv", "&compare=false"), owner);
        Assert.Equal($"invoice-aging-{HubSeed.Day(0)}.csv", aging.Content.Headers.ContentDisposition!.FileName);

        // PDF: an attachment with the right name and a real document.
        var pdf = await host.GetAsync(ExportPath("technician_utilization", "pdf"), owner);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.ToString());
        Assert.Equal($"technician-utilization-{HubSeed.Day(-29)}-to-{HubSeed.Day(0)}.pdf", pdf.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        // One normalized row per success: type, format, filters, rowCount and branch.
        var metadata = JsonNode.Parse(await database.ScalarAsync<string>(
            "SELECT metadata::text FROM audit_logs WHERE organization_id = @o AND metadata->>'reportType' = 'report_job_profitability' ORDER BY id LIMIT 1",
            ("o", org)))!;
        Assert.Equal(("csv", 6, "revenue", "desc", false), (Text(metadata["format"]), metadata["rowCount"]!.GetValue<int>(), Text(metadata["filters"]!["sort"]), Text(metadata["filters"]!["direction"]), metadata["filters"]!["compare"]!.GetValue<bool>()));
        Assert.Equal((HubSeed.Day(-29), HubSeed.Day(0)), (Text(metadata["filters"]!["from"]), Text(metadata["filters"]!["to"])));
        var agingMetadata = JsonNode.Parse(await database.ScalarAsync<string>(
            "SELECT metadata::text FROM audit_logs WHERE organization_id = @o AND metadata->>'reportType' = 'report_invoice_aging'", ("o", org)))!;
        Assert.Equal(HubSeed.Day(0), Text(agingMetadata["filters"]!["asOf"]));
        Assert.Equal(4L, await database.ExportCountAsync(org));

        // Accounting sees the financial exports in the audit trail but not the operational one.
        var (accounting, _) = await database.SignInAsync(host, org, Accounting);
        var trail = await BillingSeed.ReadAsync(await host.GetAsync("/audit-log?action=export.generated&pageSize=50", accounting));
        Assert.Equal(3, trail["items"]!.AsArray().Count);

        // Limits (lowered for the test): more rows than the limit is a 409 and writes nothing; 400s and 403s write nothing.
        var before = await database.ExportCountAsync(org);
        var tooLarge = await BillingSeed.ProblemAsync(await host.GetAsync(ExportPath("job_profitability", "pdf"), owner), HttpStatusCode.Conflict, "export_too_large");
        Assert.Equal("Narrow the filters to export 1,000 rows or fewer.", Text(tooLarge["title"]));
        var tooLargeCsv = await BillingSeed.ProblemAsync(await host.GetAsync(ExportPath("quote_conversion", "csv"), owner), HttpStatusCode.Conflict, "export_too_large");
        Assert.Equal("Narrow the filters to export 5,000 rows or fewer.", Text(tooLargeCsv["title"]));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.GetAsync(ExportPath("job_profitability", "xls"), owner)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.GetAsync(ExportPath("job_profitability", "csv"), (await database.SignInAsync(host, org, Dispatcher)).Cookie)).StatusCode);
        Assert.Equal(before, await database.ExportCountAsync(org));

        // A failing audit insert is a 500 without a file and without a row.
        host.State.Fail = true;
        var failed = await host.GetAsync(ExportPath("job_profitability", "csv"), owner);
        host.State.Fail = false;
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("text/csv", failed.Content.Headers.ContentType?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(before, await database.ExportCountAsync(org));
    }

    // AC-18: the history lists the caller's own report exports of the last 90 days, newest first, at most 20, visible reports only.
    [Fact]
    public async Task ExportHistory_OwnReportExportsOnly_LastNinetyDaysTwentyNewest()
    {
        var seed = await SeedAsync(database);
        await using var host = ReportsHost.Create(database);
        var org = seed.World.Org;
        var (dispatcher, member) = await database.SignInAsync(host, org, Dispatcher);
        var (owner, other) = await database.SignInAsync(host, org, Owner);

        async Task InsertAsync(Guid user, string type, int daysAgo, string action = "export.generated") =>
            await database.ExecuteAsync(
                """
                INSERT INTO audit_logs (organization_id, actor_user_id, action, entity_type, metadata, occurred_at)
                VALUES (@o, @u, @a, 'export', CAST(@m AS jsonb), now() - make_interval(days => @d, mins => @n))
                """,
                ("o", org),
                ("u", user),
                ("a", action),
                ("m", $$"""{"reportType":"{{type}}","format":"csv","filters":{"from":"2026-01-01","to":"2026-01-31","branchId":null,"compare":true},"rowCount":3}"""),
                ("d", daysAgo),
                ("n", Random.Shared.Next(0, 1000)));

        for (var index = 0; index < 25; index++)
        {
            await InsertAsync(member.UserId, "report_quote_conversion", 1 + (index % 20));
        }

        await InsertAsync(member.UserId, "report_quote_conversion", 120);
        await InsertAsync(member.UserId, "report_job_profitability", 2);
        await InsertAsync(member.UserId, "audit_log", 1);
        await InsertAsync(other.UserId, "report_quote_conversion", 1);
        await InsertAsync(member.UserId, "report_quote_conversion", 1, "something.else");

        var history = await BillingSeed.ReadAsync(await host.GetAsync("/reports/exports", dispatcher));
        var items = history["items"]!.AsArray();
        Assert.Equal(20, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(("quote_conversion", "Quote conversion", "csv", 3), (Text(item!["reportKey"]), Text(item["reportName"]), Text(item["format"]), item["rowCount"]!.GetValue<int>()));
            Assert.Equal("2026-01-01", Text(item["filters"]!["from"]));
        });
        var times = items.Select(item => item!["occurredAt"]!.GetValue<DateTimeOffset>()).ToArray();
        Assert.Equal(times.OrderByDescending(time => time).ToArray(), times);

        // The owner sees only their own row and the profitability row of the dispatcher stays hidden from the dispatcher.
        var mine = await BillingSeed.ReadAsync(await host.GetAsync("/reports/exports", owner));
        Assert.Single(mine["items"]!.AsArray());
    }
}
