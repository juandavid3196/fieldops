using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Team;

/// <summary>Team and technician management: AC-02 to AC-19 backend evidence.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TeamEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short OperationsManager = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    // AC-02, AC-05, AC-18: filters, sort, paging beyond the last page, Team members count, metrics, alerts,
    // coverage and invalid queries.
    [Fact]
    public async Task ListMetricsAndCoverage_ApplyFiltersPagingAndRejectInvalidQueries()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branchA = await database.SeedBranchAsync(org, "Alpha Branch", isMain: true);
        var branchB = await database.SeedBranchAsync(org, "Bravo Branch");
        var foreignSkill = await database.SeedSkillAsync(other, "Foreign skill");
        var hvac = await database.SeedSkillAsync(org, "HVAC");
        var plumbing = await database.SeedSkillAsync(org, "Plumbing");
        var lonely = await database.SeedSkillAsync(org, "Zebra craft");
        var retired = await database.SeedSkillAsync(org, "Retired", active: false);

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var linkedMember = await database.SeedMemberAsync(org, Technician, "Lin", "Ked");

        var ids = new List<Guid>();

        for (var i = 1; i <= 12; i++)
        {
            ids.Add(await database.SeedTechAsync(
                org, i <= 10 ? branchA.Id : branchB.Id, $"Tech{i:00}", "Worker",
                email: i == 1 ? "First.Tech@Example.com" : null,
                phone: i == 2 ? "(512) 555-0199" : null,
                code: i == 3 ? "EMP-3" : null,
                membershipId: i == 12 ? linkedMember.MembershipId : null));
        }

        var inactive = await database.SeedTechAsync(org, branchA.Id, "Idle", "Person", "inactive");
        await database.SeedTechAsync(org, branchA.Id, "Susp", "Ended", "suspended");
        await database.SeedTechAsync(other, (await database.SeedBranchAsync(other, isMain: true)).Id, "Foreign", "Person");

        // Five HVAC, four Plumbing (one inactive technician must not count), none Zebra.
        foreach (var id in ids.Take(5))
        {
            await database.GiveSkillAsync(id, hvac);
        }

        foreach (var id in ids.Skip(5).Take(4))
        {
            await database.GiveSkillAsync(id, plumbing);
        }

        await database.GiveSkillAsync(inactive, plumbing);
        await database.GiveSkillAsync(ids[0], retired);

        // On job by status (BR-05) with a visit started now and no availability: at capacity, "No availability".
        await database.SeedAssignmentAsync(
            org, ops.Member.UserId, branchA.Id, ids[0], "in_progress", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30));

        var page1 = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/technicians", ops.Cookie));
        Assert.Equal(10, page1["items"]!.AsArray().Count);
        Assert.Equal(12, page1["totalCount"]!.GetValue<int>());
        Assert.Equal(12, page1["teamMembersCount"]!.GetValue<int>());
        Assert.Equal(10, page1["pageSize"]!.GetValue<int>());
        Assert.Equal("Tech01 Worker", TeamSeed.Names(page1)[0]);

        var first = page1["items"]![0]!;
        Assert.Equal("on_job", first["todayStatus"]!.GetValue<string>());
        Assert.Equal("no_availability", first["workloadState"]!.GetValue<string>());
        Assert.True(first["atCapacity"]!.GetValue<bool>());
        Assert.Equal(1, first["jobs"]!.GetValue<int>());
        Assert.Equal("none", first["nextAvailable"]!["kind"]!.GetValue<string>());
        Assert.Equal("off", page1["items"]![1]!["todayStatus"]!.GetValue<string>());
        Assert.Equal("Alpha Branch", first["branchName"]!.GetValue<string>());

        var beyond = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/technicians?page=3", ops.Cookie));
        Assert.Empty(beyond["items"]!.AsArray());
        Assert.Equal(12, beyond["totalCount"]!.GetValue<int>());
        Assert.Equal(["Tech11 Worker", "Tech12 Worker"], TeamSeed.Names(
            await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/technicians?page=2", ops.Cookie))));
        Assert.Equal("Tech12 Worker", TeamSeed.Names(
            await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/technicians?sort=name_desc", ops.Cookie)))[0]);

        async Task<JsonNode> Get(string query) =>
            await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians?{query}", ops.Cookie));

        // Search: email (case-insensitive), phone digits (3+), profile ID; statuses only under their option.
        Assert.Equal(["Tech01 Worker"], TeamSeed.Names(await Get("search=FIRST.tech@")));
        Assert.Equal(["Tech02 Worker"], TeamSeed.Names(await Get("search=555-0199")));
        Assert.Equal(["Tech03 Worker"], TeamSeed.Names(await Get("search=emp-3")));
        Assert.Equal(["Idle Person"], TeamSeed.Names(await Get("status=inactive")));
        Assert.Equal(["Susp Ended"], TeamSeed.Names(await Get("status=suspended")));
        Assert.Equal(["Tech01 Worker"], TeamSeed.Names(await Get("status=on_job")));
        Assert.Equal(11, (await Get("status=off"))["totalCount"]!.GetValue<int>());
        Assert.Equal(12, (await Get("search=zzzz"))["teamMembersCount"]!.GetValue<int>());
        Assert.Equal(0, (await Get("search=zzzz"))["totalCount"]!.GetValue<int>());
        Assert.Equal(5, (await Get($"skillId={hvac}"))["totalCount"]!.GetValue<int>());
        Assert.Equal(["Tech12 Worker"], TeamSeed.Names(await Get("accountLink=linked")));
        Assert.Equal(11, (await Get("accountLink=not_linked"))["totalCount"]!.GetValue<int>());

        var branchBList = await Get($"branchId={branchB.Id}");
        Assert.Equal(2, branchBList["totalCount"]!.GetValue<int>());
        Assert.Equal(2, branchBList["teamMembersCount"]!.GetValue<int>());

        // Metrics (BR-08, BR-14): independent of search; the alerts name the first match.
        var metrics = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/metrics", ops.Cookie));
        Assert.Equal(12, metrics["activeProfiles"]!.GetValue<int>());
        Assert.Equal(0, metrics["availableNow"]!.GetValue<int>());
        Assert.Equal(1, metrics["onJobs"]!.GetValue<int>());
        Assert.Equal(1, metrics["atCapacity"]!.GetValue<int>());
        Assert.Equal(11, metrics["unlinkedAccounts"]!.GetValue<int>());
        Assert.Equal("Tech01 Worker", metrics["capacityAlert"]!["name"]!.GetValue<string>());
        Assert.True(metrics["capacityAlert"]!["noAvailability"]!.GetValue<bool>());
        Assert.Equal(0, metrics["capacityAlert"]!["moreCount"]!.GetValue<int>());
        Assert.Equal("Tech01 Worker", metrics["unlinkedAlert"]!["name"]!.GetValue<string>());
        Assert.Equal(10, metrics["unlinkedAlert"]!["moreCount"]!.GetValue<int>());
        Assert.Equal(
            2,
            (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/metrics?branchId={branchB.Id}&period=week", ops.Cookie)))["activeProfiles"]!.GetValue<int>());

        // Coverage: every active skill, count desc then name, Healthy >= 5, Watch = 4, Low <= 3; inactive skill absent.
        var coverage = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/skill-coverage", ops.Cookie));
        var rows = coverage.AsArray().Select(row => (row!["name"]!.GetValue<string>(), row["technicianCount"]!.GetValue<int>(), row["health"]!.GetValue<string>())).ToArray();
        Assert.Equal([("HVAC", 5, "healthy"), ("Plumbing", 4, "watch"), ("Zebra craft", 0, "low")], rows);

        // Invalid query values are 400, and a skill of another organization is errors.skillId.
        foreach (var query in new[] { "status=bogus", "accountLink=x", "sort=up", "period=month", "page=0", "page=abc", "skillId=nope", $"skillId={foreignSkill}", $"search={new string('a', 101)}" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/team/technicians?{query}", ops.Cookie)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, "/team/metrics?period=month", ops.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, "/team/skill-coverage?branchId=nope", ops.Cookie)).StatusCode);

        var options = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/options", ops.Cookie));
        Assert.Equal(["Alpha Branch", "Bravo Branch"], options["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal(["HVAC", "Plumbing", "Zebra craft"], options["skills"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToArray());
        Assert.NotEqual(Guid.Empty, lonely);
    }

    // AC-09, AC-10, AC-11: create/edit validation, normalization, uniqueness, audit without email/phone, no-op edit.
    [Fact]
    public async Task CreateAndEdit_ValidateNormalizeAuditAndSkipNoOps()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        var closed = await database.SeedBranchAsync(org, "Closed Branch", isActive: false);

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);

        var created = await host.SendAsync(HttpMethod.Post, "/team/technicians", ops.Cookie, TeamSeed.Body(branch.Id));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = Guid.Parse((await CustomerHost.ReadAsync(created))["id"]!.GetValue<string>());
        Assert.Equal($"/team/technicians/{id}", created.Headers.Location?.OriginalString);

        Assert.Equal(
            ("grace@example.com", "G-100", "active", false),
            (
                await database.ScalarAsync<string>("SELECT email FROM technician_profiles WHERE id = @i", ("i", id)),
                await database.ScalarAsync<string>("SELECT employee_code FROM technician_profiles WHERE id = @i", ("i", id)),
                await database.ScalarAsync<string>("SELECT status FROM technician_profiles WHERE id = @i", ("i", id)),
                await database.ScalarAsync<bool>("SELECT organization_user_id IS NOT NULL FROM technician_profiles WHERE id = @i", ("i", id))));

        var createdAudit = await database.ScalarAsync<string>(
            "SELECT concat(after_data::text, metadata::text, before_data::text) FROM audit_logs WHERE entity_id = @i AND action = 'technician_profile.created'", ("i", id));
        Assert.Contains(branch.Id.ToString(), createdAudit);
        Assert.DoesNotContain("grace", createdAudit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("555", createdAudit);

        // Duplicates are case-insensitive; invalid fields carry their table messages; nothing is saved.
        var other = await database.SeedTechAsync(org, branch.Id, "Other", "Person", email: "taken@example.com", code: "TAKEN-1");
        var cases = new (JsonObject Body, string Key, string Message)[]
        {
            (TeamSeed.Body(branch.Id, email: "GRACE@example.COM", code: "z-1"), "email", "Another technician profile already uses this email."),
            (TeamSeed.Body(branch.Id, email: "new@example.com", code: "g-100"), "employeeCode", "This profile ID is already in use."),
            (TeamSeed.Body(branch.Id, first: " "), "firstName", "Enter a first name."),
            (TeamSeed.Body(branch.Id, last: new string('x', 101)), "lastName", "Use 100 characters or fewer."),
            (TeamSeed.Body(branch.Id, email: "not-an-email", code: "ok-1"), "email", "Enter a valid email address."),
            (TeamSeed.Body(branch.Id, email: null, phone: "12-34", code: "ok-2"), "phone", "Enter a valid phone number."),
            (TeamSeed.Body(branch.Id, email: null, code: "bad code!"), "employeeCode", "Use letters, numbers, hyphens or underscores."),
            (TeamSeed.Body(branch.Id, email: null, code: "ok-3", notes: new string('n', 2001)), "notes", "Use 2000 characters or fewer."),
            (TeamSeed.Body(closed.Id, email: null, code: "ok-4"), "branchId", TeamSeed.NoBranchAccess),
            (TeamSeed.Body(Guid.NewGuid(), email: null, code: "ok-5"), "branchId", TeamSeed.NoBranchAccess),
        };

        foreach (var (body, key, message) in cases)
        {
            var response = await host.SendAsync(HttpMethod.Post, "/team/technicians", ops.Cookie, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, TeamSeed.Error(await CustomerHost.ReadAsync(response), key));
        }

        Assert.Equal(2, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_profiles WHERE organization_id = @o", ("o", org)));

        // Empty optionals become null.
        var minimal = await host.SendAsync(
            HttpMethod.Post, "/team/technicians", ops.Cookie, TeamSeed.Body(branch.Id, "Min", "Imal", email: " ", phone: "", code: null, notes: " "));
        Assert.Equal(HttpStatusCode.Created, minimal.StatusCode);

        // Edit: changed fields are audited by name only; the same values again write nothing; own email/ID are not duplicates.
        var edit = TeamSeed.Body(branch.Id, last: "Murray-Hopper");
        var updated = await host.SendAsync(HttpMethod.Put, $"/team/technicians/{id}", ops.Cookie, edit);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var detail = await CustomerHost.ReadAsync(updated);
        Assert.Equal("Murray-Hopper", detail["lastName"]!.GetValue<string>());
        Assert.Equal("Prefers mornings", detail["notes"]!.GetValue<string>());
        Assert.Equal("active", detail["profileStatus"]!.GetValue<string>());

        var updateAudit = await database.ScalarAsync<string>(
            "SELECT concat(metadata::text, after_data::text) FROM audit_logs WHERE entity_id = @i AND action = 'technician_profile.updated'", ("i", id));
        Assert.Contains("lastName", updateAudit);
        Assert.DoesNotContain("Murray", updateAudit);
        Assert.DoesNotContain("grace", updateAudit, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"/team/technicians/{id}", ops.Cookie, edit)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.updated", id));

        var clash = await host.SendAsync(HttpMethod.Put, $"/team/technicians/{id}", ops.Cookie, TeamSeed.Body(branch.Id, email: "TAKEN@example.com", code: "taken-1"));
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.updated", id));

        // An unchanged inactive branch is kept; moving to one is refused.
        await database.ExecuteAsync("UPDATE technician_profiles SET branch_id = @b WHERE id = @i", ("b", closed.Id), ("i", other));
        var keep = await host.SendAsync(HttpMethod.Put, $"/team/technicians/{other}", ops.Cookie, TeamSeed.Body(closed.Id, "Other", "Person", email: "taken@example.com", code: "TAKEN-1", notes: "x"));
        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, $"/team/technicians/{id}", ops.Cookie, TeamSeed.Body(closed.Id))).StatusCode);
    }

    // AC-12: the upcoming-visit guard, status changes, audit and same-status no-ops.
    [Fact]
    public async Task Status_DeactivateGuardActivateAndNoOps()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);

        var busy = await database.SeedTechAsync(org, branch.Id, "Busy", "Tech");
        var free = await database.SeedTechAsync(org, branch.Id, "Free", "Tech");
        var suspended = await database.SeedTechAsync(org, branch.Id, "Susp", "Tech", "suspended");
        await database.SeedAssignmentAsync(org, owner.Member.UserId, branch.Id, busy, "assigned", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1));
        // A finished or past visit does not block.
        await database.SeedAssignmentAsync(org, owner.Member.UserId, branch.Id, free, "completed", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2).AddHours(1));
        await database.SeedAssignmentAsync(org, owner.Member.UserId, branch.Id, free, "scheduled", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));

        var refused = await host.SendAsync(HttpMethod.Post, $"/team/technicians/{busy}/deactivate", owner.Cookie);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await CustomerHost.ReadAsync(refused);
        Assert.Equal("Reassign this technician's upcoming visits before deactivating the profile.", problem["title"]!.GetValue<string>());
        Assert.Equal(1, problem["upcomingVisitCount"]!.GetValue<int>());
        Assert.Equal("active", await database.ScalarAsync<string>("SELECT status FROM technician_profiles WHERE id = @i", ("i", busy)));
        Assert.Equal(0, await database.CountAuditAsync(org, "technician_profile.deactivated"));

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{free}/deactivate", owner.Cookie)).StatusCode);
        Assert.Equal("inactive", await database.ScalarAsync<string>("SELECT status FROM technician_profiles WHERE id = @i", ("i", free)));
        var audit = await database.ScalarAsync<string>(
            "SELECT concat(before_data::text, after_data::text) FROM audit_logs WHERE entity_id = @i AND action = 'technician_profile.deactivated'", ("i", free));
        Assert.Contains("active", audit);
        Assert.Contains("inactive", audit);

        // Same status: 204, no new audit row.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{free}/deactivate", owner.Cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.deactivated", free));

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{free}/activate", owner.Cookie)).StatusCode);
        Assert.Equal("active", await database.ScalarAsync<string>("SELECT status FROM technician_profiles WHERE id = @i", ("i", free)));
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{free}/activate", owner.Cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.activated", free));

        // Suspended can be deactivated and activated; once unassigned the busy profile can be deactivated.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{suspended}/deactivate", owner.Cookie)).StatusCode);
        await database.ExecuteAsync("UPDATE visit_assignments SET unassigned_at = now() WHERE technician_id = @t", ("t", busy));
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{busy}/deactivate", owner.Cookie)).StatusCode);
    }

    // AC-13, AC-14, AC-15: eligibility matrix, already linked, race, untouched memberships, unlink audit.
    [Fact]
    public async Task Linking_EnforcesEligibilityRaceAndLeavesMembershipsUntouched()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);

        var tech = await database.SeedMemberAsync(org, Technician, "Tina", "Tech");
        var dispatcher = await database.SeedMemberAsync(org, Dispatcher, "Dan", "Dispatch");
        var manager = await database.SeedMemberAsync(org, OperationsManager, "Mona", "Manager");
        var accounting = await database.SeedMemberAsync(org, Accounting, "Acc", "Ounting");
        var suspendedMember = await database.SeedMemberAsync(org, Technician, "Sam", "Suspended", status: "suspended");
        var taken = await database.SeedMemberAsync(org, Technician, "Tom", "Taken");
        var foreign = await database.SeedMemberAsync(other, Technician, "Fay", "Foreign");
        await database.SeedTechAsync(org, branch.Id, "Owner", "OfLink", membershipId: taken.MembershipId);
        var foreignProfile = await database.SeedTechAsync(other, (await database.SeedBranchAsync(other, isMain: true)).Id, "Far", "Away");

        var linkable = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/linkable-accounts", ops.Cookie));
        var linkableIds = linkable.AsArray().Select(row => row!["organizationUserId"]!.GetValue<string>()).ToHashSet();
        Assert.Equal(
            new[] { tech.MembershipId, dispatcher.MembershipId, manager.MembershipId, ops.Member.MembershipId }.Select(id => id.ToString()).ToHashSet(),
            linkableIds);
        Assert.Equal("technician", (await CustomerHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, "/team/linkable-accounts?search=TINA", ops.Cookie)))[0]!["roleName"]!.GetValue<string>().ToLowerInvariant());

        var profiles = new List<Guid>();

        for (var i = 0; i < 8; i++)
        {
            profiles.Add(await database.SeedTechAsync(org, branch.Id, $"Prof{i}", "Link"));
        }

        async Task<HttpStatusCode> Link(Guid profile, Guid membership) =>
            (await host.SendAsync(HttpMethod.Put, $"/team/technicians/{profile}/account-link", ops.Cookie, new JsonObject { ["organizationUserId"] = membership.ToString() })).StatusCode;

        foreach (var ineligible in new[] { accounting.MembershipId, suspendedMember.MembershipId, taken.MembershipId, foreign.MembershipId, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.Conflict, await Link(profiles[0], ineligible));
        }

        var conflict = await host.SendAsync(HttpMethod.Put, $"/team/technicians/{profiles[0]}/account-link", ops.Cookie, new JsonObject { ["organizationUserId"] = accounting.MembershipId.ToString() });
        Assert.Equal("This user account can't be linked to this profile.", (await CustomerHost.ReadAsync(conflict))["title"]!.GetValue<string>());

        var before = await database.ScalarAsync<string>(
            "SELECT concat(ou.role_id, ou.status, ou.is_all_branches, ou.updated_at, u.email, u.status, u.updated_at) FROM organization_users ou JOIN users u ON u.id = ou.user_id WHERE ou.id = @m", ("m", tech.MembershipId));

        Assert.Equal(HttpStatusCode.NoContent, await Link(profiles[0], tech.MembershipId));
        Assert.Equal(HttpStatusCode.Conflict, await Link(profiles[0], dispatcher.MembershipId));
        Assert.Equal(HttpStatusCode.Conflict, await Link(profiles[1], tech.MembershipId));
        Assert.Equal(HttpStatusCode.NotFound, await Link(foreignProfile, tech.MembershipId));
        Assert.Equal(HttpStatusCode.Conflict, await Link(profiles[1], foreign.MembershipId));

        // Race: the same membership to two profiles at once; exactly one wins and the index holds.
        var results = await Task.WhenAll(Link(profiles[2], manager.MembershipId), Link(profiles[3], manager.MembershipId));
        Assert.Equal(1, results.Count(code => code == HttpStatusCode.NoContent));
        Assert.Equal(1, results.Count(code => code == HttpStatusCode.Conflict));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM technician_profiles WHERE organization_user_id = @m", ("m", manager.MembershipId)));

        Assert.Equal(before, await database.ScalarAsync<string>(
            "SELECT concat(ou.role_id, ou.status, ou.is_all_branches, ou.updated_at, u.email, u.status, u.updated_at) FROM organization_users ou JOIN users u ON u.id = ou.user_id WHERE ou.id = @m", ("m", tech.MembershipId)));

        var unlink = await host.SendAsync(HttpMethod.Delete, $"/team/technicians/{profiles[0]}/account-link", ops.Cookie);
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Delete, $"/team/technicians/{profiles[0]}/account-link", ops.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Delete, $"/team/technicians/{foreignProfile}/account-link", ops.Cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.account_unlinked", profiles[0]));
        Assert.Equal(1, await database.CountAuditAsync(org, "technician_profile.account_linked", profiles[0]));
        Assert.Contains(tech.MembershipId.ToString(), await database.ScalarAsync<string>(
            "SELECT metadata::text FROM audit_logs WHERE entity_id = @i AND action = 'technician_profile.account_unlinked'", ("i", profiles[0])));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM organization_users WHERE id = @m AND status = 'active'", ("m", tech.MembershipId)));
    }

    // AC-06, AC-07, AC-08, AC-19: role matrix, dispatcher branch scope, cross-organization denial, /team/me.
    [Fact]
    public async Task Security_RoleMatrixBranchScopeTenantIsolationAndOwnProfile()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray Branch", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee Branch");
        var foreignBranch = await database.SeedBranchAsync(other, "Foreign Branch", isMain: true);

        var inScope = await database.SeedTechAsync(org, branchX.Id, "Xena", "Scope");
        var outOfScope = await database.SeedTechAsync(org, branchY.Id, "Yuri", "Hidden");
        var foreignProfile = await database.SeedTechAsync(other, foreignBranch.Id, "Far", "Away");
        var foreignMember = await database.SeedMemberAsync(other, Technician, "Fay", "Foreign");

        await using var host = CustomerHost.Create(database);
        var actors = new Dictionary<short, TeamActor>
        {
            [Owner] = await database.ActorAsync(host, org, Owner),
            [OperationsManager] = await database.ActorAsync(host, org, OperationsManager),
            [Dispatcher] = await database.ActorAsync(host, org, Dispatcher, branchX.Id),
            [Technician] = await database.ActorAsync(host, org, Technician),
            [Accounting] = await database.ActorAsync(host, org, Accounting),
            [Viewer] = await database.ActorAsync(host, org, Viewer),
        };

        // Random ids keep allowed calls side-effect free (404) while still proving the role gate.
        var id = Guid.NewGuid();
        var link = new JsonObject { ["organizationUserId"] = Guid.NewGuid().ToString() };
        var endpoints = new (HttpMethod Method, string Path, JsonObject? Body, short[] Allowed)[]
        {
            (HttpMethod.Get, "/team/options", null, [Owner, OperationsManager, Dispatcher]),
            (HttpMethod.Get, "/team/metrics", null, [Owner, OperationsManager, Dispatcher]),
            (HttpMethod.Get, "/team/technicians", null, [Owner, OperationsManager, Dispatcher]),
            (HttpMethod.Get, $"/team/technicians/{id}", null, [Owner, OperationsManager, Dispatcher]),
            (HttpMethod.Get, "/team/skill-coverage", null, [Owner, OperationsManager, Dispatcher]),
            (HttpMethod.Post, "/team/technicians", TeamSeed.Body(Guid.NewGuid()), [Owner, OperationsManager]),
            (HttpMethod.Put, $"/team/technicians/{id}", TeamSeed.Body(Guid.NewGuid()), [Owner, OperationsManager]),
            (HttpMethod.Post, $"/team/technicians/{id}/activate", null, [Owner, OperationsManager]),
            (HttpMethod.Post, $"/team/technicians/{id}/deactivate", null, [Owner, OperationsManager]),
            (HttpMethod.Get, "/team/linkable-accounts", null, [Owner, OperationsManager]),
            (HttpMethod.Put, $"/team/technicians/{id}/account-link", link, [Owner, OperationsManager]),
            (HttpMethod.Delete, $"/team/technicians/{id}/account-link", null, [Owner, OperationsManager]),
            (HttpMethod.Get, "/team/me", null, [Technician]),
        };

        foreach (var (method, path, body, allowed) in endpoints)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(method, path, null, body)).StatusCode);

            foreach (var (role, actor) in actors)
            {
                var status = (await host.SendAsync(method, path, actor.Cookie, body)).StatusCode;
                Assert.True(
                    allowed.Contains(role) ? status != HttpStatusCode.Forbidden : status == HttpStatusCode.Forbidden,
                    $"{method} {path} role {role} returned {status}");
            }
        }

        // Dispatcher branch scope.
        var dispatcher = actors[Dispatcher].Cookie;
        Assert.Equal(["Xena Scope"], TeamSeed.Names(await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/technicians", dispatcher))));
        Assert.Equal(1, (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/metrics", dispatcher)))["activeProfiles"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{inScope}", dispatcher)).StatusCode);

        foreach (var hidden in new[] { outOfScope, foreignProfile })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{hidden}", dispatcher)).StatusCode);
        }

        foreach (var branchId in new[] { branchY.Id, foreignBranch.Id })
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/team/technicians?branchId={branchId}", dispatcher);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(TeamSeed.NoBranchAccess, TeamSeed.Error(await CustomerHost.ReadAsync(response), "branchId"));
            Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/team/skill-coverage?branchId={branchId}", dispatcher)).StatusCode);
        }

        var options = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/options", dispatcher));
        Assert.Equal([branchX.Id.ToString()], options["branches"]!.AsArray().Select(b => b!["id"]!.GetValue<string>()).ToArray());
        Assert.Null((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{inScope}", dispatcher)))["notes"]);

        // Cross-organization denial for the profile (404, unchanged) and the membership (409).
        var ops = actors[OperationsManager].Cookie;
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Put, $"/team/technicians/{foreignProfile}", ops, TeamSeed.Body(branchX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/team/technicians/{foreignProfile}/deactivate", ops)).StatusCode);
        Assert.Equal("active", await database.ScalarAsync<string>("SELECT status FROM technician_profiles WHERE id = @i", ("i", foreignProfile)));
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await host.SendAsync(HttpMethod.Put, $"/team/technicians/{inScope}/account-link", ops, new JsonObject { ["organizationUserId"] = foreignMember.MembershipId.ToString() })).StatusCode);

        // /team/me: 404 while unlinked; once linked, the own profile outside any branch scope, without notes.
        var technician = actors[Technician];
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, "/team/me", technician.Cookie)).StatusCode);
        var own = await database.SeedTechAsync(org, branchY.Id, "Tess", "Own", membershipId: technician.Member.MembershipId, notes: "private");
        var me = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/team/me", technician.Cookie));
        Assert.Equal(own.ToString(), me["id"]!.GetValue<string>());
        Assert.Null(me["notes"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{own}", technician.Cookie)).StatusCode);
    }

    // AC-16 data: skills, grouped availability data, linked account, today block and the notes visibility.
    [Fact]
    public async Task Detail_ReturnsProfileSkillsAvailabilityAccountAndToday()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, "Main Branch", isMain: true);
        var prefix = await database.ScalarAsync<string>("SELECT work_order_prefix FROM organizations WHERE id = @o", ("o", org));

        await using var host = CustomerHost.Create(database);
        var ops = await database.ActorAsync(host, org, OperationsManager);
        var dispatcher = await database.ActorAsync(host, org, Dispatcher);
        var account = await database.SeedMemberAsync(org, Dispatcher, "Dee", "Account");

        var tech = await database.SeedTechAsync(
            org, branch.Id, "Rosa", "Diaz", email: "rosa@example.com", phone: "5125550100", code: "R-1", membershipId: account.MembershipId, notes: "Internal");
        var hvac = await database.SeedSkillAsync(org, "HVAC");
        var electrical = await database.SeedSkillAsync(org, "Electrical");
        var retired = await database.SeedSkillAsync(org, "Retired", active: false);
        await database.GiveSkillAsync(tech, electrical, proficiency: 3);
        await database.GiveSkillAsync(tech, hvac, primary: true, proficiency: 5);
        await database.GiveSkillAsync(tech, retired);

        foreach (short day in new short[] { 1, 2, 3, 4, 5 })
        {
            await database.SeedSlotAsync(tech, day, "08:00", "17:00");
        }

        await database.SeedAssignmentAsync(
            org, ops.Member.UserId, branch.Id, tech, "in_progress", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            workOrderNumber: 1042, scope: "Replace the condenser\nSecond line");

        var detail = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}", ops.Cookie));
        Assert.Equal("Rosa", detail["firstName"]!.GetValue<string>());
        Assert.Equal("rosa@example.com", detail["email"]!.GetValue<string>());
        Assert.Equal("R-1", detail["employeeCode"]!.GetValue<string>());
        Assert.Equal("Internal", detail["notes"]!.GetValue<string>());
        Assert.Equal("Main Branch", detail["branch"]!["name"]!.GetValue<string>());
        Assert.Equal(account.Email, detail["account"]!["email"]!.GetValue<string>());
        Assert.Equal("Dispatcher", detail["account"]!["roleName"]!.GetValue<string>());
        Assert.Equal(
            [("HVAC", 5, true), ("Electrical", 3, false)],
            detail["skills"]!.AsArray().Select(skill => (skill!["name"]!.GetValue<string>(), skill["proficiency"]!.GetValue<int>(), skill["isPrimary"]!.GetValue<bool>())).ToArray());
        Assert.Equal([1, 2, 3, 4, 5], detail["availability"]!.AsArray().Select(day => day!["dayOfWeek"]!.GetValue<int>()).ToArray());
        Assert.Equal("08:00", detail["availability"]![0]!["windows"]![0]!["start"]!.GetValue<string>());
        Assert.Equal("17:00", detail["availability"]![0]!["windows"]![0]!["end"]!.GetValue<string>());
        Assert.Equal("on_job", detail["today"]!["todayStatus"]!.GetValue<string>());
        Assert.Equal($"{prefix}-1042 – Replace the condenser", detail["today"]!["currentJob"]!["label"]!.GetValue<string>());
        Assert.Equal(1, detail["today"]!["jobs"]!.GetValue<int>());
        Assert.Equal("UTC", detail["timezone"]!.GetValue<string>());

        // A dispatcher reads the detail but never the notes.
        var asDispatcher = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}?period=week", dispatcher.Cookie));
        Assert.Null(asDispatcher["notes"]);
        Assert.Equal("Rosa", asDispatcher["firstName"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}?period=month", ops.Cookie)).StatusCode);
    }
}
