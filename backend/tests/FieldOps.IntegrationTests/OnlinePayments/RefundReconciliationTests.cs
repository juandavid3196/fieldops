using System.Net;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>Provider-initiated partial and full refunds reconciled by delta (customer-invoice-payments AC-09, BR-15).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class RefundReconciliationTests(CompanySettingsDatabaseFixture database)
{
    private static string NewEvent() => $"evt_{Guid.NewGuid():N}";

    [Fact]
    public async Task ChargeRefunded_AppliesOnlyTheNewDeltaAndReopensThePaidInvoice()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var (attempt, intent) = await OnlineApi.StartCardAsync(host.Client, invoice);

        // A refund before the payment exists changes nothing.
        var early = NewEvent();
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(early, intent, 357.28m, 100m))).StatusCode);
        Assert.Equal("no_effect", await database.EventOutcomeAsync(early));

        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), intent, 357.28m))).StatusCode);
        Assert.Equal("paid|357.28|0.00", await database.InvoiceMoneyAsync(invoice.Id));
        var paymentId = await database.ScalarAsync<Guid>("SELECT payment_id FROM invoice_payment_attempts WHERE id = @a", ("a", attempt));

        // First refund of 100.00: partially refunded payment and attempt, the invoice moves back to partially paid.
        var first = NewEvent();
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(first, intent, 357.28m, 100m))).StatusCode);

        Assert.Equal("partially_refunded|100.00", await database.ScalarAsync<string>("SELECT status::text || '|' || refunded_amount FROM payments WHERE id = @p", ("p", paymentId)));
        Assert.Equal("partially_refunded|-|100.00|true", await database.AttemptAsync(attempt));
        Assert.Equal("partially_paid|257.28|100.00", await database.InvoiceMoneyAsync(invoice.Id));
        Assert.Equal("applied", await database.EventOutcomeAsync(first));

        // Replays change nothing: the same event, the same cumulative under a new event id and a smaller cumulative.
        var applied = await database.FlowStateAsync(world.Org, invoice.Id);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(first, intent, 357.28m, 100m))).StatusCode);
        Assert.Equal(applied, await database.FlowStateAsync(world.Org, invoice.Id));

        foreach (var cumulative in new[] { 100m, 40m })
        {
            var replay = NewEvent();
            Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(replay, intent, 357.28m, cumulative))).StatusCode);
            Assert.Equal("no_effect", await database.EventOutcomeAsync(replay));
        }

        Assert.Equal("partially_paid|257.28|100.00", await database.InvoiceMoneyAsync(invoice.Id));
        Assert.Equal(1, await database.AuditCountAsync(world.Org, "payment.refunded"));

        // A refund exceeding the payment is stored for attention and applies nothing.
        var excess = NewEvent();
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(excess, intent, 400m, 400m))).StatusCode);
        Assert.Equal("needs_attention", await database.EventOutcomeAsync(excess));
        Assert.Equal("partially_paid|257.28|100.00", await database.InvoiceMoneyAsync(invoice.Id));

        // The public page returns to the payable layout with the refund shown on the payment.
        var partial = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token));
        Assert.Equal(("partially_paid", 257.28m, 100.00m), (partial["status"]!.GetValue<string>(), partial["amountPaid"]!.GetValue<decimal>(), partial["balanceDue"]!.GetValue<decimal>()));
        Assert.NotNull(partial["paymentOptions"]);

        // The full refund: refunded payment and attempt, the invoice reopens as sent with the whole balance.
        var full = NewEvent();
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(full, intent, 357.28m, 357.28m))).StatusCode);

        Assert.Equal("refunded|357.28", await database.ScalarAsync<string>("SELECT status::text || '|' || refunded_amount FROM payments WHERE id = @p", ("p", paymentId)));
        Assert.Equal("refunded|-|357.28|true", await database.AttemptAsync(attempt));
        Assert.Equal("sent|0.00|357.28", await database.InvoiceMoneyAsync(invoice.Id));

        // One audit pair per applied step with before and after, and no email beyond the original receipt.
        Assert.Equal(
            ["succeeded|0.00|partially_refunded|100.00", "partially_refunded|100.00|refunded|357.28"],
            await database.TextsAsync(
                "SELECT (before_data ->> 'status') || '|' || (before_data ->> 'refundedAmount') || '|' || (after_data ->> 'status') || '|' || (after_data ->> 'refundedAmount') FROM audit_logs WHERE organization_id = @o AND action = 'payment.refunded' ORDER BY id",
                ("o", world.Org)));
        Assert.Equal(
            ["paid|357.28|0.00|partially_paid|257.28|100.00", "partially_paid|257.28|100.00|sent|0.00|357.28"],
            await database.TextsAsync(
                """
                SELECT (before_data ->> 'status') || '|' || (before_data ->> 'amountPaid') || '|' || (before_data ->> 'balanceDue') || '|' || (after_data ->> 'status')
                    || '|' || (after_data ->> 'amountPaid') || '|' || (after_data ->> 'balanceDue')
                FROM audit_logs WHERE organization_id = @o AND action = 'invoice.refund_applied' AND metadata ->> 'paymentId' = @p ORDER BY id
                """,
                ("o", world.Org),
                ("p", paymentId.ToString())));
        Assert.Single(host.Sender.Messages);

        // After the full refund any further refund is a no-effect.
        var after = NewEvent();
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(after, intent, 357.28m, 357.28m))).StatusCode);
        Assert.Equal("no_effect", await database.EventOutcomeAsync(after));

        // The status endpoint and the public invoice show the refunded payment (gross amount, refunded amount) and the reopened balance.
        var status = await BillingSeed.ReadAsync(await OnlineApi.StatusAsync(host.Client, invoice.Token, attempt));
        Assert.Equal(("refunded", "refunded", 357.28m, 357.28m), (status["status"]!.GetValue<string>(), status["payment"]!["status"]!.GetValue<string>(), status["payment"]!["amount"]!.GetValue<decimal>(), status["payment"]!["refundedAmount"]!.GetValue<decimal>()));
        Assert.Equal(("sent", 0m, 357.28m), (status["invoice"]!["status"]!.GetValue<string>(), status["invoice"]!["amountPaid"]!.GetValue<decimal>(), status["invoice"]!["balanceDue"]!.GetValue<decimal>()));

        var view = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token));
        var item = Assert.Single(view["payments"]!.AsArray())!;
        Assert.Equal(("refunded", 357.28m, 357.28m), (item["status"]!.GetValue<string>(), item["amount"]!.GetValue<decimal>(), item["refundedAmount"]!.GetValue<decimal>()));
        Assert.Equal(("sent", false), (view["status"]!.GetValue<string>(), view["review"]!["available"]!.GetValue<bool>()));
        Assert.NotNull(view["paymentOptions"]);
        Assert.Null(view["timeline"]!["paidOn"]);
    }
}
