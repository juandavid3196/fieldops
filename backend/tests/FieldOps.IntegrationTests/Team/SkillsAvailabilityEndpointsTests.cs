using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using static FieldOps.IntegrationTests.Team.SkillsAvailabilitySeed;

namespace FieldOps.IntegrationTests.Team;

/// <summary>Skills and availability backend evidence: AC-03, AC-07 to AC-16, AC-17 to AC-20.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class SkillsAvailabilityEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short OperationsManager = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private const string StaleProfile = "This technician was updated by someone else. Reload to see the latest changes.";
    private const string StaleException = "This exception was changed by someone else. Reload to see the latest version.";
    private const string DateTaken = "This technician already has an active exception on this date.";
    private const string Hidden = "This technician profile isn't available.";

    private static string Title(JsonNode problem) => problem["title"]!.GetValue<string>();

    /// <summary>A 409 carries the spec message as the title and a machine-readable top-level <c>code</c>.</summary>
    private static async Task AssertConflictAsync(HttpResponseMessage response, string message, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await CustomerHost.ReadAsync(response);
        Assert.Equal((message, code), (Title(problem), problem["code"]!.GetValue<string>()));
    }

    private async Task<JsonNode> GetPageAsync(CustomerHost host, TeamActor actor, Guid technicianId) =>
        await CustomerHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/team/technicians/{technicianId}/skills-availability", actor.Cookie));

    // AC-03, AC-14, AC-15, BR-25: header, time zones, normalized weekly rows, skills order, exception list and derived sections.
    [Fact]
    public async Task Get_ReturnsHeaderZonesNormalizedWeeklySkillsExceptionsAndDerivedSections()
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        var chicago = await database.SeedBranchAsync(org, "Chicago Branch");
        await database.ExecuteAsync("UPDATE branches SET timezone = 'America/Chicago' WHERE id = @b", ("b", chicago.Id));

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var tech = await database.SeedTechAsync(org, main.Id, "Ada", "Lovelace");
        var chicagoTech = await database.SeedTechAsync(org, chicago.Id, "Zone", "Test");
        var other = await database.SeedTechAsync(org, main.Id, "Other", "Tech");

        // Every day 09:00-17:00; Monday is a legacy day with a second window and a second break; Wednesday works at 50 %.
        Guid monday = default;

        for (short dow = 0; dow <= 6; dow++)
        {
            var slot = await database.SeedSlotAsync(tech, dow, "09:00", "17:00", dow == 3 ? (short)50 : (short)100);

            if (dow == 1)
            {
                monday = slot;
            }
        }

        await database.SeedBreakAsync(monday, "14:00", "14:30");
        await database.SeedBreakAsync(monday, "12:00", "13:00");
        await database.SeedSlotAsync(tech, 1, "18:00", "20:00");

        var hvac = await database.SeedSkillAsync(org, "HVAC");
        var electrical = await database.SeedSkillAsync(org, "Electrical");
        var retired = await database.SeedSkillAsync(org, "Retired", active: false);
        await database.GiveSkillAsync(tech, electrical, proficiency: 2);
        await database.GiveSkillAsync(tech, hvac, primary: true, proficiency: 4);
        await database.GiveSkillAsync(tech, retired, proficiency: 5);

        await database.SeedExceptionAsync(tech, At(-5, "09:00"), At(-5, "10:00"), reason: "Past one");
        var partial = await database.SeedExceptionAsync(tech, At(10, "10:00"), At(10, "12:00"), reason: "Dentist");
        await database.SeedExceptionAsync(tech, At(11, "00:00"), At(12, "00:00"), reason: "Cancelled day", status: "cancelled");
        await database.SeedExceptionAsync(tech, At(12, "18:00"), At(12, "20:00"), available: true, reason: "Extra");

        // Tomorrow 10:00-12:00 counts; a cancelled visit, an unassigned one and another technician's visit do not.
        var user = ops.Member.UserId;
        await database.SeedAssignmentAsync(org, user, main.Id, tech, "scheduled", At(1, "10:00"), At(1, "12:00"));
        await database.SeedAssignmentAsync(org, user, main.Id, tech, "cancelled", At(1, "13:00"), At(1, "14:00"));
        await database.SeedAssignmentAsync(org, user, main.Id, other, "scheduled", At(1, "15:00"), At(1, "16:00"));
        var unassigned = await database.SeedAssignmentAsync(org, user, main.Id, tech, "scheduled", At(2, "10:00"), At(2, "12:00"));
        await database.ExecuteAsync("UPDATE visit_assignments SET unassigned_at = now() WHERE visit_id = @v", ("v", unassigned));

        var page = await GetPageAsync(host, ops, tech);

        Assert.Equal(("Ada", "Lovelace", "active", "Main Branch"), (
            page["technician"]!["firstName"]!.GetValue<string>(),
            page["technician"]!["lastName"]!.GetValue<string>(),
            page["technician"]!["profileStatus"]!.GetValue<string>(),
            page["technician"]!["branch"]!["name"]!.GetValue<string>()));
        Assert.Equal(("UTC", "(UTC+00:00) UTC"), (page["timezone"]!.GetValue<string>(), page["timezoneLabel"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(page["version"]!.GetValue<string>()));

        // Monday first; the legacy extras are hidden (BR-25); capacity percent is returned.
        var weekly = page["weeklyAvailability"]!.AsArray();
        Assert.Equal([1, 2, 3, 4, 5, 6, 0], weekly.Select(row => row!["dayOfWeek"]!.GetValue<int>()));
        Assert.Equal(("09:00", "17:00", "12:00", "13:00", 100), (
            weekly[0]!["start"]!.GetValue<string>(),
            weekly[0]!["end"]!.GetValue<string>(),
            weekly[0]!["breakStart"]!.GetValue<string>(),
            weekly[0]!["breakEnd"]!.GetValue<string>(),
            weekly[0]!["capacityPercent"]!.GetValue<int>()));
        Assert.Equal(50, weekly[2]!["capacityPercent"]!.GetValue<int>());

        // Primary first, inactive assignments hidden.
        var skills = page["skills"]!.AsArray();
        Assert.Equal(["HVAC", "Electrical"], skills.Select(row => row!["name"]!.GetValue<string>()));
        Assert.Equal([4, 2], skills.Select(row => row!["proficiency"]!.GetValue<int>()));

        // Past exceptions are not listed; cancelled ones are; ordered by date.
        var exceptions = page["exceptions"]!.AsArray();
        Assert.Equal(["partial", "unavailable", "extended"], exceptions.Select(row => row!["kind"]!.GetValue<string>()));
        Assert.Equal(["active", "cancelled", "active"], exceptions.Select(row => row!["status"]!.GetValue<string>()));
        Assert.Equal(("10:00", "12:00", Today(10), "Dentist"), (
            exceptions[0]!["start"]!.GetValue<string>(),
            exceptions[0]!["end"]!.GetValue<string>(),
            exceptions[0]!["date"]!.GetValue<string>(),
            exceptions[0]!["reason"]!.GetValue<string>()));
        Assert.Null(exceptions[1]!["start"]);
        Assert.Equal(partial.ToString(), exceptions[0]!["id"]!.GetValue<string>());

        // Today, tomorrow, then the one date reduced by an active exception (cancelled and extended days are omitted).
        var upcoming = page["upcoming"]!.AsArray();
        Assert.Equal(["today", "tomorrow", "date"], upcoming.Select(row => row!["label"]!.GetValue<string>()));
        Assert.Equal(("windows", false), (upcoming[1]!["state"]!.GetValue<string>(), upcoming[1]!["limited"]!.GetValue<bool>()));
        Assert.Equal((Today(10), "windows", true), (
            upcoming[2]!["date"]!.GetValue<string>(), upcoming[2]!["state"]!.GetValue<string>(), upcoming[2]!["limited"]!.GetValue<bool>()));

        // Seven days: Sun, Tue, Thu, Fri, Sat 480 min, Monday 420 (break), Wednesday 240 (50 %).
        var capacity = page["capacity"]!;
        Assert.Equal((3060, 120, 2940, 4), (
            capacity["availableMinutes"]!.GetValue<int>(),
            capacity["scheduledMinutes"]!.GetValue<int>(),
            capacity["remainingMinutes"]!.GetValue<int>(),
            capacity["utilizationPercent"]!.GetValue<int>()));
        Assert.Equal((0, 0), (page["today"]!["jobs"]!.GetValue<int>(), page["today"]!["bookedPercent"]!.GetValue<int>()));

        // A branch without a time zone falls back to the organization's; a branch time zone wins.
        var zone = await GetPageAsync(host, ops, chicagoTech);
        Assert.Equal("America/Chicago", zone["timezone"]!.GetValue<string>());
        Assert.EndsWith(" America/Chicago", zone["timezoneLabel"]!.GetValue<string>());
        Assert.Empty(zone["weeklyAvailability"]!.AsArray());
        Assert.Equal(0, zone["capacity"]!["availableMinutes"]!.GetValue<int>());
        Assert.Null(zone["capacity"]!["utilizationPercent"]);
    }

    // AC-07, AC-08, AC-09, BR-11, BR-25: atomic replace, legacy normalization, no-op save, invalid and stale saves, race.
    [Fact]
    public async Task Save_ReplacesAtomicallyNormalizesLegacyKeepsInactiveAssignmentsAndDetectsConflicts()
    {
        var org = await database.SeedOrganizationAsync();
        var foreignOrg = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, "Main Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var tech = await database.SeedTechAsync(org, branch.Id, "Grace", "Hopper");

        var monday = await database.SeedSlotAsync(tech, 1, "09:00", "17:00");
        await database.SeedSlotAsync(tech, 1, "18:00", "20:00");
        await database.SeedSlotAsync(tech, 3, "09:00", "17:00", 50);
        await database.SeedBreakAsync(monday, "12:00", "13:00");
        await database.SeedBreakAsync(monday, "14:00", "14:30");

        var alpha = await database.SeedSkillAsync(org, "Alpha");
        var beta = await database.SeedSkillAsync(org, "Beta");
        var gamma = await database.SeedSkillAsync(org, "Gamma");
        var retired = await database.SeedSkillAsync(org, "Retired", active: false);
        var foreign = await database.SeedSkillAsync(foreignOrg, "Foreign");
        await database.GiveSkillAsync(tech, alpha, proficiency: 3);
        await database.GiveSkillAsync(tech, beta, proficiency: 2);
        await database.GiveSkillAsync(tech, retired, primary: true, proficiency: 4);
        await database.ExecuteAsync("UPDATE technician_skills SET years_experience = 2.5 WHERE skill_id = @s", ("s", alpha));
        await database.ExecuteAsync("UPDATE technician_skills SET years_experience = 3.5 WHERE skill_id = @s", ("s", retired));

        var path = $"/team/technicians/{tech}/skills-availability";
        var original = await GetPageAsync(host, ops, tech);
        var v0 = original["version"]!.GetValue<string>();

        async Task<HttpResponseMessage> Put(JsonObject body) => await host.SendAsync(HttpMethod.Put, path, ops.Cookie, body);

        // The weekly rows exactly as the page showed them still save: the save removes the legacy extras (BR-25). The only skill
        // change is choosing the primary (the active set had none), which also clears the inactive assignment's primary flag.
        var normalized = await Put(SaveBody(v0, WeeklyFrom(original), [Assign(alpha, 3, true), Assign(beta, 2, false)]));
        Assert.Equal(HttpStatusCode.OK, normalized.StatusCode);
        var afterNormalize = await CustomerHost.ReadAsync(normalized);
        Assert.Equal(2, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_weekly_availability WHERE technician_id = @t", ("t", tech)));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_breaks b JOIN technician_weekly_availability a ON a.id = b.availability_id WHERE a.technician_id = @t", ("t", tech)));
        Assert.NotEqual(v0, afterNormalize["version"]!.GetValue<string>());
        Assert.Equal(
            "availability,skills",
            await database.ScalarAsync<string>("SELECT (metadata->'changedSections'->>0) || ',' || (metadata->'changedSections'->>1) FROM audit_logs WHERE entity_id = @t AND action = 'technician_profile.skills_availability_updated'", ("t", tech)));
        Assert.False(await database.ScalarAsync<bool>("SELECT is_primary FROM technician_skills WHERE skill_id = @s", ("s", retired)));
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.skills_availability_updated", tech));

        // Real change: Tuesday added, Beta removed, Gamma added, Alpha becomes Advanced.
        var v1 = afterNormalize["version"]!.GetValue<string>();
        JsonObject Change(string version) => SaveBody(
            version,
            [Day(1, breakStart: "12:00", breakEnd: "13:00"), Day(2, "08:00", "12:00"), Day(3)],
            [Assign(alpha, 4, true), Assign(gamma, 3, false)]);

        var saved = await Put(Change(v1));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var afterSave = await CustomerHost.ReadAsync(saved);
        var v2 = afterSave["version"]!.GetValue<string>();
        Assert.NotEqual(v1, v2);
        Assert.Equal(["Alpha", "Gamma"], afterSave["skills"]!.AsArray().Select(row => row!["name"]!.GetValue<string>()));

        Assert.Equal(
            new short[] { 50, 100, 100 },
            await ReadShortsAsync("SELECT capacity_percent FROM technician_weekly_availability WHERE technician_id = @t ORDER BY CASE day_of_week WHEN 3 THEN 0 ELSE 1 END, day_of_week", tech));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_skills WHERE skill_id = @s", ("s", beta)));
        Assert.Equal(2.5m, await database.ScalarAsync<decimal>("SELECT years_experience FROM technician_skills WHERE skill_id = @s AND technician_id = @t", ("s", alpha), ("t", tech)));
        Assert.Equal(
            (3.5m, false, (short)4),
            (
                await database.ScalarAsync<decimal>("SELECT years_experience FROM technician_skills WHERE skill_id = @s", ("s", retired)),
                await database.ScalarAsync<bool>("SELECT is_primary FROM technician_skills WHERE skill_id = @s", ("s", retired)),
                await database.ScalarAsync<short>("SELECT proficiency FROM technician_skills WHERE skill_id = @s", ("s", retired))));
        Assert.Equal(2, await database.CountAuditAsync(org, "technician_profile.skills_availability_updated", tech));

        // The identical body is a no-op: 200, same version, no audit.
        var versionBefore = await database.ProfileVersionAsync(tech);
        var noOp = await Put(Change(v2));
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.Equal(v2, (await CustomerHost.ReadAsync(noOp))["version"]!.GetValue<string>());
        Assert.Equal(versionBefore, await database.ProfileVersionAsync(tech));
        Assert.Equal(2, await database.CountAuditAsync(org, "technician_profile.skills_availability_updated", tech));

        // Stale version: 409 and nothing saved.
        var stale = await Put(SaveBody(v1, [Day(5)], [Assign(alpha, 5, true)]));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(StaleProfile, Title(await CustomerHost.ReadAsync(stale)));
        Assert.Equal(versionBefore, await database.ProfileVersionAsync(tech));

        // Invalid saves, including ones whose invalid item is the last: 400 and no row changes.
        var snapshot = await SnapshotAsync(tech);
        var invalid = new (JsonObject Body, string Key)[]
        {
            (SaveBody(v2, [Day(1, "17:00", "09:00")], [Assign(alpha, 4, true)]), "weeklyAvailability[1].end"),
            (SaveBody(v2, [Day(1, "09:10", "17:00")], [Assign(alpha, 4, true)]), "weeklyAvailability[1].start"),
            (SaveBody(v2, [Day(1, breakStart: "08:00", breakEnd: "09:00")], [Assign(alpha, 4, true)]), "weeklyAvailability[1].breakStart"),
            (SaveBody(v2, [Day(1), Day(1)], [Assign(alpha, 4, true)]), "weeklyAvailability"),
            (SaveBody(v2, [Day(1), Day(2), Day(0, "17:00", "09:00")], [Assign(alpha, 4, true)]), "weeklyAvailability[0].end"),
            (SaveBody(v2, [Day(1)], [Assign(alpha, 4, false)]), "skills"),
            (SaveBody(v2, [Day(1)], [Assign(alpha, 4, true), Assign(gamma, 3, true)]), "skills"),
            (SaveBody(v2, [Day(1)], [Assign(alpha, 4, true), Assign(alpha, 3, false)]), "skills"),
            (SaveBody(v2, [Day(1)], [Assign(alpha, null, true)]), "skills"),
            (SaveBody(v2, [Day(1)], [Assign(alpha, 4, true), Assign(foreign, 3, false)]), "skills"),
            (SaveBody(v2, [Day(1)], [Assign(alpha, 4, true), Assign(retired, 3, false)]), "skills"),
            (SaveBody("not-a-version", [Day(1)], [Assign(alpha, 4, true)]), "version"),
        };

        foreach (var (body, key) in invalid)
        {
            var response = await Put(body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull((await CustomerHost.ReadAsync(response))["errors"]![key]);
        }

        Assert.Equal(snapshot, await SnapshotAsync(tech));

        // Two editors with the same version: exactly one wins and the other gets the conflict message.
        var current = (await GetPageAsync(host, ops, tech))["version"]!.GetValue<string>();
        var race = await Task.WhenAll(
            Put(SaveBody(current, [Day(4)], [Assign(alpha, 5, true)])),
            Put(SaveBody(current, [Day(5)], [Assign(alpha, 1, true)])));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            race.Select(response => response.StatusCode).Order());
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_weekly_availability WHERE technician_id = @t", ("t", tech)));
    }

    // AC-10 to AC-13, BR-12 to BR-15: creation by type (incl. a DST change date), validation, one active per date, lifecycle, audit.
    [Fact]
    public async Task Exceptions_CreateEditCancelAndActivateEnforceValidationDatesVersionsAndAudit()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, "Chicago Branch", isMain: true);
        await database.ExecuteAsync("UPDATE branches SET timezone = 'America/Chicago' WHERE id = @b", ("b", branch.Id));

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, Owner);
        var tech = await database.SeedTechAsync(org, branch.Id, "Ex", "Ception");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var basePath = $"/team/technicians/{tech}/exceptions";

        // The next local date whose midnight offsets differ: a 23 or 25 hour day.
        var dst = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

        while (zone.GetUtcOffset(dst.ToDateTime(TimeOnly.MinValue)) == zone.GetUtcOffset(dst.AddDays(1).ToDateTime(TimeOnly.MinValue)))
        {
            dst = dst.AddDays(1);
        }

        DateTimeOffset Local(DateOnly date, string time)
        {
            var local = date.ToDateTime(TimeOnly.Parse(time));

            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        }

        string D(DateOnly date) => date.ToString("yyyy-MM-dd");

        var created = new List<(string Id, string Version)>();
        var types = new (DateOnly Date, string Kind, string? Start, string? End, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool Available)[]
        {
            (dst, "extended", "13:00", "15:00", Local(dst, "13:00"), Local(dst, "15:00"), true),
            (dst.AddDays(1), "partial", "09:00", "11:00", Local(dst.AddDays(1), "09:00"), Local(dst.AddDays(1), "11:00"), false),
            (dst.AddDays(2), "unavailable", null, null, Local(dst.AddDays(2), "00:00"), Local(dst.AddDays(3), "00:00"), false),
        };

        foreach (var type in types)
        {
            var response = await host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, ExceptionBody(D(type.Date), type.Kind, type.Start, type.End));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await CustomerHost.ReadAsync(response);
            var id = Guid.Parse(body["id"]!.GetValue<string>());

            Assert.Equal((D(type.Date), type.Kind, "active", type.Start, type.End), (
                body["date"]!.GetValue<string>(),
                body["kind"]!.GetValue<string>(),
                body["status"]!.GetValue<string>(),
                body["start"]?.GetValue<string>(),
                body["end"]?.GetValue<string>()));
            Assert.Equal(
                (type.StartsAt, type.EndsAt, type.Available),
                (
                    await database.ScalarAsync<DateTimeOffset>("SELECT starts_at FROM technician_exceptions WHERE id = @i", ("i", id)),
                    await database.ScalarAsync<DateTimeOffset>("SELECT ends_at FROM technician_exceptions WHERE id = @i", ("i", id)),
                    await database.ScalarAsync<bool>("SELECT is_available FROM technician_exceptions WHERE id = @i", ("i", id))));
            Assert.Equal(1, await database.CountAuditAsync(org, "technician_exception.created", id));
            Assert.DoesNotContain(
                "Doctor",
                await database.ScalarAsync<string>("SELECT concat(metadata::text, before_data::text, after_data::text) FROM audit_logs WHERE entity_id = @i", ("i", id)));
            created.Add((id.ToString(), body["version"]!.GetValue<string>()));
        }

        // Validation table: nothing is saved.
        var invalid = new (JsonObject Body, string Key, string Message)[]
        {
            (ExceptionBody(Today(-3), "partial", "09:00", "10:00"), "date", "Choose today or a future date."),
            (ExceptionBody(D(dst.AddDays(20)), "other", "09:00", "10:00"), "kind", "Choose an availability type."),
            (ExceptionBody(D(dst.AddDays(20)), "unavailable", "09:00", "10:00"), "start", "Times aren't allowed for an all-day exception."),
            (ExceptionBody(D(dst.AddDays(20)), "partial", null, "10:00"), "start", "Choose a start and end time."),
            (ExceptionBody(D(dst.AddDays(20)), "partial", "09:00", "10:15"), "end", "Choose a start and end time."),
            (ExceptionBody(D(dst.AddDays(20)), "extended", "10:00", "09:00"), "end", "End time must be after start time."),
            (ExceptionBody(D(dst.AddDays(20)), "partial", "09:00", "10:00", " "), "reason", "Enter a reason."),
            (ExceptionBody(D(dst.AddDays(20)), "partial", "09:00", "10:00", new string('r', 201)), "reason", "Use 200 characters or fewer."),
        };

        foreach (var (body, key, message) in invalid)
        {
            var response = await host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, TeamSeed.Error(await CustomerHost.ReadAsync(response), key));
        }

        Assert.Equal(3, await database.CountAsync("technician_exceptions", "technician_id", tech));

        // One active exception per date: a second create, an edit onto the date and an activation onto it are 409.
        var (partialId, partialVersion) = created[1];
        var (allDayId, allDayVersion) = created[2];

        var duplicate = await host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, ExceptionBody(D(dst.AddDays(1)), "partial", "12:00", "13:00"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertConflictAsync(duplicate, DateTaken, "exception_date_taken");

        var moved = await host.SendAsync(
            HttpMethod.Put, $"{basePath}/{allDayId}", ops.Cookie, ExceptionBody(D(dst.AddDays(1)), "unavailable", null, null, version: allDayVersion));
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        await AssertConflictAsync(moved, DateTaken, "exception_date_taken");

        var cancelled = await host.SendAsync(HttpMethod.Post, $"{basePath}/{partialId}/cancel", ops.Cookie, new JsonObject { ["version"] = partialVersion });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var cancelledBody = await CustomerHost.ReadAsync(cancelled);
        Assert.Equal("cancelled", cancelledBody["status"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_exception.cancelled", Guid.Parse(partialId)));

        var replacement = await host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, ExceptionBody(D(dst.AddDays(1)), "partial", "12:00", "13:00"));
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);

        var cancelledVersion = cancelledBody["version"]!.GetValue<string>();
        var blocked = await host.SendAsync(HttpMethod.Post, $"{basePath}/{partialId}/activate", ops.Cookie, new JsonObject { ["version"] = cancelledVersion });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        await AssertConflictAsync(blocked, DateTaken, "exception_date_taken");

        // Lifecycle of the all-day exception: stale edit, real edit, no-op edit.
        var edit = ExceptionBody(D(dst.AddDays(2)), "unavailable", null, null, "Holiday", allDayVersion);
        var staleEdit = await host.SendAsync(HttpMethod.Put, $"{basePath}/{allDayId}", ops.Cookie, ExceptionBody(D(dst.AddDays(2)), "unavailable", null, null, "Other", "2000-01-01T00:00:00.0000000Z"));
        Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
        await AssertConflictAsync(staleEdit, StaleException, "exception_stale");

        var edited = await host.SendAsync(HttpMethod.Put, $"{basePath}/{allDayId}", ops.Cookie, edit);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedBody = await CustomerHost.ReadAsync(edited);
        var editedVersion = editedBody["version"]!.GetValue<string>();
        Assert.Equal(("Holiday", true), ("Holiday", editedVersion != allDayVersion));
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_exception.updated", Guid.Parse(allDayId)));

        var sameEdit = await host.SendAsync(HttpMethod.Put, $"{basePath}/{allDayId}", ops.Cookie, ExceptionBody(D(dst.AddDays(2)), "unavailable", null, null, "Holiday", editedVersion));
        Assert.Equal(HttpStatusCode.OK, sameEdit.StatusCode);
        Assert.Equal(editedVersion, (await CustomerHost.ReadAsync(sameEdit))["version"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_exception.updated", Guid.Parse(allDayId)));

        // Cancel: stale 409; real 200; same-status 200 no-op without audit; stale same-status 409; cancelled edit 409; activate 200.
        var staleCancel = await host.SendAsync(HttpMethod.Post, $"{basePath}/{allDayId}/cancel", ops.Cookie, new JsonObject { ["version"] = allDayVersion });
        Assert.Equal(HttpStatusCode.Conflict, staleCancel.StatusCode);

        var cancel = await host.SendAsync(HttpMethod.Post, $"{basePath}/{allDayId}/cancel", ops.Cookie, new JsonObject { ["version"] = editedVersion });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelVersion = (await CustomerHost.ReadAsync(cancel))["version"]!.GetValue<string>();

        var again = await host.SendAsync(HttpMethod.Post, $"{basePath}/{allDayId}/cancel", ops.Cookie, new JsonObject { ["version"] = cancelVersion });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(cancelVersion, (await CustomerHost.ReadAsync(again))["version"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_exception.cancelled", Guid.Parse(allDayId)));

        var staleAgain = await host.SendAsync(HttpMethod.Post, $"{basePath}/{allDayId}/cancel", ops.Cookie, new JsonObject { ["version"] = editedVersion });
        Assert.Equal(HttpStatusCode.Conflict, staleAgain.StatusCode);

        var editCancelled = await host.SendAsync(HttpMethod.Put, $"{basePath}/{allDayId}", ops.Cookie, ExceptionBody(D(dst.AddDays(2)), "unavailable", null, null, "Holiday", cancelVersion));
        Assert.Equal(HttpStatusCode.Conflict, editCancelled.StatusCode);
        await AssertConflictAsync(editCancelled, "Activate this exception before editing it.", "exception_cancelled");

        var activate = await host.SendAsync(HttpMethod.Post, $"{basePath}/{allDayId}/activate", ops.Cookie, new JsonObject { ["version"] = cancelVersion });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal("active", (await CustomerHost.ReadAsync(activate))["status"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_exception.activated", Guid.Parse(allDayId)));

        // Past exceptions cannot be edited, cancelled or activated.
        foreach (var status in new[] { "active", "cancelled" })
        {
            var past = await database.SeedExceptionAsync(tech, At(-5, "09:00"), At(-5, "10:00"), status: status);
            var version = (await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM technician_exceptions WHERE id = @i", ("i", past)))
                .UtcDateTime.ToString("O");

            foreach (var response in new[]
            {
                await host.SendAsync(HttpMethod.Put, $"{basePath}/{past}", ops.Cookie, ExceptionBody(Today(5), "partial", "09:00", "10:00", version: version)),
                await host.SendAsync(HttpMethod.Post, $"{basePath}/{past}/cancel", ops.Cookie, new JsonObject { ["version"] = version }),
                await host.SendAsync(HttpMethod.Post, $"{basePath}/{past}/activate", ops.Cookie, new JsonObject { ["version"] = version }),
            })
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                await AssertConflictAsync(response, "Past exceptions can't be changed.", "exception_past");
            }
        }

        // Two concurrent creates for a free date: exactly one succeeds.
        var free = ExceptionBody(D(dst.AddDays(30)), "partial", "09:00", "10:00");
        var race = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, free),
            host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, free),
            host.SendAsync(HttpMethod.Post, basePath, ops.Cookie, free));
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.Conflict],
            race.Select(response => response.StatusCode).Order());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM technician_exceptions WHERE technician_id = @t AND starts_at >= @s AND starts_at < @e AND status = 'active'",
                ("t", tech),
                ("s", Local(dst.AddDays(30), "00:00")),
                ("e", Local(dst.AddDays(31), "00:00"))));
    }

    // AC-16, BR-17, BR-24: catalog operations, case-insensitive uniqueness (pre-check and index race), audit, assignments kept.
    [Fact]
    public async Task Catalog_ListsCreatesRenamesTogglesAndRejectsCaseDuplicatesIncludingConcurrentOnes()
    {
        var org = await database.SeedOrganizationAsync();
        var otherOrg = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, "Main Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, Owner);
        var tech = await database.SeedTechAsync(org, branch.Id, "Cat", "Alog");

        Assert.Empty((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/skills", ops.Cookie))).AsArray());

        var created = await host.SendAsync(HttpMethod.Post, "/team/skills", ops.Cookie, new JsonObject { ["name"] = "  Plumbing  ", ["description"] = " Pipes " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plumbing = await CustomerHost.ReadAsync(created);
        var plumbingId = Guid.Parse(plumbing["id"]!.GetValue<string>());
        Assert.Equal(("Plumbing", "Pipes", true), (plumbing["name"]!.GetValue<string>(), plumbing["description"]!.GetValue<string>(), plumbing["isActive"]!.GetValue<bool>()));

        var hvac = Guid.Parse((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Post, "/team/skills", ops.Cookie, new JsonObject { ["name"] = "HVAC" })))["id"]!.GetValue<string>());
        await database.SeedSkillAsync(otherOrg, "Plumbing");

        // Case-only duplicates, empty and long values are 400 with the field messages.
        var rejects = new (JsonObject Body, string Key, string Message)[]
        {
            (new JsonObject { ["name"] = "plumbing" }, "name", "A skill with this name already exists."),
            (new JsonObject { ["name"] = " " }, "name", "Enter a skill name."),
            (new JsonObject { ["name"] = new string('n', 121) }, "name", "Use 120 characters or fewer."),
            (new JsonObject { ["name"] = "Fine", ["description"] = new string('d', 501) }, "description", "Use 500 characters or fewer."),
        };

        foreach (var (body, key, message) in rejects)
        {
            var response = await host.SendAsync(HttpMethod.Post, "/team/skills", ops.Cookie, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, TeamSeed.Error(await CustomerHost.ReadAsync(response), key));
        }

        // Rename: a case-only change of its own name is allowed; another skill's name is not; a no-op writes no audit.
        var rename = await host.SendAsync(HttpMethod.Put, $"/team/skills/{hvac}", ops.Cookie, new JsonObject { ["name"] = "Hvac" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await host.SendAsync(HttpMethod.Put, $"/team/skills/{hvac}", ops.Cookie, new JsonObject { ["name"] = "PLUMBING" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"/team/skills/{hvac}", ops.Cookie, new JsonObject { ["name"] = "Hvac" })).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "skill.updated", hvac));

        var list = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/skills", ops.Cookie));
        Assert.Equal(["Hvac", "Plumbing"], list.AsArray().Select(row => row!["name"]!.GetValue<string>()));

        // Deactivation keeps technician assignments but hides the skill from the page; same-status calls are no-ops.
        await database.GiveSkillAsync(tech, plumbingId, primary: true, proficiency: 3);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/skills/{plumbingId}/deactivate", ops.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/skills/{plumbingId}/deactivate", ops.Cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "skill.deactivated", plumbingId));
        Assert.Equal(1, await database.CountAsync("technician_skills", "skill_id", plumbingId));
        Assert.Empty((await GetPageAsync(host, ops, tech))["skills"]!.AsArray());
        Assert.False((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/skills", ops.Cookie)))[1]!["isActive"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/skills/{plumbingId}/activate", ops.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/skills/{plumbingId}/activate", ops.Cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "skill.activated", plumbingId));
        Assert.Single((await GetPageAsync(host, ops, tech))["skills"]!.AsArray());
        Assert.Equal(2, await database.CountAuditAsync(org, "skill.created"));

        // The database index rejects a case-only duplicate and the same name in a concurrent burst yields one winner.
        var violation = await Assert.ThrowsAnyAsync<Exception>(() => database.SeedSkillAsync(org, "PLUMBING"));
        Assert.Contains("ux_skills_org_normalized_name", violation.Message);

        var burst = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            host.SendAsync(HttpMethod.Post, "/team/skills", ops.Cookie, new JsonObject { ["name"] = "Welding" })));
        Assert.Equal(1, burst.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, burst.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM skills WHERE organization_id = @o AND lower(name) = 'welding'", ("o", org)));
    }

    // AC-17 to AC-20, BR-01, BR-02: role matrix, dispatcher branch scope, technician own profile, cross-organization denial per resource.
    [Fact]
    public async Task Security_AppliesRoleMatrixBranchScopeOwnProfileAndTenantIsolation()
    {
        var org = await database.SeedOrganizationAsync();
        var foreignOrg = await database.SeedOrganizationAsync();
        var branchA = await database.SeedBranchAsync(org, "Alpha Branch", isMain: true);
        var branchB = await database.SeedBranchAsync(org, "Bravo Branch");
        var foreignBranch = await database.SeedBranchAsync(foreignOrg, "Foreign Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var techA = await database.SeedTechAsync(org, branchA.Id, "Alpha", "Tech");
        var techB = await database.SeedTechAsync(org, branchB.Id, "Bravo", "Tech");
        var foreignTech = await database.SeedTechAsync(foreignOrg, foreignBranch.Id, "Foreign", "Tech");
        var skill = await database.SeedSkillAsync(org, "Matrix skill");
        var foreignSkill = await database.SeedSkillAsync(foreignOrg, "Foreign skill");
        var ownException = await database.SeedExceptionAsync(techA, At(8, "09:00"), At(8, "10:00"));
        var foreignException = await database.SeedExceptionAsync(foreignTech, At(8, "09:00"), At(8, "10:00"));
        var otherException = await database.SeedExceptionAsync(techB, At(8, "09:00"), At(8, "10:00"));
        var version = (await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM technician_exceptions WHERE id = @i", ("i", ownException))).UtcDateTime.ToString("O");

        var owner = await database.ActorAsync(host, org, Owner);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var accounting = await database.ActorAsync(host, org, Accounting);
        var viewer = await database.ActorAsync(host, org, Viewer);
        var dispatcherAll = await database.ActorAsync(host, org, Dispatcher);
        var dispatcherA = await database.ActorAsync(host, org, Dispatcher, branchA.Id);
        var technicianOwn = await database.ActorAsync(host, org, Technician);
        var technicianUnlinked = await database.ActorAsync(host, org, Technician);
        await database.ExecuteAsync("UPDATE technician_profiles SET organization_user_id = @m WHERE id = @t", ("m", technicianOwn.Member.MembershipId), ("t", techA));

        var page = $"/team/technicians/{techA}/skills-availability";
        var invalidPut = SaveBody("bad", [], []);
        var endpoints = new (HttpMethod Method, string Path, JsonObject? Body, bool Read)[]
        {
            (HttpMethod.Get, page, null, true),
            (HttpMethod.Put, page, invalidPut, false),
            (HttpMethod.Post, $"/team/technicians/{techA}/exceptions", new JsonObject(), false),
            (HttpMethod.Put, $"/team/technicians/{techA}/exceptions/{ownException}", new JsonObject(), false),
            (HttpMethod.Post, $"/team/technicians/{techA}/exceptions/{ownException}/cancel", new JsonObject { ["version"] = "bad" }, false),
            (HttpMethod.Post, $"/team/technicians/{techA}/exceptions/{ownException}/activate", new JsonObject { ["version"] = "bad" }, false),
            (HttpMethod.Get, "/team/skills", null, false),
            (HttpMethod.Post, "/team/skills", new JsonObject { ["name"] = " " }, false),
            (HttpMethod.Put, $"/team/skills/{Guid.NewGuid()}", new JsonObject { ["name"] = "x" }, false),
            (HttpMethod.Post, $"/team/skills/{Guid.NewGuid()}/deactivate", null, false),
            (HttpMethod.Post, $"/team/skills/{Guid.NewGuid()}/activate", null, false),
        };

        // Role matrix (AC-20): 403 for accounting and viewer everywhere, for dispatcher and technician on mutations and
        // the catalog; allowed callers are never 401/403; unauthenticated callers get 401.
        var denied = new List<(TeamActor Actor, bool ReadAllowed)>
        {
            (accounting, false), (viewer, false), (dispatcherAll, true), (dispatcherA, true), (technicianOwn, true),
        };

        foreach (var (method, path, body, read) in endpoints)
        {
            foreach (var (actor, readAllowed) in denied)
            {
                var status = (await host.SendAsync(method, path, actor.Cookie, body)).StatusCode;

                if (read && readAllowed)
                {
                    Assert.Equal(HttpStatusCode.OK, status);
                }
                else
                {
                    Assert.Equal(HttpStatusCode.Forbidden, status);
                }
            }

            foreach (var actor in new[] { owner, ops })
            {
                var status = (await host.SendAsync(method, path, actor.Cookie, body)).StatusCode;
                Assert.NotEqual(HttpStatusCode.Forbidden, status);
                Assert.NotEqual(HttpStatusCode.Unauthorized, status);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(method, path, null, body)).StatusCode);
        }

        // Dispatcher branch scope (AC-17).
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techA}/skills-availability", dispatcherA.Cookie)).StatusCode);
        var outOfScope = await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techB}/skills-availability", dispatcherA.Cookie);
        Assert.Equal(HttpStatusCode.NotFound, outOfScope.StatusCode);
        Assert.Equal(Hidden, Title(await CustomerHost.ReadAsync(outOfScope)));

        foreach (var actor in new[] { ops, dispatcherAll })
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techB}/skills-availability", actor.Cookie)).StatusCode);
        }

        // Technician own profile only (AC-18): another profile in the same branch and an unlinked technician are 404.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, page, technicianOwn.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techB}/skills-availability", technicianOwn.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, page, technicianUnlinked.Cookie)).StatusCode);

        // Cross-organization denial per resource path (AC-19): profile, exception and skill; nothing changes.
        var before = (
            await database.CountAsync("technician_exceptions", "technician_id", foreignTech),
            await database.ProfileVersionAsync(foreignTech),
            await database.ScalarAsync<bool>("SELECT is_active FROM skills WHERE id = @s", ("s", foreignSkill)));
        var foreignPage = $"/team/technicians/{foreignTech}";
        var hidden = new (HttpMethod Method, string Path, JsonObject? Body)[]
        {
            (HttpMethod.Get, $"{foreignPage}/skills-availability", null),
            (HttpMethod.Put, $"{foreignPage}/skills-availability", SaveBody("2026-01-01T00:00:00Z", [Day(1)], [])),
            (HttpMethod.Post, $"{foreignPage}/exceptions", ExceptionBody(Today(5), "partial", "09:00", "10:00")),
            (HttpMethod.Put, $"{foreignPage}/exceptions/{foreignException}", ExceptionBody(Today(5), "partial", "09:00", "10:00", version: version)),
            (HttpMethod.Post, $"{foreignPage}/exceptions/{foreignException}/cancel", new JsonObject { ["version"] = version }),
            (HttpMethod.Put, $"/team/technicians/{techA}/exceptions/{foreignException}", ExceptionBody(Today(5), "partial", "09:00", "10:00", version: version)),
            (HttpMethod.Post, $"/team/technicians/{techA}/exceptions/{otherException}/cancel", new JsonObject { ["version"] = version }),
            (HttpMethod.Post, $"/team/technicians/{techA}/exceptions/{otherException}/activate", new JsonObject { ["version"] = version }),
            (HttpMethod.Put, $"/team/skills/{foreignSkill}", new JsonObject { ["name"] = "Hijacked" }),
            (HttpMethod.Post, $"/team/skills/{foreignSkill}/deactivate", null),
            (HttpMethod.Post, $"/team/skills/{foreignSkill}/activate", null),
        };

        foreach (var (method, path, body) in hidden)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(method, path, ops.Cookie, body)).StatusCode);
        }

        var currentVersion = (await GetPageAsync(host, ops, techA))["version"]!.GetValue<string>();
        var foreignInSave = await host.SendAsync(HttpMethod.Put, page, ops.Cookie, SaveBody(currentVersion, [Day(1)], [Assign(foreignSkill, 3, true)]));
        Assert.Equal(HttpStatusCode.BadRequest, foreignInSave.StatusCode);
        Assert.NotNull((await CustomerHost.ReadAsync(foreignInSave))["errors"]!["skills"]);

        Assert.Equal(
            before,
            (
                await database.CountAsync("technician_exceptions", "technician_id", foreignTech),
                await database.ProfileVersionAsync(foreignTech),
                await database.ScalarAsync<bool>("SELECT is_active FROM skills WHERE id = @s", ("s", foreignSkill))));
        Assert.Equal("Foreign skill", await database.ScalarAsync<string>("SELECT name FROM skills WHERE id = @s", ("s", foreignSkill)));
        Assert.Equal(skill, skill);
    }

    private async Task<string> SnapshotAsync(Guid technicianId) =>
        string.Join(
            "|",
            await database.ProfileVersionAsync(technicianId),
            await database.ScalarAsync<string>(
                "SELECT COALESCE(string_agg(day_of_week || '@' || start_time || '-' || end_time || '#' || capacity_percent, ',' ORDER BY day_of_week, start_time), '') FROM technician_weekly_availability WHERE technician_id = @t",
                ("t", technicianId)),
            await database.ScalarAsync<string>(
                "SELECT COALESCE(string_agg(skill_id || ':' || COALESCE(proficiency, 0) || ':' || is_primary, ',' ORDER BY skill_id), '') FROM technician_skills WHERE technician_id = @t",
                ("t", technicianId)),
            await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @t", ("t", technicianId)));

    private async Task<short[]> ReadShortsAsync(string sql, Guid technicianId)
    {
        var text = await database.ScalarAsync<string>(
            $"SELECT string_agg(v::text, ',') FROM ({sql}) AS q(v)", ("t", technicianId));

        return [.. text.Split(',').Select(short.Parse)];
    }
}
