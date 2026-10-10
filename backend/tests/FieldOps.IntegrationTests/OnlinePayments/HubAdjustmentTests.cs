using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>The invoices and payments hub with online and refunded payments (customer-invoice-payments AC-18, BR-31).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class HubAdjustmentTests(CompanySettingsDatabaseFixture database)
{
    private static string NewEvent() => $"evt_{Guid.NewGuid():N}";

    [Fact]
    public async Task Hub_UsesNetAmountsStatusAndTheOnlineCardMethodAndRecordingAssignsReceipts()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var receiver = await database.SeedReceiverAsync(world.Org);
        var today = BillingSeed.Today(OnlineSeed.Timezone);

        // A card payment partially refunded, a card payment fully refunded and an external cash payment.
        var partial = await database.InvoiceAsync(world, member.UserId);
        var (_, partialIntent) = await OnlineApi.StartCardAsync(host.Client, partial);
        await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), partialIntent, 357.28m));
        await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(NewEvent(), partialIntent, 357.28m, 100m));

        var refunded = await database.InvoiceAsync(world, member.UserId);
        var (_, refundedIntent) = await OnlineApi.StartCardAsync(host.Client, refunded);
        await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), refundedIntent, 357.28m));
        await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(NewEvent(), refundedIntent, 357.28m, 357.28m));

        var cash = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Total = 100m });
        var recorded = await BillingSeed.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                PaymentApi.Path(cash.Id),
                owner,
                PaymentApi.Body(amount: 40m, paidDate: today, method: "cash", receivedBy: receiver.UserId, updatedAt: await database.StampAsync(cash.Id))),
            HttpStatusCode.Created);

        // BR-31 e: the external payment gets the first receipt number of its invoice, the next one continues the sequence.
        Assert.Equal(
            $"RCT-{cash.Number}-01",
            await database.ScalarAsync<string>("SELECT receipt_number FROM payments WHERE id = @p", ("p", recorded["payment"]!["id"]!.GetValue<Guid>())));
        var second = await BillingSeed.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                PaymentApi.Path(cash.Id),
                owner,
                PaymentApi.Body(amount: 10m, paidDate: today, method: "cash", receivedBy: receiver.UserId, updatedAt: recorded["invoice"]!["updatedAt"]!.GetValue<string>())),
            HttpStatusCode.Created);
        Assert.Equal($"RCT-{cash.Number}-02", await database.ScalarAsync<string>("SELECT receipt_number FROM payments WHERE id = @p", ("p", second["payment"]!["id"]!.GetValue<Guid>())));

        // The record body never creates an online card payment.
        var rejected = await BillingSeed.ProblemAsync(
            await host.SendAsync(
                HttpMethod.Post,
                PaymentApi.Path(cash.Id),
                owner,
                PaymentApi.Body(amount: 5m, paidDate: today, method: "card_online", receivedBy: receiver.UserId, updatedAt: second["invoice"]!["updatedAt"]!.GetValue<string>())),
            HttpStatusCode.BadRequest,
            errorKey: "method");
        Assert.Equal("Select a payment method.", rejected["errors"]!["method"]![0]!.GetValue<string>());

        // Overview: paid this month, outstanding and the recent payments are net of refunds.
        var overview = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/overview", owner));
        Assert.Equal(257.28m + 50m, overview["metrics"]!["paidThisMonth"]!.GetValue<decimal>());
        Assert.Equal(100m + 357.28m + 50m, overview["metrics"]!["outstanding"]!.GetValue<decimal>());
        var recent = overview["recentPayments"]!.AsArray().Where(row => row!["invoiceId"]!.GetValue<Guid>() != cash.Id).ToDictionary(row => row!["invoiceId"]!.GetValue<Guid>(), row => (row!["amount"]!.GetValue<decimal>(), row["method"]!.GetValue<string>()));
        Assert.Equal((257.28m, "card_online"), recent[partial.Id]);
        Assert.Equal((0m, "card_online"), recent[refunded.Id]);

        // The payments list: gross amount plus the refunded amount and status, no receiver for online payments, filterable by the new method.
        var all = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/payments", owner));
        var rows = PaymentApi.Items(all).ToDictionary(row => (row!["invoiceId"]!.GetValue<Guid>(), row["amount"]!.GetValue<decimal>()), row => row!);
        var partialRow = rows[(partial.Id, 357.28m)];
        var refundedRow = rows[(refunded.Id, 357.28m)];

        Assert.Equal(("card_online", "partially_refunded", 100m, null), (partialRow["method"]!.GetValue<string>(), partialRow["status"]!.GetValue<string>(), partialRow["refundedAmount"]!.GetValue<decimal>(), partialRow["receivedByName"]?.GetValue<string>()));
        Assert.Equal(("refunded", 357.28m, null), (refundedRow["status"]!.GetValue<string>(), refundedRow["refundedAmount"]!.GetValue<decimal>(), refundedRow["receivedByName"]?.GetValue<string>()));
        var cashRow = rows[(cash.Id, 40m)];
        Assert.Equal(("cash", "succeeded", 0m, "Rita Receiver"), (cashRow["method"]!.GetValue<string>(), cashRow["status"]!.GetValue<string>(), cashRow["refundedAmount"]!.GetValue<decimal>(), cashRow["receivedByName"]!.GetValue<string>()));
        Assert.Equal(
            2,
            PaymentApi.Items(await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/payments?method=card_online", owner))).Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, "/invoices/payments?method=wire", owner)).StatusCode);

        // The export: "Online card", the net amount, an empty receiver and the new Status column.
        var csv = (await (await host.SendAsync(HttpMethod.Get, "/invoices/payments/export?method=card_online", owner)).Content.ReadAsStringAsync())
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Payment,Date,Customer,Invoice,Method,Reference,Amount,Currency,Received by,Status", csv[0]);
        Assert.Contains(csv, line => line.Contains($",{partial.Display},Online card,,257.28,USD,,Partially refunded", StringComparison.Ordinal));
        Assert.Contains(csv, line => line.Contains($",{refunded.Display},Online card,,0.00,USD,,Refunded", StringComparison.Ordinal));

        // The invoice list shows the net figures already stored on the invoices.
        var invoices = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices", owner));
        var byId = PaymentApi.Items(invoices).ToDictionary(row => row!["id"]!.GetValue<Guid>(), row => row!);
        Assert.Equal(("partially_paid", 100m), (byId[partial.Id]["status"]!.GetValue<string>(), byId[partial.Id]["balanceDue"]!.GetValue<decimal>()));
        Assert.Equal(("sent", 357.28m), (byId[refunded.Id]["status"]!.GetValue<string>(), byId[refunded.Id]["balanceDue"]!.GetValue<decimal>()));
    }
}
