using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.Domain.Invoices;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.InvoicePayments;

/// <summary>Recording external payments: effects, receipt, idempotency, concurrency, rollback and guards (AC-07 to AC-12).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoicePaymentRecordTests(CompanySettingsDatabaseFixture database)
{
    private const string Tz = HubSeed.Timezone;

    private const string Recipient = "carla@example.com";

    private static decimal Money(JsonNode node) => node.GetValue<decimal>();

    private static string Url(Guid invoice) => PaymentApi.Path(invoice);

    [Fact]
    public async Task Record_PartialThenFullPayment_NumbersAppliesAuditsAndSendsTheReceiptAfterTheCommit()
    {
        var world = await database.SeedWorldAsync(Tz);
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var receiver = await database.SeedReceiverAsync(world.Org);
        await database.ExecuteAsync("UPDATE organizations SET next_payment_number = 7 WHERE id = @o", ("o", world.Org));
        var organizationName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        var invoice = await database.SeedInvoiceAsync(world, ownerMember.UserId, new InvoiceSpec { Total = 100m, IssueDays = -10, DueDays = 5 });
        var keyOne = Guid.NewGuid();
        var keyTwo = Guid.NewGuid();
        var token = await database.StampAsync(invoice.Id);

        // AC-07: a partial payment takes the next number, one allocation, a local-midnight paid_at and the audit pair.
        var partial = await PaymentApi.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                Url(invoice.Id),
                owner,
                PaymentApi.Body(keyOne, 40m, HubSeed.Day(-2), "cash", "   ", receiver.UserId, true, "  n1  ", token)),
            HttpStatusCode.Created);

        Assert.True(partial["changed"]!.GetValue<bool>());
        Assert.Equal("sent", partial["emailStatus"]!.GetValue<string>());
        var paymentOne = partial["payment"]!;
        Assert.Equal(
            ("PAY-7", HubSeed.Day(-2), "Carla Customer", invoice.Id, invoice.Display, "cash", null, 40m, "USD", "Rita Receiver"),
            (paymentOne["number"]!.GetValue<string>(), paymentOne["paidDate"]!.GetValue<string>(), paymentOne["customerName"]!.GetValue<string>(),
                paymentOne["invoiceId"]!.GetValue<Guid>(), paymentOne["invoiceNumber"]!.GetValue<string>(), paymentOne["method"]!.GetValue<string>(),
                (string?)paymentOne["reference"]?.GetValue<string>(), Money(paymentOne["amount"]!), paymentOne["currency"]!.GetValue<string>(),
                paymentOne["receivedByName"]!.GetValue<string>()));
        Assert.Equal(
            ("partially_paid", 40m, 60m),
            (partial["invoice"]!["status"]!.GetValue<string>(), Money(partial["invoice"]!["amountPaid"]!), Money(partial["invoice"]!["balanceDue"]!)));
        Assert.NotEqual(token, partial["invoice"]!["updatedAt"]!.GetValue<string>());
        var firstId = paymentOne["id"]!.GetValue<Guid>();

        Assert.Equal(
            $"7|cash|40.00|USD|-|n1|{ownerMember.UserId}|{receiver.UserId}|{world.Customer}|{keyOne}",
            await database.ScalarAsync<string>(
                """
                SELECT payment_number || '|' || method::text || '|' || amount || '|' || currency || '|' || COALESCE(external_reference, '-') || '|' || COALESCE(notes, '-')
                    || '|' || recorded_by_user_id || '|' || received_by_user_id || '|' || customer_id || '|' || idempotency_key
                FROM payments WHERE id = @p
                """,
                ("p", firstId)));
        Assert.True(
            await database.ScalarAsync<bool>(
                "SELECT paid_at = ((CAST(@d AS date) + time '00:00') AT TIME ZONE @tz) FROM payments WHERE id = @p",
                ("d", HubSeed.Today().AddDays(-2)),
                ("tz", Tz),
                ("p", firstId)));
        Assert.Equal(1, await database.CountAsync("payment_allocations", "payment_id = @p AND invoice_id = @i AND amount = 40", ("p", firstId), ("i", invoice.Id)));
        Assert.Equal("partially_paid|40.00|60.00", await InvoiceTotalsAsync(invoice.Id));
        Assert.Equal(8, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal(
            [$"40.00|USD|cash|7|{invoice.Id}|true|{world.BranchA}|{ownerMember.UserId}|{firstId}"],
            await database.TextsAsync(
                """
                SELECT (after_data ->> 'amount') || '|' || (after_data ->> 'currency') || '|' || (after_data ->> 'method') || '|' || (metadata ->> 'paymentNumber')
                    || '|' || (metadata ->> 'invoiceId') || '|' || (metadata ->> 'receiptRequested') || '|' || branch_id || '|' || actor_user_id || '|' || entity_id
                FROM audit_logs WHERE organization_id = @o AND action = 'payment.recorded' AND entity_type = 'payment'
                """,
                ("o", world.Org)));
        Assert.Equal(
            [$"sent|0.00|100.00|partially_paid|40.00|60.00|{firstId}|{world.BranchA}"],
            await database.TextsAsync(
                """
                SELECT (before_data ->> 'status') || '|' || (before_data ->> 'amountPaid') || '|' || (before_data ->> 'balanceDue')
                    || '|' || (after_data ->> 'status') || '|' || (after_data ->> 'amountPaid') || '|' || (after_data ->> 'balanceDue')
                    || '|' || (metadata ->> 'paymentId') || '|' || branch_id
                FROM audit_logs WHERE organization_id = @o AND action = 'invoice.payment_applied' AND entity_type = 'invoice' AND entity_id = @i
                """,
                ("o", world.Org),
                ("i", invoice.Id)));

        // AC-11: the receipt follows the approved subject and lines and never carries the reference, note or receiver.
        var receipt = Assert.Single(host.Sender.Messages);
        var paidOn = HubSeed.Today().AddDays(-2).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        Assert.Equal(Recipient, receipt.To);
        Assert.Equal($"{organizationName}: payment receipt PAY-7", receipt.Subject);
        Assert.Contains("We received your payment. Thank you!", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Payment PAY-7 · 40.00 USD · {paidOn} · Cash", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Invoice {invoice.Display} · Remaining balance 60.00 USD", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains(organizationName, receipt.HtmlBody, StringComparison.Ordinal);
        Assert.All(["n1", "Rita", "Receiver", "Olive", ownerMember.Email], secret =>
        {
            Assert.DoesNotContain(secret, receipt.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, receipt.HtmlBody, StringComparison.Ordinal);
        });

        // The balance payment: status paid, balance zero, next number; the sender throws but the payment stays (201 failed).
        host.Sender.Fail = true;
        var full = await PaymentApi.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                Url(invoice.Id),
                owner,
                PaymentApi.Body(keyTwo, 60m, null, "bank_transfer", "WIRE-REF-9", receiver.UserId, true, "SECRET-NOTE", await database.StampAsync(invoice.Id))),
            HttpStatusCode.Created);
        host.Sender.Fail = false;

        Assert.True(full["changed"]!.GetValue<bool>());
        Assert.Equal("failed", full["emailStatus"]!.GetValue<string>());
        Assert.Equal("PAY-8", full["payment"]!["number"]!.GetValue<string>());
        Assert.Equal("WIRE-REF-9", full["payment"]!["reference"]!.GetValue<string>());
        Assert.Equal(
            ("paid", 100m, 0m),
            (full["invoice"]!["status"]!.GetValue<string>(), Money(full["invoice"]!["amountPaid"]!), Money(full["invoice"]!["balanceDue"]!)));
        Assert.Equal("paid|100.00|0.00", await InvoiceTotalsAsync(invoice.Id));
        Assert.Equal(2, await database.CountAsync("payment_allocations", "invoice_id = @i", ("i", invoice.Id)));
        Assert.Equal(2, host.Sender.Attempts);
        Assert.Single(host.Sender.Messages);
        var failure = Assert.Single(host.Logs.Entries, entry => entry.Message.StartsWith("Payment receipt email failed.", StringComparison.Ordinal));
        Assert.Equal(full["payment"]!["id"]!.GetValue<Guid>(), (Guid)failure.Properties["PaymentId"]!);
        Assert.Equal(nameof(InvalidOperationException), (string)failure.Properties["Category"]!);

        // No recipient or no request: 201 with not_sent and no email attempt.
        var noRecipient = await database.SeedInvoiceAsync(world, ownerMember.UserId, new InvoiceSpec { Total = 25m, Recipient = null });
        var third = await PaymentApi.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post, Url(noRecipient.Id), owner, PaymentApi.Body(amount: 25m, receivedBy: receiver.UserId, sendReceipt: true, updatedAt: await database.StampAsync(noRecipient.Id))),
            HttpStatusCode.Created);
        var withRecipient = await database.SeedInvoiceAsync(world, ownerMember.UserId, new InvoiceSpec { Total = 30m });
        var fourth = await PaymentApi.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post, Url(withRecipient.Id), owner, PaymentApi.Body(amount: 30m, receivedBy: receiver.UserId, sendReceipt: false, updatedAt: await database.StampAsync(withRecipient.Id))),
            HttpStatusCode.Created);

        Assert.Equal(("not_sent", "PAY-9", "paid"), (third["emailStatus"]!.GetValue<string>(), third["payment"]!["number"]!.GetValue<string>(), third["invoice"]!["status"]!.GetValue<string>()));
        Assert.Equal(("not_sent", "PAY-10"), (fourth["emailStatus"]!.GetValue<string>(), fourth["payment"]!["number"]!.GetValue<string>()));
        Assert.Equal(2, host.Sender.Attempts);
        Assert.Equal(11, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));

        // The list shows the paid invoice with its last payment date and no further action.
        var paid = PaymentApi.Items(await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/invoices?search={invoice.Display}", owner), HttpStatusCode.OK)).Single()!;
        Assert.Equal(("paid", "paid", HubSeed.Day(0), false), (paid["status"]!.GetValue<string>(), paid["lastActivity"]!["kind"]!.GetValue<string>(), paid["lastActivity"]!["date"]!.GetValue<string>(), paid["canRecordPayment"]!.GetValue<bool>()));

        // AC-12: audit rows and logs carry no reference, note, recipient, customer name or idempotency key.
        var audit = await BillingSeed.AuditTextAsync(database, world.Org);
        var logs = string.Join('\n', host.Logs.Entries.Select(entry => $"{entry.Message} {entry.Exception} {string.Join(' ', entry.Properties.Values)}"));

        foreach (var secret in new[] { "WIRE-REF-9", "SECRET-NOTE", "n1", Recipient, "Carla Customer", "Rita", keyOne.ToString(), keyTwo.ToString() })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.OrdinalIgnoreCase);

            if (secret != "n1")
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(8, await database.CountAsync("audit_logs", "organization_id = @o AND action IN ('payment.recorded', 'invoice.payment_applied')", ("o", world.Org)));
    }

    [Fact]
    public async Task Record_IdempotencyAndConcurrency_ProduceOnePaymentPerKeyAndNeverOverpay()
    {
        var world = await database.SeedWorldAsync(Tz);
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var by = ownerMember.UserId;
        var invoice = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Total = 100m });
        var other = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Total = 100m });
        var stale = await database.StampAsync(invoice.Id);
        var key = Guid.NewGuid();

        Task<HttpResponseMessage> Post(HubInvoice target, JsonObject body) => host.SendAsync(HttpMethod.Post, Url(target.Id), owner, body);

        // Sequential replay: 201 once, then 200 changed = false with no write, no email and no new number, even with a stale token.
        var created = await PaymentApi.ReadAsync(await Post(invoice, PaymentApi.Body(key, 30m, null, "cash", null, by, true, null, stale)), HttpStatusCode.Created);
        var afterFirst = await database.PaymentStateAsync(world.Org);
        var replay = await PaymentApi.ReadAsync(await Post(invoice, PaymentApi.Body(key, 30m, null, "cash", null, by, true, null, stale)), HttpStatusCode.OK);

        Assert.False(replay["changed"]!.GetValue<bool>());
        Assert.Equal("not_sent", replay["emailStatus"]!.GetValue<string>());
        Assert.Equal(created["payment"]!["id"]!.GetValue<Guid>(), replay["payment"]!["id"]!.GetValue<Guid>());
        Assert.Equal(created["payment"]!["number"]!.GetValue<string>(), replay["payment"]!["number"]!.GetValue<string>());
        Assert.Equal(30m, Money(replay["invoice"]!["amountPaid"]!));
        Assert.Equal(afterFirst, await database.PaymentStateAsync(world.Org));
        Assert.Single(host.Sender.Messages);
        Assert.Equal(2, await database.CountAsync("audit_logs", "organization_id = @o AND action IN ('payment.recorded', 'invoice.payment_applied')", ("o", world.Org)));

        // The same key with any difference (amount, note, another invoice) is a conflict and writes nothing.
        foreach (var (target, body) in new[]
        {
            (invoice, PaymentApi.Body(key, 31m, null, "cash", null, by, true, null, stale)),
            (invoice, PaymentApi.Body(key, 30m, null, "cash", null, by, true, "different note", stale)),
            (invoice, PaymentApi.Body(key, 30m, HubSeed.Day(-1), "cash", null, by, true, null, stale)),
            (other, PaymentApi.Body(key, 30m, null, "cash", null, by, true, null, await database.StampAsync(other.Id))),
        })
        {
            var conflict = await BillingSeed.ProblemAsync(await Post(target, body), HttpStatusCode.Conflict, "idempotency_conflict");
            Assert.Equal("This payment request was already used for different details.", conflict["title"]!.GetValue<string>());
        }

        Assert.Equal(afterFirst, await database.PaymentStateAsync(world.Org));

        // Concurrent requests with one key: one 201, the rest 200 changed = false, one number, one email.
        var concurrentKey = Guid.NewGuid();
        var token = await database.StampAsync(invoice.Id);
        var burst = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => Post(invoice, PaymentApi.Body(concurrentKey, 20m, null, "cash", null, by, true, null, token))));
        var statuses = burst.Select(response => response.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, await database.CountAsync("payments", "organization_id = @o AND idempotency_key = @k", ("o", world.Org), ("k", concurrentKey)));
        Assert.Equal(2, host.Sender.Messages.Count);
        Assert.Equal(4, await database.CountAsync("audit_logs", "organization_id = @o AND action IN ('payment.recorded', 'invoice.payment_applied')", ("o", world.Org)));
        Assert.Equal("partially_paid|50.00|50.00", await InvoiceTotalsAsync(invoice.Id));

        // Different keys with the same token: exactly one wins, the other is invoice_changed; the sum never exceeds the total.
        var sameToken = await database.StampAsync(invoice.Id);
        var race = await Task.WhenAll(
            Post(invoice, PaymentApi.Body(Guid.NewGuid(), 10m, null, "cash", null, by, false, null, sameToken)),
            Post(invoice, PaymentApi.Body(Guid.NewGuid(), 10m, null, "cash", null, by, false, null, sameToken)));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            race.Select(response => response.StatusCode).Order().ToArray());
        await BillingSeed.ProblemAsync(race.Single(response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "invoice_changed");
        Assert.Equal("partially_paid|60.00|40.00", await InvoiceTotalsAsync(invoice.Id));

        // One key used on two invoices at once: one payment wins, the other request is an idempotency conflict (unique index backstop).
        var sharedKey = Guid.NewGuid();
        var cross = await Task.WhenAll(
            Post(invoice, PaymentApi.Body(sharedKey, 10m, null, "cash", null, by, false, null, await database.StampAsync(invoice.Id))),
            Post(other, PaymentApi.Body(sharedKey, 10m, null, "cash", null, by, false, null, await database.StampAsync(other.Id))));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], cross.Select(response => response.StatusCode).Order().ToArray());
        await BillingSeed.ProblemAsync(cross.Single(response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "idempotency_conflict");
        Assert.Equal(1, await database.CountAsync("payments", "organization_id = @o AND idempotency_key = @k", ("o", world.Org), ("k", sharedKey)));

        // Allocations always add up to the stored amounts and never exceed the invoice total.
        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM invoices i
                WHERE i.organization_id = @o
                  AND ((SELECT COALESCE(SUM(amount), 0) FROM payment_allocations a WHERE a.invoice_id = i.id) <> i.amount_paid
                    OR (SELECT COALESCE(SUM(amount), 0) FROM payment_allocations a WHERE a.invoice_id = i.id) > i.total)
                """,
                ("o", world.Org)));
    }

    [Fact]
    public async Task Record_FailureAfterTheAllocationRollsBackEverythingIncludingTheCounter()
    {
        var world = await database.SeedWorldAsync(Tz);
        var interceptor = new FailAfterAllocationInterceptor();
        var sender = new RecordingEmailSender();
        await using var session = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        using var client = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(interceptor));
        })));
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sam", "Staff");
        var cookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);
        var invoice = await database.SeedInvoiceAsync(world, member.UserId, new InvoiceSpec { Total = 100m });
        await database.ExecuteAsync("UPDATE organizations SET next_payment_number = 12 WHERE id = @o", ("o", world.Org));
        var body = PaymentApi.Body(Guid.NewGuid(), 40m, null, "cash", null, member.UserId, true, "rollback note", await database.StampAsync(invoice.Id)).ToJsonString();

        // AC-10: the failure happens after the payment, the allocation and the invoice update were written.
        var before = await database.PaymentStateAsync(world.Org);
        interceptor.Armed = true;
        var failed = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, Url(invoice.Id), body, cookie);
        interceptor.Armed = false;

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(interceptor.Fired);
        Assert.Equal(before, await database.PaymentStateAsync(world.Org));
        Assert.Equal(0, await database.CountAsync("payments", "organization_id = @o", ("o", world.Org)));
        Assert.Equal(12, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Empty(sender.Messages);
        Assert.Equal(0, sender.Attempts);

        // The same request then succeeds with the number that was never consumed.
        var retried = await PaymentApi.ReadAsync(
            await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, Url(invoice.Id), body, cookie), HttpStatusCode.Created);
        Assert.Equal("PAY-12", retried["payment"]!["number"]!.GetValue<string>());
        Assert.Equal(13, await database.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal("partially_paid|40.00|60.00", await InvoiceTotalsAsync(invoice.Id));
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Record_GuardsAndFieldRules_RejectInvalidRequestsWithoutWritingAndAcceptCashWithoutReference()
    {
        var world = await database.SeedWorldAsync(Tz);
        var foreign = await database.SeedWorldAsync(Tz);
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var by = ownerMember.UserId;
        var inactive = await database.SeedReceiverAsync(world.Org, "Ina", "Inactive", status: "suspended");
        var foreignMember = await database.SeedReceiverAsync(foreign.Org, "Fay", "Foreign");
        var draft = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Status = "draft", Recipient = null });
        var paid = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Status = "paid", Total = 50m, Paid = 50m });
        var sent = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Total = 100m, IssueDays = -10, DueDays = 5 });
        var token = await database.StampAsync(sent.Id);
        var long161 = new string('r', 161);

        JsonObject Valid(string? updatedAt = null) => PaymentApi.Body(amount: 10m, receivedBy: by, updatedAt: updatedAt ?? token);

        JsonObject With(string field, JsonNode? value, string? updatedAt = null)
        {
            var body = Valid(updatedAt);
            body[field] = value;

            return body;
        }

        // (invoice, body, status, code or field error, message): the table of AC-09.
        var cases = new (string Name, HubInvoice Target, JsonObject Body, HttpStatusCode Status, string Key, string Message)[]
        {
            ("draft", draft, Valid(), HttpStatusCode.Conflict, "invoice_not_payable", "This invoice can't receive payments."),
            ("paid", paid, Valid(), HttpStatusCode.Conflict, "invoice_not_payable", "This invoice can't receive payments."),
            ("stale", sent, Valid("2020-01-01T00:00:00Z"), HttpStatusCode.Conflict, "invoice_changed", "This invoice changed. Refresh to see the latest."),
            ("missing token", sent, Valid(string.Empty), HttpStatusCode.Conflict, "invoice_changed", "This invoice changed. Refresh to see the latest."),
            ("zero", sent, With("amount", 0m), HttpStatusCode.BadRequest, "amount", "Enter an amount greater than 0."),
            ("three decimals", sent, With("amount", 10.005m), HttpStatusCode.BadRequest, "amount", "Use up to 2 decimal places."),
            ("over balance", sent, With("amount", 100.01m), HttpStatusCode.BadRequest, "amount", "Amount can't exceed the outstanding balance of 100.00 USD."),
            ("future", sent, With("paidDate", HubSeed.Day(1)), HttpStatusCode.BadRequest, "paidDate", "Payment date can't be in the future."),
            ("before issue", sent, With("paidDate", HubSeed.Day(-11)), HttpStatusCode.BadRequest, "paidDate", "Payment date can't be before the invoice date."),
            ("no method", sent, With("method", null), HttpStatusCode.BadRequest, "method", "Select a payment method."),
            ("card no ref", sent, With("method", "card_external"), HttpStatusCode.BadRequest, "reference", "Enter the reference number."),
            ("transfer blank ref", sent, With("method", "bank_transfer").Also("reference", "  "), HttpStatusCode.BadRequest, "reference", "Enter the reference number."),
            ("check no ref", sent, With("method", "check"), HttpStatusCode.BadRequest, "reference", "Enter the reference number."),
            ("long ref", sent, With("reference", long161), HttpStatusCode.BadRequest, "reference", "Use 160 characters or fewer."),
            ("inactive receiver", sent, With("receivedByUserId", inactive.UserId.ToString()), HttpStatusCode.BadRequest, "receivedByUserId", "Select who received the payment."),
            ("foreign receiver", sent, With("receivedByUserId", foreignMember.UserId.ToString()), HttpStatusCode.BadRequest, "receivedByUserId", "Select who received the payment."),
            ("invalid receiver on draft", draft, With("receivedByUserId", inactive.UserId.ToString()), HttpStatusCode.BadRequest, "receivedByUserId", "Select who received the payment."),
            ("invalid receiver on paid", paid, With("receivedByUserId", Guid.NewGuid().ToString()), HttpStatusCode.BadRequest, "receivedByUserId", "Select who received the payment."),
            ("unknown receiver", sent, With("receivedByUserId", Guid.NewGuid().ToString()), HttpStatusCode.BadRequest, "receivedByUserId", "Select who received the payment."),
            ("long note", sent, With("note", new string('n', 501)), HttpStatusCode.BadRequest, "note", "Use 500 characters or fewer."),
            ("no key", sent, With("idempotencyKey", null), HttpStatusCode.BadRequest, "idempotencyKey", "Try again."),
            ("bad key", sent, With("idempotencyKey", "not-a-guid"), HttpStatusCode.BadRequest, "idempotencyKey", "Try again."),
        };
        var unknownFields = new[] { "status", "number", "customer", "currency", "organizationId", "balanceDue" };
        var state = await database.PaymentStateAsync(world.Org);
        var receiverProblems = new List<string>();

        foreach (var (name, target, body, status, key, message) in cases)
        {
            var response = await host.SendAsync(HttpMethod.Post, Url(target.Id), owner, body);
            var problem = await BillingSeed.ProblemAsync(response, status);

            if (status == HttpStatusCode.Conflict)
            {
                Assert.Equal(key, problem["code"]!.GetValue<string>());
                Assert.Equal(message, problem["title"]!.GetValue<string>());
            }
            else
            {
                Assert.True(problem["errors"]?[key] is not null, $"{name}: expected errors.{key} in {problem.ToJsonString()}");
                Assert.Equal(message, problem["errors"]![key]![0]!.GetValue<string>());
                Assert.Single(problem["errors"]!.AsObject());
            }

            if (key == "receivedByUserId")
            {
                receiverProblems.Add(await PaymentApi.WithoutTraceAsync(response));
            }

            Assert.Equal(state, await database.PaymentStateAsync(world.Org));
        }

        // The receiver error reveals nothing about the id: inactive, foreign and unknown users look the same.
        Assert.Single(receiverProblems.Distinct());

        // Unknown client fields are rejected outright.
        foreach (var field in unknownFields)
        {
            var response = await host.SendAsync(HttpMethod.Post, Url(sent.Id), owner, With(field, "paid"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(state, await database.PaymentStateAsync(world.Org));
        Assert.Empty(host.Sender.Messages);

        // Cash and other without a reference succeed; the second one needs the new token.
        var cash = await PaymentApi.ReadAsync(
            await host.SendAsync(HttpMethod.Post, Url(sent.Id), owner, With("amount", 10m)), HttpStatusCode.Created);
        var otherMethod = await PaymentApi.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                Url(sent.Id),
                owner,
                With("method", "other", cash["invoice"]!["updatedAt"]!.GetValue<string>()).Also("reference", "")),
            HttpStatusCode.Created);

        Assert.Equal(("cash", "other"), (cash["payment"]!["method"]!.GetValue<string>(), otherMethod["payment"]!["method"]!.GetValue<string>()));
        Assert.Null(otherMethod["payment"]!["reference"]);
        Assert.Equal("partially_paid|20.00|80.00", await InvoiceTotalsAsync(sent.Id));
        Assert.Equal(2, await database.CountAsync("payments", "organization_id = @o AND external_reference IS NULL", ("o", world.Org)));
    }

    private Task<string> InvoiceTotalsAsync(Guid invoice) =>
        database.ScalarAsync<string>("SELECT status::text || '|' || amount_paid || '|' || balance_due FROM invoices WHERE id = @i", ("i", invoice));

    /// <summary>Test-only fault seam: fails right after the save that wrote the allocation, before the commit.</summary>
    private sealed class FailAfterAllocationInterceptor : SaveChangesInterceptor
    {
        private volatile bool _pending;

        public volatile bool Armed;

        public volatile bool Fired;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Mark(eventData);

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Mark(eventData);

            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Fail();

            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Fail();

            return ValueTask.FromResult(result);
        }

        private void Mark(DbContextEventData eventData) =>
            _pending = Armed && eventData.Context!.ChangeTracker.Entries<PaymentAllocation>().Any(entry => entry.State == EntityState.Added);

        private void Fail()
        {
            if (_pending)
            {
                _pending = false;
                Fired = true;

                throw new InvalidOperationException("Injected failure after saving the payment allocation.");
            }
        }
    }
}

internal static class JsonObjectExtensions
{
    /// <summary>Sets another property and returns the object, to build a body from a base body in one expression.</summary>
    public static JsonObject Also(this JsonObject body, string field, JsonNode? value)
    {
        body[field] = value;

        return body;
    }
}
