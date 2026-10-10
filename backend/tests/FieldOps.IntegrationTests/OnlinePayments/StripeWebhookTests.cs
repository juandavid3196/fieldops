using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>The Stripe webhook: signature, size, idempotency, success effects, failures, needs-attention and rollback (customer-invoice-payments AC-04 to AC-07, AC-10, AC-11).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class StripeWebhookTests(CompanySettingsDatabaseFixture database)
{
    private static string NewEvent() => $"evt_{Guid.NewGuid():N}";

    private static async Task<HttpStatusCode> DeliverAsync(OnlineHost host, string payload) =>
        (await OnlineApi.DeliverAsync(host.Client, payload)).StatusCode;

    [Fact]
    public async Task Webhook_RejectsBadSignaturesAndOversizedBodiesAndIgnoresUnhandledAndUnknownEvents()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var (_, intent) = await OnlineApi.StartCardAsync(host.Client, invoice);
        var eventId = NewEvent();
        var payload = OnlineApi.Succeeded(eventId, intent, 357.28m);
        var before = await database.FlowStateAsync(world.Org, invoice.Id);

        // AC-04: a missing, malformed, foreign, tampered or stale signature is a 400 and stores nothing.
        foreach (var signature in new string?[]
        {
            null,
            "t=1,v1=bad",
            OnlineApi.Sign(payload, "whsec_other"),
            OnlineApi.Sign(payload + " "),
            OnlineApi.Sign(payload, at: DateTimeOffset.UtcNow.AddMinutes(-10)),
        })
        {
            var response = await OnlineApi.WebhookAsync(host.Client, payload, signature);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }

        Assert.Equal(0, await database.EventCountAsync(eventId));
        Assert.Equal(before, await database.FlowStateAsync(world.Org, invoice.Id));

        // A body over 64 KB is a 413 whatever the signature says.
        var big = $$"""{"pad":"{{new string('x', 70 * 1024)}}"}""";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await OnlineApi.DeliverAsync(host.Client, big)).StatusCode);
        Assert.Equal(before, await database.FlowStateAsync(world.Org, invoice.Id));

        // A verified event of another type is a 200 that stores nothing; a handled event of an unknown intent is stored as ignored.
        var unhandled = NewEvent();
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Unhandled(unhandled)));
        Assert.Equal(0, await database.EventCountAsync(unhandled));

        var unknown = NewEvent();
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Succeeded(unknown, "pi_unknown", 10m)));
        Assert.Equal("ignored", await database.EventOutcomeAsync(unknown));
        Assert.Equal(0, await database.CountAsync("payments", "organization_id = @o", ("o", world.Org)));

        // The webhook has no rate limit: signature, size and deduplication apply instead.
        for (var i = 0; i < 80; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await OnlineApi.WebhookAsync(host.Client, payload, null)).StatusCode);
        }
    }

    [Fact]
    public async Task Webhook_SucceededRecordsThePaymentReceiptAllocationBalanceAuditAndEmailsAfterTheCommit()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var receiver = await database.SeedReceiverAsync(world.Org);
        await database.ExecuteAsync("UPDATE organizations SET next_payment_number = 41 WHERE id = @o", ("o", world.Org));
        var organizationName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));

        // A sent invoice paid in full by card, and a partially paid one (external payment first) completed by card.
        var sent = await database.InvoiceAsync(world, member.UserId);
        var key = Guid.NewGuid();
        var created = await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, sent.Token, key), HttpStatusCode.Created);
        var attempt = created["attemptId"]!.GetValue<Guid>();
        var intent = FakePaymentGateway.IntentId(attempt);
        var eventId = NewEvent();

        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Succeeded(eventId, intent, 357.28m, OnlineApi.Metadata(attempt, world.Org, sent.Id))));

        Assert.Equal(
            $"41|card_online|succeeded|357.28|0.00|RCT-{sent.Number}-01|visa|4242|-|-|{key}|true",
            await database.ScalarAsync<string>(
                """
                SELECT payment_number || '|' || method::text || '|' || status::text || '|' || amount || '|' || refunded_amount || '|' || receipt_number
                    || '|' || card_brand || '|' || card_last4 || '|' || COALESCE(recorded_by_user_id::text, '-') || '|' || COALESCE(received_by_user_id::text, '-')
                    || '|' || idempotency_key || '|' || (receipt_sent_at IS NOT NULL)
                FROM payments WHERE organization_id = @o AND method::text = 'card_online'
                """,
                ("o", world.Org)));
        var paymentId = await database.ScalarAsync<Guid>("SELECT id FROM payments WHERE organization_id = @o AND method::text = 'card_online'", ("o", world.Org));
        Assert.Equal(1, await database.CountAsync("payment_allocations", "payment_id = @p AND invoice_id = @i AND amount = 357.28", ("p", paymentId), ("i", sent.Id)));
        Assert.Equal("paid|357.28|0.00", await database.InvoiceMoneyAsync(sent.Id));
        Assert.Equal("succeeded|-|0.00|true", await database.AttemptAsync(attempt));
        Assert.Equal(42, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal("applied", await database.EventOutcomeAsync(eventId));
        Assert.Equal(
            $"{world.Org}|{attempt}",
            await database.ScalarAsync<string>("SELECT organization_id || '|' || attempt_id FROM payment_webhook_events WHERE provider_event_id = @e", ("e", eventId)));

        // BR-26: ids, numbers, codes and amounts only, no actor, and the invoice branch.
        Assert.Equal(
            [$"357.28|USD|card_online|41|RCT-{sent.Number}-01|{sent.Id}|online|{world.BranchA}|-"],
            await database.TextsAsync(
                """
                SELECT (after_data ->> 'amount') || '|' || (after_data ->> 'currency') || '|' || (after_data ->> 'method') || '|' || (metadata ->> 'paymentNumber')
                    || '|' || (metadata ->> 'receiptNumber') || '|' || (metadata ->> 'invoiceId') || '|' || (metadata ->> 'source') || '|' || branch_id
                    || '|' || COALESCE(actor_user_id::text, '-')
                FROM audit_logs WHERE organization_id = @o AND action = 'payment.recorded'
                """,
                ("o", world.Org)));
        Assert.Equal(
            [$"sent|0.00|357.28|paid|357.28|0.00|{paymentId}|-"],
            await database.TextsAsync(
                """
                SELECT (before_data ->> 'status') || '|' || (before_data ->> 'amountPaid') || '|' || (before_data ->> 'balanceDue') || '|' || (after_data ->> 'status')
                    || '|' || (after_data ->> 'amountPaid') || '|' || (after_data ->> 'balanceDue') || '|' || (metadata ->> 'paymentId') || '|' || COALESCE(actor_user_id::text, '-')
                FROM audit_logs WHERE organization_id = @o AND action = 'invoice.payment_applied' AND entity_id = @i
                """,
                ("o", world.Org),
                ("i", sent.Id)));

        // BR-12: the receipt goes to the invoice recipient after the commit, with no link and no attachment.
        var email = Assert.Single(host.Sender.Messages);
        var paidOn = DateTimeOffset.UtcNow.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        Assert.Equal(("carla@example.com", $"{organizationName}: payment receipt RCT-{sent.Number}-01"), (email.To, email.Subject));
        Assert.Contains($"Receipt RCT-{sent.Number}-01 · Payment PAY-41", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("357.28 USD ·", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("· Visa ending in 4242", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Invoice {sent.Display} · Remaining balance 0.00 USD", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("http", email.TextBody + email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.True(paidOn.Length > 0);

        // The page polls the status: succeeded with the payment of the BR-05 shape and the invoice figures.
        var status = await BillingSeed.ReadAsync(await OnlineApi.StatusAsync(host.Client, sent.Token, attempt));
        Assert.Equal(("succeeded", (string?)null), (status["status"]!.GetValue<string>(), status["failureCategory"]?.GetValue<string>()));
        var item = status["payment"]!;
        Assert.Equal(
            (paymentId, "PAY-41", $"RCT-{sent.Number}-01", "card_online", "Visa ending in 4242", 357.28m, 0m, "succeeded"),
            (item["paymentId"]!.GetValue<Guid>(), item["number"]!.GetValue<string>(), item["receiptNumber"]!.GetValue<string>(), item["method"]!.GetValue<string>(),
                item["methodLabel"]!.GetValue<string>(), item["amount"]!.GetValue<decimal>(), item["refundedAmount"]!.GetValue<decimal>(), item["status"]!.GetValue<string>()));
        Assert.Equal(("paid", 357.28m, 0m), (status["invoice"]!["status"]!.GetValue<string>(), status["invoice"]!["amountPaid"]!.GetValue<decimal>(), status["invoice"]!["balanceDue"]!.GetValue<decimal>()));

        // A second receipt on the same invoice gets the next sequence: an external payment first (BR-31 e), then the card.
        var partial = await database.InvoiceAsync(world, member.UserId);
        var recorded = await BillingSeed.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                PaymentApi.Path(partial.Id),
                owner,
                PaymentApi.Body(amount: 100m, paidDate: BillingSeed.Today(OnlineSeed.Timezone), receivedBy: receiver.UserId, updatedAt: await database.StampAsync(partial.Id))),
            HttpStatusCode.Created);
        Assert.Equal("partially_paid", recorded["invoice"]!["status"]!.GetValue<string>());
        Assert.Equal(
            $"RCT-{partial.Number}-01",
            await database.ScalarAsync<string>("SELECT receipt_number FROM payments WHERE id = @p", ("p", recorded["payment"]!["id"]!.GetValue<Guid>())));

        var second = await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, partial.Token), HttpStatusCode.Created);
        Assert.Equal(257.28m, second["amount"]!.GetValue<decimal>());
        var secondAttempt = second["attemptId"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Succeeded(NewEvent(), FakePaymentGateway.IntentId(secondAttempt), 257.28m)));
        Assert.Equal("paid|357.28|0.00", await database.InvoiceMoneyAsync(partial.Id));
        Assert.Equal(
            [$"RCT-{partial.Number}-01", $"RCT-{partial.Number}-02"],
            await database.TextsAsync(
                "SELECT p.receipt_number FROM payments p JOIN payment_allocations a ON a.payment_id = p.id WHERE a.invoice_id = @i ORDER BY p.payment_number",
                ("i", partial.Id)));
        Assert.Equal(2, host.Sender.Messages.Count);

        // BR-12: a receipt email failure is logged by payment id and never undoes the payment; a replay never resends it.
        var failing = await database.InvoiceAsync(world, member.UserId);
        var (_, failingIntent) = await OnlineApi.StartCardAsync(host.Client, failing);
        var failingPayload = OnlineApi.Succeeded(NewEvent(), failingIntent, 357.28m);
        host.Sender.Fail = true;

        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, failingPayload));

        var failingPayment = await database.ScalarAsync<Guid>(
            "SELECT p.id FROM payments p JOIN payment_allocations a ON a.payment_id = p.id WHERE a.invoice_id = @i", ("i", failing.Id));
        Assert.Equal("paid|357.28|0.00", await database.InvoiceMoneyAsync(failing.Id));
        Assert.True(await database.ScalarAsync<bool>("SELECT receipt_sent_at IS NULL FROM payments WHERE id = @p", ("p", failingPayment)));
        Assert.Contains(
            host.Logs.Entries,
            entry => entry.Message.Contains("receipt email failed", StringComparison.OrdinalIgnoreCase) && entry.Message.Contains(failingPayment.ToString(), StringComparison.Ordinal));
        host.Sender.Fail = false;
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, failingPayload));
        Assert.Equal(2, host.Sender.Messages.Count);
        Assert.True(await database.ScalarAsync<bool>("SELECT receipt_sent_at IS NULL FROM payments WHERE id = @p", ("p", failingPayment)));
    }

    [Fact]
    public async Task Webhook_DuplicateAndConcurrentEventsApplyExactlyOnce()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var first = await database.InvoiceAsync(world, member.UserId);
        var (firstAttempt, firstIntent) = await OnlineApi.StartCardAsync(host.Client, first);
        var duplicate = OnlineApi.Succeeded(NewEvent(), firstIntent, 357.28m);

        // Sequential duplicate, and a distinct event id for the same intent: no second payment, number, allocation, audit pair or email.
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, duplicate));
        var applied = await database.FlowStateAsync(world.Org, first.Id);
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, duplicate));
        Assert.Equal(applied, await database.FlowStateAsync(world.Org, first.Id));

        var other = NewEvent();
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Succeeded(other, firstIntent, 357.28m)));
        Assert.Equal("no_effect", await database.EventOutcomeAsync(other));
        Assert.Equal(1, await database.CountAsync("payments", "organization_id = @o", ("o", world.Org)));
        Assert.Equal(1, await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", first.Id)));
        Assert.Equal(1, await database.AuditCountAsync(world.Org, "payment.recorded"));
        Assert.Equal(1, await database.AuditCountAsync(world.Org, "invoice.payment_applied"));
        Assert.Single(host.Sender.Messages);
        Assert.Equal("succeeded", (await database.AttemptAsync(firstAttempt)).Split('|')[0]);

        // The same event delivered concurrently: one payment, one email.
        var second = await database.InvoiceAsync(world, member.UserId);
        var (_, secondIntent) = await OnlineApi.StartCardAsync(host.Client, second);
        var concurrent = OnlineApi.Succeeded(NewEvent(), secondIntent, 357.28m);
        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DeliverAsync(host, concurrent)));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", second.Id)));
        Assert.Equal(2, host.Sender.Messages.Count);

        // Two distinct event ids for one intent racing: one applies, the other is a no-effect.
        var third = await database.InvoiceAsync(world, member.UserId);
        var (_, thirdIntent) = await OnlineApi.StartCardAsync(host.Client, third);
        var racing = await Task.WhenAll(
            DeliverAsync(host, OnlineApi.Succeeded(NewEvent(), thirdIntent, 357.28m)),
            DeliverAsync(host, OnlineApi.Succeeded(NewEvent(), thirdIntent, 357.28m)));

        Assert.All(racing, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", third.Id)));
        Assert.Equal(3, host.Sender.Messages.Count);

        // Concurrent successes on two invoices of one organization: unique, gap-free payment numbers and a receipt per invoice.
        var left = await database.InvoiceAsync(world, member.UserId);
        var right = await database.InvoiceAsync(world, member.UserId);
        var (_, leftIntent) = await OnlineApi.StartCardAsync(host.Client, left);
        var (_, rightIntent) = await OnlineApi.StartCardAsync(host.Client, right);
        var counter = await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org));
        var both = await Task.WhenAll(
            DeliverAsync(host, OnlineApi.Succeeded(NewEvent(), leftIntent, 357.28m)),
            DeliverAsync(host, OnlineApi.Succeeded(NewEvent(), rightIntent, 357.28m)));

        Assert.All(both, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(counter + 2, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal(
            [counter.ToString(CultureInfo.InvariantCulture), (counter + 1).ToString(CultureInfo.InvariantCulture)],
            await database.TextsAsync(
                "SELECT payment_number::text FROM payments p JOIN payment_allocations a ON a.payment_id = p.id WHERE a.invoice_id IN (@l, @r) ORDER BY payment_number",
                ("l", left.Id),
                ("r", right.Id)));
        Assert.Equal(
            [$"RCT-{left.Number}-01", $"RCT-{right.Number}-01"],
            (await database.TextsAsync(
                "SELECT receipt_number FROM payments p JOIN payment_allocations a ON a.payment_id = p.id WHERE a.invoice_id IN (@l, @r)",
                ("l", left.Id),
                ("r", right.Id))).Order().ToArray());
        Assert.Equal(5, host.Sender.Messages.Count);
    }

    [Fact]
    public async Task Webhook_FailureCancelAndLateSuccessFollowTheConfirmedCancellationRule()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        // The provider confirms the cancellation: failed with the mapped category, the invoice unchanged and the lock released.
        var confirmed = await database.InvoiceAsync(world, member.UserId);
        var (confirmedAttempt, confirmedIntent) = await OnlineApi.StartCardAsync(host.Client, confirmed);
        host.Gateway.CancelResult = GatewayCancelResult.Canceled;
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Failed(NewEvent(), confirmedIntent, 357.28m)));

        Assert.Equal("failed|insufficient_funds|0.00|false", await database.AttemptAsync(confirmedAttempt));
        Assert.Equal("sent|0.00|357.28", await database.InvoiceMoneyAsync(confirmed.Id));
        Assert.Contains(confirmedIntent, host.Gateway.Canceled);
        Assert.Equal(1, await database.AuditCountAsync(world.Org, "invoice_payment_attempt.failed"));
        var status = await BillingSeed.ReadAsync(await OnlineApi.StatusAsync(host.Client, confirmed.Token, confirmedAttempt));
        Assert.Equal(("failed", "insufficient_funds"), (status["status"]!.GetValue<string>(), status["failureCategory"]!.GetValue<string>()));

        // A new attempt is possible; its intent is canceled by the provider (canceled event keeps no category).
        var retry = await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, confirmed.Token), HttpStatusCode.Created);
        var retryAttempt = retry["attemptId"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Canceled(NewEvent(), FakePaymentGateway.IntentId(retryAttempt), 357.28m)));
        Assert.Equal("failed|canceled|0.00|false", await database.AttemptAsync(retryAttempt));

        // A later success for the failed attempt still applies BR-11 once: the money was captured.
        var late = NewEvent();
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Succeeded(late, confirmedIntent, 357.28m)));
        Assert.Equal("paid|357.28|0.00", await database.InvoiceMoneyAsync(confirmed.Id));
        Assert.Equal("succeeded|insufficient_funds|0.00|true", await database.AttemptAsync(confirmedAttempt));
        Assert.Equal(1, await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", confirmed.Id)));
        Assert.Equal("applied", await database.EventOutcomeAsync(late));

        // The cancellation refused (the intent succeeded or is processing) or the provider down: the attempt stays pending.
        foreach (var unavailable in new[] { false, true })
        {
            var invoice = await database.InvoiceAsync(world, member.UserId);
            var (attempt, intent) = await OnlineApi.StartCardAsync(host.Client, invoice);
            host.Gateway.CancelResult = GatewayCancelResult.NotCancelable;
            host.Gateway.ThrowOnCancel = unavailable;
            var eventId = NewEvent();

            Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Failed(eventId, intent, 357.28m, "generic_decline")));

            Assert.Equal("pending|card_declined|0.00|false", await database.AttemptAsync(attempt));
            Assert.Equal("no_effect", await database.EventOutcomeAsync(eventId));
            Assert.Equal("sent|0.00|357.28", await database.InvoiceMoneyAsync(invoice.Id));
            await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token), HttpStatusCode.Conflict, "payment_in_progress");

            // A later event with a confirmed cancellation releases it.
            host.Gateway.ThrowOnCancel = false;
            host.Gateway.CancelResult = GatewayCancelResult.AlreadyCanceled;
            Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, OnlineApi.Failed(NewEvent(), intent, 357.28m, "generic_decline")));
            Assert.Equal("failed|card_declined|0.00|false", await database.AttemptAsync(attempt));
        }
    }

    [Fact]
    public async Task Webhook_SucceededThatCannotBeAppliedIsStoredAsNeedsAttentionWithoutAnyPayment()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var voided = await database.InvoiceAsync(world, member.UserId);
        var (voidedAttempt, voidedIntent) = await OnlineApi.StartCardAsync(host.Client, voided);
        await database.ExecuteAsync("UPDATE invoices SET status = 'void' WHERE id = @i", ("i", voided.Id));

        var mismatch = await database.InvoiceAsync(world, member.UserId);
        var (mismatchAttempt, mismatchIntent) = await OnlineApi.StartCardAsync(host.Client, mismatch);

        var below = await database.InvoiceAsync(world, member.UserId);
        var (belowAttempt, belowIntent) = await OnlineApi.StartCardAsync(host.Client, below);
        await database.ExecuteAsync("UPDATE invoices SET status = 'partially_paid', amount_paid = 300, balance_due = 57.28 WHERE id = @i", ("i", below.Id));

        var cases = new (Guid Attempt, string Payload, string Category, Guid Invoice)[]
        {
            (voidedAttempt, OnlineApi.Succeeded(NewEvent(), voidedIntent, 357.28m), "invoice_not_payable", voided.Id),
            (mismatchAttempt, OnlineApi.Succeeded(NewEvent(), mismatchIntent, 100m), "amount_mismatch", mismatch.Id),
            (belowAttempt, OnlineApi.Succeeded(NewEvent(), belowIntent, 357.28m), "balance_below_amount", below.Id),
        };

        foreach (var (attempt, payload, category, invoice) in cases)
        {
            var before = await database.InvoiceMoneyAsync(invoice);

            Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, payload));

            var eventId = JsonNode.Parse(payload)!["id"]!.GetValue<string>();
            Assert.Equal("needs_attention", await database.EventOutcomeAsync(eventId));
            Assert.Equal("pending|-|0.00|false", await database.AttemptAsync(attempt));
            Assert.Equal(before, await database.InvoiceMoneyAsync(invoice));
            Assert.Equal(
                [$"{attempt}|{category}"],
                await database.TextsAsync(
                    "SELECT (metadata ->> 'attemptId') || '|' || (metadata ->> 'category') FROM audit_logs WHERE entity_id = @a AND action = 'invoice_payment_attempt.needs_attention'",
                    ("a", attempt)));
            Assert.Contains(
                host.Logs.Entries,
                entry => entry.Message.Contains(attempt.ToString(), StringComparison.Ordinal) && entry.Message.Contains(category, StringComparison.Ordinal));

            // A replay is a duplicate: no second audit row.
            Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, payload));
            Assert.Equal(1, await database.CountAsync("audit_logs", "entity_id = @a AND action = 'invoice_payment_attempt.needs_attention'", ("a", attempt)));
        }

        Assert.Equal(0, await database.CountAsync("payments", "organization_id = @o", ("o", world.Org)));
        Assert.Empty(host.Sender.Messages);
        Assert.DoesNotContain("4242", string.Join('\n', host.Logs.Entries.Select(entry => entry.Message)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Webhook_FailureWhileApplyingRollsBackEverythingAndTheRetryUsesTheUnconsumedNumbers()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        await database.ExecuteAsync("UPDATE organizations SET next_payment_number = 77 WHERE id = @o", ("o", world.Org));
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var (attempt, intent) = await OnlineApi.StartCardAsync(host.Client, invoice);
        var eventId = NewEvent();
        var payload = OnlineApi.Succeeded(eventId, intent, 357.28m);
        var before = await database.FlowStateAsync(world.Org, invoice.Id);
        var trigger = $"trg_fail_{Guid.NewGuid():N}";

        // AC-11: a failure after the allocation insert (a trigger of the throwaway database) rolls back the payment, the
        // allocation, the invoice, the attempt, the event row and the payment counter; the response is a 500 so the provider retries.
        await database.ExecuteAsync(
            $$"""
            CREATE FUNCTION {{trigger}}() RETURNS trigger AS $$
            BEGIN
                IF NEW.invoice_id = '{{invoice.Id}}' THEN RAISE EXCEPTION 'injected failure'; END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {{trigger}} AFTER INSERT ON payment_allocations FOR EACH ROW EXECUTE FUNCTION {{trigger}}();
            """);

        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, await DeliverAsync(host, payload));
        }
        finally
        {
            await database.ExecuteAsync($"DROP TRIGGER {trigger} ON payment_allocations; DROP FUNCTION {trigger}();");
        }

        Assert.Equal(before, await database.FlowStateAsync(world.Org, invoice.Id));
        Assert.Equal(0, await database.EventCountAsync(eventId));
        Assert.Equal(0, await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", invoice.Id)));
        Assert.Equal(77, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal("pending|-|0.00|false", await database.AttemptAsync(attempt));
        Assert.Empty(host.Sender.Messages);

        // The retry applies once with the unconsumed payment number and the first receipt sequence.
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, payload));
        Assert.Equal(
            $"77|RCT-{invoice.Number}-01",
            await database.ScalarAsync<string>("SELECT payment_number || '|' || receipt_number FROM payments WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(78, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal("applied", await database.EventOutcomeAsync(eventId));
        Assert.Single(host.Sender.Messages);

        // A provider failure while reading the card details is a 500 too and writes nothing (the event is retried).
        var next = await database.InvoiceAsync(world, member.UserId);
        var (_, nextIntent) = await OnlineApi.StartCardAsync(host.Client, next);
        var nextPayload = OnlineApi.Succeeded(NewEvent(), nextIntent, 357.28m);
        var untouched = await database.FlowStateAsync(world.Org, next.Id);
        host.Gateway.FailCardDetails = true;
        Assert.Equal(HttpStatusCode.InternalServerError, await DeliverAsync(host, nextPayload));
        Assert.Equal(untouched, await database.FlowStateAsync(world.Org, next.Id));
        host.Gateway.FailCardDetails = false;
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(host, nextPayload));
        Assert.Equal("paid|357.28|0.00", await database.InvoiceMoneyAsync(next.Id));
    }
}
