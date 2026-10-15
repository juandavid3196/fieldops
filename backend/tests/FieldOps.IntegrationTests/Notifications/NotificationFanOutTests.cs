using System.Text.Json.Nodes;
using FieldOps.Application.Features.Notifications;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.Extensions.DependencyInjection;
using static FieldOps.IntegrationTests.Notifications.NotificationSeed;

namespace FieldOps.IntegrationTests.Notifications;

/// <summary>Recipient fan-out and stored snapshot of the notification writer (notifications-center BR-04, BR-06, BR-09; AC-04).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class NotificationFanOutTests(CompanySettingsDatabaseFixture database)
{
    // BR-04, written independently of the catalog: the roles each event notifies.
    private static readonly (string Code, string[] Roles)[] Events =
    [
        ("request.created", ["owner", "dispatcher"]),
        ("customer.message_sent", ["owner", "dispatcher"]),
        ("quote.approved", ["owner", "dispatcher"]),
        ("quote.rejected", ["owner", "dispatcher"]),
        ("quote.clarification_requested", ["owner", "dispatcher"]),
        ("visit.assigned", ["owner", "operations_manager", "dispatcher"]),
        ("visit.on_the_way", ["owner", "operations_manager", "dispatcher"]),
        ("visit.started", ["owner", "operations_manager", "dispatcher"]),
        ("visit.paused", ["owner", "operations_manager", "dispatcher"]),
        ("job.completed", ["owner", "operations_manager", "dispatcher", "accounting"]),
        ("invoice.sent", ["owner", "accounting"]),
        ("invoice.bank_transfer_reported", ["owner", "accounting"]),
        ("payment.received", ["owner", "accounting"]),
    ];

    // AC-04: every event on a branch A entity and on a null branch, with every role, both branch scopes, an inactive member and the actor.
    [Fact]
    public async Task Writer_EveryEvent_NotifiesOnlyEligibleMembersWithTheStoredSnapshot()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var members = new Dictionary<string, (Guid User, string Role)>();

        async Task AddAsync(string key, short roleId, string role, string status = "active", bool all = true, Guid? branch = null, Guid? org = null)
        {
            var member = await database.SeedMemberAsync(org ?? world.Org, roleId, key, "Member", status: status, isAllBranches: all);

            if (branch is { } scoped)
            {
                await database.LinkMembershipToBranchAsync(member.MembershipId, scoped);
            }

            members[key] = (member.UserId, role);
        }

        await AddAsync("owner", Owner, "owner");
        await AddAsync("ops", Operations, "operations_manager");
        await AddAsync("dispatcherA", Dispatcher, "dispatcher", all: false, branch: world.BranchA);
        await AddAsync("dispatcherB", Dispatcher, "dispatcher", all: false, branch: world.BranchB);
        await AddAsync("dispatcherAll", Dispatcher, "dispatcher");
        await AddAsync("accountingA", Accounting, "accounting", all: false, branch: world.BranchA);
        await AddAsync("accountingAll", Accounting, "accounting");
        await AddAsync("viewer", Viewer, "viewer");
        await AddAsync("technician", Technician, "technician");
        await AddAsync("suspendedOwner", Owner, "owner", status: "suspended");
        await AddAsync("foreignOwner", Owner, "owner", org: foreign.Org);
        var inScopeOfA = new HashSet<string> { "owner", "ops", "dispatcherA", "dispatcherAll", "accountingA", "accountingAll", "viewer", "technician", "suspendedOwner" };
        var scopeless = inScopeOfA.Concat(["dispatcherB"]).ToHashSet();

        await using var session = SessionTestHost.Create(database.ConnectionString);
        var at = new DateTimeOffset(2026, 10, 15, 16, 20, 5, TimeSpan.Zero);
        var checkedRows = 0;

        foreach (var (code, roles) in Events)
        {
            foreach (var (branch, scope) in new[] { ((Guid?)world.BranchA, inScopeOfA), ((Guid?)null, scopeless) })
            {
                foreach (var actor in new[] { (Guid?)members["dispatcherAll"].User, null })
                {
                    var entity = Guid.NewGuid();
                    var expected = members
                        .Where(pair => roles.Contains(pair.Value.Role) && scope.Contains(pair.Key) && pair.Key != "suspendedOwner" && pair.Key != "foreignOwner")
                        .Where(pair => actor is null || pair.Value.User != actor)
                        .Select(pair => pair.Value.User)
                        .Order()
                        .ToArray();

                    await using var scopeOfRequest = session.Factory.Services.CreateAsyncScope();
                    var writer = scopeOfRequest.ServiceProvider.GetRequiredService<INotificationEventWriter>();
                    var context = scopeOfRequest.ServiceProvider.GetRequiredService<FieldOpsDbContext>();

                    await writer.AddAsync(
                        new NotificationEvent(
                            code, world.Org, branch, actor, at, entity,
                            new NotificationContent("work_order", "WO-7", "Visit 2", $"Title of {code}", "A description with the customer name only.")),
                        CancellationToken.None);

                    // The writer only adds rows: nothing is stored until the producer's own save.
                    Assert.Equal(0L, await RowsOfAsync(entity));
                    await context.SaveChangesAsync();

                    var recipients = await database.QueryGuidsAsync(
                        "SELECT recipient_user_id FROM notifications WHERE organization_id = @o AND payload->>'entityId' = @e ORDER BY recipient_user_id",
                        ("o", world.Org),
                        ("e", entity.ToString()));
                    Assert.Equal(expected.Order(), recipients.Order());
                    checkedRows += recipients.Length;
                }
            }
        }

        Assert.True(checkedRows > 100);

        // The stored snapshot (BR-06): channel, status, times, subject and exactly the payload keys; nothing else.
        var sample = Guid.NewGuid();
        await using (var scopeOfSample = session.Factory.Services.CreateAsyncScope())
        {
            await scopeOfSample.ServiceProvider.GetRequiredService<INotificationEventWriter>().AddAsync(
                new NotificationEvent(
                    "quote.approved", world.Org, world.BranchA, null, at, sample,
                    new NotificationContent("quote", "Q-9", "REQ-4", "Quote Q-9 was approved", "Carla approved the quote for 10.00 USD.")),
                CancellationToken.None);
            await scopeOfSample.ServiceProvider.GetRequiredService<FieldOpsDbContext>().SaveChangesAsync();
        }

        var row = await database.ScalarAsync<string>(
            """
            SELECT string_agg(channel || '|' || status::text || '|' || template_code || '|' || subject || '|' || COALESCE(recipient_contact_id::text, '-') || '|'
                || (scheduled_at = @at)::text || '|' || (sent_at = @at)::text || '|' || (created_at = @at)::text, ';' ORDER BY recipient_user_id)
            FROM (SELECT DISTINCT channel, status, template_code, subject, recipient_contact_id, scheduled_at, sent_at, created_at, recipient_user_id
                  FROM notifications WHERE payload->>'entityId' = @e) n
            """,
            ("at", at),
            ("e", sample.ToString()));
        Assert.All(
            row.Split(';'),
            line => Assert.Equal("in_app|sent|quote.approved|Quote Q-9 was approved|-|true|true|true", line));

        var payload = JsonNode.Parse(await database.ScalarAsync<string>(
            "SELECT payload::text FROM notifications WHERE payload->>'entityId' = @e LIMIT 1", ("e", sample.ToString())))!.AsObject();
        Assert.Equal(
            ["branchId", "description", "entityId", "entityKind", "entityLabel", "secondaryLabel", "type"],
            payload.Select(pair => pair.Key).Order().ToArray());
        Assert.Equal(
            ("quote", "quote", "Q-9", "REQ-4", world.BranchA.ToString(), sample.ToString()),
            (payload["type"]!.GetValue<string>(), payload["entityKind"]!.GetValue<string>(), payload["entityLabel"]!.GetValue<string>(), payload["secondaryLabel"]!.GetValue<string>(), payload["branchId"]!.GetValue<string>(), payload["entityId"]!.GetValue<string>()));

        // Zero recipients write nothing (an organization of technicians only), and a rolled back unit of work leaves no row.
        var empty = await database.SeedWorldAsync();
        await database.SeedMemberAsync(empty.Org, Technician, "Tess", "Tech");
        var lonely = Guid.NewGuid();
        await using (var scopeOfEmpty = session.Factory.Services.CreateAsyncScope())
        {
            await scopeOfEmpty.ServiceProvider.GetRequiredService<INotificationEventWriter>().AddAsync(
                new NotificationEvent("request.created", empty.Org, null, null, at, lonely, new NotificationContent("request", "REQ-1", null, "New service request", "X submitted REQ-1.")),
                CancellationToken.None);
            await scopeOfEmpty.ServiceProvider.GetRequiredService<FieldOpsDbContext>().SaveChangesAsync();
        }

        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE organization_id = @o", ("o", empty.Org)));

        var rolledBack = Guid.NewGuid();
        await using (var scopeOfRollback = session.Factory.Services.CreateAsyncScope())
        {
            var context = scopeOfRollback.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await scopeOfRollback.ServiceProvider.GetRequiredService<INotificationEventWriter>().AddAsync(
                new NotificationEvent("request.created", world.Org, null, null, at, rolledBack, new NotificationContent("request", "REQ-2", null, "New service request", "X submitted REQ-2.")),
                CancellationToken.None);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Equal(0L, await RowsOfAsync(rolledBack));
    }

    private Task<long> RowsOfAsync(Guid entity) =>
        database.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE payload->>'entityId' = @e", ("e", entity.ToString()));
}
