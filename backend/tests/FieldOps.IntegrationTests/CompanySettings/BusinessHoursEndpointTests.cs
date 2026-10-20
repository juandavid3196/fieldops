using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// GET/PUT /branches/{id}/business-hours and the dispatch businessHours field (business-hours AC-02 to AC-10, AC-22,
/// AC-23). Budget: 5 endpoint methods, 2 authorization/tenant methods, 1 dispatch method.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BusinessHoursEndpointTests(CompanySettingsDatabaseFixture database)
{
    private const string StoredHours =
        """{"monday":{"start":"08:00","end":"17:00"},"tuesday":{"start":"08:00","end":"17:00"},"wednesday":{"start":"08:00","end":"17:00"},"thursday":{"start":"08:00","end":"17:00"},"friday":{"start":"08:00","end":"17:00"},"saturday":{"start":"08:00","end":"12:00"}}""";

    private static readonly string[] DayNames = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    // AC-02.
    [Fact]
    public async Task GetBusinessHours_Owner_ReturnsSevenOrderedDaysWithBranchDataAndCanManage()
    {
        var world = await database.SeedWorldAsync();
        await SetHoursAsync(world.BranchA, StoredHours, "America/Chicago");
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var response = await host.SendAsync(HttpMethod.Get, Path(world.BranchA), owner);
        var body = await DispatchSeed.ReadAsync(response);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(world.BranchA, body["branchId"]!.GetValue<Guid>());
        Assert.Equal("Alpha Branch", body["branchName"]!.GetValue<string>());
        Assert.Equal("America/Chicago", body["timezone"]!.GetValue<string>());
        Assert.True(body["canManage"]!.GetValue<bool>());
        Assert.Equal(await database.GetBranchUpdatedAtAsync(world.BranchA), body["updatedAt"]!.GetValue<DateTimeOffset>());
        var days = body["days"]!.AsArray();
        Assert.Equal(DayNames, days.Select(day => day!["dayOfWeek"]!.GetValue<string>()));
        Assert.Equal("08:00", days[0]!["openingTime"]!.GetValue<string>());
        Assert.Equal("12:00", days[5]!["closingTime"]!.GetValue<string>());
        Assert.False(days[6]!["isOpen"]!.GetValue<bool>());
        Assert.Null(days[6]!["openingTime"]);
        Assert.Null(days[6]!["closingTime"]);
    }

    // AC-06, AC-10: canonical storage, advanced updated_at, one audit row with only the changed days, visits untouched.
    [Fact]
    public async Task PutBusinessHours_ValidChange_StoresCanonicalAuditsChangedDaysAndLeavesVisitsUntouched()
    {
        var world = await database.SeedWorldAsync();
        await SetHoursAsync(world.BranchA, StoredHours, "UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var slot = DispatchSeed.Future(5, 18);
        var order = await database.SeedOrderAsync(
            world, member.UserId, status: "scheduled", visitStatus: "scheduled", start: slot, end: slot.AddHours(1));
        var visitBefore = await database.VisitStateAsync(order.Visit);
        var before = await database.GetBranchUpdatedAtAsync(world.BranchA);

        var days = Week();
        days[2] = Day("wednesday", false, null, null);
        days[5] = Day("saturday", true, "08:00", "13:00");

        var response = await host.SendAsync(
            HttpMethod.Put, Path(world.BranchA), owner, Request(days, before.ToString("O")));
        var body = await DispatchSeed.ReadAsync(response);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(body["canManage"]!.GetValue<bool>());
        Assert.False(body["days"]![2]!["isOpen"]!.GetValue<bool>());
        Assert.Equal("13:00", body["days"]![5]!["closingTime"]!.GetValue<string>());

        var stored = await database.ScalarAsync<string>(
            "SELECT business_hours::text FROM branches WHERE id = @b", ("b", world.BranchA));
        var storedJson = JsonNode.Parse(stored)!.AsObject();
        Assert.Equal(
            ["friday", "monday", "saturday", "thursday", "tuesday"],
            storedJson.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Null(storedJson["wednesday"]);
        Assert.Equal("13:00", storedJson["saturday"]!["end"]!.GetValue<string>());
        Assert.Equal("08:00", storedJson["saturday"]!["start"]!.GetValue<string>());
        var after = await database.GetBranchUpdatedAtAsync(world.BranchA);
        Assert.True(after > before);
        Assert.Equal(after, body["updatedAt"]!.GetValue<DateTimeOffset>());

        Assert.Equal(1, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));
        var (auditBefore, auditAfter, metadata) = await database.GetLatestAuditAsync(world.Org, "branch.business_hours_updated");
        Assert.Equal(["wednesday", "saturday"], JsonNode.Parse(metadata!)!["changedDays"]!.AsArray().Select(day => day!.GetValue<string>()));
        Assert.Equal(["wednesday", "saturday"], JsonNode.Parse(auditBefore!)!.AsObject().Select(pair => pair.Key).OrderBy(key => Array.IndexOf(DayNames, key)));
        Assert.Equal("12:00", JsonNode.Parse(auditBefore!)!["saturday"]!["closingTime"]!.GetValue<string>());
        Assert.False(JsonNode.Parse(auditAfter!)!["wednesday"]!["isOpen"]!.GetValue<bool>());
        Assert.Null(JsonNode.Parse(auditAfter!)!["wednesday"]!["openingTime"]);
        Assert.Equal("13:00", JsonNode.Parse(auditAfter!)!["saturday"]!["closingTime"]!.GetValue<string>());
        Assert.DoesNotContain("Alpha", auditAfter, StringComparison.Ordinal);
        Assert.Equal(
            $"branch|{world.BranchA}|{world.BranchA}|{member.UserId}",
            await database.ScalarAsync<string>(
                "SELECT entity_type || '|' || entity_id || '|' || branch_id || '|' || actor_user_id FROM audit_logs WHERE organization_id = @o AND action = 'branch.business_hours_updated'",
                ("o", world.Org)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT ip_address IS NOT NULL FROM audit_logs WHERE organization_id = @o AND action = 'branch.business_hours_updated'", ("o", world.Org)));

        // FR-10: the scheduled visit does not change.
        Assert.Equal(visitBefore, await database.VisitStateAsync(order.Visit));
    }

    // AC-09.
    [Fact]
    public async Task PutBusinessHours_IdenticalHours_Returns200WithoutChangingUpdatedAtOrAuditing()
    {
        var world = await database.SeedWorldAsync();
        await SetHoursAsync(world.BranchA, StoredHours, "UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var before = await database.GetBranchUpdatedAtAsync(world.BranchA);

        var body = await DispatchSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Put, Path(world.BranchA), owner, Request(Week(), before.ToString("O"))));

        Assert.Equal(before, body["updatedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(7, body["days"]!.AsArray().Count);
        Assert.Equal(before, await database.GetBranchUpdatedAtAsync(world.BranchA));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));
    }

    // AC-07.
    [Theory]
    [InlineData("six-days", "days", "Enter a valid value.")]
    [InlineData("duplicate-day", "days", "Enter a valid value.")]
    [InlineData("unknown-day", "days", "Enter a valid value.")]
    [InlineData("extra-property", "days", "Enter a valid value.")]
    [InlineData("missing-opening", "days.monday.openingTime", "This field is required.")]
    [InlineData("malformed-closing", "days.monday.closingTime", "Enter a valid value.")]
    [InlineData("closing-not-after", "days.monday.closingTime", "Closing time must be after opening time.")]
    [InlineData("closed-with-time", "days.sunday", "Enter a valid value.")]
    [InlineData("missing-updated-at", "updatedAt", "Enter a valid value.")]
    [InlineData("malformed-updated-at", "updatedAt", "Enter a valid value.")]
    public async Task PutBusinessHours_RuleViolation_Returns400WithKeyAndChangesNothing(
        string scenario, string key, string message)
    {
        var world = await database.SeedWorldAsync();
        await SetHoursAsync(world.BranchA, StoredHours, "UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var before = await database.GetBranchUpdatedAtAsync(world.BranchA);
        var days = Week();
        string? updatedAt = before.ToString("O");

        switch (scenario)
        {
            case "six-days":
                days.RemoveAt(6);
                break;
            case "duplicate-day":
                days[6]!["dayOfWeek"] = "monday";
                break;
            case "unknown-day":
                days[0]!["dayOfWeek"] = "funday";
                break;
            case "extra-property":
                days[0]!["note"] = "x";
                break;
            case "missing-opening":
                days[0]!["openingTime"] = null;
                break;
            case "malformed-closing":
                days[0]!["closingTime"] = "5pm";
                break;
            case "closing-not-after":
                days[0]!["closingTime"] = "08:00";
                break;
            case "closed-with-time":
                days[6]!["closingTime"] = "10:00";
                break;
            case "missing-updated-at":
                updatedAt = null;
                break;
            default:
                updatedAt = "not-a-date";
                break;
        }

        var response = await host.SendAsync(HttpMethod.Put, Path(world.BranchA), owner, Request(days, updatedAt));
        var problem = await DispatchSeed.ProblemAsync(response, HttpStatusCode.BadRequest, errorKey: key);

        Assert.Equal(message, problem["errors"]![key]![0]!.GetValue<string>());
        Assert.Equal(before, await database.GetBranchUpdatedAtAsync(world.BranchA));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(StoredHours),
            JsonNode.Parse(await database.ScalarAsync<string>("SELECT business_hours::text FROM branches WHERE id = @b", ("b", world.BranchA)))));
    }

    // AC-08.
    [Fact]
    public async Task PutBusinessHours_StaleToken_Returns409AndOfTwoConcurrentSavesExactlyOneSucceeds()
    {
        var world = await database.SeedWorldAsync();
        await SetHoursAsync(world.BranchA, StoredHours, "UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var token = await database.GetBranchUpdatedAtAsync(world.BranchA);
        var changed = Week();
        changed[0] = Day("monday", true, "09:00", "17:00");

        var stale = await host.SendAsync(
            HttpMethod.Put, Path(world.BranchA), owner, Request(changed, token.AddMinutes(-5).ToString("O")));
        var staleProblem = await DispatchSeed.ReadAsync(stale, HttpStatusCode.Conflict);

        Assert.Null(staleProblem["errors"]);
        Assert.Equal(token, await database.GetBranchUpdatedAtAsync(world.BranchA));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));

        var other = Week();
        other[1] = Day("tuesday", true, "10:00", "17:00");
        var results = await Task.WhenAll(
            host.SendAsync(HttpMethod.Put, Path(world.BranchA), owner, Request(changed, token.ToString("O"))),
            host.SendAsync(HttpMethod.Put, Path(world.BranchA), owner, Request(other, token.ToString("O"))));

        Assert.Equal(1, results.Count(result => result.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(result => result.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));
        Assert.True(await database.GetBranchUpdatedAtAsync(world.BranchA) > token);
    }

    // AC-03, AC-04: cross-tenant 404 with nothing changed, Viewer scope.
    [Fact]
    public async Task BusinessHours_ForeignOrInaccessibleBranch_Returns404AndViewerCannotSave()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await SetHoursAsync(foreign.BranchA, StoredHours, "UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (viewer, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId, "Vera", "Viewer", world.BranchA);
        var foreignToken = await database.GetBranchUpdatedAtAsync(foreign.BranchA);
        var changed = Week();
        changed[0] = Day("monday", true, "09:00", "17:00");
        var request = Request(changed, foreignToken.ToString("O"));

        // Organization identifiers in the body are ignored.
        request["organizationId"] = foreign.Org;

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Path(foreign.BranchA), owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Put, Path(foreign.BranchA), owner, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Path(Guid.NewGuid()), owner)).StatusCode);
        Assert.Equal(foreignToken, await database.GetBranchUpdatedAtAsync(foreign.BranchA));
        Assert.Equal(0, await database.CountAuditLogsAsync(foreign.Org, "branch.business_hours_updated"));

        var own = await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Path(world.BranchA), viewer));
        Assert.False(own["canManage"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Path(world.BranchB), viewer)).StatusCode);

        var ownToken = await database.GetBranchUpdatedAtAsync(world.BranchA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await host.SendAsync(HttpMethod.Put, Path(world.BranchA), viewer, Request(changed, ownToken.ToString("O")))).StatusCode);
        Assert.Equal(ownToken, await database.GetBranchUpdatedAtAsync(world.BranchA));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));
    }

    // AC-05.
    [Fact]
    public async Task BusinessHours_DispatcherAndAnonymous_Return403And401WithoutChanges()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (dispatcher, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId);
        var token = await database.GetBranchUpdatedAtAsync(world.BranchA);
        var changed = Week();
        changed[0] = Day("monday", true, "09:00", "17:00");
        var request = Request(changed, token.ToString("O"));

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, Path(world.BranchA), dispatcher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Put, Path(world.BranchA), dispatcher, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, Path(world.BranchA), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Put, Path(world.BranchA), null, request)).StatusCode);
        Assert.Equal(token, await database.GetBranchUpdatedAtAsync(world.BranchA));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, "branch.business_hours_updated"));
    }

    // AC-22, AC-23.
    [Fact]
    public async Task Dispatch_OutsideBusinessHours_CarriesWarningOnEvaluationAndSaveWithoutConflictOrBlock()
    {
        var world = await database.SeedWorldAsync();
        await SetHoursAsync(world.BranchA, """{"monday":{"start":"08:00","end":"17:00"}}""", "UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var order = await database.SeedOrderAsync(world, member.UserId);
        var monday = NextWeekday(DayOfWeek.Monday);
        var sunday = NextWeekday(DayOfWeek.Sunday);

        var late = await DispatchSeed.ReadAsync(await DispatchSeed.EvaluateAsync(
            host, owner, order.Visit, DispatchSeed.EvaluationBody(monday.AddHours(16), 120)));
        Assert.True(late["businessHours"]!["outside"]!.GetValue<bool>());
        Assert.Equal("monday", late["businessHours"]!["dayOfWeek"]!.GetValue<string>());
        Assert.True(late["businessHours"]!["isOpen"]!.GetValue<bool>());
        Assert.Equal("08:00", late["businessHours"]!["openingTime"]!.GetValue<string>());
        Assert.Equal("17:00", late["businessHours"]!["closingTime"]!.GetValue<string>());
        Assert.Empty(late["conflicts"]!.AsArray());

        var closed = await DispatchSeed.ReadAsync(await DispatchSeed.EvaluateAsync(
            host, owner, order.Visit, DispatchSeed.EvaluationBody(sunday.AddHours(10))));
        Assert.True(closed["businessHours"]!["outside"]!.GetValue<bool>());
        Assert.False(closed["businessHours"]!["isOpen"]!.GetValue<bool>());
        Assert.Null(closed["businessHours"]!["openingTime"]);
        Assert.Empty(closed["conflicts"]!.AsArray());

        var inside = await DispatchSeed.ReadAsync(await DispatchSeed.EvaluateAsync(
            host, owner, order.Visit, DispatchSeed.EvaluationBody(monday.AddHours(9))));
        Assert.False(inside["businessHours"]!["outside"]!.GetValue<bool>());
        Assert.Empty(inside["conflicts"]!.AsArray());

        // The outside slot saves without a justification and the result carries the same warning.
        var token = await DispatchSeed.TokenAsync(host, owner, order.Visit);
        var saved = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, owner, order.Visit, DispatchSeed.Body(sunday.AddHours(10), token)));
        Assert.Equal("scheduled", saved["visit"]!["status"]!.GetValue<string>());
        Assert.True(saved["businessHours"]!["outside"]!.GetValue<bool>());
        Assert.False(saved["businessHours"]!["isOpen"]!.GetValue<bool>());

        // A branch without a time zone has no result and no warning.
        await database.ExecuteAsync("UPDATE branches SET timezone = NULL WHERE id = @b", ("b", world.BranchA));
        var none = await DispatchSeed.ReadAsync(await DispatchSeed.EvaluateAsync(
            host, owner, order.Visit, DispatchSeed.EvaluationBody(sunday.AddHours(10))));
        Assert.Null(none["businessHours"]);
        var resaved = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, owner, order.Visit, DispatchSeed.Body(monday.AddHours(16), saved["visit"]!["updatedAt"]!.GetValue<string>())));
        Assert.Null(resaved["businessHours"]);
    }

    private static string Path(Guid branchId) => $"/branches/{branchId}/business-hours";

    /// <summary>00:00 UTC of the next date at least three days ahead that falls on the weekday.</summary>
    private static DateTimeOffset NextWeekday(DayOfWeek weekday)
    {
        var date = DateTime.UtcNow.Date.AddDays(3);

        while (date.DayOfWeek != weekday)
        {
            date = date.AddDays(1);
        }

        return new DateTimeOffset(date, TimeSpan.Zero);
    }

    private static JsonObject Day(string name, bool open, string? opening, string? closing) =>
        new() { ["dayOfWeek"] = name, ["isOpen"] = open, ["openingTime"] = opening, ["closingTime"] = closing };

    /// <summary>The stored hours as a request: Monday-Friday 08:00-17:00, Saturday 08:00-12:00, Sunday closed.</summary>
    private static JsonArray Week() =>
        new([.. DayNames.Select(name => (JsonNode?)(name switch
        {
            "saturday" => Day(name, true, "08:00", "12:00"),
            "sunday" => Day(name, false, null, null),
            _ => Day(name, true, "08:00", "17:00"),
        }))]);

    private static JsonObject Request(JsonArray days, string? updatedAt) =>
        new() { ["days"] = JsonNode.Parse(days.ToJsonString()), ["updatedAt"] = updatedAt };

    private Task SetHoursAsync(Guid branchId, string hours, string? timezone) =>
        database.ExecuteAsync(
            "UPDATE branches SET business_hours = CAST(@h AS jsonb), timezone = @tz WHERE id = @b",
            ("h", hours),
            ("tz", timezone),
            ("b", branchId));
}
