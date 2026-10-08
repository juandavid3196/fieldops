using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.BillingReview;

/// <summary>The review detail, the evidence image and the review update (completed-jobs-review AC-07 to AC-13).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BillingReviewDetailTests(CompanySettingsDatabaseFixture database)
{
    private const string Timezone = "America/Chicago";

    private static readonly string[] SensitiveFragments =
        ["signatureContent", "SECRET-AUDIT", "203.0.113.9", "unitCost", "unit_cost", "beforeData", "afterData", "metadata", "ipAddress"];

    private static string[] Names(JsonNode detail) =>
        [.. detail["lines"]!.AsArray().Select(line => line!["name"]!.GetValue<string>())];

    [Fact]
    public async Task Detail_ComparisonTotalsVerificationEvidenceAndAudit_FollowTheBillingRules()
    {
        var world = await database.SeedWorldAsync(Timezone);
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "One");
        var completedAt = DateTimeOffset.UtcNow.AddHours(-5);
        var job = await database.SeedJobAsync(
            world,
            member.UserId,
            new JobSpec
            {
                Title = "Rich job",
                CompletedAt = completedAt,
                Technician = tech,
                WorkSeconds = 10200,
                Discount = 10m,
                Terms = "Payment due within 30 days of the invoice date.",
                AddedMaterials = [("Clamp", 2m)],
                Checklist = [new ChecklistSpec("Shut off water", true, true), new ChecklistSpec("Clean up", true, true), new ChecklistSpec("Photo of meter", false, false)],
                Lines =
                [
                    new("Labor", "service", 2m, "h", 100m),
                    new("Inspection", "service", 1m, "ea", 50m, 8.25m),
                    new("Pipe", "product", 3m, "ea", 10m, 8.25m, Planned: 3m, Used: 5m),
                    new("Valve", "product", 1m, "ea", 25m, 8.25m),
                    new("Gutter guard", "service", 1m, "ea", 40m, 8.25m, Optional: true, Selected: true),
                    new("Extra option", "service", 1m, "ea", 30m, 8.25m, Optional: true),
                ],
            });

        // A planned material without a quote line that was used, a during and an incident photo, and the audit trail.
        var sealant = Guid.NewGuid();
        await database.ExecuteAsync(
            """
            INSERT INTO work_order_planned_materials (id, organization_id, work_order_id, description, quantity, unit, source, sort_order)
            VALUES (@p, @org, @o, 'Sealant', 1, 'ea', 'truck_stock', 9);
            INSERT INTO visit_materials (visit_id, planned_material_id, description, quantity, unit, unit_cost) VALUES (@v, @p, 'Sealant', 1, 'ea', 9.99);
            """,
            ("p", sealant),
            ("org", world.Org),
            ("o", job.Order),
            ("v", job.Visit));
        await database.SeedEvidenceAsync(member.UserId, job.Visit, Guid.NewGuid(), "during", "Mid-job");
        await database.SeedEvidenceAsync(member.UserId, job.Visit, Guid.NewGuid(), "incident", null);

        var now = DateTimeOffset.UtcNow;
        await database.ExecuteAsync(
            """
            INSERT INTO audit_logs (organization_id, action, entity_type, entity_id, metadata, occurred_at)
            SELECT @org, 'visit.paused', 'visit', @v, '{}', @at - make_interval(mins => g) FROM generate_series(1, 55) AS g
            """,
            ("org", world.Org),
            ("v", job.Visit),
            ("at", now.AddDays(-1)));
        await database.SeedAuditAsync(world.Org, "work_order.created", "work_order", job.Order, member.UserId, now.AddHours(-3), "SECRET-AUDIT");
        await database.SeedAuditAsync(world.Org, "visit.dispatched", "visit", job.Visit, member.UserId, now.AddHours(-2), "SECRET-AUDIT");
        await database.SeedAuditAsync(world.Org, "visit.custom_thing", "visit", job.Visit, null, now.AddHours(-1), "SECRET-AUDIT");

        var response = await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(job.Order), owner);
        var text = await response.Content.ReadAsStringAsync();
        var detail = JsonNode.Parse(text)!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        // AC-10: no signature content, audit payloads, costs or IP addresses.
        foreach (var fragment in SensitiveFragments)
        {
            Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);
        }

        var quotePrefix = await database.ScalarAsync<string>("SELECT quote_prefix FROM organizations WHERE id = @o", ("o", world.Org));
        var quoteNumber = await database.ScalarAsync<long>(
            "SELECT q.quote_number FROM quotes q JOIN quote_versions v ON v.quote_id = q.id WHERE v.id = @v", ("v", job.Version));
        var header = detail["header"]!;
        Assert.Equal(
            ($"WO-{job.Number}", "Rich job", $"{quotePrefix}-{quoteNumber}", "Carla Customer", "Tina One", Timezone),
            (header["number"]!.GetValue<string>(),
                header["title"]!.GetValue<string>(),
                header["quoteNumber"]!.GetValue<string>(),
                header["customerName"]!.GetValue<string>(),
                header["completedByName"]!.GetValue<string>(),
                header["timezone"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(header["propertyAddress"]!.GetValue<string>()));
        Assert.True(Math.Abs((header["completedAt"]!.GetValue<DateTimeOffset>() - completedAt).TotalSeconds) < 1);

        // AC-06, AC-07: rows in order, an unselected optional line is absent, additional materials close the table.
        Assert.Equal(
            ["Labor", "Inspection", "Pipe", "Valve", "Gutter guard", "Clamp", "Sealant"],
            Names(detail));
        var lines = detail["lines"]!.AsArray();
        Assert.Equal(("over", true), (detail["laborVariance"]!.GetValue<string>(), detail["materialVariance"]!.GetValue<bool>()));
        Assert.Equal(
            ("quote", 1, "2h", "2h 50m", "2h", "non_taxable", 200.00m, "over"),
            Row(lines[0]!));
        Assert.Equal(("quote", 2, "1 ea", "1 ea", "1 ea", "taxable", 50.00m, "matches"), Row(lines[1]!));
        Assert.Equal(("quote", 3, "3 ea", "5 ea", "3 ea", "taxable", 30.00m, "over"), Row(lines[2]!));
        Assert.Equal("not_tracked", lines[3]!["status"]!.GetValue<string>());
        Assert.Null(lines[3]!["actual"]);
        Assert.Equal(("not_in_quote", null, "2 ea", "0", null, null, "not_in_quote"), NotInQuote(lines[5]!));
        Assert.Equal(("not_in_quote", null, "1 ea", "0", null, null, "not_in_quote"), NotInQuote(lines[6]!));
        Assert.DoesNotContain("labor_total", lines.Select(line => line!["kind"]!.GetValue<string>()));

        // AC-08: backend totals, discount, one tax rate label and a zero variance whatever the work recorded.
        var totals = detail["totals"]!;
        Assert.Equal(
            (345.00m, 345.00m, 10.00m, "Tax (8.25%)", job.Tax, job.Total, 0.00m, "USD"),
            (totals["approvedSubtotal"]!.GetValue<decimal>(),
                totals["invoiceSubtotal"]!.GetValue<decimal>(),
                totals["discountTotal"]!.GetValue<decimal>(),
                totals["taxLabel"]!.GetValue<string>(),
                totals["taxTotal"]!.GetValue<decimal>(),
                totals["invoiceTotal"]!.GetValue<decimal>(),
                totals["variance"]!.GetValue<decimal>(),
                totals["currency"]!.GetValue<string>()));

        // AC-09: verification items.
        Assert.Equal(
            [
                ("checklist", true, true, "2/3 tasks complete"),
                ("photos", true, true, "Before and after photos"),
                ("acknowledgment", true, true, "Customer signature: Carla Customer"),
                ("materials", true, false, "Materials recorded"),
                ("labor", true, false, "Labor time recorded: 2h 50m"),
                ("summary", true, false, "Completion summary recorded"),
            ],
            detail["verification"]!.AsArray().Select(item => (
                item!["key"]!.GetValue<string>(), item["met"]!.GetValue<bool>(), item["mandatory"]!.GetValue<bool>(), item["label"]!.GetValue<string>())));
        Assert.False(detail["ready"]!.GetValue<bool>());

        // AC-10: evidence summary, work evidence and an audit trail of at most 50 rows, newest first, with mapped labels.
        var evidence = detail["evidence"]!;
        Assert.Equal(4, evidence["count"]!.GetValue<int>());
        Assert.Equal(["before", "after"], evidence["thumbnails"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));
        Assert.Equal([job.BeforePhoto, job.AfterPhoto], evidence["thumbnails"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));

        var visit = detail["workEvidence"]!.AsArray().Single()!;
        Assert.Equal((1, job.Visit), (visit["visitNumber"]!.GetValue<int>(), visit["visitId"]!.GetValue<Guid>()));
        Assert.Equal(3, visit["checklist"]!.AsArray().Count);
        Assert.Equal(
            ["before", "during", "after", "incident"],
            visit["evidence"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));
        Assert.Equal(
            [("Clamp", "2", "added"), ("Pipe", "5", "planned"), ("Sealant", "1", "planned")],
            visit["materials"]!.AsArray()
                .Select(item => (item!["description"]!.GetValue<string>(), item["quantity"]!.GetValue<string>(), item["origin"]!.GetValue<string>()))
                .OrderBy(item => item.Item1, StringComparer.Ordinal));
        Assert.Equal("All done", visit["completionSummary"]!.GetValue<string>());
        var acknowledgment = visit["acknowledgment"]!;
        Assert.Equal(("signed", "Carla Customer", "customer", "Looks good", true), (
            acknowledgment["method"]!.GetValue<string>(),
            acknowledgment["signerName"]!.GetValue<string>(),
            acknowledgment["relationship"]!.GetValue<string>(),
            acknowledgment["comment"]!.GetValue<string>(),
            acknowledgment["signatureCaptured"]!.GetValue<bool>()));

        var trail = detail["auditTrail"]!.AsArray();
        Assert.Equal(50, trail.Count);
        Assert.Equal(
            [("Activity recorded", "System"), ("Visit scheduled", "Sam Staff"), ("Work order created", "Sam Staff"), ("Job paused", "System")],
            trail.Take(4).Select(item => (item!["label"]!.GetValue<string>(), item["actorName"]!.GetValue<string>())));

        // AC-12: payment terms from the frozen text, issue and due date in the organization timezone, tax label and currency.
        var defaults = detail["invoiceDefaults"]!;
        Assert.Equal(
            ("INV-1", BillingSeed.Today(Timezone), "net_30", BillingSeed.DaysFromToday(Timezone, 30), "Tax (8.25%)", "USD"),
            (defaults["numberPreview"]!.GetValue<string>(),
                defaults["issueDate"]!.GetValue<string>(),
                defaults["paymentTerms"]!.GetValue<string>(),
                defaults["dueDate"]!.GetValue<string>(),
                defaults["taxLabel"]!.GetValue<string>(),
                defaults["currency"]!.GetValue<string>()));
        Assert.True(detail["canAct"]!.GetValue<bool>());

        // AC-08, AC-09, AC-12: mixed tax rates read "Tax"; unmet mandatory items and the signature setting change the labels.
        await database.ExecuteAsync("UPDATE organizations SET require_customer_signature = true WHERE id = @o", ("o", world.Org));
        var bare = await database.SeedJobAsync(
            world,
            member.UserId,
            new JobSpec
            {
                Title = "Bare job",
                Photos = false,
                Method = "customer_absent",
                WorkSeconds = 0,
                Summary = null,
                Terms = "Net 45, early discount 2%.",
                Checklist = [new ChecklistSpec("Shut off water", true, true), new ChecklistSpec("Clean up", true, false)],
                Lines = [new("Fee A", "service", 1m, "ea", 100m, 5m), new("Fee B", "service", 1m, "ea", 100m, 8.25m)],
            });
        var bareDetail = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(bare.Order), owner));
        Assert.Equal("Tax", bareDetail["totals"]!["taxLabel"]!.GetValue<string>());
        Assert.Equal(
            ("due_upon_receipt", BillingSeed.Today(Timezone)),
            (bareDetail["invoiceDefaults"]!["paymentTerms"]!.GetValue<string>(), bareDetail["invoiceDefaults"]!["dueDate"]!.GetValue<string>()));
        Assert.Equal(
            [
                (false, "1/2 tasks complete"),
                (false, "Before and after photos missing"),
                (false, "Customer signature missing"),
                (false, "No materials recorded"),
                (false, "No labor time recorded"),
                (false, "No completion summary"),
            ],
            bareDetail["verification"]!.AsArray().Select(item => (item!["met"]!.GetValue<bool>(), item["label"]!.GetValue<string>())));
        Assert.Null(bareDetail["header"]!["completedByName"]);

        await database.ExecuteAsync("UPDATE organizations SET require_customer_signature = false WHERE id = @o", ("o", world.Org));
        var relaxed = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(bare.Order), owner));
        Assert.Equal(
            (true, "Customer acknowledgment: Customer not available"),
            (relaxed["verification"]![2]!["met"]!.GetValue<bool>(), relaxed["verification"]![2]!["label"]!.GetValue<string>()));

        // AC-11: the image of a queue member with the BR-13 headers; foreign, excluded and unknown evidence is a 404.
        var image = await host.SendAsync(HttpMethod.Get, $"{BillingSeed.Detail(job.Order)}/evidence/{job.BeforePhoto}", owner);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(BillingSeed.Png, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal("inline", image.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", image.Headers.CacheControl?.ToString());

        var invoiced = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Invoiced" });
        await database.SeedBillingInvoiceAsync(world, member.UserId, invoiced, "draft");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"{BillingSeed.Detail(invoiced.Order)}/evidence/{invoiced.BeforePhoto}", owner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"{BillingSeed.Detail(job.Order)}/evidence/{invoiced.BeforePhoto}", owner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"{BillingSeed.Detail(job.Order)}/evidence/{Guid.NewGuid()}", owner)).StatusCode);
    }

    [Fact]
    public async Task Review_NoteFollowUpReturnAndNoOp_AreStoredAndAuditedWithoutTheNoteText()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var (accounting, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Ada", "Counts");
        var job = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Reviewed job" });

        async Task<long> AuditCount(string action) =>
            await database.CountAsync("audit_logs", "entity_id = @o AND action = @a", ("o", job.Order), ("a", action));

        // Note and follow-up: trimmed, stored, marked by the actor; audit carries the length and the reason only.
        var saved = await BillingSeed.ReadAsync(await BillingSeed.PatchReviewAsync(
            host, owner, job.Order, new JsonObject { ["note"] = "  Check HVAC invoice  ", ["followUp"] = true, ["organizationId"] = Guid.NewGuid() }));
        Assert.Equal("Check HVAC invoice", saved["note"]!.GetValue<string>());
        Assert.Equal("Olive Owner", saved["followUp"]!["byName"]!.GetValue<string>());
        Assert.Equal(
            ("Check HVAC invoice", member.UserId, "completed"),
            (await database.ScalarAsync<string>("SELECT billing_review_note FROM work_orders WHERE id = @o", ("o", job.Order)),
                await database.ScalarAsync<Guid>("SELECT billing_follow_up_by_user_id FROM work_orders WHERE id = @o", ("o", job.Order)),
                await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @o", ("o", job.Order))));
        Assert.Equal(
            ("{\"length\": 18}", "{\"reason\": \"manual\"}"),
            (await database.ScalarAsync<string>("SELECT metadata::text FROM audit_logs WHERE entity_id = @o AND action = 'work_order.billing_note_updated'", ("o", job.Order)),
                await database.ScalarAsync<string>("SELECT metadata::text FROM audit_logs WHERE entity_id = @o AND action = 'work_order.billing_follow_up_marked'", ("o", job.Order))));

        // The same values again change nothing: no write, no audit, no new concurrency value.
        var before = await database.OrderStateAsync(job.Order);
        var repeated = await BillingSeed.ReadAsync(await BillingSeed.PatchReviewAsync(
            host, accounting, job.Order, new JsonObject { ["note"] = "Check HVAC invoice", ["followUp"] = true }));
        Assert.Equal(saved["followUp"]!["at"]!.GetValue<DateTimeOffset>(), repeated["followUp"]!["at"]!.GetValue<DateTimeOffset>());
        Assert.Equal(before, await database.OrderStateAsync(job.Order));

        // Return to queue: the current note with the follow-up cleared; status and invoices are unchanged.
        var returned = await BillingSeed.ReadAsync(await BillingSeed.PatchReviewAsync(
            host, accounting, job.Order, new JsonObject { ["note"] = "Check HVAC invoice", ["followUp"] = false, ["reason"] = "returned_to_queue" }));
        Assert.Null(returned["followUp"]);
        Assert.Equal("{\"reason\": \"returned_to_queue\"}", await database.ScalarAsync<string>(
            "SELECT metadata::text FROM audit_logs WHERE entity_id = @o AND action = 'work_order.billing_follow_up_cleared'", ("o", job.Order)));
        Assert.Equal(
            ("completed", 0, 1, 1),
            (await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @o", ("o", job.Order)),
                (int)await database.InvoiceCountAsync(world.Org),
                (int)await AuditCount("work_order.billing_note_updated"),
                (int)await AuditCount("work_order.billing_follow_up_cleared")));
        Assert.Contains(job.Order, (await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(), owner)))["items"]!
            .AsArray().Select(item => item!["workOrderId"]!.GetValue<Guid>()));

        // An absent note leaves it alone, an explicit empty one clears it; the client may not send the system reason.
        var kept = await BillingSeed.ReadAsync(await BillingSeed.PatchReviewAsync(host, owner, job.Order, new JsonObject { ["followUp"] = true, ["reason"] = "manual" }));
        Assert.Equal("Check HVAC invoice", kept["note"]!.GetValue<string>());
        var cleared = await BillingSeed.ReadAsync(await BillingSeed.PatchReviewAsync(host, owner, job.Order, new JsonObject { ["note"] = "   " }));
        Assert.Null(cleared["note"]);
        Assert.NotNull(cleared["followUp"]);

        var unchanged = await database.OrderStateAsync(job.Order);
        await BillingSeed.ProblemAsync(
            await BillingSeed.PatchReviewAsync(host, owner, job.Order, new JsonObject { ["note"] = new string('n', 501) }), HttpStatusCode.BadRequest, errorKey: "note");
        await BillingSeed.ProblemAsync(await BillingSeed.PatchReviewAsync(host, owner, job.Order, new JsonObject()), HttpStatusCode.BadRequest, errorKey: "body");
        await BillingSeed.ProblemAsync(
            await BillingSeed.PatchReviewAsync(host, owner, job.Order, new JsonObject { ["followUp"] = false, ["reason"] = "requirements_unmet" }),
            HttpStatusCode.BadRequest,
            errorKey: "reason");
        Assert.Equal(unchanged, await database.OrderStateAsync(job.Order));

        // The note text never reaches audit rows or logs.
        Assert.DoesNotContain("HVAC", await database.AuditTextAsync(world.Org), StringComparison.Ordinal);
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Message.Contains("HVAC", StringComparison.Ordinal));
    }

    private static (string Kind, int? Index, string? Approved, string? Actual, string? Billable, string? Tax, decimal? Amount, string Status) Row(JsonNode line) =>
        (line["kind"]!.GetValue<string>(),
            line["index"]!.GetValue<int>(),
            line["approved"]?.GetValue<string>(),
            line["actual"]?.GetValue<string>(),
            line["billable"]?.GetValue<string>(),
            line["tax"]?.GetValue<string>(),
            line["amount"]?.GetValue<decimal>(),
            line["status"]!.GetValue<string>());

    private static (string Kind, int? Index, string? Actual, string? Billable, string? Tax, decimal? Amount, string Status) NotInQuote(JsonNode line) =>
        (line["kind"]!.GetValue<string>(),
            line["index"]?.GetValue<int>(),
            line["actual"]?.GetValue<string>(),
            line["billable"]?.GetValue<string>(),
            line["tax"]?.GetValue<string>(),
            line["amount"]?.GetValue<decimal>(),
            line["status"]!.GetValue<string>());
}
