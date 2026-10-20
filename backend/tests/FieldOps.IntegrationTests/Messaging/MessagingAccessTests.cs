using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.TechnicianVisits;

namespace FieldOps.IntegrationTests.Messaging;

/// <summary>technician-messages: participation, role gates and search leakage (AC-01 to AC-04 boundaries).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class MessagingAccessTests(CompanySettingsDatabaseFixture database)
{
    private static readonly short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;

    private static readonly short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;

    private static JsonObject Job(Guid workOrder) => new() { ["type"] = "job", ["workOrderId"] = workOrder };

    private static JsonObject Team(Guid branch) => new() { ["type"] = "team", ["branchId"] = branch };

    [Fact]
    public async Task Participation_ByRoleScopeAssignmentAndOrganization_IsExactWithIdentical404AndNoWrites()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);

        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var dispatcherA = await database.StaffAsync(host, world.Org, Dispatcher, "Dana", false, world.BranchA);
        var dispatcherB = await database.StaffAsync(host, world.Org, Dispatcher, "Dora", false, world.BranchB);
        var foreignOwner = await database.StaffAsync(host, foreign.Org, Owner, "Fiona");
        var assigned = await database.TechAsync(host, world.Org, world.BranchA, "Tess");
        var otherJob = await database.TechAsync(host, world.Org, world.BranchA, "Tom");
        var unassigned = await database.TechAsync(host, world.Org, world.BranchA, "Tina");
        var otherBranch = await database.TechAsync(host, world.Org, world.BranchB, "Theo");

        var w1 = await database.SeedJobAsync(world, owner.UserId, assigned.Profile, "assigned", DispatchSeed.Future(1, 9), title: "Boiler kestrel repair");
        await database.AssignAsync(w1.Visit, unassigned.Profile!.Value, owner.UserId, primary: false);
        await database.UnassignTechAsync(w1.Visit, unassigned.Profile!.Value);
        var w2 = await database.SeedJobAsync(world, owner.UserId, otherJob.Profile, "assigned", DispatchSeed.Future(1, 11), title: "Attic fan");
        var draft = await database.SeedOrderAsync(world, owner.UserId, status: "draft", title: "Draft quote job");
        var foreignJob = await database.SeedOrderAsync(foreign, foreignOwner.UserId, status: "scheduled", title: "Foreign zebra job");

        var jobId = await host.ResolveJobAsync(owner, w1.Order);
        var teamId = await host.ResolveTeamAsync(owner, world.BranchA);
        Assert.Equal(jobId, await host.ResolveJobAsync(owner, w1.Order));
        Assert.Equal(teamId, await host.ResolveTeamAsync(dispatcherA, world.BranchA));
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(owner, jobId, "Bring the kestrel gasket")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(owner, teamId, "Team note")).StatusCode);

        var foreignTeam = await host.ResolveTeamAsync(foreignOwner, foreign.BranchA);
        var foreignConversation = await host.ResolveJobAsync(foreignOwner, foreignJob.Order);
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(foreignOwner, foreignConversation, "Foreign orchid secret")).StatusCode);

        // job access, team access.
        (Member Who, bool Job, bool Team)[] matrix =
        [
            (assigned, true, true),
            (otherJob, false, true),
            (unassigned, false, true),
            (otherBranch, false, false),
            (dispatcherA, true, true),
            (dispatcherB, false, false),
            (foreignOwner, false, false),
        ];

        var unknown = await TechnicianVisitSeed.WithoutTraceAsync(
            await host.GetAsync($"/messages/conversations/{Guid.NewGuid()}", owner.Cookie));

        // Denials first: identical 404 for every endpoint and no row written anywhere.
        var before = await database.StateAsync(world.Org, foreign.Org);

        foreach (var (who, job, team) in matrix)
        {
            foreach (var (id, allowed) in new[] { (jobId, job), (teamId, team) }.Where(entry => !entry.Item2))
            {
                foreach (var response in new[]
                {
                    await host.GetAsync($"/messages/conversations/{id}", who.Cookie),
                    await host.GetAsync($"/messages/conversations/{id}/messages", who.Cookie),
                    await host.PostMessageAsync(who, id, "denied body"),
                    await host.SendAsync(HttpMethod.Post, $"/messages/conversations/{id}/read", who.Cookie),
                })
                {
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                    Assert.Equal(unknown, await TechnicianVisitSeed.WithoutTraceAsync(response));
                }

                Assert.False(allowed);
            }
        }

        // Resolve of an unresolved work order and branch: denied callers get the same 404 and nothing is created.
        (Member Who, JsonObject Body)[] deniedResolves =
        [
            (assigned, Job(w2.Order)),
            (assigned, Job(draft.Order)),
            (assigned, Job(foreignJob.Order)),
            (assigned, Team(world.BranchB)),
            (assigned, Team(foreign.BranchB)),
            (dispatcherB, Job(w2.Order)),
            (dispatcherB, Job(foreignJob.Order)),
            (dispatcherB, Team(world.BranchA)),
            (dispatcherB, Team(foreign.BranchB)),
            (otherBranch, Job(w2.Order)),
            (otherBranch, Team(world.BranchA)),
            (foreignOwner, Job(w2.Order)),
            (foreignOwner, Team(world.BranchA)),
            (foreignOwner, Team(world.BranchB)),
        ];

        foreach (var (who, body) in deniedResolves)
        {
            var response = await host.SendAsync(HttpMethod.Post, "/messages/conversations/resolve", who.Cookie, body);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(unknown, await TechnicianVisitSeed.WithoutTraceAsync(response));
        }

        // The Draft work order is not a conversation even for its own organization owner.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(
                HttpMethod.Post, "/messages/conversations/resolve", owner.Cookie, new JsonObject { ["type"] = "job", ["workOrderId"] = draft.Order })).StatusCode);

        Assert.Equal(before, await database.StateAsync(world.Org, foreign.Org));

        // Allowed callers read, mark read and send in exactly the conversations of the matrix.
        foreach (var (who, job, team) in matrix)
        {
            foreach (var (id, allowed) in new[] { (jobId, job), (teamId, team) }.Where(entry => entry.Item2))
            {
                Assert.Equal(HttpStatusCode.OK, (await host.GetAsync($"/messages/conversations/{id}", who.Cookie)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await host.GetAsync($"/messages/conversations/{id}/messages", who.Cookie)).StatusCode);
                Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(who, id, $"hello from {who.Name}")).StatusCode);
                Assert.Equal(
                    HttpStatusCode.NoContent,
                    (await host.SendAsync(HttpMethod.Post, $"/messages/conversations/{id}/read", who.Cookie)).StatusCode);
                Assert.True(allowed);
            }
        }

        Assert.Equal(teamId, await host.ResolveTeamAsync(assigned, world.BranchA));
        Assert.Equal(await host.ResolveJobAsync(otherJob, w2.Order), await host.ResolveJobAsync(owner, w2.Order));
        Assert.NotEqual(foreignTeam, teamId);

        // Search runs only over authorized conversations (BR-05): titles and bodies of other scopes never leak.
        var kestrel = "?search=kestrel";
        Assert.Equal(["Boiler kestrel repair"], MessagingSeed.Titles(await host.ListAsync(owner, kestrel)));
        Assert.Equal(["Boiler kestrel repair"], MessagingSeed.Titles(await host.ListAsync(assigned, kestrel)));

        foreach (var who in new[] { dispatcherB, otherBranch, otherJob, unassigned, foreignOwner })
        {
            Assert.Empty((await host.ListAsync(who, kestrel))["items"]!.AsArray());
        }

        foreach (var term in new[] { "zebra", "orchid", "foreign" })
        {
            Assert.Empty((await host.ListAsync(owner, $"?search={term}"))["items"]!.AsArray());
            Assert.Empty((await host.ListAsync(dispatcherB, $"?search={term}"))["items"]!.AsArray());
        }

        Assert.Single((await host.ListAsync(foreignOwner, "?search=orchid"))["items"]!.AsArray());

        // AC-03: a technician unassigned after sending loses access; participants still see the message with the author name.
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(assigned, jobId, "Tess was here")).StatusCode);
        await database.UnassignTechAsync(w1.Visit, assigned.Profile!.Value);

        var staffView = await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/messages/conversations/{jobId}/messages", owner.Cookie));
        var tessMessage = staffView["items"]!.AsArray().Single(item => item!["body"]!.GetValue<string>() == "Tess was here")!;
        Assert.Equal("Tess Tech", tessMessage["author"]!["name"]!.GetValue<string>());
        Assert.Equal("TT", tessMessage["author"]!["initials"]!.GetValue<string>());
        Assert.Equal("Technician", tessMessage["author"]!["roleLabel"]!.GetValue<string>());
        Assert.False(tessMessage["isMine"]!.GetValue<bool>());
        Assert.Null(tessMessage["clientMessageId"]);

        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync($"/messages/conversations/{jobId}", assigned.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.PostMessageAsync(assigned, jobId, "after unassign")).StatusCode);
        Assert.DoesNotContain("Boiler kestrel repair", MessagingSeed.Titles(await host.ListAsync(assigned)));
        Assert.Empty((await host.ListAsync(assigned, "?search=kestrel"))["items"]!.AsArray());
    }

    [Fact]
    public async Task Endpoints_ForViewerAccountingUnlinkedInactiveAndAnonymous_AreRejectedWithoutWrites()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var viewer = await database.StaffAsync(host, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId, "Vera");
        var accounting = await database.StaffAsync(host, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Adam");
        var unlinked = await database.StaffAsync(host, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Una");
        var inactive = await database.TechAsync(host, world.Org, world.BranchA, "Ivan", status: "inactive");
        var suspended = await database.TechAsync(host, world.Org, world.BranchA, "Sam", status: "suspended");
        var order = await database.SeedJobAsync(world, owner.UserId, inactive.Profile, "assigned", DispatchSeed.Future(1, 9));
        var jobId = await host.ResolveJobAsync(owner, order.Order);
        var before = await database.StateAsync(world.Org);

        (string Method, string Path, JsonObject? Body)[] endpoints =
        [
            ("GET", "/messages/summary", null),
            ("GET", "/messages/conversations", null),
            ("POST", "/messages/conversations/resolve", new JsonObject { ["type"] = "team", ["branchId"] = world.BranchA }),
            ("GET", $"/messages/conversations/{jobId}", null),
            ("GET", $"/messages/conversations/{jobId}/messages", null),
            ("POST", $"/messages/conversations/{jobId}/messages", MessagingSeed.Message("hello")),
            ("POST", $"/messages/conversations/{jobId}/read", null),
        ];

        foreach (var (method, path, body) in endpoints)
        {
            var verb = new HttpMethod(method);

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(verb, path, null, body)).StatusCode);

            foreach (var forbidden in new[] { viewer, accounting })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(verb, path, forbidden.Cookie, body)).StatusCode);
            }

            var notLinked = await host.SendAsync(verb, path, unlinked.Cookie, body);
            Assert.Equal(HttpStatusCode.NotFound, notLinked.StatusCode);
            Assert.Equal("technician_profile_not_linked", (await RequestsHost.ReadAsync(notLinked))["code"]!.GetValue<string>());

            foreach (var blocked in new[] { inactive, suspended })
            {
                var response = await host.SendAsync(verb, path, blocked.Cookie, body);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("technician_inactive", (await RequestsHost.ReadAsync(response))["code"]!.GetValue<string>());
            }
        }

        Assert.Equal(before, await database.StateAsync(world.Org));
    }
}
