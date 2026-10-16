using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;
using static FieldOps.IntegrationTests.AuditLog.AuditSeed;

namespace FieldOps.IntegrationTests.AuditLog;

/// <summary>Redaction, search, detail, targets and the CSV of the audit log (audit-trail AC-06, AC-07, AC-13, AC-14, AC-24).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AuditLogContentTests(CompanySettingsDatabaseFixture database)
{
    private static readonly string[] Forbidden =
    [
        "hunter2", "hunter3", "old@secret.test", "new@secret.test", "4242424242424242", "4000000000000002", "tok_abc",
        "DE89370400440532013000", "x@y.test", "+1 555 0100",
    ];

    // AC-06, AC-07: nested secrets, emails and phones never leave the server through the list, the detail or the CSV, and are never searchable.
    [Fact]
    public async Task RedactionAndSearch_AcrossListDetailAndCsv_NeverExposeOrMatchSensitiveValues()
    {
        var world = await database.SeedWorldAsync("UTC");
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, Owner);
        var dana = await database.SeedMemberAsync(world.Org, Dispatcher, "Dana", "Dispatcher");
        var danaEmail = await database.ScalarAsync<string>("SELECT email FROM users WHERE id = @u", ("u", dana.UserId));
        await database.ClearAuditAsync(world.Org);
        var at = LocalStart("UTC").AddMinutes(1);

        var id = await database.InsertAsync(
            world.Org, "customer.updated", "customer", at, actor: dana.UserId, entity: world.Customer, branch: world.BranchA, ip: "192.168.7.9",
            before: """{"password":"hunter2","email":"old@secret.test","card":{"cardNumber":"4242424242424242","last4":"4242"},"city":"A"}""",
            after: """{"password":"hunter3","email":"new@secret.test","card":{"cardNumber":"4000000000000002","last4":"0002"},"city":"B","phone":"+1 555 0100"}""",
            metadata: """{"token":"tok_abc","contactEmail":"x@y.test","items":[{"iban":"DE89370400440532013000","label":"Main"}],"channel":"web"}""");
        var decoy = await database.InsertAsync(world.Org, "quote.sent", "quote", at.AddMinutes(1), entity: Guid.NewGuid());

        var listBody = await TextAsync(host, owner, "/audit-log");
        var detailBody = await TextAsync(host, owner, $"/audit-log/{id}");
        var csv = await TextAsync(host, owner, "/audit-log/export");

        foreach (var secret in Forbidden)
        {
            Assert.DoesNotContain(secret, listBody);
            Assert.DoesNotContain(secret, detailBody);
            Assert.DoesNotContain(secret, csv);
        }

        var row = Item(JsonNode.Parse(listBody)!, id);
        var summary = row["summary"]!.GetValue<string>();
        Assert.StartsWith("Card: ", summary, StringComparison.Ordinal);
        Assert.EndsWith("(+4 more)", summary, StringComparison.Ordinal);

        var detail = JsonNode.Parse(detailBody)!;
        var changes = detail["changes"]!.AsArray().ToDictionary(
            change => change!["field"]!.GetValue<string>(), change => (change!["before"]!.GetValue<string>(), change["after"]!.GetValue<string>()));
        Assert.Equal(("[redacted]", "[redacted]"), changes["password"]);
        Assert.Equal(("[changed]", "[changed]"), changes["email"]);
        Assert.Equal(("—", "[changed]"), changes["phone"]);
        Assert.Equal(("A", "B"), changes["city"]);
        Assert.Contains("\"last4\":\"4242\"", changes["card"].Item1);
        Assert.Contains("\"last4\":\"0002\"", changes["card"].Item2);
        Assert.Contains("[redacted]", changes["card"].Item1);

        var metadata = detail["metadata"]!.AsArray().ToDictionary(pair => pair!["key"]!.GetValue<string>(), pair => pair!["value"]!.GetValue<string>());
        Assert.Equal("[redacted]", metadata["token"]);
        Assert.Equal("[changed]", metadata["contactEmail"]);
        Assert.Contains("\"iban\":\"[redacted]\"", metadata["items"]);
        Assert.Contains("\"label\":\"Main\"", metadata["items"]);
        Assert.Equal("web", metadata["channel"]);

        // BR-04: redacted values, emails, before/after and metadata values match nothing; names, references, labels, IP and the event id do.
        foreach (var term in new[] { "hunter2", "secret.test", "4242", "tok_abc", "x%40y.test", "DE89", "%2B1%20555", Uri.EscapeDataString(danaEmail), "old", "%25", "_" })
        {
            Assert.Empty(Ids(await GetAsync(host, owner, "/audit-log?q=" + term)));
        }

        foreach (var term in new[]
        {
            "Dana", "dispatcher", "DANA%20dispatcher", "Carla", "carla%20customer", $"AUD-{id}", $"aud-{id}", "192.168.7", "Customer%20updated", "customer", "%20Dana%20",
        })
        {
            var ids = Ids(await GetAsync(host, owner, "/audit-log?q=" + term));
            Assert.Contains(id, ids);
            Assert.DoesNotContain(decoy, ids);
        }

        Assert.Equal([decoy], Ids(await GetAsync(host, owner, "/audit-log?q=Quote%20sent")));
        Assert.Equal([decoy], Ids(await GetAsync(host, owner, "/audit-log?q=system")));
        Assert.Equal([id], Ids(await GetAsync(host, owner, "/audit-log?q=dana&action=customer.updated")));

        // The same search narrows the CSV.
        var narrowed = Records(await TextAsync(host, owner, "/audit-log/export?q=" + Uri.EscapeDataString($"AUD-{id}")));
        Assert.Equal([$"AUD-{id}"], narrowed.Skip(1).Select(record => record[0]));
    }

    // AC-13, BR-25: the detail of an event, the actor role, the changes, the metadata and a target only where the caller can open it.
    [Fact]
    public async Task Detail_ActorChangesMetadataAndTarget_FollowTheCallerRoleAndScope()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, Owner, "Olivia", "Owner");
        var (ops, opsMember) = await host.SignInAsync(database, world.Org, Operations, "Omar", "Ops");
        var (accountingA, _) = await host.SignInAsync(database, world.Org, Accounting, "Ada", "Accounting", world.BranchA);
        var requestA = await database.SeedRequestAsync(world, branch: world.BranchA);
        var requestB = await database.SeedRequestAsync(world, branch: world.BranchB);
        var job = await database.SeedOrderAsync(world, ownerMember.UserId);
        var invoice = await database.SeedInvoiceAsync(world, ownerMember.UserId, new InvoiceSpec());
        var payment = await database.SeedPaymentAsync(world.Org, invoice, 10m, -1, "cash", null, ownerMember.UserId, ownerMember.UserId);
        await database.ClearAuditAsync(world.Org);
        var at = LocalStart("UTC").AddMinutes(1);
        var roleName = await database.ScalarAsync<string>("SELECT name FROM roles WHERE id = @r", ("r", Operations));

        async Task<long> RowAsync(string action, string type, Guid? entity, string? before = null, string? after = null, string metadata = "{}") =>
            await database.InsertAsync(world.Org, action, type, at, actor: opsMember.UserId, entity: entity, branch: world.BranchA, before: before, after: after, metadata: metadata, ip: "203.0.113.4");

        var request = await RowAsync(
            "service_request.status_changed", "service_request", requestA.Id, """{"status":"new"}""", """{"status":"quoted"}""", """{"note":"Called customer"}""");
        var workOrder = await RowAsync("work_order.billing_note_updated", "work_order", job.Order);
        var visit = await RowAsync("visit.completed", "visit", job.Visit);
        var invoiceRow = await RowAsync("invoice.sent", "invoice", invoice.Id);
        var paymentRow = await RowAsync("payment.recorded", "payment", payment.Id);
        var gone = await RowAsync("service_request.status_changed", "service_request", Guid.NewGuid());
        var branchRow = await RowAsync("branch.updated", "branch", world.BranchA);
        var catalog = await RowAsync("catalog_item.updated", "catalog_item", world.Service);
        var inScope = await RowAsync("invoice.sent", "service_request", requestA.Id);
        var outOfScope = await RowAsync("invoice.sent", "service_request", requestB.Id);

        var detail = await GetAsync(host, ops, $"/audit-log/{request}");
        Assert.Equal(("Omar Ops", roleName, "Status changed", "Request", $"REQ-{requestA.Number}", "Status: new → quoted", "203.0.113.4", "medium", "UTC"),
            (detail["actor"]!["name"]!.GetValue<string>(), detail["actor"]!["roleName"]!.GetValue<string>(), detail["action"]!["label"]!.GetValue<string>(),
                detail["entity"]!["label"]!.GetValue<string>(), detail["reference"]!.GetValue<string>(), detail["summary"]!.GetValue<string>(),
                detail["ipAddress"]!.GetValue<string>(), detail["impact"]!.GetValue<string>(), detail["timeZone"]!.GetValue<string>()));
        Assert.Equal(opsMember.UserId, detail["actor"]!["id"]!.GetValue<Guid>());
        var change = Assert.Single(detail["changes"]!.AsArray())!;
        Assert.Equal(("status", "Status", "new", "quoted"),
            (change["field"]!.GetValue<string>(), change["label"]!.GetValue<string>(), change["before"]!.GetValue<string>(), change["after"]!.GetValue<string>()));
        var metadata = detail["metadata"]!.AsArray().ToDictionary(pair => pair!["key"]!.GetValue<string>(), pair => pair!["value"]!.GetValue<string>());
        Assert.Equal(
            ($"AUD-{request}", "service_request", requestA.Id.ToString(), opsMember.UserId.ToString(), world.BranchA.ToString(), "Web app", "Called customer"),
            (metadata["eventId"], metadata["entityType"], metadata["entityId"], metadata["actorUserId"], metadata["branchId"], metadata["source"], metadata["note"]));

        async Task<(string? Kind, Guid? Id)> TargetAsync(string cookie, long id)
        {
            var target = (await GetAsync(host, cookie, $"/audit-log/{id}"))["target"];

            return target is null ? (null, null) : (target["kind"]!.GetValue<string>(), target["id"]!.GetValue<Guid>());
        }

        Assert.Equal(("service_request", requestA.Id), await TargetAsync(ops, request));
        Assert.Equal(("work_order", job.Order), await TargetAsync(ops, workOrder));
        Assert.Equal(("work_order", job.Order), await TargetAsync(ops, visit));
        Assert.Equal(("invoice", invoice.Id), await TargetAsync(ops, invoiceRow));
        Assert.Equal(("invoice", invoice.Id), await TargetAsync(ops, paymentRow));
        Assert.Equal(("catalog_item", world.Service), await TargetAsync(ops, catalog));
        Assert.Equal((null, null), await TargetAsync(ops, gone));
        Assert.Equal((null, null), await TargetAsync(ops, branchRow));
        Assert.Equal(("branch", world.BranchA), await TargetAsync(owner, branchRow));

        // BR-06: the reference of each entity in its existing display format; a visit shows its work order.
        Assert.Equal(job.Number.ToString(), (await GetAsync(host, ops, $"/audit-log/{visit}"))["reference"]!.GetValue<string>()["WO-".Length..]);
        Assert.Equal(invoice.Display, (await GetAsync(host, ops, $"/audit-log/{invoiceRow}"))["reference"]!.GetValue<string>());
        Assert.Equal(payment.Display, (await GetAsync(host, ops, $"/audit-log/{paymentRow}"))["reference"]!.GetValue<string>());

        // Accounting cannot open work orders; a financial event of an entity outside its branch scope has no target.
        Assert.Equal((null, null), await TargetAsync(accountingA, workOrder));
        Assert.Equal(("invoice", invoice.Id), await TargetAsync(accountingA, invoiceRow));
        Assert.Equal(("invoice", invoice.Id), await TargetAsync(accountingA, paymentRow));
        Assert.Equal(("service_request", requestA.Id), await TargetAsync(accountingA, inScope));
        Assert.Equal((null, null), await TargetAsync(accountingA, outOfScope));
    }

    // AC-14, AC-24: exact headers, local and UTC timestamps, escaping with formula neutralization, the sort, the filename and the limit.
    [Fact]
    public async Task Export_CsvContentEscapingSortAndLimit_FollowBr14()
    {
        var world = await database.SeedWorldAsync(Bogota);
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, Owner);
        var evil = await database.SeedMemberAsync(world.Org, Dispatcher, "=cmd|' /C calc'!A0", "Evil");
        var weirdName = "Smith, \"Bob\"\nJr";
        var weird = await database.SeedCustomerAsync(world.Org, world.BranchA, weirdName);
        await database.ClearAuditAsync(world.Org);
        var start = LocalStart(Bogota);
        var first = start.AddHours(10).AddMinutes(30);

        var low = await database.InsertAsync(world.Org, "customer.updated", "customer", first, actor: evil.UserId, entity: weird, branch: world.BranchA, ip: "192.0.2.5");
        var high = await database.InsertAsync(world.Org, "payment.recorded", "payment", first.AddMinutes(30), entity: Guid.NewGuid());

        var response = await host.SendAsync(HttpMethod.Get, "/audit-log/export?sort=impact&direction=desc", owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal($"audit-log-{LocalDay(Bogota)}.csv", response.Content.Headers.ContentDisposition!.FileName);
        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split("\r\n");
        Assert.Equal(
            "Event ID,Timestamp (local),Timestamp (UTC),Time zone,User,Action,Entity,Reference,Summary,Branch,Source,IP address,Impact",
            lines[0]);

        var records = Records(body);
        Assert.Equal(3, records.Count);
        Assert.Equal(
            [$"AUD-{high}", $"{LocalDay(Bogota)} 11:00:00", first.AddMinutes(30).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Bogota, "System", "Payment recorded", "Payment", "—", "Payment recorded", "All branches", "System", string.Empty, "High"],
            records[1]);
        Assert.Equal(
            [$"AUD-{low}", $"{LocalDay(Bogota)} 10:30:00", first.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Bogota, "'=cmd|' /C calc'!A0 Evil", "Customer updated", "Customer", weirdName, "Customer updated", "Alpha Branch", "Web app", "192.0.2.5", "Low"],
            records[2]);

        // The raw escaping: quoted with doubled quotes and the newline kept inside the cell; a formula-leading cell starts with an apostrophe.
        Assert.Contains("\"Smith, \"\"Bob\"\"\nJr\"", body, StringComparison.Ordinal);
        Assert.Contains(",'=cmd|' /C calc'!A0 Evil,", body, StringComparison.Ordinal);

        // Filters and sort apply, and a successful export recorded exactly one row.
        Assert.Equal([$"AUD-{low}"], Records(await TextAsync(host, owner, "/audit-log/export?action=customer.updated")).Skip(1).Select(record => record[0]));
        Assert.Equal([$"AUD-{low}", $"AUD-{high}"], Records(await TextAsync(host, owner, "/audit-log/export?sort=occurredAt&direction=asc")).Skip(1).Take(2).Select(record => record[0]));
        Assert.Equal(3, await database.CountAuditAsync(world.Org, "export.generated"));

        // BR-14: more than 5,000 matches is a conflict with its code and writes nothing.
        await database.ExecuteAsync(
            "INSERT INTO audit_logs (organization_id, action, entity_type, occurred_at) SELECT @o, 'quote.expired', 'quote', @at + (g * interval '1 second') FROM generate_series(1, 5001) g",
            ("o", world.Org),
            ("at", start.AddHours(1)));
        var tooLarge = await host.SendAsync(HttpMethod.Get, "/audit-log/export", owner);
        Assert.Equal(HttpStatusCode.Conflict, tooLarge.StatusCode);
        var problem = JsonNode.Parse(await tooLarge.Content.ReadAsStringAsync())!;
        Assert.Equal(("export_too_large", "Narrow the filters to export 5,000 rows or fewer."), (problem["code"]!.GetValue<string>(), problem["title"]!.GetValue<string>()));
        Assert.Equal(3, await database.CountAuditAsync(world.Org, "export.generated"));
        Assert.Equal(2, Records(await TextAsync(host, owner, "/audit-log/export?action=customer.updated&impact=low")).Count);
    }

    /// <summary>Parses RFC 4180 records (quoted cells with doubled quotes and embedded newlines).</summary>
    internal static List<List<string>> Records(string csv)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var current = csv[index];

            if (quoted)
            {
                if (current == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (current == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(current);
                }
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == ',')
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
            {
                record.Add(cell.ToString());
                cell.Clear();
                records.Add(record);
                record = [];
                index++;
            }
            else
            {
                cell.Append(current);
            }
        }

        return records;
    }
}
