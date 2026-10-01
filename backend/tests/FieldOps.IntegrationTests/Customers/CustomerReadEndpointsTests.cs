using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>GET /customers and /customers/metrics (FR-02 to FR-04): AC-02 to AC-07.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerReadEndpointsTests(CompanySettingsDatabaseFixture database)
{
    // One seeded dataset proves lifecycle, balance, overdue precedence, tab counts, sorts, filters, search and paging.
    [Fact]
    public async Task ListAndMetrics_DeriveLifecycleBalanceTabsSortsFiltersAndPaging()
    {
        var now = DateTimeOffset.UtcNow;
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        await database.ExecuteAsync("UPDATE organizations SET currency = 'EUR', timezone = 'America/Chicago' WHERE id = @o", ("o", org));
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var main = await database.SeedBranchAsync(org, isMain: true);
        var second = await database.SeedBranchAsync(org);
        var otherBranch = await database.SeedBranchAsync(other, isMain: true);

        var lead = await database.SeedCustomerAsync(
            org, main.Id, "Alpha Lead", email: "alpha@example.com", phone: "5125557832",
            createdAt: now.AddMonths(-2), updatedAt: now.AddDays(-5));
        var active = await database.SeedCustomerAsync(
            org, main.Id, "bravo Active", type: "company", email: "bravo@example.com", phone: "4155550100",
            updatedAt: now.AddDays(-9));
        var overdue = await database.SeedCustomerAsync(
            org, main.Id, "Charlie Overdue", email: "charlie@example.com", updatedAt: now.AddHours(-1));
        var archived = await database.SeedCustomerAsync(
            org, main.Id, "Delta Archived", active: false, createdAt: now.AddMonths(-2));
        var elsewhere = await database.SeedCustomerAsync(
            org, second.Id, "Echo Other Branch", updatedAt: now.AddHours(-2));

        foreach (var index in Enumerable.Range(1, 8))
        {
            await database.SeedCustomerAsync(org, main.Id, $"Filler {index:00}", updatedAt: now.AddDays(-10 - index));
        }

        // Active through a completed visit, with the next scheduled visit and balances that exclude draft/paid.
        await database.SeedWorkAsync(
            org, owner.UserId, main.Id, active, "in_progress", scope: "Fix leaks\nsecond line",
            visitStatus: "completed", scheduledStart: now.AddDays(-3).AddHours(-2), scheduledEnd: now.AddDays(-3),
            completedAt: now.AddDays(-3).AddHours(-1));
        await database.SeedWorkAsync(
            org, owner.UserId, main.Id, active, "scheduled", visitStatus: "scheduled",
            scheduledStart: now.AddDays(2), scheduledEnd: now.AddDays(2).AddHours(1));
        await database.SeedInvoiceAsync(org, owner.UserId, main.Id, active, "sent", 100m);
        await database.SeedInvoiceAsync(org, owner.UserId, main.Id, active, "partially_paid", 50.5m);
        await database.SeedInvoiceAsync(org, owner.UserId, main.Id, active, "draft", 999m);

        // Active only through a completed work order (no completed visit: no last service); overdue wins the display status.
        await database.SeedWorkAsync(org, owner.UserId, main.Id, overdue, "completed");
        await database.SeedInvoiceAsync(org, owner.UserId, main.Id, overdue, "overdue", 200m);
        await database.SeedInvoiceAsync(org, owner.UserId, main.Id, overdue, "paid", 10m);
        await database.SeedInvoiceAsync(org, owner.UserId, main.Id, archived, "sent", 40m);

        var vip = await database.SeedTagAsync(org, "VIP");
        await database.AssignTagAsync(org, active, vip);
        await database.AssignTagAsync(org, elsewhere, vip);

        // Another organization's customer, work and invoices never appear.
        var foreign = await database.SeedCustomerAsync(other, otherBranch.Id, "Zulu Foreign");
        await database.SeedInvoiceAsync(other, owner.UserId, otherBranch.Id, foreign, "overdue", 5000m);

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);

        // Metrics: Total excludes archived, Active is derived, New this month uses the organization month,
        // Outstanding covers every in-scope customer (archived included) in the organization currency.
        var metrics = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers/metrics", cookie));
        Assert.Equal(12, metrics["totalCustomers"]!.GetValue<int>());
        Assert.Equal(2, metrics["activeCustomers"]!.GetValue<int>());
        Assert.Equal(11, metrics["newThisMonth"]!.GetValue<int>());
        Assert.Equal(390.5m, metrics["outstandingBalance"]!.GetValue<decimal>());
        Assert.Equal("EUR", metrics["currency"]!.GetValue<string>());

        var branchMetrics = await CustomerHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/customers/metrics?branchId={second.Id}", cookie));
        Assert.Equal(1, branchMetrics["totalCustomers"]!.GetValue<int>());
        Assert.Equal(0, branchMetrics["activeCustomers"]!.GetValue<int>());
        Assert.Equal(0m, branchMetrics["outstandingBalance"]!.GetValue<decimal>());

        // Default tab and sort: Last activity, customers without a completed visit last by updated_at desc.
        var first = await host.SendAsync(HttpMethod.Get, "/customers", cookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var list = await CustomerHost.ReadAsync(first);
        Assert.Equal(
            ["bravo Active", "Charlie Overdue", "Echo Other Branch", "Alpha Lead", "Filler 01", "Filler 02", "Filler 03", "Filler 04", "Filler 05", "Filler 06"],
            CustomerSeed.Names(list));
        Assert.Equal(12, list["totalCount"]!.GetValue<int>());
        Assert.Equal(10, list["pageSize"]!.GetValue<int>());
        Assert.Equal("EUR", list["currency"]!.GetValue<string>());
        Assert.Equal("America/Chicago", list["timezone"]!.GetValue<string>());

        var counts = list["tabCounts"]!;
        Assert.Equal((12, 10, 2, 1), (
            counts["all"]!.GetValue<int>(),
            counts["leads"]!.GetValue<int>(),
            counts["active"]!.GetValue<int>(),
            counts["archived"]!.GetValue<int>()));

        var row = list["items"]![0]!;
        Assert.Equal("commercial", row["type"]!.GetValue<string>());
        Assert.Equal("active", row["lifecycle"]!.GetValue<string>());
        Assert.Equal("active", row["displayStatus"]!.GetValue<string>());
        Assert.Equal(150.5m, row["balance"]!.GetValue<decimal>());
        Assert.Equal(1, row["propertyCount"]!.GetValue<int>());
        Assert.Equal("Fix leaks", row["lastService"]!["summary"]!.GetValue<string>());
        Assert.NotNull(row["lastService"]!["completedAt"]);
        Assert.NotNull(row["nextService"]!["startsAt"]);
        Assert.Equal(main.Id, row["branchId"]!.GetValue<Guid>());

        var overdueRow = list["items"]![1]!;
        Assert.Equal("active", overdueRow["lifecycle"]!.GetValue<string>());
        Assert.Equal("overdue", overdueRow["displayStatus"]!.GetValue<string>());
        Assert.Equal(200m, overdueRow["balance"]!.GetValue<decimal>());
        Assert.Null(overdueRow["lastService"]);
        Assert.Null(overdueRow["nextService"]);

        var leadRow = list["items"]![3]!;
        Assert.Equal("lead", leadRow["lifecycle"]!.GetValue<string>());
        Assert.Equal(0m, leadRow["balance"]!.GetValue<decimal>());

        var cases = new (string Query, string[] Expected, int Total)[]
        {
            ("?page=2", ["Filler 07", "Filler 08"], 12),
            ("?page=9", [], 12),
            ("?tab=archived", ["Delta Archived"], 1),
            ("?tab=active&sort=name_desc", ["Charlie Overdue", "bravo Active"], 2),
            ("?tab=leads&type=residential&sort=name_asc", ["Alpha Lead", "Echo Other Branch", "Filler 01", "Filler 02", "Filler 03", "Filler 04", "Filler 05", "Filler 06", "Filler 07", "Filler 08"], 10),
            ("?sort=name_asc", ["Alpha Lead", "bravo Active", "Charlie Overdue", "Echo Other Branch", "Filler 01", "Filler 02", "Filler 03", "Filler 04", "Filler 05", "Filler 06"], 12),
            ("?sort=balance_desc&search=ch", ["Charlie Overdue", "Echo Other Branch"], 2),
            ("?sort=newest&search=filler", ["Filler 08", "Filler 07", "Filler 06", "Filler 05", "Filler 04", "Filler 03", "Filler 02", "Filler 01"], 8),
            ("?type=commercial", ["bravo Active"], 1),
            ($"?branchId={second.Id}", ["Echo Other Branch"], 1),
            ($"?tagIds={vip}", ["bravo Active", "Echo Other Branch"], 2),
            ("?balanceStatus=has_balance&sort=balance_desc", ["Charlie Overdue", "bravo Active"], 2),
            ("?balanceStatus=overdue", ["Charlie Overdue"], 1),
            ("?balanceStatus=none&tab=archived", [], 0),
            ("?search=5557832", ["Alpha Lead"], 1),
            ("?search=%28512%29%20555", ["Alpha Lead"], 1),
            ("?search=12", [], 0),
            ("?search=BRAVO%40EXAMPLE", ["bravo Active"], 1),
            ("?search=%25_", [], 0),
        };

        foreach (var (query, expected, total) in cases)
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/customers{query}", cookie);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{query}: {response.StatusCode}");

            var page = await CustomerHost.ReadAsync(response);
            Assert.Equal(expected, CustomerSeed.Names(page));
            Assert.True(total == page["totalCount"]!.GetValue<int>(), $"{query}: totalCount");
        }

        // The primary contact name is searched too: every seeded contact is "Pat Contact".
        var byContact = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers?search=contact", cookie));
        Assert.Equal(12, byContact["totalCount"]!.GetValue<int>());

        // Tab counts honor search and filters but not the tab itself.
        var filtered = (await CustomerHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, "/customers?tab=archived&search=filler", cookie)))["tabCounts"]!;
        Assert.Equal(8, filtered["all"]!.GetValue<int>());
        Assert.Equal(8, filtered["leads"]!.GetValue<int>());
        Assert.Equal(0, filtered["active"]!.GetValue<int>());
        Assert.Equal(0, filtered["archived"]!.GetValue<int>());

        foreach (var invalid in new[] { "tab=all2", "type=x", "sort=oldest", "balanceStatus=big", "page=0", "page=abc", "branchId=abc", $"search={new string('x', 101)}" })
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/customers?{invalid}", cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull((await CustomerHost.ReadAsync(response))["errors"]![invalid[..invalid.IndexOf('=', StringComparison.Ordinal)]]);
        }
    }
}
