using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FieldOps.IntegrationTests.Notifications.NotificationSeed;

namespace FieldOps.IntegrationTests.Notifications;

/// <summary>The notifications inbox: authorization, tenancy, re-authorization, list, summary, read state and links (notifications-center).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class NotificationInboxTests(CompanySettingsDatabaseFixture database)
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTimeOffset LocalMidnightToday() =>
        FieldOps.Application.Features.ServiceRequests.OrganizationTime.StartOfDayUtc(
            FieldOps.Application.Features.ServiceRequests.OrganizationTime.LocalDate(DateTimeOffset.UtcNow, NewYork), NewYork);

    // AC-01, AC-02, AC-03: role and session gates, tenant and recipient isolation, one identical 404, and the re-authorization of
    // list, counts, summary, options, read-all and ids when the branch scope or the role changes (BR-01, BR-03, BR-11).
    [Fact]
    public async Task Inbox_RolesTenancyAndLostAccess_AreReauthorizedOnEveryRead()
    {
        var world = await database.SeedWorldAsync("America/New_York");
        var foreign = await database.SeedWorldAsync("America/New_York");
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, Owner);
        var (dispatcher, dee) = await host.SignInAsync(database, world.Org, Dispatcher, "Dee", "Alpha", world.BranchA);
        var (technician, _) = await host.SignInAsync(database, world.Org, Technician);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, Owner);
        var inactive = await database.SeedBranchAsync(world.Org, "Closed Branch", isActive: false);
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        var inA = await database.SeedNotificationAsync(world.Org, dee.UserId, "request.created", at.AddMinutes(1), branch: world.BranchA);
        var inB = await database.SeedNotificationAsync(world.Org, dee.UserId, "request.created", at.AddMinutes(2), branch: world.BranchB);
        var noBranch = await database.SeedNotificationAsync(world.Org, dee.UserId, "request.created", at.AddMinutes(3));
        var job = await database.SeedNotificationAsync(world.Org, dee.UserId, "job.completed", at.AddMinutes(4), branch: world.BranchA, kind: "work_order");
        var invoice = await database.SeedNotificationAsync(world.Org, dee.UserId, "invoice.sent", at.AddMinutes(5), branch: world.BranchA, kind: "invoice");
        var email = await database.SeedNotificationAsync(world.Org, dee.UserId, "request.created", at.AddMinutes(6), channel: "email");
        var ofOwner = await database.SeedNotificationAsync(world.Org, ownerMember.UserId, "request.created", at.AddMinutes(7));
        var ofForeign = await database.SeedNotificationAsync(foreign.Org, foreignMember.UserId, "request.created", at.AddMinutes(8));

        // AC-01: a technician gets 403 and an anonymous caller 401 on every endpoint, before any read or write.
        var routes = new[]
        {
            (HttpMethod.Get, "/notifications"),
            (HttpMethod.Get, "/notifications/summary"),
            (HttpMethod.Get, "/notifications/options"),
            (HttpMethod.Post, $"/notifications/{inA}/read"),
            (HttpMethod.Post, $"/notifications/{inA}/unread"),
            (HttpMethod.Post, "/notifications/read-all"),
        };

        foreach (var (method, path) in routes)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(method, path, technician)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(method, path, null)).StatusCode);
        }

        Assert.Equal(["sent", "sent", "sent", "sent", "sent", "sent", "sent", "sent"], await StatusesAsync(inA, inB, noBranch, job, invoice, email, ofOwner, ofForeign));

        // BR-03: only the in-app rows of the caller, of the role's events, in the caller's branches (and without a branch).
        var page = await GetAsync(host, dispatcher, "/notifications");
        Assert.Equal([job, noBranch, inA], Ids(page));
        Assert.Equal((3, 3), Counts(page));
        Assert.Equal(("America/New_York", 25, 3), (page["timeZone"]!.GetValue<string>(), page["pageSize"]!.GetValue<int>(), page["total"]!.GetValue<int>()));
        Assert.Equal(3, Summary(await GetAsync(host, dispatcher, "/notifications/summary")).Unread);

        // AC-02: a hidden, other-recipient, other-organization and unknown id are one identical 404; nothing changes.
        var unknown = await host.SendAsync(HttpMethod.Post, $"/notifications/{Guid.NewGuid()}/read", dispatcher);
        var unknownBody = await NotFoundBodyAsync(unknown, "notification_not_found");
        Assert.Equal("no-store", unknown.Headers.CacheControl?.ToString());

        foreach (var (cookie, id) in new[] { (dispatcher, inB), (dispatcher, invoice), (dispatcher, email), (dispatcher, ofOwner), (dispatcher, ofForeign), (owner, inA), (foreignOwner, inA) })
        {
            foreach (var action in new[] { "read", "unread" })
            {
                Assert.Equal(unknownBody, await NotFoundBodyAsync(await host.SendAsync(HttpMethod.Post, $"/notifications/{id}/{action}", cookie), "notification_not_found"));
            }
        }

        Assert.Equal(["sent", "sent", "sent", "sent", "sent", "sent", "sent", "sent"], await StatusesAsync(inA, inB, noBranch, job, invoice, email, ofOwner, ofForeign));
        Assert.Equal([ofOwner], Ids(await GetAsync(host, owner, "/notifications")));
        Assert.Equal([ofForeign], Ids(await GetAsync(host, foreignOwner, "/notifications")));

        // BR-11: a foreign, other-scope, inactive, unknown and malformed branch are one identical 404 branch_not_found.
        var unknownBranch = await NotFoundBodyAsync(await host.SendAsync(HttpMethod.Get, $"/notifications?branchId={Guid.NewGuid()}", dispatcher), "branch_not_found");

        foreach (var branch in new[] { world.BranchB.ToString(), foreign.BranchA.ToString(), inactive.Id.ToString(), "not-a-guid" })
        {
            Assert.Equal(unknownBranch, await NotFoundBodyAsync(await host.SendAsync(HttpMethod.Get, $"/notifications?branchId={branch}", dispatcher), "branch_not_found"));
        }

        Assert.Equal([job, inA], Ids(await GetAsync(host, dispatcher, $"/notifications?branchId={world.BranchA}")));
        Assert.Equal(["Alpha Branch"], (await GetAsync(host, dispatcher, "/notifications/options"))["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>()).ToArray());

        // AC-03 (branch scope): Dee moves from branch A to branch B. Branch A rows disappear from every read and from read-all.
        await database.ExecuteAsync("DELETE FROM organization_user_branches WHERE organization_user_id = @m", ("m", dee.MembershipId));
        await database.LinkMembershipToBranchAsync(dee.MembershipId, world.BranchB);

        page = await GetAsync(host, dispatcher, "/notifications");
        Assert.Equal([noBranch, inB], Ids(page));
        Assert.Equal((2, 2), Counts(page));
        Assert.Equal(2, (await GetAsync(host, dispatcher, "/notifications/summary"))["unread"]!.GetValue<int>());
        Assert.Equal(unknownBody, await NotFoundBodyAsync(await host.SendAsync(HttpMethod.Post, $"/notifications/{inA}/read", dispatcher), "notification_not_found"));
        await NotFoundBodyAsync(await host.SendAsync(HttpMethod.Get, $"/notifications?branchId={world.BranchA}", dispatcher), "branch_not_found");
        Assert.Empty(Ids(await GetAsync(host, dispatcher, $"/notifications?branchId={world.BranchB}&type=job")));

        // A branch deactivated after the event is out of scope too (BR-03: an active branch).
        await database.ExecuteAsync("UPDATE branches SET is_active = false WHERE id = @b", ("b", world.BranchB));
        Assert.Equal([noBranch], Ids(await GetAsync(host, dispatcher, "/notifications")));
        await database.ExecuteAsync("UPDATE branches SET is_active = true WHERE id = @b", ("b", world.BranchB));

        var readAll = await PostAsync(host, dispatcher, "/notifications/read-all");
        Assert.Equal(2, readAll["updated"]!.GetValue<int>());
        Assert.Equal(0, Summary(readAll["summary"]!).Unread);
        Assert.Equal(["sent", "read", "sent", "read", "sent"], await StatusesAsync(inA, inB, job, noBranch, invoice));

        // The access returns: the rows reappear in their stored state.
        await database.ExecuteAsync("DELETE FROM organization_user_branches WHERE organization_user_id = @m", ("m", dee.MembershipId));
        await database.LinkMembershipToBranchAsync(dee.MembershipId, world.BranchA);
        page = await GetAsync(host, dispatcher, "/notifications");
        Assert.Equal([job, noBranch, inA], Ids(page));
        Assert.Equal((3, 2), Counts(page));
        Assert.Equal([false, true, false], page["items"]!.AsArray().Select(item => item!["isRead"]!.GetValue<bool>()).ToArray());

        // AC-03 (role): Dee becomes an accounting user; the event types of the new role replace the old ones.
        await database.ExecuteAsync("UPDATE organization_users SET role_id = @r WHERE id = @m", ("r", Accounting), ("m", dee.MembershipId));
        page = await GetAsync(host, dispatcher, "/notifications");
        Assert.Equal([invoice, job], Ids(page));
        Assert.Equal((2, 2), Counts(page));
        Assert.Equal(unknownBody, await NotFoundBodyAsync(await host.SendAsync(HttpMethod.Post, $"/notifications/{inA}/unread", dispatcher), "notification_not_found"));
        Assert.Equal(200, (int)(await host.SendAsync(HttpMethod.Post, $"/notifications/{invoice}/read", dispatcher)).StatusCode);

        // The role returns: the request rows reappear untouched, the invoice row keeps the state it was given.
        await database.ExecuteAsync("UPDATE organization_users SET role_id = @r WHERE id = @m", ("r", Dispatcher), ("m", dee.MembershipId));
        page = await GetAsync(host, dispatcher, "/notifications");
        Assert.Equal([job, noBranch, inA], Ids(page));
        Assert.Equal("read", await database.StatusAsync(invoice));
        Assert.Equal("read", await database.StatusAsync(noBranch));
        Assert.Equal("sent", await database.StatusAsync(inA));
    }

    private async Task<string[]> StatusesAsync(params Guid[] ids)
    {
        var statuses = new List<string>();

        foreach (var id in ids)
        {
            statuses.Add(await database.StatusAsync(id));
        }

        return [.. statuses];
    }

    private static Task<JsonNode> GetAsync(RequestsHost host, string cookie, string path) => NotificationSeed.GetAsync(host, cookie, path);

    private static Task<JsonNode> PostAsync(RequestsHost host, string cookie, string path) => NotificationSeed.PostAsync(host, cookie, path);
}
