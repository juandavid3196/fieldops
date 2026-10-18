using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.TechnicianVisits;

namespace FieldOps.IntegrationTests.Messaging;

/// <summary>technician-messages: list, unread and read state, paging, send, resolve and privacy (AC-04 to AC-09).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class MessagingFlowTests(CompanySettingsDatabaseFixture database)
{
    private static readonly short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;

    private static readonly short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;

    private static void Tick(TechnicianHost host) => host.Time.Advance(TimeSpan.FromMinutes(1));

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) =>
        await TechnicianVisitSeed.ReadAsync(response, expected);

    private static Guid[] Ids(JsonNode page) => [.. page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>())];

    [Fact]
    public async Task List_FiltersSearchesOrdersAndCountsUnreadWithMonotonicReadState()
    {
        var world = await database.SeedWorldAsync();
        await database.SeedBranchAsync(world.Org, "Charlie Branch", isActive: false);
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var tech = await database.TechAsync(host, world.Org, world.BranchA, "Tess");
        var w1 = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "assigned", DispatchSeed.Future(1, 9), title: "Boiler repair");
        var w2 = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "assigned", DispatchSeed.Future(1, 11), title: "Attic fan");
        var w3 = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "assigned", DispatchSeed.Future(1, 13), title: "Gutter clean");
        await database.SeedOrderAsync(world, owner.UserId, branch: world.BranchB, status: "scheduled", title: "Basement pump");
        var prefix = await database.PrefixAsync(world.Org);

        var teamA = await host.ResolveTeamAsync(tech, world.BranchA);
        var c1 = await host.ResolveJobAsync(owner, w1.Order);
        var c2 = await host.ResolveJobAsync(owner, w2.Order);
        var c3 = await host.ResolveJobAsync(owner, w3.Order);

        // Before any message: the Team entry always appears, Job conversations never.
        Assert.Equal(["Alpha Branch team"], MessagingSeed.Titles(await host.ListAsync(tech)));

        Tick(host);
        await host.PostMessageAsync(owner, c1, "Please bring spare valves\n   and extra tape");
        Tick(host);
        await host.PostMessageAsync(owner, c2, "Gate    code\n is 4412");
        Tick(host);
        await host.PostMessageAsync(tech, teamA, "On my way to the depot");
        Tick(host);
        await host.PostMessageAsync(owner, c1, "Second note about valves");

        var all = await host.ListAsync(tech);
        var items = all["items"]!.AsArray();
        Assert.Null(all["nextCursor"]);
        Assert.Equal(["Boiler repair", "Alpha Branch team", "Attic fan"], MessagingSeed.Titles(all));
        Assert.Equal([c1, teamA, c2], Ids(all));
        Assert.Equal([2, 0, 1], items.Select(item => item!["unreadCount"]!.GetValue<int>()).ToArray());
        Assert.Equal("job", items[0]!["type"]!.GetValue<string>());
        Assert.Equal(w1.Order, items[0]!["workOrder"]!["id"]!.GetValue<Guid>());
        Assert.Equal($"{prefix}-{w1.Number}", items[0]!["workOrder"]!["displayNumber"]!.GetValue<string>());
        Assert.Equal(world.BranchA, items[0]!["branchId"]!.GetValue<Guid>());
        Assert.Equal("Second note about valves", items[0]!["lastMessage"]!["preview"]!.GetValue<string>());
        Assert.Equal("Olive Staff", items[0]!["lastMessage"]!["authorName"]!.GetValue<string>());
        Assert.Equal("OS", items[0]!["lastMessage"]!["authorInitials"]!.GetValue<string>());
        Assert.False(items[0]!["lastMessage"]!["isMine"]!.GetValue<bool>());
        Assert.Equal("team", items[1]!["type"]!.GetValue<string>());
        Assert.Null(items[1]!["workOrder"]);
        Assert.True(items[1]!["lastMessage"]!["isMine"]!.GetValue<bool>());
        Assert.Equal("Gate code is 4412", items[2]!["lastMessage"]!["preview"]!.GetValue<string>());

        var summary = await host.SummaryAsync(tech);
        Assert.Equal(3, summary["unreadMessages"]!.GetValue<int>());
        Assert.Equal(2, summary["unreadConversations"]!.GetValue<int>());

        // Filters, search (title, display number, branch name, body, case-insensitive) and literal wildcards.
        Assert.Equal([c1, c2], Ids(await host.ListAsync(tech, "?filter=unread")));
        Assert.Equal([c1, c2], Ids(await host.ListAsync(tech, "?filter=jobs")));
        Assert.Equal([teamA], Ids(await host.ListAsync(tech, "?filter=team")));
        Assert.Equal([c1], Ids(await host.ListAsync(tech, "?search=VALVES")));
        Assert.Equal([c1], Ids(await host.ListAsync(tech, "?search=boiler&filter=jobs")));
        Assert.Equal([c2], Ids(await host.ListAsync(tech, $"?search={prefix}-{w2.Number}")));
        Assert.Equal([c1, teamA, c2], Ids(await host.ListAsync(tech, "?search=alpha%20BRANCH")));
        Assert.Empty((await host.ListAsync(tech, "?search=%25%25"))["items"]!.AsArray());
        Assert.Empty((await host.ListAsync(tech, "?search=valves&filter=team"))["items"]!.AsArray());
        Assert.Equal(3, (await host.ListAsync(tech, "?search=%20%20%20"))["items"]!.AsArray().Count);

        // Invalid parameters never echo the input.
        foreach (var (query, field) in new[]
        {
            ("?search=a", "search"),
            ($"?search={new string('a', 81)}", "search"),
            ("?filter=bogus", "filter"),
            ("?cursor=not-a-cursor", "cursor"),
        })
        {
            var problem = await ReadAsync(await host.GetAsync($"/messages/conversations{query}", tech.Cookie), HttpStatusCode.BadRequest);
            Assert.NotNull(problem["errors"]![field]);
        }

        var searchError = await ReadAsync(
            await host.GetAsync("/messages/conversations?search=a", tech.Cookie), HttpStatusCode.BadRequest);
        Assert.Equal("Enter 2 to 80 characters.", searchError["errors"]!["search"]![0]!.GetValue<string>());

        // Owner: Job conversations with messages, the existing Team and the virtual Team of the other active branch last.
        var staff = await host.ListAsync(owner);
        Assert.Equal(["Boiler repair", "Alpha Branch team", "Attic fan", "Bravo Branch team"], MessagingSeed.Titles(staff));
        var virtualTeam = staff["items"]![3]!;
        Assert.Null(virtualTeam["id"]);
        Assert.Null(virtualTeam["lastMessage"]);
        Assert.Equal(world.BranchB, virtualTeam["branchId"]!.GetValue<Guid>());
        Assert.Equal(0, virtualTeam["unreadCount"]!.GetValue<int>());

        // Read: the read state moves to the latest message, the summary decreases, and a message without one reads as nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/messages/conversations/{c1}/read", tech.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/messages/conversations/{c3}/read", tech.Cookie)).StatusCode);
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM conversation_read_states WHERE conversation_id = @c", ("c", c3)));

        var latest = await database.ScalarAsync<DateTimeOffset>("SELECT max(created_at) FROM conversation_messages WHERE conversation_id = @c", ("c", c1));
        var readAt = await database.ScalarAsync<DateTimeOffset>(
            "SELECT last_read_at FROM conversation_read_states WHERE conversation_id = @c AND user_id = @u", ("c", c1), ("u", tech.UserId));
        Assert.Equal(latest, readAt);
        Assert.Equal(0, (await host.ListAsync(tech))["items"]![0]!["unreadCount"]!.GetValue<int>());
        Assert.Equal(1, (await host.SummaryAsync(tech))["unreadConversations"]!.GetValue<int>());

        Tick(host);
        await host.PostMessageAsync(owner, c1, "Third note");
        Assert.Equal(1, (await host.ListAsync(tech))["items"]![0]!["unreadCount"]!.GetValue<int>());
        Assert.Equal(2, (await host.SummaryAsync(tech))["unreadMessages"]!.GetValue<int>());

        // Never backwards: a read state ahead of the latest message is left alone.
        await database.ExecuteAsync(
            "UPDATE conversation_read_states SET last_read_at = last_read_at + interval '1 hour' WHERE conversation_id = @c AND user_id = @u",
            ("c", c1),
            ("u", tech.UserId));
        var ahead = await database.ScalarAsync<DateTimeOffset>(
            "SELECT last_read_at FROM conversation_read_states WHERE conversation_id = @c AND user_id = @u", ("c", c1), ("u", tech.UserId));
        await host.SendAsync(HttpMethod.Post, $"/messages/conversations/{c1}/read", tech.Cookie);
        Assert.Equal(
            ahead,
            await database.ScalarAsync<DateTimeOffset>(
                "SELECT last_read_at FROM conversation_read_states WHERE conversation_id = @c AND user_id = @u", ("c", c1), ("u", tech.UserId)));
    }

    [Fact]
    public async Task List_PagesThirtyAtATimeWithStableKeysetAcrossTiesAndNullLastMessages()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var baseTime = DateTimeOffset.UtcNow.AddDays(-1);
        var created = new List<Guid>();

        for (var i = 0; i < 33; i++)
        {
            var order = await database.SeedOrderAsync(
                world, owner.UserId, status: "scheduled", title: i is 29 or 30 ? "Tied job" : $"Job {i:00}");
            var at = baseTime.AddMinutes(-(i == 30 ? 29 : i));
            created.Add(await database.SeedConversationAsync(world.Org, world.BranchA, order.Order, owner.UserId, $"message {i}", at));
        }

        // 33 jobs plus the two virtual Team entries (nulls last, ordered by title).
        var seen = new List<Guid?>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await host.ListAsync(owner, cursor is null ? string.Empty : $"?cursor={Uri.EscapeDataString(cursor)}");
            var items = page["items"]!.AsArray();
            Assert.Equal(pages == 0 ? 30 : 5, items.Count);
            seen.AddRange(items.Select(item => item!["id"]?.GetValue<Guid>()));
            cursor = page["nextCursor"]?.GetValue<string>();
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(2, pages);
        Assert.Equal(35, seen.Count);
        Assert.Equal(33, seen.Where(id => id is not null).Distinct().Count());
        Assert.Equal(created.OrderBy(id => id).ToArray(), seen.Where(id => id is not null).Select(id => id!.Value).OrderBy(id => id).ToArray());
        Assert.Equal([null, null], seen.Skip(33).ToArray());
        Assert.Equal(created[..29], seen.Take(29).Select(id => id!.Value).ToArray());

        // Team filter: 34 virtual entries ordered by ordinal title, paged through a null last-message cursor.
        for (var i = 0; i < 32; i++)
        {
            await database.SeedBranchAsync(world.Org, $"Zone {i:00}");
        }

        var teams = new List<string>();
        var branches = new List<Guid>();
        cursor = null;

        do
        {
            var page = await host.ListAsync(owner, "?filter=team" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}"));
            teams.AddRange(MessagingSeed.Titles(page));
            branches.AddRange(page["items"]!.AsArray().Select(item => item!["branchId"]!.GetValue<Guid>()));
            cursor = page["nextCursor"]?.GetValue<string>();
        }
        while (cursor is not null);

        Assert.Equal(34, teams.Count);
        Assert.Equal(34, branches.Distinct().Count());
        Assert.Equal(teams.OrderBy(title => title.ToLowerInvariant(), StringComparer.Ordinal).ToArray(), teams.ToArray());

        // A cursor is bound to its filter and search.
        var first = await host.ListAsync(owner);
        var firstCursor = first["nextCursor"]!.GetValue<string>();
        var mismatch = await ReadAsync(
            await host.GetAsync($"/messages/conversations?filter=jobs&cursor={Uri.EscapeDataString(firstCursor)}", owner.Cookie),
            HttpStatusCode.BadRequest);
        Assert.NotNull(mismatch["errors"]!["cursor"]);
    }

    [Fact]
    public async Task Send_ValidatesBodiesIsIdempotentAdvancesStateAndRateLimitsPerUser()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var other = await database.StaffAsync(host, world.Org, Owner, "Otto");
        var tech = await database.TechAsync(host, world.Org, world.BranchA, "Tess");
        var completed = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "completed", DispatchSeed.Future(-1, 9), title: "Done job");
        await database.ExecuteAsync("UPDATE work_orders SET status = 'completed' WHERE id = @w", ("w", completed.Order));
        var cancelled = await database.SeedOrderAsync(world, owner.UserId, status: "cancelled", title: "Cancelled job", visitStatus: "cancelled");

        var conversation = await host.ResolveJobAsync(tech, completed.Order);
        var path = $"/messages/conversations/{conversation}/messages";

        // Invalid bodies and ids: 400 with the field message, nothing stored.
        foreach (var body in new[] { string.Empty, "   \n\t ", new string('a', 2001) })
        {
            var problem = await ReadAsync(await host.PostMessageAsync(tech, conversation, body), HttpStatusCode.BadRequest);
            Assert.Equal("Enter a message of up to 2000 characters.", problem["errors"]!["body"]![0]!.GetValue<string>());
        }

        foreach (var invalid in new JsonObject[]
        {
            new() { ["body"] = "x" },
            new() { ["clientMessageId"] = "nope", ["body"] = "x" },
            new() { ["clientMessageId"] = 7, ["body"] = "x" },
        })
        {
            var problem = await ReadAsync(await host.SendAsync(HttpMethod.Post, path, tech.Cookie, invalid), HttpStatusCode.BadRequest);
            Assert.NotNull(problem["errors"]!["clientMessageId"]);
        }

        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM conversation_messages WHERE conversation_id = @c", ("c", conversation)));

        // Valid bodies: 1, 2000 and padded characters (trimmed) on a completed work order.
        var sentId = Guid.NewGuid();
        var created = await host.PostMessageAsync(tech, conversation, "  padded  ", sentId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var message = await ReadAsync(created, HttpStatusCode.Created);
        Assert.Equal("padded", message["body"]!.GetValue<string>());
        Assert.Equal(sentId, message["clientMessageId"]!.GetValue<Guid>());
        Assert.True(message["isMine"]!.GetValue<bool>());
        Assert.Equal("Tess Tech", message["author"]!["name"]!.GetValue<string>());
        Assert.Equal("Technician", message["author"]!["roleLabel"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(tech, conversation, "a")).StatusCode);
        Tick(host);
        var longest = await ReadAsync(await host.PostMessageAsync(tech, conversation, new string('b', 2000)), HttpStatusCode.Created);
        Assert.Equal(2000, longest["body"]!.GetValue<string>().Length);

        // last_message_at and the author's read state follow the newest message.
        var newest = await database.ScalarAsync<DateTimeOffset>("SELECT max(created_at) FROM conversation_messages WHERE conversation_id = @c", ("c", conversation));
        Assert.Equal(newest, await database.ScalarAsync<DateTimeOffset>("SELECT last_message_at FROM conversations WHERE id = @c", ("c", conversation)));
        Assert.Equal(
            newest,
            await database.ScalarAsync<DateTimeOffset>(
                "SELECT last_read_at FROM conversation_read_states WHERE conversation_id = @c AND user_id = @u", ("c", conversation), ("u", tech.UserId)));
        Assert.Equal(0, (await host.SummaryAsync(tech))["unreadMessages"]!.GetValue<int>());

        // Repeat: 200 with the stored message, no new row; the author alone sees the client id.
        var repeat = await ReadAsync(await host.PostMessageAsync(tech, conversation, "different body", sentId));
        Assert.Equal(message["id"]!.GetValue<Guid>(), repeat["id"]!.GetValue<Guid>());
        Assert.Equal("padded", repeat["body"]!.GetValue<string>());
        Assert.Equal(3, await database.ScalarAsync<long>("SELECT COUNT(*) FROM conversation_messages WHERE conversation_id = @c", ("c", conversation)));

        var seenByOwner = await ReadAsync(await host.GetAsync(path, owner.Cookie));
        Assert.All(seenByOwner["items"]!.AsArray(), item => Assert.Null(item!["clientMessageId"]));

        // Staff may send on a cancelled work order too; a concurrent repeat of one clientMessageId stores one row.
        var cancelledConversation = await host.ResolveJobAsync(owner, cancelled.Order);
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(owner, cancelledConversation, "closing note")).StatusCode);

        var teamId = await host.ResolveTeamAsync(owner, world.BranchA);
        var shared = Guid.NewGuid();
        var statuses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(async _ => (await host.PostMessageAsync(owner, teamId, "race", shared)).StatusCode));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(7, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>("SELECT COUNT(*) FROM conversation_messages WHERE conversation_id = @c AND client_message_id = @m", ("c", teamId), ("m", shared)));

        // Rate: 30 per minute per user and organization; another user is unaffected; the window slides.
        var chatty = await database.StaffAsync(host, world.Org, Owner, "Chad");
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(chatty, teamId, $"msg {i}")).StatusCode);
        }

        var limited = await host.PostMessageAsync(chatty, teamId, "one too many");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
        var problemDetails = await RequestsHost.ReadAsync(limited);
        Assert.Equal("message_rate_limited", problemDetails["code"]!.GetValue<string>());
        Assert.Equal("You're sending messages too quickly. Try again in a moment.", problemDetails["title"]!.GetValue<string>());
        Assert.Equal(30, await database.ScalarAsync<long>("SELECT COUNT(*) FROM conversation_messages WHERE author_user_id = @u", ("u", chatty.UserId)));
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(other, teamId, "still fine")).StatusCode);

        host.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.Created, (await host.PostMessageAsync(chatty, teamId, "after the window")).StatusCode);
    }

    [Fact]
    public async Task Resolve_ConcurrentCallsReturnOneConversationPerWorkOrderAndBranchWithoutMessagesOrAudit()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var dispatcher = await database.StaffAsync(host, world.Org, Dispatcher, "Dana", false, world.BranchA, world.BranchB);
        var tech = await database.TechAsync(host, world.Org, world.BranchA, "Tess");
        var w1 = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "assigned", DispatchSeed.Future(1, 9));
        var w2 = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "assigned", DispatchSeed.Future(1, 11), title: "Second job");
        var before = await database.StateAsync(world.Org);
        Member[] callers = [owner, dispatcher, tech];

        var jobCalls = Enumerable.Range(0, 12).Select(i => host.ResolveJobAsync(callers[i % 3], i < 6 ? w1.Order : w2.Order));
        var teamCalls = Enumerable.Range(0, 12).Select(i => host.ResolveTeamAsync(callers[i % 2 == 0 ? 0 : 1], i < 6 ? world.BranchA : world.BranchB));
        var results = await Task.WhenAll(jobCalls.Concat(teamCalls));

        Assert.Single(results.Take(6).Distinct());
        Assert.Single(results.Skip(6).Take(6).Distinct());
        Assert.Single(results.Skip(12).Take(6).Distinct());
        Assert.Single(results.Skip(18).Take(6).Distinct());
        Assert.Equal(4, results.Distinct().Count());
        Assert.Equal(4, await database.ScalarAsync<long>("SELECT COUNT(*) FROM conversations WHERE organization_id = @o", ("o", world.Org)));

        // Creation writes no message, no read state and no audit row.
        Assert.Equal(before.Replace("c=0", "c=4", StringComparison.Ordinal), await database.StateAsync(world.Org));
    }

    [Fact]
    public async Task Messages_PageByCreatedAtAndIdAndDetailChoosesTheTechnicianVisit()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var tech = await database.TechAsync(host, world.Org, world.BranchA, "Tess");
        var outsider = await database.TechAsync(host, world.Org, world.BranchB, "Theo");
        var dispatcherB = await database.StaffAsync(host, world.Org, Dispatcher, "Dora", false, world.BranchB);
        var prefix = await database.PrefixAsync(world.Org);

        var order = await database.SeedOrderAsync(
            world, owner.UserId, status: "scheduled", title: "Multi visit", visitStatus: "completed", start: DispatchSeed.Future(-3, 9), end: DispatchSeed.Future(-3, 10));
        await database.AssignAsync(order.Visit, tech.Profile!.Value, owner.UserId);
        var conversation = await host.ResolveJobAsync(owner, order.Order);
        await database.AddMessagesAsync(world.Org, conversation, owner.UserId, 120, DateTimeOffset.UtcNow.AddDays(-1));

        // 120 messages in tied pairs: the newest 50 ascending, then pages by message id in (created_at, id) order.
        var expected = await database.QueryGuidsAsync(
            "SELECT id FROM conversation_messages WHERE conversation_id = @c ORDER BY created_at, id", ("c", conversation));
        var basePath = $"/messages/conversations/{conversation}/messages";

        async Task<(Guid[] Ids, bool HasMore)> PageAsync(string query)
        {
            var page = await ReadAsync(await host.GetAsync(basePath + query, tech.Cookie));

            return ([.. page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>())], page["hasMore"]!.GetValue<bool>());
        }

        var newest = await PageAsync(string.Empty);
        Assert.Equal(expected[70..], newest.Ids);
        Assert.True(newest.HasMore);

        var older = await PageAsync($"?before={newest.Ids[0]}");
        Assert.Equal(expected[20..70], older.Ids);
        Assert.True(older.HasMore);

        var oldest = await PageAsync($"?before={older.Ids[0]}");
        Assert.Equal(expected[..20], oldest.Ids);
        Assert.False(oldest.HasMore);

        var forward = await PageAsync($"?after={expected[19]}");
        Assert.Equal(expected[20..70], forward.Ids);
        Assert.True(forward.HasMore);

        var rest = await PageAsync($"?after={expected[69]}");
        Assert.Equal(expected[70..], rest.Ids);
        Assert.False(rest.HasMore);
        Assert.Empty((await PageAsync($"?after={expected[^1]}")).Ids);

        // Invalid cursors are 400 for a participant (both, unknown, malformed, a message of another conversation) and 404 for others.
        var otherConversation = await host.ResolveTeamAsync(owner, world.BranchA);
        var foreignMessage = (await ReadAsync(await host.PostMessageAsync(owner, otherConversation, "elsewhere"), HttpStatusCode.Created))["id"]!.GetValue<Guid>();

        foreach (var query in new[]
        {
            $"?before={expected[5]}&after={expected[6]}",
            $"?before={Guid.NewGuid()}",
            "?after=nope",
            $"?before={foreignMessage}",
        })
        {
            var problem = await ReadAsync(await host.GetAsync(basePath + query, tech.Cookie), HttpStatusCode.BadRequest);
            Assert.NotNull(problem["errors"]!["cursor"]);
        }

        foreach (var denied in new[] { outsider, dispatcherB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync($"{basePath}?before=nope", denied.Cookie)).StatusCode);
        }

        // BR-08 detail: staff get no visitId; the technician gets the visit openable through /technician/visits/{id}.
        var staffDetail = await ReadAsync(await host.GetAsync($"/messages/conversations/{conversation}", owner.Cookie));
        Assert.Equal("job", staffDetail["type"]!.GetValue<string>());
        Assert.Equal("Multi visit", staffDetail["title"]!.GetValue<string>());
        Assert.Equal("Alpha Branch", staffDetail["branchName"]!.GetValue<string>());
        Assert.Equal($"{prefix}-{order.Number}", staffDetail["workOrder"]!["displayNumber"]!.GetValue<string>());
        Assert.Null(staffDetail["workOrder"]!["visitId"]);

        var teamDetail = await ReadAsync(await host.GetAsync($"/messages/conversations/{otherConversation}", tech.Cookie));
        Assert.Equal("Alpha Branch team", teamDetail["title"]!.GetValue<string>());
        Assert.Null(teamDetail["workOrder"]);

        var later = await database.SeedVisitAsync(world.Org, order.Order, 2, "scheduled", DispatchSeed.Future(2, 9), DispatchSeed.Future(2, 10), tech.Profile, owner.UserId);
        var earlier = await database.SeedVisitAsync(world.Org, order.Order, 3, "scheduled", DispatchSeed.Future(1, 9), DispatchSeed.Future(1, 10), tech.Profile, owner.UserId);
        await database.SeedVisitAsync(world.Org, order.Order, 4, "cancelled", DispatchSeed.Future(1, 6), DispatchSeed.Future(1, 7), tech.Profile, owner.UserId);

        async Task<Guid?> VisitIdAsync()
        {
            var detail = await ReadAsync(await host.GetAsync($"/messages/conversations/{conversation}", tech.Cookie));
            var visitId = detail["workOrder"]!["visitId"]!.GetValue<Guid>();
            Assert.Equal(HttpStatusCode.OK, (await host.GetAsync($"/technician/visits/{visitId}", tech.Cookie)).StatusCode);

            return visitId;
        }

        Assert.Equal(earlier, await VisitIdAsync());

        await database.ExecuteAsync("UPDATE visits SET status = 'in_progress' WHERE id = @v", ("v", later));
        Assert.Equal(later, await VisitIdAsync());

        await database.ExecuteAsync(
            "UPDATE visits SET status = 'completed', scheduled_start = now() - interval '1 day', scheduled_end = now() - interval '23 hours' WHERE id = @v", ("v", later));
        await database.ExecuteAsync(
            "UPDATE visits SET status = 'completed', scheduled_start = now() - interval '2 days', scheduled_end = now() - interval '47 hours' WHERE id = @v", ("v", earlier));
        Assert.Equal(later, await VisitIdAsync());
    }

    [Fact]
    public async Task Privacy_BodiesAndSearchTextNeverReachLogsOrAuditAndEveryResponseIsNoStore()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var owner = await database.StaffAsync(host, world.Org, Owner, "Olive");
        var viewer = await database.StaffAsync(host, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId, "Vera");
        var spammer = await database.StaffAsync(host, world.Org, Owner, "Sven");
        var outsider = await database.TechAsync(host, world.Org, world.BranchB, "Theo");
        var tech = await database.TechAsync(host, world.Org, world.BranchA, "Tess");
        var order = await database.SeedJobAsync(world, owner.UserId, tech.Profile, "assigned", DispatchSeed.Future(1, 9));
        var conversation = await host.ResolveJobAsync(owner, order.Order);
        var bodyMarker = "BODYSECRET-7f3a";
        var searchMarker = "SEARCHSECRET-91bc";
        var before = await database.StateAsync(world.Org);

        var responses = new List<HttpResponseMessage>
        {
            await host.PostMessageAsync(tech, conversation, $"{bodyMarker} hello"),
            await host.PostMessageAsync(tech, conversation, bodyMarker + new string('x', 2000)),
            await host.PostMessageAsync(outsider, conversation, bodyMarker),
            await host.GetAsync($"/messages/conversations?search={searchMarker}", tech.Cookie),
            await host.GetAsync($"/messages/conversations?search={searchMarker}{new string('y', 80)}", tech.Cookie),
            await host.GetAsync($"/messages/conversations/{conversation}/messages", tech.Cookie),
            await host.SendAsync(HttpMethod.Post, $"/messages/conversations/{conversation}/read", tech.Cookie),
            await host.GetAsync("/messages/summary", tech.Cookie),
            await host.GetAsync("/messages/summary", viewer.Cookie),
            await host.GetAsync("/messages/summary", null),
            await host.GetAsync($"/messages/conversations/{Guid.NewGuid()}", tech.Cookie),
        };

        HttpResponseMessage last = null!;

        for (var i = 0; i < 31; i++)
        {
            last = await host.PostMessageAsync(spammer, conversation, $"{bodyMarker} {i}");
        }

        responses.Add(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.BadRequest],
            responses.Take(5).Select(response => response.StatusCode).ToArray());

        foreach (var response in responses)
        {
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

            // Errors never echo the submitted body or search text.
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(bodyMarker, text, StringComparison.Ordinal);
                Assert.DoesNotContain(searchMarker, text, StringComparison.Ordinal);
            }
        }

        var logs = string.Join(
            "\n",
            host.Logs.Entries.Select(entry =>
                $"{entry.Message}\n{entry.Exception}\n{string.Join(';', entry.Properties.Select(pair => $"{pair.Key}={pair.Value}"))}"));
        Assert.DoesNotContain(bodyMarker, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(searchMarker, logs, StringComparison.Ordinal);

        // No audit row: messages, searches and failures leave audit_logs untouched.
        var after = await database.StateAsync(world.Org);
        Assert.Equal(before.Split("|a=")[1], after.Split("|a=")[1]);
    }
}
