using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using static FieldOps.IntegrationTests.Team.AvailabilitySeed;
using static FieldOps.IntegrationTests.Team.SkillsAvailabilitySeed;

namespace FieldOps.IntegrationTests.Team;

/// <summary>Availability grid and availability writes (team-availability-skills AC-02 to AC-11, AC-24).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TeamAvailabilityEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short OperationsManager = CompanySettingsDatabaseFixture.OperationsManagerRoleId;

    private const string Stale = "This technician was updated by someone else. Reload to see the latest changes.";
    private const string WorkDays = "monday tuesday wednesday thursday friday";

    private static string[] Weekdays => WorkDays.Split(' ');

    // AC-02 to AC-05: cells, weekly hours, precedence, inheritance, DST week, filters, validation and paging in one world.
    [Fact]
    public async Task Grid_ReturnsCellsWeeklyHoursFiltersAndPaging()
    {
        var org = await database.SeedOrganizationAsync();
        var foreignOrg = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        var chicago = await database.SeedBranchAsync(org, "Chicago Branch");
        var closed = await database.SeedBranchAsync(org, "Closed Branch");
        var foreignBranch = await database.SeedBranchAsync(foreignOrg, "Foreign Branch", isMain: true);
        await database.SetBranchHoursAsync(main.Id, Hours("08:00", "17:00", Weekdays));
        await database.SetBranchHoursAsync(chicago.Id, Hours("08:00", "17:00", Weekdays), "America/Chicago");

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);

        var inheriting = await database.SeedTechAsync(org, main.Id, "Ada", "Inherits");
        var custom = await database.SeedTechAsync(org, main.Id, "Bea", "Custom");
        var windows = await database.SeedTechAsync(org, main.Id, "Cy", "Windows");
        var crossWeek = await database.SeedTechAsync(org, chicago.Id, "Dee", "Chicago");
        var mixed = await database.SeedTechAsync(org, main.Id, "Eli", "Mixed");
        var noHours = await database.SeedTechAsync(org, closed.Id, "Fay", "Closed");
        var cancelled = await database.SeedTechAsync(org, main.Id, "Gus", "Cancelled");
        await database.SeedTechAsync(org, main.Id, "Hal", "Inactive", status: "inactive");
        await database.SeedTechAsync(org, main.Id, "Ivy", "Suspended", status: "suspended");
        await database.SeedTechAsync(foreignOrg, foreignBranch.Id, "Foreign", "Person");

        await database.SeedSlotAsync(custom, 1, "09:00", "17:00", 50);
        await database.SeedSlotAsync(custom, 3, "10:00", "14:00");
        await database.SeedSlotAsync(windows, 1, "09:00", "17:00");
        await database.SeedSlotAsync(windows, 1, "07:00", "08:00");

        // Week 2027-03-08 contains the US clock change on Sunday 14 March: time off Thursday 11 to Monday 15 local days.
        var week = new DateOnly(2027, 3, 8);
        var zone = "America/Chicago";
        await database.SeedExceptionAsync(crossWeek, Midnight(new DateOnly(2027, 3, 11), zone), Midnight(new DateOnly(2027, 3, 16), zone));
        await database.SeedExceptionAsync(mixed, Midnight(new DateOnly(2027, 3, 9)), Midnight(new DateOnly(2027, 3, 10)));
        await database.SeedExceptionAsync(mixed, Midnight(new DateOnly(2027, 3, 10)).AddHours(10), Midnight(new DateOnly(2027, 3, 10)).AddHours(12));
        await database.SeedExceptionAsync(mixed, Midnight(new DateOnly(2027, 3, 11)), Midnight(new DateOnly(2027, 3, 12)), available: true);
        await database.SeedExceptionAsync(cancelled, Midnight(new DateOnly(2027, 3, 8)), Midnight(new DateOnly(2027, 3, 15)), status: "cancelled");

        async Task<JsonNode> Grid(string query = "", string weekStart = "2027-03-08") =>
            await OkAsync(await host.SendAsync(HttpMethod.Get, $"/team/availability?weekStart={weekStart}{query}", ops.Cookie));

        var page = await Grid();
        Assert.Equal(("2027-03-08", 7, 1, 10), (page["weekStart"]!.GetValue<string>(), (int)page["totalCount"]!.GetValue<long>(), page["page"]!.GetValue<int>(), page["pageSize"]!.GetValue<int>()));

        // Ordered by first name; inactive, suspended and foreign profiles never appear.
        Assert.Equal([inheriting, custom, windows, crossWeek, mixed, noHours, cancelled], Ids(page));

        string[] workWeek = ["available", "available", "available", "available", "available", "unavailable", "unavailable"];

        var inheritingRow = Row(page, inheriting);
        Assert.Equal(workWeek, States(inheritingRow));
        Assert.Equal(("08:00", "17:00", true, "UTC"), (
            inheritingRow["days"]![0]!["start"]!.GetValue<string>(),
            inheritingRow["days"]![0]!["end"]!.GetValue<string>(),
            inheritingRow["usesBranchHours"]!.GetValue<bool>(),
            inheritingRow["timezone"]!.GetValue<string>()));
        Assert.Equal(45 * 60, inheritingRow["weeklyMinutes"]!.GetValue<int>());
        Assert.Equal(("Main Branch", "2027-03-08", "2027-03-14"), (
            inheritingRow["branch"]!["name"]!.GetValue<string>(),
            inheritingRow["days"]![0]!["date"]!.GetValue<string>(),
            inheritingRow["days"]![6]!["date"]!.GetValue<string>()));

        // Custom rows ignore capacity and breaks; legacy windows show the earliest-starting one.
        var customRow = Row(page, custom);
        Assert.Equal(["available", "unavailable", "available", "unavailable", "unavailable", "unavailable", "unavailable"], States(customRow));
        Assert.Equal((false, (8 + 4) * 60), (customRow["usesBranchHours"]!.GetValue<bool>(), customRow["weeklyMinutes"]!.GetValue<int>()));
        Assert.Equal(("07:00", "08:00", 60), (
            Row(page, windows)["days"]![0]!["start"]!.GetValue<string>(),
            Row(page, windows)["days"]![0]!["end"]!.GetValue<string>(),
            Row(page, windows)["weeklyMinutes"]!.GetValue<int>()));

        // Time off spans the week boundary and the DST Sunday; the row's zone is the branch's.
        var chicagoRow = Row(page, crossWeek);
        Assert.Equal(["available", "available", "available", "time_off", "time_off", "time_off", "time_off"], States(chicagoRow));
        Assert.Equal(("America/Chicago", 27 * 60), (chicagoRow["timezone"]!.GetValue<string>(), chicagoRow["weeklyMinutes"]!.GetValue<int>()));
        Assert.Equal(
            ["time_off", "available", "available", "available", "available", "unavailable", "unavailable"],
            States(Row(await Grid(weekStart: "2027-03-15"), crossWeek)));

        // Single-day full-day exception is time off; partial and extended exceptions do not change cells; cancelled is released.
        Assert.Equal(["available", "time_off", "available", "available", "available", "unavailable", "unavailable"], States(Row(page, mixed)));
        Assert.Equal(4 * 540, Row(page, mixed)["weeklyMinutes"]!.GetValue<int>());
        Assert.Equal(workWeek, States(Row(page, cancelled)));

        // A branch with `{}` hours is unavailable all week.
        Assert.Equal((new string('u', 7), 0), (string.Concat(States(Row(page, noHours)).Select(state => state[0])), Row(page, noHours)["weeklyMinutes"]!.GetValue<int>()));

        // Filters (table driven).
        Assert.Equal([crossWeek, mixed], Ids(await Grid("&availability=time_off")));
        Assert.Equal([noHours], Ids(await Grid("&availability=none")));
        Assert.Equal([inheriting, custom, windows, crossWeek, mixed, cancelled], Ids(await Grid("&availability=available")));
        Assert.Equal([crossWeek], Ids(await Grid($"&branchId={chicago.Id}")));
        Assert.Equal([crossWeek], Ids(await Grid("&search=%20DEE%20")));
        Assert.Equal([inheriting], Ids(await Grid("&search=ada%20inh")));
        Assert.Empty((await Grid("&search=nobody"))["items"]!.AsArray());

        // Invalid values are 400 with the field key (AC-02, AC-04).
        var invalid = new (string Query, string Key)[]
        {
            ("weekStart=2027-03-09", "weekStart"),
            ("weekStart=not-a-date", "weekStart"),
            ($"weekStart=2027-03-08&branchId={foreignBranch.Id}", "branchId"),
            ("weekStart=2027-03-08&branchId=nope", "branchId"),
            ($"weekStart=2027-03-08&search={new string('s', 101)}", "search"),
            ("weekStart=2027-03-08&availability=sometimes", "availability"),
            ("weekStart=2027-03-08&page=0", "page"),
        };

        foreach (var (query, key) in invalid)
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/team/availability?{query}", ops.Cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull((await CustomerHost.ReadAsync(response))["errors"]![key]);
        }

        // Default week is the one containing today in the organization zone (UTC).
        var current = await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability", ops.Cookie));
        var today = TodayUtc();
        Assert.Equal(today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).Text(), current["weekStart"]!.GetValue<string>());
    }

    // AC-02: page size 10, 1-based, case-insensitive order with an id tie-breaker, and a page beyond the last.
    [Fact]
    public async Task Grid_PagesTenRowsOrderedByNameAndReturnsEmptyBeyondTheLastPage()
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);
        string[] names = ["bob", "Alice", "carl", "Dana", "eve", "Frank", "gina", "Hank", "ivy", "Jack", "kim", "Liam"];

        foreach (var name in names)
        {
            await database.SeedTechAsync(org, main.Id, name, "Same");
        }

        var first = await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability?weekStart=2027-03-08", owner.Cookie));
        var second = await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability?weekStart=2027-03-08&page=2", owner.Cookie));
        var beyond = await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability?weekStart=2027-03-08&page=3", owner.Cookie));

        Assert.Equal(
            ["Alice", "bob", "carl", "Dana", "eve", "Frank", "gina", "Hank", "ivy", "Jack"],
            first["items"]!.AsArray().Select(item => item!["firstName"]!.GetValue<string>()));
        Assert.Equal(["kim", "Liam"], second["items"]!.AsArray().Select(item => item!["firstName"]!.GetValue<string>()));
        Assert.Empty(beyond["items"]!.AsArray());
        Assert.Equal((12, 3, 10), (beyond["totalCount"]!.GetValue<int>(), beyond["page"]!.GetValue<int>(), beyond["pageSize"]!.GetValue<int>()));
    }

    // AC-06 to AC-10, BR-09 to BR-11: override from inheriting, capacity/break preservation, no-op, validation, stale, race, audit.
    [Fact]
    public async Task SaveAvailability_OverridesInheritedHoursPreservesRowsAndRejectsInvalidStaleAndConcurrentSaves()
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        await database.SetBranchHoursAsync(main.Id, Hours("08:00", "17:00", Weekdays));

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var inheriting = await database.SeedTechAsync(org, main.Id, "Ina", "Herits");
        var legacy = await database.SeedTechAsync(org, main.Id, "Cal", "Custom");

        // AC-06: the drawer is prefilled with the branch hours; saving Tuesday creates the override.
        var drawer = await GetDrawerAsync(host, ops, inheriting);
        Assert.Equal((true, "Main Branch", "UTC"), (drawer["usesBranchHours"]!.GetValue<bool>(), drawer["branch"]!["name"]!.GetValue<string>(), drawer["timezone"]!.GetValue<string>()));
        Assert.StartsWith("(UTC+00:00)", drawer["timezoneLabel"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(["08:00", "08:00", "08:00", "08:00", "08:00", null, null], drawer["days"]!.AsArray().Select(day => day!["start"]?.GetValue<string>()));
        Assert.Equal(DayNames, drawer["days"]!.AsArray().Select(day => day!["dayOfWeek"]!.GetValue<string>()));

        var before = await database.ProfileVersionAsync(inheriting);
        var saved = await OkAsync(await host.SendAsync(
            HttpMethod.Put,
            $"/team/technicians/{inheriting}/availability",
            ops.Cookie,
            SaveBody(drawer["updatedAt"]!.GetValue<string>(), Week(name => name == "tuesday" ? DayBody(name, "10:00", "15:00") : name is "saturday" or "sunday" ? DayBody(name) : DayBody(name, "08:00", "17:00")))));

        Assert.False(saved["usesBranchHours"]!.GetValue<bool>());
        Assert.Equal("10:00", saved["days"]![1]!["start"]!.GetValue<string>());
        Assert.False(await database.ScalarAsync<bool>("SELECT uses_branch_hours FROM technician_profiles WHERE id = @t", ("t", inheriting)));
        Assert.Equal(5, await database.CountAsync("technician_weekly_availability", "technician_id", inheriting));
        Assert.True(await database.ProfileVersionAsync(inheriting) > before);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician.availability_updated", inheriting));

        var audit = await database.AuditAsync(org, "technician.availability_updated");
        Assert.Contains("\"source\":\"custom\"", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\"changedDays\":[\"tuesday\"]", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("wednesday", audit, StringComparison.Ordinal);

        // AC-08: the same days again are a no-op: no row id changes, updated_at stays, no audit.
        var rowIds = await database.ScalarAsync<string>("SELECT string_agg(id::text, ',' ORDER BY id) FROM technician_weekly_availability WHERE technician_id = @t", ("t", inheriting));
        var version = await database.ProfileVersionAsync(inheriting);
        var sameBody = SaveBody(saved["updatedAt"]!.GetValue<string>(), Week(name => name == "tuesday" ? DayBody(name, "10:00", "15:00") : name is "saturday" or "sunday" ? DayBody(name) : DayBody(name, "08:00", "17:00")));
        var noOp = await OkAsync(await host.SendAsync(HttpMethod.Put, $"/team/technicians/{inheriting}/availability", ops.Cookie, sameBody));

        Assert.Equal(saved["updatedAt"]!.GetValue<string>(), noOp["updatedAt"]!.GetValue<string>());
        Assert.Equal(version, await database.ProfileVersionAsync(inheriting));
        Assert.Equal(rowIds, await database.ScalarAsync<string>("SELECT string_agg(id::text, ',' ORDER BY id) FROM technician_weekly_availability WHERE technician_id = @t", ("t", inheriting)));
        Assert.Equal(1, await database.CountAuditAsync(org, "technician.availability_updated", inheriting));

        // AC-07: capacity and fitting breaks are kept, legacy windows collapse, Thursday is cleared, a new day uses 100.
        var monday = await database.SeedSlotAsync(legacy, 1, "08:00", "18:00", 50);
        await database.SeedBreakAsync(monday, "12:00", "13:00");
        await database.SeedBreakAsync(monday, "17:00", "17:30");
        await database.SeedSlotAsync(legacy, 2, "09:00", "17:00", 70);
        await database.SeedSlotAsync(legacy, 3, "09:00", "12:00");
        await database.SeedSlotAsync(legacy, 3, "13:00", "17:00");
        var thursday = await database.SeedSlotAsync(legacy, 4, "09:00", "17:00");
        await database.SeedBreakAsync(thursday, "12:00", "12:30");

        var legacyDrawer = await GetDrawerAsync(host, ops, legacy);
        Assert.False(legacyDrawer["usesBranchHours"]!.GetValue<bool>());
        Assert.Equal("09:00", legacyDrawer["days"]![2]!["start"]!.GetValue<string>());

        var legacyBody = Week(name => name switch
        {
            "monday" => DayBody(name, "08:00", "16:00"),
            "tuesday" => DayBody(name, "09:00", "17:00"),
            "wednesday" => DayBody(name, "09:00", "12:00"),
            "thursday" => DayBody(name),
            "friday" => DayBody(name, "09:00", "17:00"),
            _ => null,
        });
        await OkAsync(await host.SendAsync(HttpMethod.Put, $"/team/technicians/{legacy}/availability", ops.Cookie, SaveBody(legacyDrawer["updatedAt"]!.GetValue<string>(), legacyBody)));

        Assert.Equal(
            "1@08:00:00-16:00:00#50[12:00:00-13:00:00],2@09:00:00-17:00:00#70,3@09:00:00-12:00:00#100,5@09:00:00-17:00:00#100",
            await database.SlotsAsync(legacy));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_breaks b JOIN technician_weekly_availability a ON a.id = b.availability_id WHERE a.technician_id = @t AND a.day_of_week = 4", ("t", legacy)));

        // AC-09: invalid days are 400 with keyed messages and nothing is written.
        var current = (await GetDrawerAsync(host, ops, legacy))["updatedAt"]!.GetValue<string>();
        var snapshot = await database.SlotsAsync(legacy);
        var stamp = await database.ProfileVersionAsync(legacy);
        var invalid = new (JsonObject Body, string Key, string Message)[]
        {
            (SaveBody(current, Week(name => name == "monday" ? DayBody(name, "09:00", null, true) : null)), "days.monday.end", "This field is required."),
            (SaveBody(current, Week(name => name == "monday" ? DayBody(name, "9am", "17:00") : null)), "days.monday.start", "Enter a valid value."),
            (SaveBody(current, Week(name => name == "monday" ? DayBody(name, "17:00", "09:00") : null)), "days.monday.end", "End time must be after start time."),
            (SaveBody(current, Week(name => name == "monday" ? DayBody(name, "23:30", "00:30") : null)), "days.monday.end", "End time must be after start time."),
            (SaveBody(current, Week(name => name == "saturday" ? DayBody(name, "09:00", "10:00", false) : null)), "days.saturday", "Enter a valid value."),
            (SaveBody(current, new JsonArray([.. Week().Take(6).Select(day => (JsonNode?)day!.DeepClone())])), "days", "Enter a valid value."),
            (SaveBody(current, new JsonArray([.. Week().Take(6).Append(DayBody("monday")).Select(day => (JsonNode?)day!.DeepClone())])), "days", "Enter a valid value."),
        };

        foreach (var (body, key, message) in invalid)
        {
            await BadRequestAsync(await host.SendAsync(HttpMethod.Put, $"/team/technicians/{legacy}/availability", ops.Cookie, body), key, message);
        }

        Assert.Equal((snapshot, stamp), (await database.SlotsAsync(legacy), await database.ProfileVersionAsync(legacy)));

        // AC-10: a stale token is 409 stale with no write; two editors with one token produce exactly one winner.
        var staleResponse = await host.SendAsync(HttpMethod.Put, $"/team/technicians/{legacy}/availability", ops.Cookie, SaveBody("2020-01-01T00:00:00Z", Week()));
        await AssertConflictAsync(staleResponse, Stale, "stale");
        Assert.Equal(snapshot, await database.SlotsAsync(legacy));

        var race = await Task.WhenAll(
            host.SendAsync(HttpMethod.Put, $"/team/technicians/{legacy}/availability", ops.Cookie, SaveBody(current, Week(name => name == "monday" ? DayBody(name, "10:00", "11:00") : null))),
            host.SendAsync(HttpMethod.Put, $"/team/technicians/{legacy}/availability", ops.Cookie, SaveBody(current, Week(name => name == "monday" ? DayBody(name, "12:00", "13:00") : null))));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], race.Select(response => response.StatusCode).Order());
    }

    // AC-10, AC-11, BR-12: Use branch hours deletes rows and breaks, inheriting is a no-op, stale is 409, and a branch change reaches inheriting rows only.
    [Fact]
    public async Task UseBranchHours_RemovesTheOverrideIsIdempotentAndFollowsBranchHourChanges()
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        await database.SetBranchHoursAsync(main.Id, Hours("08:00", "17:00", Weekdays));

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, Owner);
        var custom = await database.SeedTechAsync(org, main.Id, "Cus", "Tom");
        var inheriting = await database.SeedTechAsync(org, main.Id, "Inh", "Erit");
        var slot = await database.SeedSlotAsync(custom, 2, "10:00", "14:00", 80);
        await database.SeedBreakAsync(slot, "12:00", "12:30");

        var drawer = await GetDrawerAsync(host, ops, custom);
        var path = $"/team/technicians/{custom}/availability/use-branch-hours";

        // Stale token: 409 and rows untouched.
        await AssertConflictAsync(await host.SendAsync(HttpMethod.Post, path, ops.Cookie, Token("2020-01-01T00:00:00Z")), Stale, "stale");
        Assert.Equal(1, await database.CountAsync("technician_weekly_availability", "technician_id", custom));

        var result = await OkAsync(await host.SendAsync(HttpMethod.Post, path, ops.Cookie, Token(drawer["updatedAt"]!.GetValue<string>())));

        Assert.True(result["usesBranchHours"]!.GetValue<bool>());
        Assert.Equal("08:00", result["days"]![0]!["start"]!.GetValue<string>());
        Assert.Equal((0, 0), (
            (int)await database.CountAsync("technician_weekly_availability", "technician_id", custom),
            (int)await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_breaks WHERE availability_id = @s", ("s", slot))));
        Assert.True(await database.ProfileVersionAsync(custom) > DateTimeOffset.Parse(drawer["updatedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("\"source\":\"branch_hours\"", (await database.AuditAsync(org, "technician.availability_updated")).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // Already inheriting: 200 without a write or audit.
        var version = await database.ProfileVersionAsync(custom);
        await OkAsync(await host.SendAsync(HttpMethod.Post, path, ops.Cookie, Token(result["updatedAt"]!.GetValue<string>())));
        Assert.Equal((version, 1), (await database.ProfileVersionAsync(custom), (int)await database.CountAuditAsync(org, "technician.availability_updated", custom)));

        // A branch business-hours change immediately applies to inheriting technicians only (AC-05).
        var pinned = await database.SeedTechAsync(org, main.Id, "Pin", "Ned");
        await database.SeedSlotAsync(pinned, 1, "06:00", "07:00");
        await database.SetBranchHoursAsync(main.Id, Hours("10:00", "12:00", "monday"));

        var grid = await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability?weekStart=2027-03-08", ops.Cookie));
        Assert.Equal(["available", "unavailable", "unavailable", "unavailable", "unavailable", "unavailable", "unavailable"], States(Row(grid, inheriting)));
        Assert.Equal(("10:00", 120), (Row(grid, inheriting)["days"]![0]!["start"]!.GetValue<string>(), Row(grid, inheriting)["weeklyMinutes"]!.GetValue<int>()));
        Assert.Equal(("06:00", 60), (Row(grid, pinned)["days"]![0]!["start"]!.GetValue<string>(), Row(grid, pinned)["weeklyMinutes"]!.GetValue<int>()));
    }
}
