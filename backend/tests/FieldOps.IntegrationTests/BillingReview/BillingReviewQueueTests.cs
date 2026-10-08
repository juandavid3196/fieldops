using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.BillingReview;

/// <summary>Queue membership, filters, search, tabs, pagination and metrics (completed-jobs-review AC-03, AC-04, AC-05).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BillingReviewQueueTests(CompanySettingsDatabaseFixture database)
{
    private static string[] Titles(JsonNode queue) =>
        [.. queue["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    [Fact]
    public async Task Queue_MembershipFiltersSearchTabsAndMetrics_FollowTheBillingRules()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var zed = await database.SeedCustomerAsync(world.Org, world.BranchA, "Zed Zimmerman");
        var tech1 = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "One");
        var tech2 = await database.SeedTechAsync(world.Org, world.BranchA, "Tom", "Two");
        var now = DateTimeOffset.UtcNow;
        var user = member.UserId;

        async Task<BillingJob> Seed(JobSpec spec) => await database.SeedJobAsync(world, user, spec);

        var tie = now.AddDays(-1);
        var j1 = await Seed(new JobSpec { Title = "Alpha drain", CompletedAt = tie, Technician = tech1 });
        var j12 = await Seed(new JobSpec { Title = "Lima tie", CompletedAt = tie });
        var j11 = await Seed(new JobSpec { Title = "Kilo unmet", CompletedAt = now.AddDays(-4), Photos = false });
        var j2 = await Seed(new JobSpec { Title = "Bravo roof", CompletedAt = now.AddDays(-5), Customer = zed, WorkSeconds = 4 * 3600, Technician = tech2 });
        var jB = await Seed(new JobSpec { Title = "Mike branch B", CompletedAt = now.AddDays(-6), Branch = world.BranchB });
        var j4 = await Seed(new JobSpec { Title = "Delta void only", CompletedAt = now.AddDays(-10) });
        var j3 = await Seed(new JobSpec { Title = "Charlie wall", CompletedAt = now.AddDays(-20), AddedMaterials = [("Clamp", 2m)], Technician = tech1 });
        await Seed(new JobSpec { Title = "India old", CompletedAt = now.AddDays(-60) });
        await Seed(new JobSpec { Title = "Juliet ancient", CompletedAt = now.AddDays(-120) });
        var j5 = await Seed(new JobSpec { Title = "Echo draft invoiced", CompletedAt = now.AddDays(-2) });
        var j6 = await Seed(new JobSpec { Title = "Foxtrot sent", CompletedAt = now.AddDays(-3) });
        var j7 = await Seed(new JobSpec { Title = "Golf approved", Status = "approved_for_billing", CompletedAt = now.AddDays(-3) });
        var j8 = await Seed(new JobSpec { Title = "Hotel in progress", Status = "in_progress" });

        await database.SeedBillingInvoiceAsync(world, user, j4, "void");
        await database.SeedBillingInvoiceAsync(world, user, j5, "draft");
        await database.SeedBillingInvoiceAsync(world, user, j6, "sent");
        await database.ExecuteAsync(
            "UPDATE work_orders SET billing_follow_up_at = now(), billing_follow_up_by_user_id = @u WHERE id = @o", ("u", user), ("o", j1.Order));

        async Task<JsonNode> Get(string query) =>
            await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(query), owner));

        // AC-03: only completed work orders without a non-void invoice (void-only included), newest completion first, then number.
        var queue = await Get(string.Empty);
        Assert.Equal(
            ["Lima tie", "Alpha drain", "Kilo unmet", "Bravo roof", "Mike branch B", "Delta void only", "Charlie wall"],
            Titles(queue));
        Assert.True(j12.Number > j1.Number);

        var first = queue["items"]![1]!;
        Assert.Equal(
            ($"WO-{j1.Number}", "Alpha drain", "Carla Customer", 221.65m, "USD", "none", false, true, true, j1.Order),
            (first["number"]!.GetValue<string>(),
                first["title"]!.GetValue<string>(),
                first["customerName"]!.GetValue<string>(),
                first["approvedTotal"]!.GetValue<decimal>(),
                first["currency"]!.GetValue<string>(),
                first["laborVariance"]!.GetValue<string>(),
                first["materialVariance"]!.GetValue<bool>(),
                first["ready"]!.GetValue<bool>(),
                first["followUp"]!.GetValue<bool>(),
                first["workOrderId"]!.GetValue<Guid>()));
        Assert.Equal((1, 20, 7), (queue["page"]!.GetValue<int>(), queue["pageSize"]!.GetValue<int>(), queue["total"]!.GetValue<int>()));

        // AC-05: metrics ignore variance and tab; tab counts ignore only the tab.
        Assert.Equal((7, 2, 4, 1551.55m, "USD"), Metrics(queue));
        Assert.Equal((7, 2, 4), Tabs(queue));

        var variances = await Get("tab=variances");
        Assert.Equal(["Bravo roof", "Charlie wall"], Titles(variances));
        Assert.Equal(2, variances["total"]!.GetValue<int>());
        Assert.Equal(["Lima tie", "Alpha drain", "Mike branch B", "Delta void only"], Titles(await Get("tab=ready")));

        var labor = await Get("variance=labor");
        Assert.Equal(["Bravo roof"], Titles(labor));
        Assert.Equal("over", labor["items"]![0]!["laborVariance"]!.GetValue<string>());
        Assert.Equal((7, 2, 4, 1551.55m, "USD"), Metrics(labor));
        Assert.Equal((1, 1, 0), Tabs(labor));
        Assert.Equal(["Charlie wall"], Titles(await Get("variance=material")));
        Assert.Equal(["Bravo roof", "Charlie wall"], Titles(await Get("variance=any")));
        Assert.Equal(5, (await Get("variance=none"))["total"]!.GetValue<int>());

        // AC-04: completion ranges, branch, technician and search.
        Assert.Equal(5, (await Get("completed=7d"))["total"]!.GetValue<int>());
        Assert.Equal(8, (await Get("completed=90d"))["total"]!.GetValue<int>());
        Assert.Equal(9, (await Get("completed=all"))["total"]!.GetValue<int>());
        var sevenDays = Metrics(await Get("completed=7d"));
        Assert.Equal((5, 5 * 221.65m), (sevenDays.NeedsReview, sevenDays.Value));
        Assert.Equal(["Mike branch B"], Titles(await Get($"branchId={world.BranchB}")));
        Assert.Equal(6, (await Get($"branchId={world.BranchA}"))["total"]!.GetValue<int>());
        Assert.Equal(["Alpha drain", "Charlie wall"], Titles(await Get($"technicianId={tech1}")));
        Assert.Equal(["Bravo roof"], Titles(await Get($"technicianId={tech2}")));
        Assert.Equal(["Alpha drain"], Titles(await Get("search=ALPHA")));
        Assert.Equal(["Bravo roof"], Titles(await Get("search=zimmer")));
        Assert.Equal(["Charlie wall"], Titles(await Get($"search=WO-{j3.Number}")));
        Assert.Equal(["Charlie wall"], Titles(await Get($"search=wo-{j3.Number}")));
        Assert.Equal(["Charlie wall"], Titles(await Get($"search={j3.Number}")));
        Assert.Equal(["Alpha drain"], Titles(await Get($"search={Uri.EscapeDataString("  alpha  ")}&variance=none&completed=7d")));
        Assert.Empty((await Get("search=no-such-job-anywhere"))["items"]!.AsArray());

        // Invalid values are field errors (400); excluded work orders are not found, whatever the action.
        foreach (var (query, key) in new[]
        {
            ("variance=bogus", "variance"),
            ("completed=1y", "completed"),
            ("tab=x", "tab"),
            ("page=0", "page"),
            ("page=abc", "page"),
            ($"search={new string('x', 101)}", "search"),
            ("branchId=not-a-guid", "branchId"),
            ("technicianId=zzz", "technicianId"),
        })
        {
            await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(query), owner), HttpStatusCode.BadRequest, errorKey: key);
        }

        foreach (var excluded in new[] { j5, j6, j7, j8 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(excluded.Order), owner)).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await BillingSeed.PatchReviewAsync(host, owner, excluded.Order, new JsonObject { ["note"] = "x" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(j4.Order), owner)).StatusCode);

        // Pagination: pages of 20, an empty page beyond the last still carries the totals.
        var pages = await database.SeedWorldAsync();
        var (pagesOwner, pagesMember) = await host.SignInAsync(database, pages.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        for (var index = 0; index < 22; index++)
        {
            await database.SeedJobAsync(pages, pagesMember.UserId, new JobSpec { Title = $"Job {index:00}", CompletedAt = now.AddHours(-1 - index), Minimal = true });
        }

        var pageOne = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(), pagesOwner));
        var pageTwo = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue("page=2"), pagesOwner));
        var pageThree = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue("page=3"), pagesOwner));
        Assert.Equal((20, 22), (pageOne["items"]!.AsArray().Count, pageOne["total"]!.GetValue<int>()));
        Assert.Equal(["Job 20", "Job 21"], Titles(pageTwo));
        Assert.Empty(pageThree["items"]!.AsArray());
        Assert.Equal((3, 22, 22), (pageThree["page"]!.GetValue<int>(), pageThree["total"]!.GetValue<int>(), pageThree["metrics"]!["needsReview"]!.GetValue<int>()));
    }

    private static (int NeedsReview, int WithVariances, int Ready, decimal Value, string Currency) Metrics(JsonNode queue)
    {
        var metrics = queue["metrics"]!;

        return (
            metrics["needsReview"]!.GetValue<int>(),
            metrics["withVariances"]!.GetValue<int>(),
            metrics["readyToInvoice"]!.GetValue<int>(),
            metrics["completedValue"]!.GetValue<decimal>(),
            metrics["currency"]!.GetValue<string>());
    }

    private static (int All, int Variances, int Ready) Tabs(JsonNode queue)
    {
        var tabs = queue["tabs"]!;

        return (tabs["all"]!.GetValue<int>(), tabs["variances"]!.GetValue<int>(), tabs["ready"]!.GetValue<int>());
    }
}
