using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using static FieldOps.IntegrationTests.Team.AvailabilitySeed;
using static FieldOps.IntegrationTests.Team.SkillsAvailabilitySeed;

namespace FieldOps.IntegrationTests.Team;

/// <summary>Time off, skills matrix and assignments, security and consumers (team-availability-skills AC-12 to AC-24).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TeamTimeOffAndSkillsTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short OperationsManager = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private const string Overlap = "This technician already has time off or an exception on these dates.";
    private const string Closed = "This time off has already ended or was removed.";
    private const string StaleTimeOff = "This time off was changed by someone else. Reload to see the latest version.";

    private static JsonObject TimeOff(DateOnly start, DateOnly end, string? note = null) =>
        new() { ["startDate"] = start.Text(), ["endDate"] = end.Text(), ["note"] = note };

    // AC-12, AC-13, BR-14: DST-correct local midnights, note handling, validation, overlaps, concurrency and audit without the note.
    [Fact]
    public async Task TimeOff_CreateStoresLocalMidnightsValidatesRejectsOverlapsAndSerializesRaces()
    {
        var org = await database.SeedOrganizationAsync();
        var chicago = await database.SeedBranchAsync(org, "Chicago Branch", isMain: true);
        await database.SetBranchHoursAsync(chicago.Id, "{}", "America/Chicago");

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var tech = await database.SeedTechAsync(org, chicago.Id, "Tim", "Off");
        var path = $"/team/technicians/{tech}/time-off";

        var dst = NextDstDay("America/Chicago", TodayUtc(3));
        var created = await host.SendAsync(HttpMethod.Post, path, ops.Cookie, TimeOff(dst.AddDays(-1), dst.AddDays(1), "  Family trip  "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await CustomerHost.ReadAsync(created);
        var id = Guid.Parse(body["id"]!.GetValue<string>());

        Assert.Equal((dst.AddDays(-1).Text(), dst.AddDays(1).Text(), "Family trip"), (body["startDate"]!.GetValue<string>(), body["endDate"]!.GetValue<string>(), body["note"]!.GetValue<string>()));
        Assert.Equal(
            (Midnight(dst.AddDays(-1), "America/Chicago"), Midnight(dst.AddDays(2), "America/Chicago")),
            (await database.ScalarAsync<DateTimeOffset>("SELECT starts_at FROM technician_exceptions WHERE id = @e", ("e", id)),
                await database.ScalarAsync<DateTimeOffset>("SELECT ends_at FROM technician_exceptions WHERE id = @e", ("e", id))));
        Assert.NotEqual(
            TimeSpan.FromHours(72),
            await database.ScalarAsync<DateTimeOffset>("SELECT ends_at FROM technician_exceptions WHERE id = @e", ("e", id))
                - await database.ScalarAsync<DateTimeOffset>("SELECT starts_at FROM technician_exceptions WHERE id = @e", ("e", id)));

        // The audit row has ids and dates only; the note is never written.
        var audit = await database.AuditAsync(org, "technician.time_off_created");
        Assert.DoesNotContain("Family", audit, StringComparison.Ordinal);
        Assert.Contains(dst.AddDays(1).Text(), audit, StringComparison.Ordinal);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician.time_off_created", id));

        // A single day without a note stores null; adjacent ranges are not overlaps.
        var single = await OkOrCreatedAsync(await host.SendAsync(HttpMethod.Post, path, ops.Cookie, TimeOff(dst.AddDays(2), dst.AddDays(2), "   ")));
        Assert.Null(single["note"]);
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_exceptions WHERE technician_id = @t AND reason IS NULL", ("t", tech)));

        // Validation table.
        var today = TodayUtc();
        var invalid = new (JsonObject Body, string Key, string Message)[]
        {
            (TimeOff(today.AddDays(-2), today.AddDays(1)), "startDate", "Choose today or a future date."),
            (TimeOff(today.AddDays(10), today.AddDays(9)), "endDate", "End date can't be before start date."),
            (TimeOff(today.AddDays(10), today.AddDays(10), new string('n', 251)), "note", "Use 250 characters or fewer."),
        };

        foreach (var (invalidBody, key, message) in invalid)
        {
            await BadRequestAsync(await host.SendAsync(HttpMethod.Post, path, ops.Cookie, invalidBody), key, message);
        }

        // Overlaps: another time off, a partial exception, and a cancelled one that does not block.
        await database.SeedExceptionAsync(tech, Midnight(dst.AddDays(20)).AddHours(10), Midnight(dst.AddDays(20)).AddHours(12));
        await database.SeedExceptionAsync(tech, Midnight(dst.AddDays(30)), Midnight(dst.AddDays(31)), status: "cancelled");

        foreach (var (start, end) in new[] { (dst, dst), (dst.AddDays(-3), dst.AddDays(-1)), (dst.AddDays(20), dst.AddDays(20)) })
        {
            await AssertConflictAsync(await host.SendAsync(HttpMethod.Post, path, ops.Cookie, TimeOff(start, end)), Overlap, "time_off_overlap");
        }

        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, path, ops.Cookie, TimeOff(dst.AddDays(30), dst.AddDays(30)))).StatusCode);

        // Concurrent overlapping creates: exactly one succeeds.
        var race = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => host.SendAsync(HttpMethod.Post, path, ops.Cookie, TimeOff(dst.AddDays(40), dst.AddDays(42)))));
        Assert.Equal(1, race.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, race.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        // The drawer lists the notes (Manage), ordered by start; the grid carries no note.
        var drawer = await GetDrawerAsync(host, ops, tech);
        Assert.Equal("Family trip", drawer["timeOff"]![0]!["note"]!.GetValue<string>());
        var grid = await host.SendAsync(HttpMethod.Get, $"/team/availability?weekStart={dst.AddDays(-(((int)dst.DayOfWeek + 6) % 7)).Text()}", ops.Cookie);
        Assert.DoesNotContain("Family", await grid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<JsonNode> OkOrCreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await CustomerHost.ReadAsync(response);
    }

    // AC-14, BR-15: not started is cancelled, started is shortened to today 00:00, closed and stale are 409, foreign ids are 404.
    [Fact]
    public async Task TimeOff_RetireCancelsShortensAndRejectsClosedStaleAndForeignRanges()
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, Owner);
        var tech = await database.SeedTechAsync(org, main.Id, "Ret", "Ire");
        var other = await database.SeedTechAsync(org, main.Id, "Oth", "Er");
        var today = TodayUtc();

        var future = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(5)), Midnight(today.AddDays(8)), reason: "Secret note");
        var started = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(-1)), Midnight(today.AddDays(3)), reason: "Secret note");
        var ended = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(-4)), Midnight(today), reason: "Old");
        var gone = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(10)), Midnight(today.AddDays(12)), status: "cancelled");
        var partial = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(20)).AddHours(9), Midnight(today.AddDays(20)).AddHours(11));
        var foreign = await database.SeedExceptionAsync(other, Midnight(today.AddDays(5)), Midnight(today.AddDays(6)));

        async Task<string> VersionAsync(Guid exception) =>
            (await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM technician_exceptions WHERE id = @e", ("e", exception))).UtcDateTime.ToString("O");

        Task<HttpResponseMessage> Retire(Guid exception, string version, Guid? technician = null) =>
            host.SendAsync(HttpMethod.Post, $"/team/technicians/{technician ?? tech}/time-off/{exception}/retire", ops.Cookie, Token(version));

        // 404: another technician's id and a partial (not full-day) exception.
        Assert.Equal(HttpStatusCode.NotFound, (await Retire(foreign, await VersionAsync(foreign))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Retire(partial, await VersionAsync(partial))).StatusCode);

        // Closed is checked before stale; stale changes nothing.
        await AssertConflictAsync(await Retire(ended, "2020-01-01T00:00:00Z"), Closed, "time_off_closed");
        await AssertConflictAsync(await Retire(gone, await VersionAsync(gone)), Closed, "time_off_closed");
        await AssertConflictAsync(await Retire(future, "2020-01-01T00:00:00Z"), StaleTimeOff, "stale");
        Assert.Equal("active", await database.ScalarAsync<string>("SELECT status FROM technician_exceptions WHERE id = @e", ("e", future)));

        // Not started: cancelled and the response carries null.
        var cancelled = await OkAsync(await Retire(future, await VersionAsync(future)));
        Assert.Null(cancelled["timeOff"]);
        Assert.Equal("cancelled", await database.ScalarAsync<string>("SELECT status FROM technician_exceptions WHERE id = @e", ("e", future)));

        // Started: ends at today 00:00, stays active, history kept through yesterday.
        var shortened = await OkAsync(await Retire(started, await VersionAsync(started)));
        Assert.Equal(today.AddDays(-1).Text(), shortened["timeOff"]!["endDate"]!.GetValue<string>());
        Assert.Equal(
            ("active", Midnight(today)),
            (await database.ScalarAsync<string>("SELECT status FROM technician_exceptions WHERE id = @e", ("e", started)),
                await database.ScalarAsync<DateTimeOffset>("SELECT ends_at FROM technician_exceptions WHERE id = @e", ("e", started))));
        await AssertConflictAsync(await Retire(started, await VersionAsync(started)), Closed, "time_off_closed");

        var removed = await database.AuditAsync(org, "technician.time_off_removed");
        Assert.Contains("\"mode\":\"cancelled\"", removed.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"shortened\"", removed.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("previousEndDate", removed, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", removed, StringComparison.Ordinal);
        Assert.Equal(2, await database.CountAuditAsync(org, "technician.time_off_removed"));
    }

    // AC-15 to AC-18, BR-17 to BR-21: matrix columns, counts, filters, sort, paging, replace with promotion, archived kept, no-op, atomic failures, stale.
    [Fact]
    public async Task Skills_MatrixFiltersAndAssignmentsReplaceTransactionally()
    {
        var org = await database.SeedOrganizationAsync();
        var foreignOrg = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        var second = await database.SeedBranchAsync(org, "Second Branch");

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var alpha = await database.SeedSkillAsync(org, "Alpha");
        var bravo = await database.SeedSkillAsync(org, "Bravo");
        var charlie = await database.SeedSkillAsync(org, "Charlie");
        var delta = await database.SeedSkillAsync(org, "Delta");
        var retired = await database.SeedSkillAsync(org, "Retired", active: false);
        var foreignSkill = await database.SeedSkillAsync(foreignOrg, "Foreign");

        var zed = await database.SeedTechAsync(org, main.Id, "Zed", "Last");
        var amy = await database.SeedTechAsync(org, second.Id, "Amy", "First");
        var none = await database.SeedTechAsync(org, main.Id, "Nia", "Nobody");
        await database.GiveSkillAsync(zed, alpha, primary: true, proficiency: 5);
        await database.GiveSkillAsync(zed, bravo, proficiency: 2);
        await database.GiveSkillAsync(zed, retired, proficiency: 4);
        await database.GiveSkillAsync(amy, retired, proficiency: 4);

        async Task<JsonNode> Matrix(string query = "") =>
            await OkAsync(await host.SendAsync(HttpMethod.Get, $"/team/skills-matrix?{query}", ops.Cookie));

        var page = await Matrix();
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], page["skills"]!.AsArray().Select(skill => skill!["name"]!.GetValue<string>()));
        Assert.Equal([amy, none, zed], Ids(page));
        Assert.Equal((2, 0), (Row(page, zed)["skillCount"]!.GetValue<int>(), Row(page, amy)["skillCount"]!.GetValue<int>()));
        Assert.Equal([alpha, bravo], Row(page, zed)["skillIds"]!.AsArray().Select(id => Guid.Parse(id!.GetValue<string>())));
        Assert.Equal([zed, none, amy], Ids(await Matrix("sort=desc")));
        Assert.Equal([zed], Ids(await Matrix($"skillId={bravo}")));
        Assert.Equal([zed], Ids(await Matrix("coverage=with")));
        Assert.Equal([amy, none], Ids(await Matrix("coverage=without")));
        Assert.Equal([amy], Ids(await Matrix($"branchId={second.Id}")));
        Assert.Equal([zed], Ids(await Matrix("search=zed")));

        foreach (var query in new[] { $"skillId={retired}", $"skillId={foreignSkill}", "coverage=x", "sort=up", "page=0" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/team/skills-matrix?{query}", ops.Cookie)).StatusCode);
        }

        // Drawer read lists active skills only.
        var path = $"/team/technicians/{zed}/skill-assignments";
        var drawer = await OkAsync(await host.SendAsync(HttpMethod.Get, path, ops.Cookie));
        Assert.Equal(2, drawer["skillIds"]!.AsArray().Count);

        // Remove the primary (Alpha), keep Bravo, add Charlie and Delta: Bravo is promoted (first remaining by name... Bravo < Charlie).
        var version = drawer["updatedAt"]!.GetValue<string>();
        var before = await database.ProfileVersionAsync(zed);
        var saved = await OkAsync(await host.SendAsync(HttpMethod.Put, path, ops.Cookie, SkillsBody(version, bravo, charlie, delta)));
        Assert.Equal(3, saved["skillIds"]!.AsArray().Count);
        Assert.True(await database.ProfileVersionAsync(zed) > before);
        Assert.Equal(
            "bravo:2:true;charlie:3:false;delta:3:false;retired:4:false",
            await database.ScalarAsync<string>(
                """
                SELECT string_agg(lower(s.name) || ':' || ts.proficiency || ':' || ts.is_primary, ';' ORDER BY s.name)
                FROM technician_skills ts JOIN skills s ON s.id = ts.skill_id WHERE ts.technician_id = @t
                """,
                ("t", zed)));

        var audit = await database.AuditAsync(org, "technician.skills_updated");
        Assert.Contains(alpha.ToString(), audit, StringComparison.Ordinal);
        Assert.Contains(charlie.ToString(), audit, StringComparison.Ordinal);

        // Same set: 200, nothing written, no audit; stale token: 409.
        var stamp = await database.ProfileVersionAsync(zed);
        await OkAsync(await host.SendAsync(HttpMethod.Put, path, ops.Cookie, SkillsBody(saved["updatedAt"]!.GetValue<string>(), delta, bravo, charlie)));
        Assert.Equal((stamp, 1), (await database.ProfileVersionAsync(zed), (int)await database.CountAuditAsync(org, "technician.skills_updated", zed)));
        await AssertConflictAsync(
            await host.SendAsync(HttpMethod.Put, path, ops.Cookie, SkillsBody(version, bravo)),
            "This technician was updated by someone else. Reload to see the latest changes.",
            "stale");

        // Duplicate, archived, foreign and a bad last id: 400 skillIds and nothing changes.
        var token = saved["updatedAt"]!.GetValue<string>();
        var snapshot = await database.CountAsync("technician_skills", "technician_id", zed);
        var rejects = new (JsonObject Body, string Message)[]
        {
            (SkillsBody(token, bravo, bravo), "Choose each skill once."),
            (SkillsBody(token, bravo, retired), "Choose active skills from the catalog."),
            (SkillsBody(token, alpha, foreignSkill), "Choose active skills from the catalog."),
            (SkillsBody(token, alpha, bravo, Guid.NewGuid()), "Choose active skills from the catalog."),
        };

        foreach (var (body, message) in rejects)
        {
            await BadRequestAsync(await host.SendAsync(HttpMethod.Put, path, ops.Cookie, body), "skillIds", message);
        }

        Assert.Equal((snapshot, stamp), (await database.CountAsync("technician_skills", "technician_id", zed), await database.ProfileVersionAsync(zed)));

        // Removing every active skill leaves no primary and keeps the archived assignment.
        await OkAsync(await host.SendAsync(HttpMethod.Put, path, ops.Cookie, SkillsBody(token)));
        Assert.Equal(1, await database.CountAsync("technician_skills", "technician_id", zed));
    }

    private static JsonObject SkillsBody(string version, params Guid[] ids) =>
        new() { ["updatedAt"] = version, ["skillIds"] = new JsonArray([.. ids.Select(id => (JsonNode?)id.ToString())]) };

    // AC-20 to AC-22: role matrix over every endpoint, dispatcher branch scope, and one cross-organization denial per path.
    [Theory]
    [InlineData(Owner, true, true)]
    [InlineData(OperationsManager, true, true)]
    [InlineData(Dispatcher, true, false)]
    [InlineData(Technician, false, false)]
    [InlineData(Accounting, false, false)]
    [InlineData(Viewer, false, false)]
    public async Task Endpoints_EnforceReadAndManagePoliciesPerRole(short role, bool canRead, bool canManage)
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var actor = await database.ActorAsync(host, org, role);
        var tech = await database.SeedTechAsync(org, main.Id, "Rol", "Matrix");
        var skill = await database.SeedSkillAsync(org, "Any");
        var timeOff = await database.SeedExceptionAsync(tech, Midnight(TodayUtc(5)), Midnight(TodayUtc(7)));
        var drawer = $"/team/technicians/{tech}";

        var reads = new[] { "/team/availability?weekStart=2027-03-08", "/team/skills-matrix" };
        var manage = new (HttpMethod Method, string Path, JsonObject? Body)[]
        {
            (HttpMethod.Get, $"{drawer}/availability", null),
            (HttpMethod.Put, $"{drawer}/availability", SaveBody("2020-01-01T00:00:00Z", Week())),
            (HttpMethod.Post, $"{drawer}/availability/use-branch-hours", Token("2020-01-01T00:00:00Z")),
            (HttpMethod.Post, $"{drawer}/time-off", TimeOff(TodayUtc(50), TodayUtc(50))),
            (HttpMethod.Post, $"{drawer}/time-off/{timeOff}/retire", Token("2020-01-01T00:00:00Z")),
            (HttpMethod.Get, $"{drawer}/skill-assignments", null),
            (HttpMethod.Put, $"{drawer}/skill-assignments", SkillsBody("2020-01-01T00:00:00Z", skill)),
        };

        foreach (var path in reads)
        {
            var status = (await host.SendAsync(HttpMethod.Get, path, actor.Cookie)).StatusCode;
            Assert.Equal(canRead ? HttpStatusCode.OK : HttpStatusCode.Forbidden, status);
        }

        foreach (var (method, path, body) in manage)
        {
            var status = (await host.SendAsync(method, path, actor.Cookie, body)).StatusCode;

            if (canManage)
            {
                Assert.NotEqual(HttpStatusCode.Forbidden, status);
                Assert.NotEqual(HttpStatusCode.NotFound, status);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, status);
            }
        }
    }

    [Fact]
    public async Task Scope_RestrictsDispatcherRowsAndDeniesForeignOutOfScopeAndInactiveProfiles()
    {
        var org = await database.SeedOrganizationAsync();
        var foreignOrg = await database.SeedOrganizationAsync();
        var a = await database.SeedBranchAsync(org, "Alpha Branch", isMain: true);
        var b = await database.SeedBranchAsync(org, "Bravo Branch");
        var foreignBranch = await database.SeedBranchAsync(foreignOrg, "Foreign Branch", isMain: true);

        await using var host = CustomerHost.Create(database);
        var dispatcher = await database.ActorAsync(host, org, Dispatcher, a.Id);
        var ownerOrg = await database.ActorAsync(host, org, Owner);
        var scopedOps = await database.ActorAsync(host, org, OperationsManager, a.Id);
        var techA = await database.SeedTechAsync(org, a.Id, "Ann", "Alpha");
        var techB = await database.SeedTechAsync(org, b.Id, "Bob", "Bravo");
        var inactive = await database.SeedTechAsync(org, a.Id, "Ian", "Inactive", status: "inactive");
        var foreign = await database.SeedTechAsync(foreignOrg, foreignBranch.Id, "Fay", "Foreign");
        var foreignSkill = await database.SeedSkillAsync(foreignOrg, "Foreign");
        var foreignTimeOff = await database.SeedExceptionAsync(foreign, Midnight(TodayUtc(5)), Midnight(TodayUtc(6)));
        var ownTimeOff = await database.SeedExceptionAsync(techA, Midnight(TodayUtc(5)), Midnight(TodayUtc(6)));

        // Dispatcher: only branch A rows on both tabs, 400 for branch B, 403 on drawers.
        Assert.Equal([techA], Ids(await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability?weekStart=2027-03-08", dispatcher.Cookie))));
        Assert.Equal([techA], Ids(await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/skills-matrix", dispatcher.Cookie))));
        Assert.Equal([techA, techB], Ids(await OkAsync(await host.SendAsync(HttpMethod.Get, "/team/availability?weekStart=2027-03-08", ownerOrg.Cookie))));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/team/availability?weekStart=2027-03-08&branchId={b.Id}", dispatcher.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/team/skills-matrix?branchId={b.Id}", dispatcher.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techA}/availability", dispatcher.Cookie)).StatusCode);

        // Foreign and inactive profiles (Manage roles are not branch-limited) are the same 404 on every Manage path; nothing changes.
        foreach (var target in new[] { foreign, inactive })
        {
            var drawer = $"/team/technicians/{target}";
            var responses = new[]
            {
                await host.SendAsync(HttpMethod.Get, $"{drawer}/availability", scopedOps.Cookie),
                await host.SendAsync(HttpMethod.Put, $"{drawer}/availability", scopedOps.Cookie, SaveBody("2020-01-01T00:00:00Z", Week())),
                await host.SendAsync(HttpMethod.Post, $"{drawer}/availability/use-branch-hours", scopedOps.Cookie, Token("2020-01-01T00:00:00Z")),
                await host.SendAsync(HttpMethod.Post, $"{drawer}/time-off", scopedOps.Cookie, TimeOff(TodayUtc(60), TodayUtc(60))),
                await host.SendAsync(HttpMethod.Get, $"{drawer}/skill-assignments", scopedOps.Cookie),
                await host.SendAsync(HttpMethod.Put, $"{drawer}/skill-assignments", scopedOps.Cookie, SkillsBody("2020-01-01T00:00:00Z")),
            };

            foreach (var response in responses)
            {
                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} -> {response.StatusCode}");
                Assert.Equal(Hidden, (await CustomerHost.ReadAsync(response))["title"]!.GetValue<string>());
            }
        }

        Assert.Equal(0, await database.CountAsync("technician_exceptions", "technician_id", foreign) - 1);

        // A time off id of another technician is 404 and the foreign skill in a save is 400 skillIds.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{techA}/time-off/{foreignTimeOff}/retire", ownerOrg.Cookie, Token("2020-01-01T00:00:00Z"))).StatusCode);
        var version = (await GetDrawerAsync(host, ownerOrg, techA))["updatedAt"]!.GetValue<string>();
        await BadRequestAsync(
            await host.SendAsync(HttpMethod.Put, $"/team/technicians/{techA}/skill-assignments", ownerOrg.Cookie, SkillsBody(version, foreignSkill)),
            "skillIds",
            "Choose active skills from the catalog.");
        Assert.Equal("active", await database.ScalarAsync<string>("SELECT status FROM technician_exceptions WHERE id = @e", ("e", ownTimeOff)));
    }

    // AC-05, AC-24, BR-27 and the Team cancelled-time-off decision: Team and Design 23 read inherited hours; time off is read-only there.
    [Fact]
    public async Task Consumers_TeamAndDesign23UseInheritedHoursAndShowMultiDayTimeOff()
    {
        var org = await database.SeedOrganizationAsync();
        var main = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        await database.SetBranchHoursAsync(main.Id, Hours("00:00", "23:59", DayNames));

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, Owner);
        var tech = await database.SeedTechAsync(org, main.Id, "Con", "Sumer");
        var today = TodayUtc();

        async Task<string> TodayStatusAsync() =>
            (await OkAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}", ops.Cookie)))["today"]!["todayStatus"]!.GetValue<string>();

        // Team: inherited all-day hours make the technician available; cancelled time off is released, active time off is not.
        Assert.Equal("available", await TodayStatusAsync());
        var cancelledToday = await database.SeedExceptionAsync(tech, Midnight(today), Midnight(today.AddDays(1)), status: "cancelled");
        Assert.Equal("available", await TodayStatusAsync());
        await database.ExecuteAsync("UPDATE technician_exceptions SET status = 'active' WHERE id = @e", ("e", cancelledToday));
        Assert.Equal("time_off", await TodayStatusAsync());
        await database.ExecuteAsync("DELETE FROM technician_exceptions WHERE id = @e", ("e", cancelledToday));

        // Design 23 steps are 30 minutes: the branch now closes at 23:30.
        await database.SetBranchHoursAsync(main.Id, Hours("00:00", "23:30", DayNames));

        // Design 23: inherited weekly table, multi-day time off (started yesterday) read-only, single day keeps its kind.
        var range = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(-1)), Midnight(today.AddDays(3)), reason: "Range");
        var singleDay = await database.SeedExceptionAsync(tech, Midnight(today.AddDays(6)), Midnight(today.AddDays(7)), reason: "One day");
        var page = await OkAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}/skills-availability", ops.Cookie));

        Assert.True(page["usesBranchHours"]!.GetValue<bool>());
        Assert.Equal(7, page["weeklyAvailability"]!.AsArray().Count);
        Assert.Equal(("00:00", "23:30", 100), (
            page["weeklyAvailability"]![0]!["start"]!.GetValue<string>(),
            page["weeklyAvailability"]![0]!["end"]!.GetValue<string>(),
            page["weeklyAvailability"]![0]!["capacityPercent"]!.GetValue<int>()));
        var items = page["exceptions"]!.AsArray();
        Assert.Equal(["time_off", "unavailable"], items.Select(item => item!["kind"]!.GetValue<string>()));
        Assert.Equal((today.AddDays(-1).Text(), today.AddDays(2).Text()), (items[0]!["date"]!.GetValue<string>(), items[0]!["endDate"]!.GetValue<string>()));

        // The read-only range cannot be changed there; a new exception inside it is the BR-14 409.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{tech}/exceptions/{range}/cancel", ops.Cookie, new JsonObject { ["version"] = items[0]!["version"]!.GetValue<string>() })).StatusCode);
        var inside = await host.SendAsync(
            HttpMethod.Post,
            $"/team/technicians/{tech}/exceptions",
            ops.Cookie,
            ExceptionBody(today.AddDays(1).Text(), "partial", "10:00", "11:00"));
        await AssertConflictAsync(inside, "This technician already has an active exception on this date.", "exception_date_taken");
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{tech}/exceptions", ops.Cookie, ExceptionBody(today.AddDays(4).Text(), "unavailable", null, null))).StatusCode);

        // Saving creates the override.
        var save = await host.SendAsync(
            HttpMethod.Put,
            $"/team/technicians/{tech}/skills-availability",
            ops.Cookie,
            SaveBody(page["version"]!.GetValue<string>(), WeeklyFrom(page), []));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.False(await database.ScalarAsync<bool>("SELECT uses_branch_hours FROM technician_profiles WHERE id = @t", ("t", tech)));
        Assert.Equal(7, await database.CountAsync("technician_weekly_availability", "technician_id", tech));
        Assert.NotEqual(Guid.Empty, singleDay);
    }

    // AC-05: Dispatch evaluation uses inherited branch hours; conflict codes are unchanged and a branch change applies at once.
    [Fact]
    public async Task Dispatch_EvaluatesInheritingTechniciansAgainstBranchHours()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, Owner);
        await database.SetBranchHoursAsync(world.BranchA, Hours("08:00", "18:00", DayNames));

        var inheriting = await database.SeedTechAsync(world.Org, world.BranchA, "Ina", "Herit");
        var custom = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Cas", "Tom");
        var order = await database.SeedOrderAsync(world, ownerMember.UserId);
        var inside = DispatchSeed.Future(4, 9);
        var before = DispatchSeed.Future(4, 7);

        async Task<string[]> Codes(Guid tech, DateTimeOffset slot) =>
            DispatchSeed.Codes((await DispatchSeed.ReadAsync(await DispatchSeed.EvaluateAsync(host, owner, order.Visit, DispatchSeed.EvaluationBody(slot, 60, tech))))["conflicts"]);

        Assert.Empty(await Codes(inheriting, inside));
        Assert.Equal(["outside_availability"], await Codes(inheriting, before));

        // The branch now opens at 10:00: the inheriting technician is outside at 09:00, the custom one is unaffected.
        await database.SetBranchHoursAsync(world.BranchA, Hours("10:00", "18:00", DayNames));
        Assert.Equal(["outside_availability"], await Codes(inheriting, inside));
        Assert.Empty(await Codes(custom, inside));
    }
}
