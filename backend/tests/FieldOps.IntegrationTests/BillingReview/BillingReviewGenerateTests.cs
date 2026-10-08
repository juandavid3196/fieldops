using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.BillingReview;

/// <summary>Invoice generation: effects, guards, idempotency and concurrency (completed-jobs-review AC-14, AC-15, AC-16).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BillingReviewGenerateTests(CompanySettingsDatabaseFixture database)
{
    private const string Timezone = "America/Chicago";

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Generate_ReadyJob_CreatesTheDraftInvoiceAndEverySideEffectInOneTransaction()
    {
        var world = await database.SeedWorldAsync(Timezone);
        await using var host = RequestsHost.Create(database);
        var (accounting, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Ada", "Counts");
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "One");
        var job = await database.SeedJobAsync(
            world,
            member.UserId,
            new JobSpec
            {
                Title = "Billable job",
                Technician = tech,
                Discount = 10m,
                AddedMaterials = [("Clamp", 2m)],
                Lines =
                [
                    new("Labor", "service", 2m, "h", 100m),
                    new("Pipe fitting", "product", 2m, "ea", 10m, 8.25m, Planned: 2m, Used: 2m),
                    new("Gutter guard", "service", 1m, "ea", 40m, 8.25m, Optional: true, Selected: true),
                    new("Extra option", "service", 1m, "ea", 30m, 8.25m, Optional: true),
                ],
            });

        // A second completed visit with its own closing data, and a cancelled one that stays as it is.
        var second = Guid.NewGuid();
        await database.ExecuteAsync(
            """
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, actual_completed_at) VALUES (@v, @org, @o, 2, 'completed', now());
            INSERT INTO visits (organization_id, work_order_id, visit_number, status) VALUES (@org, @o, 3, 'cancelled');
            INSERT INTO customer_signoffs (visit_id, signer_name, accepted, acknowledgement_method, signer_relationship, signature_content, signature_mime_type, review_confirmed, recorded_by_user_id)
            VALUES (@v, 'Carla Customer', true, 'signed', 'customer', @png, 'image/png', true, @u);
            """,
            ("v", second),
            ("org", world.Org),
            ("o", job.Order),
            ("png", BillingSeed.Png),
            ("u", member.UserId));
        await database.SeedEvidenceAsync(member.UserId, second, Guid.NewGuid(), "before", null);
        await database.SeedEvidenceAsync(member.UserId, second, Guid.NewGuid(), "after", null);
        await database.ExecuteAsync(
            "UPDATE work_orders SET billing_review_note = 'old', billing_follow_up_at = now(), billing_follow_up_by_user_id = @u WHERE id = @o",
            ("u", member.UserId),
            ("o", job.Order));

        var issue = BillingSeed.Today(Timezone);
        var counter = await database.NextInvoiceNumberAsync(world.Org);
        var response = await BillingSeed.PostInvoiceAsync(
            host, accounting, job.Order, BillingSeed.InvoiceBody(issue, "net_15", "  Billing verified  ", acknowledge: true));
        var created = await BillingSeed.ReadAsync(response, HttpStatusCode.Created);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(created["changed"]!.GetValue<bool>());
        var invoice = created["invoice"]!;
        var invoiceId = invoice["id"]!.GetValue<Guid>();
        Assert.Equal(
            ($"INV-{counter}", "draft", job.Total, "USD"),
            (invoice["number"]!.GetValue<string>(), invoice["status"]!.GetValue<string>(), invoice["total"]!.GetValue<decimal>(), invoice["currency"]!.GetValue<string>()));

        // The invoice copies the approved quote: amounts, discount, terms, dates, currency.
        Assert.Equal(1, await database.CountAsync("invoices", "work_order_id = @o", ("o", job.Order)));
        Assert.Equal(
            string.Join(
                '|',
                "draft",
                issue,
                BillingSeed.DaysFromToday(Timezone, 15),
                "net_15",
                "USD",
                Money(job.Subtotal),
                Money(job.Discount),
                Money(job.Tax),
                Money(job.Total),
                "0.00",
                Money(job.Total),
                "-",
                "-",
                counter.ToString(CultureInfo.InvariantCulture),
                "true",
                "true",
                "true"),
            await database.ScalarAsync<string>(
                """
                SELECT i.status::text || '|' || i.issue_date::text || '|' || i.due_date::text || '|' || i.payment_terms || '|' || i.currency
                    || '|' || i.subtotal::text || '|' || i.discount_total::text || '|' || i.tax_total::text || '|' || i.total::text
                    || '|' || i.amount_paid::text || '|' || i.balance_due::text || '|' || COALESCE(i.notes, '-') || '|' || COALESCE(i.sent_at::text, '-')
                    || '|' || i.invoice_number::text || '|' || (i.branch_id = w.branch_id)::text || '|' || (i.customer_id = w.customer_id)::text
                    || '|' || (i.created_by_user_id = @u)::text
                FROM invoices i JOIN work_orders w ON w.id = i.work_order_id WHERE i.id = @i
                """,
                ("u", member.UserId),
                ("i", invoiceId)));

        // One line per billed quote line, never for "Not in quote" or labor rows.
        Assert.Equal(
            ["Labor", "Pipe fitting", "Gutter guard"],
            await database.TextsAsync("SELECT description FROM invoice_lines WHERE invoice_id = @i ORDER BY sort_order", ("i", invoiceId)));
        Assert.Equal(
            (job.BilledLines, job.Subtotal, job.Tax, job.Total, 3),
            ((int)await database.CountAsync("invoice_lines", "invoice_id = @i", ("i", invoiceId)),
                await database.ScalarAsync<decimal>("SELECT SUM(line_subtotal) FROM invoice_lines WHERE invoice_id = @i", ("i", invoiceId)),
                await database.ScalarAsync<decimal>("SELECT SUM(line_tax) FROM invoice_lines WHERE invoice_id = @i", ("i", invoiceId)),
                await database.ScalarAsync<decimal>("SELECT SUM(line_total) FROM invoice_lines WHERE invoice_id = @i", ("i", invoiceId)),
                (int)await database.CountAsync("invoice_lines", "invoice_id = @i AND source_quote_line_id = ANY(@l)", ("i", invoiceId), ("l", job.Lines.ToArray()))));

        // Counter, work order, visits, history and audit.
        Assert.Equal(counter + 1, await database.NextInvoiceNumberAsync(world.Org));
        Assert.Equal(
            "approved_for_billing|Billing verified|-",
            await database.ScalarAsync<string>(
                "SELECT status::text || '|' || COALESCE(billing_review_note, '-') || '|' || COALESCE(billing_follow_up_at::text, '-') FROM work_orders WHERE id = @o", ("o", job.Order)));
        Assert.Equal(
            "approved,approved,cancelled",
            string.Join(',', await database.TextsAsync("SELECT status::text FROM visits WHERE work_order_id = @o ORDER BY status::text", ("o", job.Order))));
        Assert.Equal(
            2,
            await database.CountAsync("visits", "work_order_id = @o AND status = 'approved' AND reviewed_by_user_id = @u AND reviewed_at IS NOT NULL", ("o", job.Order), ("u", member.UserId)));
        Assert.Equal(
            2,
            await database.CountAsync(
                "visit_status_history",
                "from_status = 'completed' AND to_status = 'approved' AND changed_by_user_id = @u AND visit_id IN (SELECT id FROM visits WHERE work_order_id = @o)",
                ("o", job.Order),
                ("u", member.UserId)));

        var created2 = await database.ScalarAsync<string>(
            "SELECT after_data::text || '|' || metadata::text FROM audit_logs WHERE entity_id = @i AND action = 'invoice.created' AND entity_type = 'invoice' AND actor_user_id = @u",
            ("i", invoiceId),
            ("u", member.UserId));
        Assert.Contains("\"status\": \"draft\"", created2, StringComparison.Ordinal);
        Assert.Contains($"\"invoiceNumber\": {counter}", created2, StringComparison.Ordinal);
        Assert.Contains("\"currency\": \"USD\"", created2, StringComparison.Ordinal);
        Assert.Contains($"\"workOrderId\": \"{job.Order}\"", created2, StringComparison.Ordinal);
        Assert.Contains("\"materialVariance\": true", created2, StringComparison.Ordinal);
        Assert.Contains("\"laborVariance\": false", created2, StringComparison.Ordinal);
        Assert.Equal(
            $"{{\"status\": \"completed\"}}|{{\"status\": \"approved_for_billing\"}}|{{\"invoiceId\": \"{invoiceId}\"}}",
            await database.ScalarAsync<string>(
                "SELECT before_data::text || '|' || after_data::text || '|' || metadata::text FROM audit_logs WHERE entity_id = @o AND action = 'work_order.status_changed'",
                ("o", job.Order)));

        // No payment, notification or email side effect; the job left the queue.
        Assert.Equal(
            (0, 0, 0),
            ((int)await database.CountAsync("payments", "organization_id = @o", ("o", world.Org)),
                (int)await database.CountAsync("notifications", "organization_id = @o", ("o", world.Org)),
                (int)await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", invoiceId))));
        Assert.Empty(host.Sender.Messages);
        Assert.Empty((await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(), accounting)))["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(job.Order), accounting)).StatusCode);
    }

    [Fact]
    public async Task Generate_Guards_ApplyInOrderAndWriteOnlyWhenRequirementsAreUnmet()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var today = BillingSeed.Today("UTC");

        async Task<BillingJob> Seed(JobSpec? spec = null) => await database.SeedJobAsync(world, member.UserId, spec);

        // BR-17: invalid bodies are field errors and write nothing.
        var ready = await Seed(new JobSpec { Title = "Ready job" });
        var untouched = await database.OrderStateAsync(ready.Order);

        foreach (var (body, key) in new[]
        {
            (BillingSeed.InvoiceBody(BillingSeed.DaysFromToday("UTC", -31)), "issueDate"),
            (BillingSeed.InvoiceBody(BillingSeed.DaysFromToday("UTC", 1)), "issueDate"),
            (BillingSeed.InvoiceBody("2026-13-40"), "issueDate"),
            (BillingSeed.InvoiceBody(today, "net_60"), "paymentTerms"),
            (BillingSeed.InvoiceBody(today, note: new string('n', 501)), "note"),
            (BillingSeed.InvoiceBody(today, acknowledge: null), "acknowledgeVariances"),
        })
        {
            await BillingSeed.ProblemAsync(await BillingSeed.PostInvoiceAsync(host, owner, ready.Order, body), HttpStatusCode.BadRequest, errorKey: key);
        }

        Assert.Equal(untouched, await database.OrderStateAsync(ready.Order));

        // Guard 2: a work order that is not completed.
        var running = await Seed(new JobSpec { Title = "Running job", Status = "in_progress", Photos = false });
        var runningState = await database.OrderStateAsync(running.Order);
        await BillingSeed.ProblemAsync(
            await BillingSeed.PostInvoiceAsync(host, owner, running.Order, BillingSeed.InvoiceBody(today, acknowledge: true)),
            HttpStatusCode.Conflict,
            "work_order_status_invalid");
        Assert.Equal(runningState, await database.OrderStateAsync(running.Order));

        // Guard 3 wins over guard 4 and over any confirmation: the note is saved, the follow-up marked, no invoice.
        var unmet = await Seed(new JobSpec { Title = "Unmet job", Photos = false, WorkSeconds = 4 * 3600 });
        var counter = await database.NextInvoiceNumberAsync(world.Org);
        var firstTry = await BillingSeed.ProblemAsync(
            await BillingSeed.PostInvoiceAsync(host, owner, unmet.Order, BillingSeed.InvoiceBody(today, note: "Needs photos", acknowledge: false)),
            HttpStatusCode.Conflict,
            "completion_requirements_unmet");
        Assert.Equal(
            "Complete the required closing items before generating an invoice. This job was marked for follow-up.",
            firstTry["title"]!.GetValue<string>());
        Assert.Equal(
            ("completed", "Needs photos", member.UserId, 0, counter),
            (await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @o", ("o", unmet.Order)),
                await database.ScalarAsync<string>("SELECT billing_review_note FROM work_orders WHERE id = @o", ("o", unmet.Order)),
                await database.ScalarAsync<Guid>("SELECT billing_follow_up_by_user_id FROM work_orders WHERE id = @o", ("o", unmet.Order)),
                (int)await database.CountAsync("invoices", "work_order_id = @o", ("o", unmet.Order)),
                await database.NextInvoiceNumberAsync(world.Org)));
        Assert.Equal(
            "{\"reason\": \"requirements_unmet\"}",
            await database.ScalarAsync<string>(
                "SELECT metadata::text FROM audit_logs WHERE entity_id = @o AND action = 'work_order.billing_follow_up_marked'", ("o", unmet.Order)));

        await BillingSeed.ProblemAsync(
            await BillingSeed.PostInvoiceAsync(host, owner, unmet.Order, BillingSeed.InvoiceBody(today, note: "Still pending", acknowledge: true)),
            HttpStatusCode.Conflict,
            "completion_requirements_unmet");
        Assert.Equal(
            ("Still pending", 1, 2),
            (await database.ScalarAsync<string>("SELECT billing_review_note FROM work_orders WHERE id = @o", ("o", unmet.Order)),
                (int)await database.CountAsync("audit_logs", "entity_id = @o AND action = 'work_order.billing_follow_up_marked'", ("o", unmet.Order)),
                (int)await database.CountAsync("audit_logs", "entity_id = @o AND action = 'work_order.billing_note_updated'", ("o", unmet.Order))));
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Message.Contains("Needs photos", StringComparison.Ordinal));

        // Guard 4: variances need the confirmation; nothing is written without it.
        var variance = await Seed(new JobSpec { Title = "Variance job", WorkSeconds = 4 * 3600 });
        var varianceState = await database.OrderStateAsync(variance.Order);

        await BillingSeed.ProblemAsync(
            await BillingSeed.PostInvoiceAsync(host, owner, variance.Order, BillingSeed.InvoiceBody(today, note: "x", acknowledge: false)),
            HttpStatusCode.Conflict,
            "variance_confirmation_required");

        Assert.Equal(varianceState, await database.OrderStateAsync(variance.Order));

        // Guard 5: lines that do not add up to the approved totals.
        var mismatch = await Seed(new JobSpec { Title = "Mismatch job" });
        await database.ExecuteAsync(
            "UPDATE quote_lines SET line_subtotal = line_subtotal + 1 WHERE quote_version_id = @v AND name = 'Labor'", ("v", mismatch.Version));
        var mismatchState = await database.OrderStateAsync(mismatch.Order);
        await BillingSeed.ProblemAsync(
            await BillingSeed.PostInvoiceAsync(host, owner, mismatch.Order, BillingSeed.InvoiceBody(today, acknowledge: true)),
            HttpStatusCode.Conflict,
            "invoice_totals_mismatch");
        Assert.Equal(mismatchState, await database.OrderStateAsync(mismatch.Order));

        // With the confirmation the variance job is invoiced, on the oldest allowed issue date.
        var oldest = BillingSeed.DaysFromToday("UTC", -30);
        var confirmed = await BillingSeed.ReadAsync(
            await BillingSeed.PostInvoiceAsync(host, owner, variance.Order, BillingSeed.InvoiceBody(oldest, "net_30", "x", acknowledge: true)),
            HttpStatusCode.Created);
        Assert.True(confirmed["changed"]!.GetValue<bool>());
        Assert.Equal(
            BillingSeed.DaysFromToday("UTC", 0),
            await database.ScalarAsync<string>("SELECT due_date::text FROM invoices WHERE work_order_id = @o", ("o", variance.Order)));
        Assert.Equal(counter + 1, await database.NextInvoiceNumberAsync(world.Org));
    }

    [Fact]
    public async Task Generate_RepeatedAndConcurrentRequests_CreateExactlyOneInvoiceAndOneNumber()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (accounting, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var today = BillingSeed.Today("UTC");
        var counter = await database.NextInvoiceNumberAsync(world.Org);

        // Sequential: the second call returns the same invoice, changes nothing and later edits are refused.
        var sequential = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Sequential job" });
        var first = await BillingSeed.ReadAsync(await BillingSeed.PostInvoiceAsync(host, owner, sequential.Order, BillingSeed.InvoiceBody(today, note: "once")), HttpStatusCode.Created);
        var stateAfterFirst = await database.OrderStateAsync(sequential.Order);
        var second = await BillingSeed.ReadAsync(
            await BillingSeed.PostInvoiceAsync(host, accounting, sequential.Order, BillingSeed.InvoiceBody(BillingSeed.DaysFromToday("UTC", -3), "net_30", "twice")));
        Assert.False(second["changed"]!.GetValue<bool>());
        Assert.Equal(first["invoice"]!.ToJsonString(), second["invoice"]!.ToJsonString());
        Assert.Equal(stateAfterFirst, await database.OrderStateAsync(sequential.Order));
        Assert.Equal(counter + 1, await database.NextInvoiceNumberAsync(world.Org));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await BillingSeed.PatchReviewAsync(host, owner, sequential.Order, new JsonObject { ["note"] = "late", ["followUp"] = true })).StatusCode);
        Assert.Equal("once", await database.ScalarAsync<string>("SELECT billing_review_note FROM work_orders WHERE id = @o", ("o", sequential.Order)));

        // An existing invoice of any non-void status is returned as it is, whatever the work order status.
        var sent = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Sent job" });
        await database.SeedBillingInvoiceAsync(world, member.UserId, sent, "sent");
        var existing = await BillingSeed.ReadAsync(await BillingSeed.PostInvoiceAsync(host, owner, sent.Order, BillingSeed.InvoiceBody(today)));
        Assert.Equal(("sent", false), (existing["invoice"]!["status"]!.GetValue<string>(), existing["changed"]!.GetValue<bool>()));
        Assert.Equal(1, await database.CountAsync("invoices", "work_order_id = @o", ("o", sent.Order)));

        // A work order whose only invoice is void is invoiced again: one void and one active invoice.
        var voided = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Void job" });
        await database.SeedBillingInvoiceAsync(world, member.UserId, voided, "void");
        var regenerated = await BillingSeed.ReadAsync(await BillingSeed.PostInvoiceAsync(host, owner, voided.Order, BillingSeed.InvoiceBody(today)), HttpStatusCode.Created);
        Assert.True(regenerated["changed"]!.GetValue<bool>());
        Assert.Equal(
            (2, 1),
            ((int)await database.CountAsync("invoices", "work_order_id = @o", ("o", voided.Order)),
                (int)await database.CountAsync("invoices", "work_order_id = @o AND status <> 'void'", ("o", voided.Order))));

        // Concurrent: six identical requests produce one 201, five 200 and a single set of effects.
        var concurrent = await database.SeedJobAsync(world, member.UserId, new JobSpec { Title = "Concurrent job" });
        var before = await database.NextInvoiceNumberAsync(world.Org);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async index =>
        {
            var response = await BillingSeed.PostInvoiceAsync(
                host, index % 2 == 0 ? owner : accounting, concurrent.Order, BillingSeed.InvoiceBody(today, note: $"call {index}"));
            var body = await response.Content.ReadAsStringAsync();

            return (StatusCode: response.StatusCode, Body: JsonNode.Parse(body)!);
        }));

        Assert.Equal(1, results.Count(result => result.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, results.Count(result => result.StatusCode == HttpStatusCode.OK));
        Assert.Single(results.Select(result => result.Body["invoice"]!["id"]!.GetValue<Guid>()).Distinct());
        Assert.Equal(1, results.Count(result => result.Body["changed"]!.GetValue<bool>()));
        Assert.Equal(
            (1, 1, 1, before + 1),
            ((int)await database.CountAsync("invoices", "work_order_id = @o", ("o", concurrent.Order)),
                (int)await database.CountAsync("visit_status_history", "to_status = 'approved' AND visit_id = @v", ("v", concurrent.Visit)),
                (int)await database.CountAsync("audit_logs", "organization_id = @o AND action = 'invoice.created' AND metadata::text LIKE '%' || CAST(@w AS text) || '%'", ("o", world.Org), ("w", concurrent.Order)),
                await database.NextInvoiceNumberAsync(world.Org)));
    }
}
