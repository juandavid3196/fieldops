using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.WorkOrders;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>Calendar lanes, load, current conflicts and filters, and the unscheduled panel (dispatch-calendar AC-02, AC-04, AC-05, AC-25).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DispatchReadTests(CompanySettingsDatabaseFixture database)
{
    // AC-02, AC-04: lanes with tags, the visits of every assigned lane, load figures, current conflicts (never from audit rows) and filters.
    [Fact]
    public async Task Calendar_BuildsLanesLoadAndCurrentConflicts_AndFiltersNeverChangeTheLoad()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var plumbing = await database.SeedSkillAsync(world.Org, "Plumbing");
        var welding = await database.SeedSkillAsync(world.Org, "Welding");
        var retired = await database.SeedSkillAsync(world.Org, "Retired", active: false);
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha", "active", true, plumbing);
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        var cy = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Cy", "Charlie", "active", true, welding);
        var zed = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Zed", "Zeta", "inactive");
        var dan = await database.SeedAvailableTechAsync(world.Org, world.BranchB, "Dan", "Delta");

        var monday = DateTime.UtcNow.Date.AddDays(7 - (((int)DateTime.UtcNow.DayOfWeek + 6) % 7));
        DateTimeOffset At(int dayOffset, int hour) => new(monday.AddDays(dayOffset).AddHours(hour), TimeSpan.Zero);

        var unassigned = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Unassigned job", visitStatus: "scheduled", start: At(0, 9), end: At(0, 10));
        var single = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Single job", visitStatus: "assigned", start: At(1, 9), end: At(1, 11));
        var multi = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Multi job", visitStatus: "assigned", start: At(1, 14), end: At(1, 16));
        var inactive = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Inactive job", visitStatus: "assigned", start: At(2, 9), end: At(2, 10));
        var other = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Other branch tech job", visitStatus: "assigned", start: At(2, 11), end: At(2, 12));
        var assessed = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Assessed job", visitStatus: "assigned", start: At(3, 9), end: At(3, 11));
        var timeOff = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Time off job", visitStatus: "assigned", start: At(4, 9), end: At(4, 10));
        var skilled = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Skilled job", visitStatus: "assigned", start: At(0, 14), end: At(0, 15), skills: [welding]);

        await database.AssignAsync(single.Visit, ann, user);
        await database.AssignAsync(multi.Visit, ann, user);
        await database.AssignAsync(multi.Visit, bob, user, primary: false);
        await database.AssignAsync(inactive.Visit, zed, user);
        await database.AssignAsync(other.Visit, dan, user);
        await database.AssignAsync(assessed.Visit, bob, user);
        await database.AssignAsync(timeOff.Visit, cy, user);
        await database.AssignAsync(skilled.Visit, bob, user);

        var request = await database.SeedRequestAsync(world, status: "assessment_scheduled");
        await database.SeedAssessmentAsync(world.Org, request.Id, At(3, 10), At(3, 11), user, bob);

        var date = DispatchSeed.Date(At(2, 0));
        string Url(string extra = "") => $"/dispatch/calendar?branchId={world.BranchA}&view=week&date={date}{extra}";

        async Task<JsonNode> LoadAsync(string extra = "") =>
            await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Url(extra), owner));

        static JsonNode Lane(JsonNode calendar, Guid id) =>
            calendar["technicians"]!.AsArray().Single(lane => lane!["id"]!.GetValue<Guid>() == id)!;

        static JsonNode Visit(JsonNode calendar, Guid id) =>
            calendar["visits"]!.AsArray().Single(visit => visit!["visitId"]!.GetValue<Guid>() == id)!;

        var all = await LoadAsync("&status=all");

        Assert.Equal("UTC", all["timezone"]!.GetValue<string>());
        Assert.Equal(7, all["days"]!.AsArray().Count);
        Assert.Equal(
            ["Ann Alpha|", "Bob Bravo|", "Cy Charlie|", "Dan Delta|other_branch", "Zed Zeta|inactive"],
            all["technicians"]!.AsArray().Select(lane => $"{lane!["name"]!.GetValue<string>()}|{lane["tag"]?.GetValue<string>()}").ToArray());
        Assert.Equal("AA", Lane(all, ann)["initials"]!.GetValue<string>());
        Assert.Equal("Plumbing", Lane(all, ann)["primarySkill"]!.GetValue<string>());
        Assert.Equal(8, all["visits"]!.AsArray().Count);
        Assert.Empty(Visit(all, unassigned.Visit)["technicianIds"]!.AsArray());
        Assert.Equal(new[] { ann, bob }.Order().ToArray(), Visit(all, multi.Visit)["technicianIds"]!.AsArray().Select(id => id!.GetValue<Guid>()).Order().ToArray());
        Assert.Equal(ann, Visit(all, multi.Visit)["primaryTechnicianId"]!.GetValue<Guid>());
        Assert.Equal("Single job", Visit(all, single.Visit)["title"]!.GetValue<string>());
        Assert.Equal($"WO-{single.Number}", Visit(all, single.Visit)["displayNumber"]!.GetValue<string>());
        Assert.Equal("1 Seed St", Visit(all, single.Visit)["street"]!.GetValue<string>());

        // Load: Ann 2h + 2h over 7 x 9h of availability; Bob adds the assessment and the skilled job; the time zone is the branch zone.
        var annLoad = Lane(all, ann)["load"]!;
        Assert.Equal(240d, annLoad["scheduledMinutes"]!.GetValue<double>());
        Assert.Equal(7 * 540d, annLoad["availableMinutes"]!.GetValue<double>());
        Assert.Equal("percent", annLoad["state"]!.GetValue<string>());
        Assert.Equal(120d + 120d + 60d + 60d, Lane(all, bob)["load"]!["scheduledMinutes"]!.GetValue<double>());
        Assert.Single(Lane(all, bob)["assessments"]!.AsArray());
        Assert.Equal(7, Lane(all, ann)["days"]!.AsArray().Count);
        Assert.NotEmpty(Lane(all, ann)["days"]![0]!["availability"]!.AsArray());
        Assert.NotEmpty(Lane(all, ann)["days"]![0]!["breaks"]!.AsArray());

        // Current conflicts: the assessment overlap and the missing skill, computed from the data and never from audit rows.
        var conflicts = await LoadAsync("&status=conflicts");
        Assert.Equal(
            new[] { assessed.Visit, skilled.Visit }.Order().ToArray(),
            conflicts["visits"]!.AsArray().Select(visit => visit!["visitId"]!.GetValue<Guid>()).Order().ToArray());
        Assert.Equal(["overlap"], DispatchSeed.Codes(Visit(all, assessed.Visit)["conflicts"]));
        Assert.StartsWith("Overlaps assessment", Visit(all, assessed.Visit)["conflicts"]![0]!["label"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(["missing_skills"], DispatchSeed.Codes(Visit(all, skilled.Visit)["conflicts"]));
        Assert.Empty(Visit(all, single.Visit)["conflicts"]!.AsArray());

        await database.SeedExceptionAsync(cy, At(4, 8), At(4, 18));
        var later = await LoadAsync("&status=conflicts");
        Assert.Equal(
            new[] { assessed.Visit, skilled.Visit, timeOff.Visit }.Order().ToArray(),
            later["visits"]!.AsArray().Select(visit => visit!["visitId"]!.GetValue<Guid>()).Order().ToArray());
        Assert.Equal(["time_off"], DispatchSeed.Codes(Visit(later, timeOff.Visit)["conflicts"]));
        Assert.NotEmpty(Lane(later, cy)["days"]![4]!["timeOff"]!.AsArray());

        // Status, Team and Skills filters limit blocks and lanes but never the load figures.
        var unassignedOnly = await LoadAsync("&status=unassigned");
        Assert.Equal([unassigned.Visit], unassignedOnly["visits"]!.AsArray().Select(visit => visit!["visitId"]!.GetValue<Guid>()).ToArray());
        Assert.Equal(annLoad.ToJsonString(), Lane(unassignedOnly, ann)["load"]!.ToJsonString());
        Assert.Equal(7, (await LoadAsync("&status=assigned"))["visits"]!.AsArray().Count);

        var team = await LoadAsync($"&status=all&technicianIds={ann}&technicianIds={Guid.NewGuid()}");
        Assert.Equal(
            ["Ann Alpha", "Dan Delta", "Zed Zeta"],
            team["technicians"]!.AsArray().Select(lane => lane!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal(annLoad.ToJsonString(), Lane(team, ann)["load"]!.ToJsonString());

        var skilledLanes = await LoadAsync($"&status=all&skillId={welding}");
        Assert.Equal(
            ["Cy Charlie", "Dan Delta", "Zed Zeta"],
            skilledLanes["technicians"]!.AsArray().Select(lane => lane!["name"]!.GetValue<string>()).ToArray());

        // 400s: a missing branch, bad view, date, status, and an inactive or foreign skill.
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, $"/dispatch/calendar?view=week&date={date}", owner), HttpStatusCode.BadRequest, errorKey: "branchId");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, $"/dispatch/calendar?branchId={world.BranchA}&view=month&date={date}", owner), HttpStatusCode.BadRequest, errorKey: "view");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, $"/dispatch/calendar?branchId={world.BranchA}&view=week&date=2026-13-40", owner), HttpStatusCode.BadRequest, errorKey: "date");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Url("&status=bogus"), owner), HttpStatusCode.BadRequest, errorKey: "status");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Url($"&skillId={retired}"), owner), HttpStatusCode.BadRequest, errorKey: "skillId");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Url($"&skillId={Guid.NewGuid()}"), owner), HttpStatusCode.BadRequest, errorKey: "skillId");

        // The calendar of another branch uses its own time zone.
        await database.ExecuteAsync("UPDATE branches SET timezone = 'America/New_York' WHERE id = @b", ("b", world.BranchB));
        var zoned = await DispatchSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/dispatch/calendar?branchId={world.BranchB}&view=day&date={date}", owner));
        Assert.Equal("America/New_York", zoned["timezone"]!.GetValue<string>());
        Assert.Matches("-0[45]:00$", zoned["from"]!.GetValue<string>());
        Assert.Single(zoned["days"]!.AsArray());
        Assert.Equal([dan], zoned["technicians"]!.AsArray().Select(lane => lane!["id"]!.GetValue<Guid>()).ToArray());
    }

    // AC-05, AC-25: exactly the BR-07 set per filter in BR-07 order, search, paging, and a created work order carrying its preferred window.
    [Fact]
    public async Task Unscheduled_ListsTheBr07SetInOrder_WithFiltersSearchAndPaging_AndNewOrdersCarryThePreferredWindow()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var plumbing = await database.SeedSkillAsync(world.Org, "Plumbing");
        var retired = await database.SeedSkillAsync(world.Org, "Retired", active: false);
        var now = DateTimeOffset.UtcNow;
        var midnight = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        var overdue = await database.SeedOrderAsync(world, user, title: "Overdue job", preferredStart: now.AddDays(-1).AddHours(-3), preferredEnd: now.AddDays(-1).AddHours(-1));
        var today = await database.SeedOrderAsync(world, user, title: "Today job", priority: 1, preferredStart: midnight, preferredEnd: midnight.AddHours(23).AddMinutes(59));
        var future = await database.SeedOrderAsync(world, user, title: "Future job", priority: 2, preferredStart: midnight.AddDays(3).AddHours(9), preferredEnd: midnight.AddDays(3).AddHours(12));
        var recurring = await database.SeedOrderAsync(
            world, user, title: "Recurring job", jobType: "recurring", frequency: "weekly", count: 3, skills: [plumbing],
            preferredStart: midnight.AddDays(3).AddHours(13), preferredEnd: midnight.AddDays(3).AddHours(15));
        var inProgress = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Second visit job", priority: 2);
        var noDate = await database.SeedOrderAsync(world, user, title: "Nodate job");
        await database.SeedOrderAsync(world, user, branch: world.BranchB, title: "Bravo job");
        await database.SeedOrderAsync(world, user, status: "draft", title: "Draft job");
        await database.SeedOrderAsync(world, user, status: "cancelled", title: "Cancelled order job");
        await database.SeedOrderAsync(world, user, title: "Already scheduled", visitStatus: "scheduled", start: midnight.AddDays(4).AddHours(9), end: midnight.AddDays(4).AddHours(10));

        string Url(string extra = "") => $"/dispatch/unscheduled?branchId={world.BranchA}&page=1&pageSize=25{extra}";

        async Task<JsonNode> ListAsync(string extra = "") =>
            await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Url(extra), owner));

        static string[] Titles(JsonNode page) =>
            [.. page["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

        var all = await ListAsync("&filter=all");

        Assert.Equal(
            ["Overdue job", "Today job", "Future job", "Recurring job", "Second visit job", "Nodate job"],
            Titles(all));
        Assert.Equal(6, all["total"]!.GetValue<int>());
        Assert.Equal(["Today job"], Titles(await ListAsync("&filter=today")));
        Assert.Equal(["Overdue job"], Titles(await ListAsync("&filter=overdue")));
        Assert.Equal(1, (await ListAsync("&filter=overdue"))["total"]!.GetValue<int>());

        var card = all["items"]!.AsArray().Single(item => item!["visitId"]!.GetValue<Guid>() == recurring.Visit)!;
        Assert.Equal(1, card["visitNumber"]!.GetValue<int>());
        Assert.Equal(3, card["recurrenceCount"]!.GetValue<int>());
        Assert.Equal($"WO-{recurring.Number}", card["displayNumber"]!.GetValue<string>());
        Assert.Equal("normal", card["priority"]!.GetValue<string>());
        Assert.Equal(120, card["estimatedDurationMinutes"]!.GetValue<int>());
        Assert.Equal("Carla Customer", card["customerName"]!.GetValue<string>());
        Assert.Equal("1 Seed St, Austin", card["address"]!.GetValue<string>());
        Assert.False(card["isOverdue"]!.GetValue<bool>());
        Assert.Equal(["Plumbing"], card["skills"]!.AsArray().Select(skill => skill!["name"]!.GetValue<string>()).ToArray());
        Assert.NotNull(card["preferredStart"]);
        Assert.True(all["items"]![0]!["isOverdue"]!.GetValue<bool>());
        Assert.Null(all["items"]!.AsArray().Single(item => item!["visitId"]!.GetValue<Guid>() == noDate.Visit)!["preferredStart"]);
        Assert.Null(all["items"]!.AsArray().Single(item => item!["visitId"]!.GetValue<Guid>() == inProgress.Visit)!["recurrenceCount"]);

        // Search is trimmed and case-insensitive over number, title, customer and address; skill and paging narrow the page.
        Assert.Equal(["Recurring job"], Titles(await ListAsync("&search=%20RECURRING%20")));
        Assert.Equal(["Today job"], Titles(await ListAsync($"&search=wo-{today.Number}")));
        Assert.Equal(6, (await ListAsync("&search=carla"))["total"]!.GetValue<int>());
        Assert.Equal(6, (await ListAsync("&search=seed%20st"))["total"]!.GetValue<int>());
        Assert.Empty((await ListAsync("&search=zzz"))["items"]!.AsArray());
        Assert.Equal(["Recurring job"], Titles(await ListAsync($"&skillId={plumbing}")));

        var second = await DispatchSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/dispatch/unscheduled?branchId={world.BranchA}&filter=all&page=2&pageSize=2", owner));
        Assert.Equal(["Future job", "Recurring job"], Titles(second));
        Assert.Equal(6, second["total"]!.GetValue<int>());

        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, "/dispatch/unscheduled?filter=all", owner), HttpStatusCode.BadRequest, errorKey: "branchId");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Url("&filter=soon"), owner), HttpStatusCode.BadRequest, errorKey: "filter");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Url($"&search={new string('x', 101)}"), owner), HttpStatusCode.BadRequest, errorKey: "search");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Url($"&skillId={retired}"), owner), HttpStatusCode.BadRequest, errorKey: "skillId");
        await DispatchSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, $"/dispatch/unscheduled?branchId={world.BranchA}&filter=all&page=1&pageSize=101", owner), HttpStatusCode.BadRequest, errorKey: "pageSize");

        // AC-25: the preferred window of a created work order is copied to visit #1, so the order shows under Today.
        await database.ExecuteAsync("UPDATE organizations SET next_work_order_number = 1000 WHERE id = @o", ("o", world.Org));
        var approved = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var body = WorkOrdersApi.Body(world, approved).Edit(change =>
        {
            change["title"] = "Created with window";
            change["preferredDate"] = DispatchSeed.Date(now);
            change["arrivalWindow"] = "08-11";
        });
        var created = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.CreateAsync(host, owner, approved.QuoteId, body), HttpStatusCode.Created);
        var createdOrder = created["id"]!.GetValue<Guid>();

        Assert.Equal(
            "true",
            await database.ScalarAsync<string>(
                "SELECT (v.preferred_start = w.preferred_start AND v.preferred_end = w.preferred_end AND v.preferred_start IS NOT NULL)::text FROM visits v JOIN work_orders w ON w.id = v.work_order_id WHERE w.id = @w",
                ("w", createdOrder)));
        Assert.Contains("Created with window", Titles(await ListAsync("&filter=today")));
        Assert.Contains("Created with window", Titles(await ListAsync("&filter=all")));
    }
}
