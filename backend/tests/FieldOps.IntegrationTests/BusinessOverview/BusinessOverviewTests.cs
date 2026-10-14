using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;
using static FieldOps.IntegrationTests.BusinessOverview.BusinessOverviewSeed;

namespace FieldOps.IntegrationTests.BusinessOverview;

/// <summary>The Owner-only business overview and export (business-overview AC-02 to AC-04, AC-06 to AC-14, AC-16).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BusinessOverviewTests(CompanySettingsDatabaseFixture database)
{
    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static int Int(JsonNode? node) => node!.GetValue<int>();

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    [Fact]
    public async Task Overview_AllBranchesAndOneBranch_AggregatesEverySectionFromSeededData()
    {
        await using var host = RequestsHost.Create(database);
        var seed = await SeedAsync(database, host);
        var world = seed.World;

        var options = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/business-overview/options", seed.Owner));
        Assert.Equal((Timezone, "USD", 40.0m, HubSeed.Day(0)), (Text(options["timezone"]), Text(options["currency"]), Dec(options["marginTarget"]), Text(options["today"])));
        Assert.Equal(["Alpha Branch", "Bravo Branch"], options["branches"]!.AsArray().Select(branch => Text(branch!["name"])).ToArray());

        var response = await host.SendAsync(HttpMethod.Get, Overview(), seed.Owner);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var all = await BillingSeed.ReadAsync(response);
        var kpis = all["kpis"]!;

        // Period, granularity and previous period (BR-03); revenue, cost sources, profit and margin (BR-05).
        Assert.Equal(("day", HubSeed.Day(-59), HubSeed.Day(-30), 30), (Text(all["granularity"]), Text(all["previousFrom"]), Text(all["previousTo"]), all["series"]!.AsArray().Count));
        Assert.Equal((630m, 100m), (Dec(kpis["revenue"]!["current"]), Dec(kpis["revenue"]!["previous"])));
        Assert.Equal((325m, 100m), (Dec(kpis["grossProfit"]!["current"]), Dec(kpis["grossProfit"]!["previous"])));
        Assert.Equal((51.6m, 100.0m), (Dec(kpis["grossMarginPercent"]!["current"]), Dec(kpis["grossMarginPercent"]!["previous"])));

        // Quote conversion, outstanding with overdue, jobs completed (BR-06, BR-07, BR-08).
        Assert.Equal((7, 3, 233), (Int(kpis["quoteConversion"]!["accepted"]), Int(kpis["quoteConversion"]!["sent"]), Int(kpis["quoteConversion"]!["percent"])));
        Assert.Equal((650m, 450m, 3), (Dec(kpis["outstanding"]!["amount"]), Dec(kpis["outstanding"]!["overdueAmount"]), Int(kpis["outstanding"]!["overdueCount"])));
        Assert.Equal((6, 1), (Int(kpis["jobsCompleted"]!["current"]), Int(kpis["jobsCompleted"]!["previous"])));

        // Chart buckets by issue date and local paid date; the previous bucket is shifted back one period length (BR-12).
        var series = all["series"]!.AsArray().ToDictionary(point => Text(point!["start"]), point => point!);
        Assert.Equal((200m, 165m), (Dec(series[HubSeed.Day(-2)]["revenue"]), Dec(series[HubSeed.Day(-2)]["grossProfit"])));
        Assert.Equal((80m, 75m), (Dec(series[HubSeed.Day(-5)]["revenue"]), Dec(series[HubSeed.Day(-4)]["collected"])));
        Assert.Equal((100m, 100m, 30m), (Dec(series[HubSeed.Day(-10)]["previousRevenue"]), Dec(series[HubSeed.Day(-10)]["previousGrossProfit"]), Dec(series[HubSeed.Day(-10)]["previousCollected"])));

        // Cash flow with the refund-adjusted collection, as-of-today receivables and aging; pipeline (BR-13, BR-14).
        var cash = all["cashFlow"]!;
        Assert.Equal((650m, 115m, 650m, 450m), (Dec(cash["invoiced"]), Dec(cash["collected"]), Dec(cash["outstanding"]), Dec(cash["overdue"])));
        Assert.Equal(
            (200m, 350m, 100m, 0m),
            (Dec(cash["aging"]!["current"]), Dec(cash["aging"]!["days1To30"]), Dec(cash["aging"]!["days31To60"]), Dec(cash["aging"]!["days60Plus"])));
        Assert.Equal((3, 3, 7, 12), (Int(all["pipeline"]!["requests"]), Int(all["pipeline"]!["quotesSent"]), Int(all["pipeline"]!["accepted"]), Int(all["pipeline"]!["jobsCreated"])));

        // Branch performance (BR-15): revenue order, inactive branch with data, on-time, rating, totals.
        var branches = all["branches"]!.AsArray();
        Assert.Equal(["Bravo Branch", "Alpha Branch", "Gamma, Inc"], branches.Select(row => Text(row!["name"])).ToArray());
        Assert.Equal((300m, 33.3m, 1, 0, 3.0m, true), (Dec(branches[0]!["revenue"]), Dec(branches[0]!["marginPercent"]), Int(branches[0]!["jobsCompleted"]), Int(branches[0]!["onTimePercent"]), Dec(branches[0]!["rating"]), (bool)branches[0]!["isActive"]!));
        Assert.Equal((280m, 62.5m, 4, 100, 4.5m, 2), (Dec(branches[1]!["revenue"]), Dec(branches[1]!["marginPercent"]), Int(branches[1]!["jobsCompleted"]), Int(branches[1]!["onTimePercent"]), Dec(branches[1]!["rating"]), Int(branches[1]!["reviewCount"])));
        Assert.Equal((50m, 100.0m, false), (Dec(branches[2]!["revenue"]), Dec(branches[2]!["marginPercent"]), (bool)branches[2]!["isActive"]!));
        Assert.Null(branches[2]!["onTimePercent"]);
        Assert.Null(branches[2]!["rating"]);
        Assert.Equal((630m, 51.6m, 6, 50), (Dec(all["branchTotals"]!["revenue"]), Dec(all["branchTotals"]!["marginPercent"]), Int(all["branchTotals"]!["jobsCompleted"]), Int(all["branchTotals"]!["onTimePercent"])));

        // Service profitability (BR-16) and team capacity with the inactive technician excluded (BR-17).
        var services = all["services"]!.AsArray();
        Assert.Equal(["Plumbing", "=Drains"], services.Select(row => Text(row!["name"])).ToArray());
        Assert.Equal((550m, 57.3m, 3, 183.33m), (Dec(services[0]!["revenue"]), Dec(services[0]!["marginPercent"]), Int(services[0]!["jobCount"]), Dec(services[0]!["averageJobValue"])));
        Assert.Equal((80m, 12.5m, 1, 80m), (Dec(services[1]!["revenue"]), Dec(services[1]!["marginPercent"]), Int(services[1]!["jobCount"]), Dec(services[1]!["averageJobValue"])));
        var team = all["team"]!;
        Assert.Equal((1, 6, 900, 14400), (Int(team["technicians"]), Int(team["utilizationPercent"]), Int(team["scheduledMinutes"]), Int(team["availableMinutes"])));
        Assert.Equal((180, 60, 7), (Int(team["overtimeMinutes"]), Int(team["previousOvertimeMinutes"]), Int(team["next7DaysPercent"])));

        // Owner attention in BR-18 order.
        var alerts = all["alerts"]!.AsArray();
        Assert.Equal(["overdue_invoices", "branch_margin", "service_margin", "overtime"], alerts.Select(alert => Text(alert!["type"])).ToArray());
        Assert.Equal(["critical", "warning", "warning", "warning"], alerts.Select(alert => Text(alert!["severity"])).ToArray());
        Assert.Equal((3, 450m), (Int(alerts[0]!["count"]), Dec(alerts[0]!["amount"])));
        Assert.Equal(("Bravo Branch", 33.3m, world.BranchB), (Text(alerts[1]!["name"]), Dec(alerts[1]!["marginPercent"]), alerts[1]!["branchId"]!.GetValue<Guid>()));
        Assert.Equal(("=Drains", 12.5m, seed.Drains), (Text(alerts[2]!["name"]), Dec(alerts[2]!["marginPercent"]), alerts[2]!["categoryId"]!.GetValue<Guid>()));
        Assert.Equal((180, 60, 200.0m), (Int(alerts[3]!["overtimeMinutes"]), Int(alerts[3]!["previousOvertimeMinutes"]), Dec(alerts[3]!["changePercent"])));

        // One branch limits every figure to the branch rows: no totals row, no branch alerts, a payment through its allocations.
        var one = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Overview(branch: world.BranchA.ToString()), seed.Owner));
        var oneKpis = one["kpis"]!;
        Assert.Equal((280m, 100m, 175m, 62.5m), (Dec(oneKpis["revenue"]!["current"]), Dec(oneKpis["revenue"]!["previous"]), Dec(oneKpis["grossProfit"]!["current"]), Dec(oneKpis["grossMarginPercent"]!["current"])));
        Assert.Equal((5, 1, 500), (Int(oneKpis["quoteConversion"]!["accepted"]), Int(oneKpis["quoteConversion"]!["sent"]), Int(oneKpis["quoteConversion"]!["percent"])));
        Assert.Equal((300m, 100m, 1), (Dec(oneKpis["outstanding"]!["amount"]), Dec(oneKpis["outstanding"]!["overdueAmount"]), Int(oneKpis["outstanding"]!["overdueCount"])));
        Assert.Equal((4, 1), (Int(oneKpis["jobsCompleted"]!["current"]), Int(oneKpis["jobsCompleted"]!["previous"])));
        Assert.Equal((300m, 75m), (Dec(one["cashFlow"]!["invoiced"]), Dec(one["cashFlow"]!["collected"])));
        Assert.Equal((2, 1, 5, 10), (Int(one["pipeline"]!["requests"]), Int(one["pipeline"]!["quotesSent"]), Int(one["pipeline"]!["accepted"]), Int(one["pipeline"]!["jobsCreated"])));
        Assert.Equal(["Alpha Branch"], one["branches"]!.AsArray().Select(row => Text(row!["name"])).ToArray());
        Assert.Null(one["branchTotals"]);
        Assert.Equal(["overdue_invoices", "service_margin", "overtime"], one["alerts"]!.AsArray().Select(alert => Text(alert!["type"])).ToArray());
    }

    [Fact]
    public async Task Overview_InvalidRangeBranchOrCompare_ReturnsFieldErrors()
    {
        await using var host = RequestsHost.Create(database);
        var seed = await SeedAsync(database, host);
        var cookie = seed.Owner;

        async Task ExpectAsync(string path, string errorKey)
        {
            var problem = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, path, cookie), HttpStatusCode.BadRequest, errorKey: errorKey);
            Assert.NotNull(problem["errors"]![errorKey]);
        }

        await ExpectAsync("/business-overview", "from");
        await ExpectAsync("/business-overview", "to");
        await ExpectAsync($"/business-overview?from=2026-13-01&to={HubSeed.Day(0)}", "from");
        await ExpectAsync($"/business-overview?from={HubSeed.Day(0)}&to=tomorrow", "to");
        await ExpectAsync($"/business-overview?from={HubSeed.Day(-1)}&to={HubSeed.Day(-2)}", "to");
        await ExpectAsync($"/business-overview?from={HubSeed.Day(0)}&to={HubSeed.Day(1)}", "to");
        await ExpectAsync($"/business-overview?from={HubSeed.Day(-366)}&to={HubSeed.Day(0)}", "to");
        await ExpectAsync(Overview(branch: "not-a-guid"), "branchId");
        await ExpectAsync($"/business-overview/export?{Window}&compare=maybe", "compare");
        await ExpectAsync($"/business-overview/export?from=nope&to={HubSeed.Day(0)}", "from");

        // Exactly 366 days is the longest valid period; a client organization id is ignored.
        var longest = await host.SendAsync(
            HttpMethod.Get, $"/business-overview?from={HubSeed.Day(-365)}&to={HubSeed.Day(0)}&organizationId={seed.Foreign.Org}", cookie);
        var body = await BillingSeed.ReadAsync(longest);
        Assert.Equal(("month", 650m), (Text(body["granularity"]), Dec(body["cashFlow"]!["outstanding"])));
    }

    [Fact]
    public async Task Export_WithCompareOffAndOn_EqualsOverviewEscapesTextAndHoldsNoRecords()
    {
        await using var host = RequestsHost.Create(database);
        var seed = await SeedAsync(database, host);

        var overview = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Overview(), seed.Owner));
        var off = await host.SendAsync(HttpMethod.Get, $"/business-overview/export?{Window}&compare=false", seed.Owner);
        var csv = await off.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", off.Content.Headers.ContentType?.ToString());
        Assert.Equal($"business-overview-{HubSeed.Day(-29)}-to-{HubSeed.Day(0)}.csv", off.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("attachment", off.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("no-store", off.Headers.CacheControl?.ToString());

        // Formula-leading and comma text is escaped; no customer, contact, invoice or work order data is exported.
        Assert.Contains("'=Drains", csv, StringComparison.Ordinal);
        Assert.Contains("\"Gamma, Inc (inactive)\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Carla", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("carla@example.com", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("INV-", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("WO-", csv, StringComparison.Ordinal);

        var rows = ParseCsv(csv);
        Assert.Equal(["Section", "Metric", "Dimension", "Current", "Previous", "Unit"], rows[0]);
        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.All(rows.Skip(1), row => Assert.Equal(string.Empty, row[4]));

        string Current(string section, string metric, string dimension = "") =>
            rows.Single(row => row[0] == section && row[1] == metric && row[2] == dimension)[3];

        // The values are the overview values of the same parameters.
        Assert.Equal(Money(overview["kpis"]!["revenue"]!["current"]), Current("KPIs", "Revenue"));
        Assert.Equal(Money(overview["kpis"]!["grossProfit"]!["current"]), Current("KPIs", "Estimated gross profit"));
        Assert.Equal("51.6", Current("KPIs", "Estimated gross margin"));
        Assert.Equal("233", Current("KPIs", "Quote conversion"));
        Assert.Equal("6", Current("KPIs", "Jobs completed"));
        Assert.Equal(Money(overview["cashFlow"]!["collected"]), Current("Cash flow", "Collected"));
        Assert.Equal(Money(overview["cashFlow"]!["aging"]!["days31To60"]), Current("Aging", "31-60 days"));
        Assert.Equal(Int(overview["pipeline"]!["jobsCreated"]).ToString(CultureInfo.InvariantCulture), Current("Sales pipeline", "Jobs created"));
        Assert.Equal("300.00", Current("Branch performance", "Revenue", "Bravo Branch"));
        Assert.Equal("100.0", Current("Branch performance", "Estimated margin", "Gamma, Inc (inactive)"));
        Assert.Equal("630.00", Current("Branch performance", "Revenue", "Total"));
        Assert.Equal("183.33", Current("Service profitability", "Average job value", "Plumbing"));
        Assert.Equal("12.5", Current("Service profitability", "Estimated margin", "'=Drains"));
        Assert.Equal("3", Current("Team & capacity", "Scheduled overtime"));
        Assert.Equal("6", Current("Team & capacity", "Utilization"));

        foreach (var point in overview["series"]!.AsArray())
        {
            Assert.Equal(Money(point!["revenue"]), Current("Chart", "Revenue", Text(point["start"])));
            Assert.Equal(Money(point["collected"]), Current("Chart", "Cash collected", Text(point["start"])));
        }

        // With comparison the previous period fills the Previous column where it applies.
        var on = ParseCsv(await (await host.SendAsync(HttpMethod.Get, $"/business-overview/export?{Window}", seed.Owner)).Content.ReadAsStringAsync());
        string Previous(string section, string metric, string dimension = "") =>
            on.Single(row => row[0] == section && row[1] == metric && row[2] == dimension)[4];

        Assert.Equal("100.00", Previous("KPIs", "Revenue"));
        Assert.Equal("1", Previous("KPIs", "Jobs completed"));
        Assert.Equal("100.00", Previous("Chart", "Revenue", HubSeed.Day(-10)));
        Assert.Equal("1", Previous("Team & capacity", "Scheduled overtime"));
        Assert.Equal(string.Empty, Previous("Cash flow", "Collected"));
    }

    [Fact]
    public async Task Access_NonOwnerRolesAndForeignOrInactiveBranches_AreRefusedWithoutForeignData()
    {
        await using var host = RequestsHost.Create(database);
        var seed = await SeedAsync(database, host);
        var org = seed.World.Org;
        var paths = new[] { "/business-overview/options", Overview(), $"/business-overview/export?{Window}" };

        // Only the Owner reads; every other role is refused before any read, anonymous callers get 401.
        foreach (var role in new[]
        {
            CompanySettingsDatabaseFixture.OperationsManagerRoleId,
            CompanySettingsDatabaseFixture.AccountingRoleId,
            CompanySettingsDatabaseFixture.DispatcherRoleId,
            CompanySettingsDatabaseFixture.ViewerRoleId,
            CompanySettingsDatabaseFixture.TechnicianRoleId,
        })
        {
            var (cookie, _) = await host.SignInAsync(database, org, role);

            foreach (var path in paths)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, cookie)).StatusCode);
            }
        }

        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, path, null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, path, seed.Owner)).StatusCode);
        }

        // An Owner of another organization sees only their own organization.
        var (foreignOwner, _) = await host.SignInAsync(database, seed.Foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var foreignView = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Overview(), foreignOwner));
        Assert.Equal(5000m, Dec(foreignView["kpis"]!["revenue"]!["current"]));
        Assert.Equal(["Alpha Branch"], foreignView["branches"]!.AsArray().Where(row => Dec(row!["revenue"]) > 0).Select(row => Text(row!["name"])).ToArray());

        // Unknown, inactive and foreign branches are the same 404 for the overview and the export, with no data.
        var unknown = await host.SendAsync(HttpMethod.Get, Overview(branch: Guid.NewGuid().ToString()), seed.Owner);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var expected = await WithoutTraceAsync(unknown);

        foreach (var branch in new[] { seed.InactiveBranch, seed.Foreign.BranchA })
        {
            foreach (var path in new[] { Overview(branch: branch.ToString()), $"/business-overview/export?{Window}&branchId={branch}" })
            {
                var refused = await host.SendAsync(HttpMethod.Get, path, seed.Owner);

                Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
                Assert.Equal(expected, await WithoutTraceAsync(refused));
            }
        }

        // All branches never contain the other organization's rows, branch ids or names.
        var all = await (await host.SendAsync(HttpMethod.Get, Overview(), seed.Owner)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(seed.Foreign.BranchA.ToString(), all, StringComparison.OrdinalIgnoreCase);
    }
}
