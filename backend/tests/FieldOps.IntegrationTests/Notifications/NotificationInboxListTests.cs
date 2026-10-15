using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FieldOps.IntegrationTests.Notifications.NotificationSeed;

namespace FieldOps.IntegrationTests.Notifications;

/// <summary>The inbox list, summary, read state and links (notifications-center AC-07 to AC-11, AC-24).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class NotificationInboxListTests(CompanySettingsDatabaseFixture database)
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTimeOffset LocalMidnightToday() =>
        OrganizationTime.StartOfDayUtc(OrganizationTime.LocalDate(DateTimeOffset.UtcNow, NewYork), NewYork);

    // AC-07, AC-08: tabs, search, type, branch and date filters, counts that ignore the tab, newest-first order with an id tiebreak,
    // paging and every validation error, in the organization time zone (BR-10, BR-11, BR-13).
    [Fact]
    public async Task List_FiltersCountsOrderPagingAndValidation_RunInTheDatabase()
    {
        var world = await database.SeedWorldAsync("America/New_York");
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var today = LocalMidnightToday();

        // a and f tie on created_at; the order falls back to the id, descending.
        var a = await database.SeedNotificationAsync(world.Org, member.UserId, "request.created", today.AddSeconds(5), branch: world.BranchA, title: "New service request", description: "Carla submitted REQ-1.", label: "REQ-1");
        var f = await database.SeedNotificationAsync(world.Org, member.UserId, "job.completed", today.AddSeconds(5), branch: world.BranchA, kind: "work_order", title: "Job completed", description: "Tina completed WO-6.", label: "WO-6");
        var b = await database.SeedNotificationAsync(world.Org, member.UserId, "quote.approved", today.AddSeconds(1), read: true, branch: world.BranchA, kind: "quote", title: "Quote Q-2 was approved", description: "Carla Customer approved the quote for 100.00 USD.", label: "Q-2", secondary: "REQ-1");
        var c = await database.SeedNotificationAsync(world.Org, member.UserId, "visit.assigned", today.AddDays(-2).AddHours(12), branch: world.BranchB, kind: "visit", title: "Visit assigned", description: "WO-3 was assigned to Tess.", label: "WO-3", secondary: "Visit 1");
        var d = await database.SeedNotificationAsync(world.Org, member.UserId, "payment.received", today.AddDays(-10), kind: "invoice", title: "Payment received", description: "Payment of 40.00 USD was recorded for invoice INV-4.", label: "INV-4");
        var e = await database.SeedNotificationAsync(world.Org, member.UserId, "invoice.sent", today.AddDays(-40), read: true, branch: world.BranchA, kind: "invoice", title: "Invoice INV-5 was sent", description: "INV-5 was sent to Carla.", label: "INV-5");

        var tied = await database.QueryGuidsAsync("SELECT id FROM notifications WHERE id IN (@a, @f) ORDER BY id DESC", ("a", a), ("f", f));
        Guid[] Expect(params Guid[] others) => [.. tied, .. others];

        async Task<JsonNode> ListAsync(string query) => await NotificationSeed.GetAsync(host, owner, "/notifications" + query);

        var all = await ListAsync(string.Empty);
        Assert.Equal(Expect(b, c, d, e), Ids(all));
        Assert.Equal((6, 6, 4), (all["total"]!.GetValue<int>(), Counts(all).All, Counts(all).Unread));

        // Row contract: type, event code, snapshot text, labels, branch name, UTC time, read state and the target.
        var row = all["items"]!.AsArray().Single(item => item!["id"]!.GetValue<Guid>() == b)!;
        Assert.Equal(
            ("quote", "quote.approved", "Quote Q-2 was approved", "Carla Customer approved the quote for 100.00 USD.", "quote", "Q-2", "REQ-1", true),
            (row["type"]!.GetValue<string>(), row["eventCode"]!.GetValue<string>(), row["title"]!.GetValue<string>(), row["description"]!.GetValue<string>(), row["entityKind"]!.GetValue<string>(), row["entityLabel"]!.GetValue<string>(), row["secondaryLabel"]!.GetValue<string>(), row["isRead"]!.GetValue<bool>()));
        Assert.Equal(("Alpha Branch", world.BranchA), (row["branch"]!["name"]!.GetValue<string>(), row["branch"]!["id"]!.GetValue<Guid>()));
        Assert.Equal(today.AddSeconds(1), row["createdAt"]!.GetValue<DateTimeOffset>());
        Assert.Null(row["target"]);
        Assert.Null(all["items"]!.AsArray().Single(item => item!["id"]!.GetValue<Guid>() == d)!["branch"]);

        // Tabs apply to the items and the total, never to the counts.
        var unread = await ListAsync("?tab=unread");
        Assert.Equal(Expect(c, d), Ids(unread));
        Assert.Equal((4, 6, 4), (unread["total"]!.GetValue<int>(), Counts(unread).All, Counts(unread).Unread));

        // Date windows follow the organization time zone, not UTC.
        Assert.Equal(Expect(b), Ids(await ListAsync("?date=today")));
        Assert.Equal(Expect(b, c), Ids(await ListAsync("?date=7d")));
        Assert.Equal(Expect(b, c, d), Ids(await ListAsync("?date=30d")));
        var weekUnread = await ListAsync("?date=7d&tab=unread");
        Assert.Equal(Expect(c), Ids(weekUnread));
        Assert.Equal((4, 3), Counts(weekUnread));

        // Type, branch and search: title, description, entity label and secondary label; case-insensitive; trimmed; literal wildcards.
        Assert.Equal([b], Ids(await ListAsync("?type=quote")));
        Assert.Equal(Expect(b, e), Ids(await ListAsync($"?branchId={world.BranchA}")));
        Assert.Equal([c], Ids(await ListAsync($"?branchId={world.BranchB}")));
        Assert.Equal([b], Ids(await ListAsync("?q=APPROVED")));
        Assert.Equal([a, b, e], Ids(await ListAsync("?q=%20carla%20")));
        Assert.Equal([d], Ids(await ListAsync("?q=inv-4")));
        Assert.Equal([c], Ids(await ListAsync("?q=visit%201")));
        Assert.Equal([b], Ids(await ListAsync("?q=req-1&type=quote")));
        Assert.Empty(Ids(await ListAsync("?q=%25")));
        Assert.Empty(Ids(await ListAsync("?q=_")));
        Assert.Equal(6, (await ListAsync("?q=%20%20"))["total"]!.GetValue<int>());
        var combined = await ListAsync($"?type=invoice&branchId={world.BranchA}&date=7d&q=inv");
        Assert.Empty(Ids(combined));
        Assert.Equal((0, 0), Counts(combined));

        // Paging: the real total on a page past the last, and the server default page size.
        var second = await ListAsync("?pageSize=2&page=2");
        Assert.Equal(Expect(b, c, d, e).Skip(2).Take(2), Ids(second));
        Assert.Equal((6, 2, 2), (second["total"]!.GetValue<int>(), second["page"]!.GetValue<int>(), second["pageSize"]!.GetValue<int>()));
        var past = await ListAsync("?pageSize=4&page=9");
        Assert.Empty(Ids(past));
        Assert.Equal((6, 6), (past["total"]!.GetValue<int>(), Counts(past).All));

        // AC-08: one 400 per invalid value, keyed by field, and several at once; a search of exactly 100 characters is valid.
        foreach (var (query, key) in new[]
        {
            ("?tab=drafts", "tab"),
            ($"?q={new string('x', 101)}", "q"),
            ("?type=work_order", "type"),
            ("?date=90d", "date"),
            ("?page=0", "page"),
            ("?pageSize=101", "pageSize"),
            ("?pageSize=0", "pageSize"),
        })
        {
            var problem = await ReadAsync(await host.SendAsync(HttpMethod.Get, "/notifications" + query, owner), HttpStatusCode.BadRequest);
            Assert.Equal([key], problem["errors"]!.AsObject().Select(error => error.Key).ToArray());
        }

        var several = await ReadAsync(await host.SendAsync(HttpMethod.Get, "/notifications?tab=x&page=-1&date=y", owner), HttpStatusCode.BadRequest);
        Assert.Equal(["date", "page", "tab"], several["errors"]!.AsObject().Select(error => error.Key).Order().ToArray());
        Assert.Empty(Ids(await ListAsync($"?q={new string('x', 100)}")));
    }

    // AC-09, AC-11: summary in the organization time zone, idempotent read and unread that only touch the status, and read-all up to the
    // request time.
    [Fact]
    public async Task SummaryReadUnreadAndReadAll_FollowTheLocalDayAndChangeOnlyTheStatus()
    {
        var world = await database.SeedWorldAsync("America/New_York");
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var midnight = LocalMidnightToday();

        // One minute before local midnight and the first minute after it share a UTC day: the zone, not UTC, decides.
        var yesterday = await database.SeedNotificationAsync(world.Org, member.UserId, "request.created", midnight.AddMinutes(-1));
        var first = await database.SeedNotificationAsync(world.Org, member.UserId, "request.created", midnight);
        var readToday = await database.SeedNotificationAsync(world.Org, member.UserId, "quote.approved", midnight.AddMinutes(1), read: true, kind: "quote");
        var future = await database.SeedNotificationAsync(world.Org, member.UserId, "request.created", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal((3, 3), Summary(await NotificationSeed.GetAsync(host, owner, "/notifications/summary")));

        var digest = await DigestAsync(yesterday);
        Assert.Equal((2, 3), Summary((await NotificationSeed.PostAsync(host, owner, $"/notifications/{yesterday}/read"))["summary"]!));
        Assert.Equal("read", await database.StatusAsync(yesterday));
        Assert.Equal((2, 3), Summary((await NotificationSeed.PostAsync(host, owner, $"/notifications/{yesterday}/read"))["summary"]!));
        Assert.Equal(digest, await DigestAsync(yesterday));

        Assert.Equal((3, 3), Summary((await NotificationSeed.PostAsync(host, owner, $"/notifications/{yesterday}/unread"))["summary"]!));
        Assert.Equal((3, 3), Summary((await NotificationSeed.PostAsync(host, owner, $"/notifications/{yesterday}/unread"))["summary"]!));
        Assert.Equal("sent", await database.StatusAsync(yesterday));
        Assert.Equal(digest, await DigestAsync(yesterday));

        // Read-all marks every visible unread notification created up to now, whatever the view; a later one stays unread.
        var readAll = await NotificationSeed.PostAsync(host, owner, "/notifications/read-all");
        Assert.Equal(2, readAll["updated"]!.GetValue<int>());
        Assert.Equal((1, 3), Summary(readAll["summary"]!));
        Assert.Equal(
            ["read", "read", "read", "sent"],
            new[] { await database.StatusAsync(yesterday), await database.StatusAsync(first), await database.StatusAsync(readToday), await database.StatusAsync(future) });
        Assert.Equal(0, (await NotificationSeed.PostAsync(host, owner, "/notifications/read-all"))["updated"]!.GetValue<int>());
    }

    // AC-10, AC-24: links only when the current role may read the kind and the entity exists in the caller's scope, and a command count
    // that does not depend on the page size.
    [Fact]
    public async Task Targets_KindPolicyAndScope_AndTheCommandCountDoesNotDependOnThePageSize()
    {
        var world = await database.SeedWorkspaceWorldAsync();
        var counter = new CommandCounter();
        await using var session = SessionTestHost.Create(database.ConnectionString);
        using var client = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(counter)))));
        var ownerMember = await database.SeedMemberAsync(world.Org, Owner, "Olive", "Owner");
        var owner = await CompanySettingsApi.SignInCookieAsync(client, ownerMember.Email);
        var dispatcherMember = await database.SeedMemberAsync(world.Org, Dispatcher, "Dee", "Alpha", isAllBranches: false);
        await database.LinkMembershipToBranchAsync(dispatcherMember.MembershipId, world.BranchA);
        var dispatcher = await CompanySettingsApi.SignInCookieAsync(client, dispatcherMember.Email);
        var accountingMember = await database.SeedMemberAsync(world.Org, Accounting, "Ann", "Books");
        var accounting = await CompanySettingsApi.SignInCookieAsync(client, accountingMember.Email);
        var user = ownerMember.UserId;
        var at = DateTimeOffset.UtcNow.AddMinutes(-30);

        var requestA = await database.SeedRequestAsync(world, branch: world.BranchA);
        var requestB = await database.SeedRequestAsync(world, branch: world.BranchB);
        var quote = await database.SeedWorkspaceQuoteAsync(world, user, "sent");
        var orderA = await database.SeedOrderAsync(world, user);
        var orderB = await database.SeedOrderAsync(world, user, branch: world.BranchB);
        var invoice = await database.SeedInvoiceAsync(world, user, new InvoiceSpec());

        async Task<Guid> RowAsync(Guid recipient, string code, string kind, Guid entity, int minutes) =>
            await database.SeedNotificationAsync(world.Org, recipient, code, at.AddMinutes(minutes), kind: kind, entityId: entity);

        var rows = new Dictionary<string, Guid>
        {
            ["request"] = await RowAsync(user, "request.created", "request", requestA.Id, 1),
            ["customer"] = await RowAsync(user, "customer.message_sent", "customer", world.Customer, 2),
            ["quote"] = await RowAsync(user, "quote.approved", "quote", quote.Id, 3),
            ["visit"] = await RowAsync(user, "visit.started", "visit", orderA.Visit, 4),
            ["work_order"] = await RowAsync(user, "job.completed", "work_order", orderA.Order, 5),
            ["invoice"] = await RowAsync(user, "invoice.sent", "invoice", invoice.Id, 6),
            ["deleted"] = await RowAsync(user, "request.created", "request", Guid.NewGuid(), 7),
        };

        async Task<Dictionary<Guid, string?>> TargetsAsync(string cookie)
        {
            var page = await ReadAsync(await CompanySettingsApi.SendRawAsync(client, HttpMethod.Get, "/notifications?pageSize=100", null, cookie));

            return page["items"]!.AsArray().ToDictionary(
                item => item!["id"]!.GetValue<Guid>(),
                item => item!["target"] is { } target ? $"{target["kind"]!.GetValue<string>()}:{target["id"]!.GetValue<Guid>()}" : null);
        }

        // The owner links every kind that exists; a deleted entity keeps the row but has no link.
        var ownerTargets = await TargetsAsync(owner);
        Assert.Equal(
            new string?[] { $"request:{requestA.Id}", $"customer:{world.Customer}", $"quote:{quote.Id}", $"visit:{orderA.Visit}", $"work_order:{orderA.Order}", $"invoice:{invoice.Id}", null },
            new[] { "request", "customer", "quote", "visit", "work_order", "invoice", "deleted" }.Select(key => ownerTargets[rows[key]]).ToArray());

        // A dispatcher limited to branch A: an entity of branch B is out of scope, one of branch A is linked.
        var dee = dispatcherMember.UserId;
        var inScope = await RowAsync(dee, "request.created", "request", requestA.Id, 10);
        var outOfScope = await RowAsync(dee, "request.created", "request", requestB.Id, 11);
        var visitOfB = await RowAsync(dee, "visit.started", "visit", orderB.Visit, 12);
        var jobOfB = await RowAsync(dee, "job.completed", "work_order", orderB.Order, 13);
        var jobOfA = await RowAsync(dee, "job.completed", "work_order", orderA.Order, 14);
        var deeTargets = await TargetsAsync(dispatcher);
        Assert.Equal(
            new string?[] { $"request:{requestA.Id}", null, null, null, $"work_order:{orderA.Order}" },
            new[] { inScope, outOfScope, visitOfB, jobOfB, jobOfA }.Select(id => deeTargets[id]).ToArray());

        // Accounting reads invoices but not jobs: the job.completed row is listed without a link.
        var ann = accountingMember.UserId;
        var accountingJob = await RowAsync(ann, "job.completed", "work_order", orderA.Order, 20);
        var accountingInvoice = await RowAsync(ann, "payment.received", "invoice", invoice.Id, 21);
        var annTargets = await TargetsAsync(accounting);
        Assert.Equal(new string?[] { null, $"invoice:{invoice.Id}" }, new[] { annTargets[accountingJob], annTargets[accountingInvoice] });

        // AC-24: 10 versus 100 visible notifications. Every kind appears in each page, so the per-kind target lookups are the same.
        await database.ExecuteAsync(
            """
            INSERT INTO notifications (id, organization_id, recipient_user_id, channel, template_code, subject, payload, status, scheduled_at, sent_at, created_at)
            SELECT gen_random_uuid(), @org, @user, 'in_app', k.code, 'Bulk ' || g,
                jsonb_build_object('type', split_part(k.code, '.', 1), 'description', 'Bulk', 'entityKind', k.kind, 'entityId', gen_random_uuid(),
                    'entityLabel', 'X-' || g, 'secondaryLabel', CAST(NULL AS text), 'branchId', CASE WHEN g % 2 = 0 THEN CAST(@branch AS text) ELSE NULL END),
                CAST('sent' AS notification_status), now(), now(), now() - (g || ' minutes')::interval
            FROM generate_series(1, 100) g
            JOIN (VALUES (0, 'request.created', 'request'), (1, 'customer.message_sent', 'customer'), (2, 'quote.approved', 'quote'),
                (3, 'visit.started', 'visit'), (4, 'job.completed', 'work_order'), (5, 'invoice.sent', 'invoice'))
                AS k(n, code, kind) ON k.n = g % 6
            """,
            ("org", world.Org),
            ("user", user),
            ("branch", world.BranchA));

        async Task<(int Commands, JsonNode Body)> MeasureAsync(HttpMethod method, string path)
        {
            counter.Reset();
            var body = await ReadAsync(await CompanySettingsApi.SendRawAsync(client, method, path, null, owner));

            return (counter.Count, body);
        }

        var (ten, tenPage) = await MeasureAsync(HttpMethod.Get, "/notifications?pageSize=10");
        var (fifty, fiftyPage) = await MeasureAsync(HttpMethod.Get, "/notifications?pageSize=50");
        var (filtered, _) = await MeasureAsync(HttpMethod.Get, "/notifications?pageSize=10&tab=unread&q=bulk&type=quote&date=30d");
        var (summary, _) = await MeasureAsync(HttpMethod.Get, "/notifications/summary");
        Assert.Equal((10, 50, 107), (Ids(tenPage).Length, Ids(fiftyPage).Length, tenPage["total"]!.GetValue<int>()));
        Assert.Equal(ten, fifty);
        Assert.InRange(ten, 1, 20);
        Assert.InRange(filtered, 1, 20);
        Assert.InRange(summary, 1, 6);

        var (readAll100, all100) = await MeasureAsync(HttpMethod.Post, "/notifications/read-all");
        Assert.Equal(107, all100["updated"]!.GetValue<int>());
        await database.ExecuteAsync(
            "UPDATE notifications SET status = CAST('sent' AS notification_status) WHERE id IN (SELECT id FROM notifications WHERE recipient_user_id = @u ORDER BY created_at LIMIT 10)",
            ("u", user));
        var (readAll10, all10) = await MeasureAsync(HttpMethod.Post, "/notifications/read-all");
        Assert.Equal(10, all10["updated"]!.GetValue<int>());
        Assert.Equal(readAll100, readAll10);
    }

    // Every column except the status, so a read or unread call can be proven to change nothing else (BR-19).
    private Task<string> DigestAsync(Guid id) =>
        database.ScalarAsync<string>(
            "SELECT md5(concat_ws('|', organization_id, recipient_user_id, channel, template_code, subject, payload::text, scheduled_at, sent_at, created_at)) FROM notifications WHERE id = @id",
            ("id", id));
}
