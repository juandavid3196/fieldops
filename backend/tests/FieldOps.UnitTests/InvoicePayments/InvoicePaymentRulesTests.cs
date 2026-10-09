using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;

namespace FieldOps.UnitTests.InvoicePayments;

public class InvoicePaymentRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    // BR-03, BR-05: overdue is derived from the local due date and never stored; the aging buckets split at 0, 1, 30, 31, 60 and 61 days.
    [Theory]
    [InlineData(InvoiceStatus.Sent, 0, "sent", null, AgingBucket.Current)]
    [InlineData(InvoiceStatus.Sent, 1, "overdue", 1, AgingBucket.Days1To30)]
    [InlineData(InvoiceStatus.PartiallyPaid, 30, "overdue", 30, AgingBucket.Days1To30)]
    [InlineData(InvoiceStatus.PartiallyPaid, 31, "overdue", 31, AgingBucket.Days31To60)]
    [InlineData(InvoiceStatus.Sent, 60, "overdue", 60, AgingBucket.Days31To60)]
    [InlineData(InvoiceStatus.Sent, 61, "overdue", 61, AgingBucket.Days60Plus)]
    [InlineData(InvoiceStatus.Sent, -5, "sent", null, AgingBucket.Current)]
    [InlineData(InvoiceStatus.Draft, 90, "draft", null, AgingBucket.Days60Plus)]
    [InlineData(InvoiceStatus.Paid, 90, "paid", null, AgingBucket.Days60Plus)]
    public void DisplayStatusAndAging_FollowTheLocalDueDate(
        InvoiceStatus stored, int daysPastDue, string expectedStatus, int? expectedDays, AgingBucket expectedBucket)
    {
        var due = Today.AddDays(-daysPastDue);

        Assert.Equal((expectedStatus, expectedDays), InvoiceHubRules.Display(stored, due, Today));
        Assert.Equal(expectedBucket, InvoiceHubRules.Bucket(due, Today));
        Assert.Equal("Overdue 1 day", InvoiceHubRules.StatusLabel("overdue", 1));
        Assert.Equal("Overdue 2 days", InvoiceHubRules.StatusLabel("overdue", 2));
    }

    // BR-04, OD-02: local dates in a non-UTC zone, the 90-day window, one decimal, null when empty, and the DST gap midnight.
    [Fact]
    public void AverageDaysToPayAndDayStart_UseTheOrganizationTimezone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var today = new DateOnly(2026, 10, 9);

        // 2026-10-09 02:00 UTC is still 2026-10-08 locally: 1 day after the issue date on 10-07, not 2.
        var lateUtc = new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero);
        var earlyUtc = new DateTimeOffset(2026, 10, 9, 16, 0, 0, TimeSpan.Zero);
        var average = InvoiceHubRules.AverageDaysToPay(
            [
                (new DateOnly(2026, 10, 7), lateUtc),
                (new DateOnly(2026, 10, 7), earlyUtc),
                (new DateOnly(2026, 10, 4), earlyUtc),
                (today.AddDays(-100), InvoiceHubRules.DayStartUtc(today.AddDays(-90), zone)),
                (today.AddDays(-99), InvoiceHubRules.DayStartUtc(today.AddDays(-89), zone)),
            ],
            zone,
            today);

        // (1 + 2 + 5 + 10) / 4 = 4.5 (the payment 90 days ago is outside the window).
        Assert.Equal(4.5m, average);
        Assert.Null(InvoiceHubRules.AverageDaysToPay([], zone, today));
        Assert.Equal(
            new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero),
            InvoiceHubRules.DayStartUtc(new DateOnly(2026, 10, 9), zone));

        // America/Sao_Paulo skipped local midnight in 2018-11-04: the first valid instant still converts back to that date.
        var brazil = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var gap = new DateOnly(2018, 11, 4);

        Assert.Equal(gap, OrganizationTime.LocalDate(InvoiceHubRules.DayStartUtc(gap, brazil), brazil));
        Assert.Equal(gap.AddDays(-1), OrganizationTime.LocalDate(InvoiceHubRules.DayStartUtc(gap, brazil).AddTicks(-1), brazil));
    }

    // Payment field rules: amount, date, method and the reference required for card_external, bank_transfer and check.
    [Theory]
    [InlineData("cash", null, true)]
    [InlineData("other", "  ", true)]
    [InlineData("card_external", null, false)]
    [InlineData("bank_transfer", "   ", false)]
    [InlineData("check", "", false)]
    [InlineData("check", "1001", true)]
    public void PaymentFieldRules_RequireTheReferenceByMethod(string method, string? reference, bool valid)
    {
        var outcome = PaymentFieldRules.Validate(Body(method: method, reference: reference), Today);

        Assert.Equal(valid, outcome is BillingOutcome<PaymentInput>.Succeeded);

        if (outcome is BillingOutcome<PaymentInput>.Invalid invalid)
        {
            Assert.Equal([InvoicePaymentMessages.ReferenceRequired], invalid.Errors["reference"]);
            Assert.Single(invalid.Errors);
        }
    }

    [Fact]
    public void PaymentFieldRules_ReportEveryInvalidFieldAndNormalizeValidOnes()
    {
        var invalid = Assert.IsType<BillingOutcome<PaymentInput>.Invalid>(PaymentFieldRules.Validate(
            new PaymentBodyText("nope", 10.005m, "2026-10-10", "wire", new string('r', 161), "x", null, new string('n', 501), null),
            Today));

        Assert.Equal(
            ["amount", "idempotencyKey", "method", "note", "paidDate", "receivedByUserId", "reference"],
            invalid.Errors.Keys.Order().ToArray());
        Assert.Equal([InvoicePaymentMessages.AmountPrecision], invalid.Errors["amount"]);
        Assert.Equal([InvoicePaymentMessages.PaidDateFuture], invalid.Errors["paidDate"]);
        Assert.Equal([InvoicePaymentMessages.KeyInvalid], invalid.Errors["idempotencyKey"]);
        Assert.Equal([InvoicePaymentMessages.AmountInvalid], Errors(Body(amount: 0m))["amount"]);
        Assert.Equal([InvoicePaymentMessages.MethodRequired], Errors(Body(method: null))["method"]);

        var valid = Assert.IsType<BillingOutcome<PaymentInput>.Succeeded>(
            PaymentFieldRules.Validate(Body(method: "cash", reference: "  ", note: "  hello  "), Today)).Value;

        Assert.Null(valid.Reference);
        Assert.Equal("hello", valid.Note);
        Assert.Equal(25.50m, valid.Amount);
        Assert.Equal(Today, valid.PaidDate);
    }

    // BR-17: the receipt carries the approved lines only, encodes values and leaves out a missing phone.
    [Fact]
    public void ReceiptComposer_WritesTheApprovedLinesAndEncodesValues()
    {
        var data = new PaymentReceiptData(
            Guid.NewGuid(), "carla@example.com", "Acme <b>\nInc", "+1 555 0100", "PAY-12", 1250.5m, "USD",
            new DateOnly(2026, 10, 8), "Bank transfer", "INV-7", 49.5m);
        var message = PaymentReceiptComposer.Compose(data);

        Assert.Equal("carla@example.com", message.To);
        Assert.Equal("Acme <b> Inc: payment receipt PAY-12", message.Subject);
        Assert.Contains("We received your payment. Thank you!", message.TextBody);
        Assert.Contains("Payment PAY-12 · 1,250.50 USD · Oct 8, 2026 · Bank transfer", message.TextBody);
        Assert.Contains("Invoice INV-7 · Remaining balance 49.50 USD", message.TextBody);
        Assert.Contains("Questions? Call us at +1 555 0100.", message.TextBody);
        Assert.Contains("Acme &lt;b&gt;", message.HtmlBody);
        Assert.DoesNotContain("<b>", message.HtmlBody);
        Assert.DoesNotContain("Questions?", PaymentReceiptComposer.Compose(data with { OrganizationPhone = null }).TextBody);
    }

    // BR-13, BR-15: a payment moves sent or partially paid invoices to partially_paid or paid and never exceeds the balance.
    [Fact]
    public void Invoice_ApplyPaymentMovesTheStatusAndNeverExceedsTheBalance()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), 7, Guid.NewGuid(), Guid.NewGuid(), "USD", 100m, 0m, 100m, 100m, Guid.NewGuid(),
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), 0m, "due_upon_receipt");

        Assert.Throws<InvalidOperationException>(() => invoice.ApplyPayment(10m, DateTimeOffset.UtcNow));

        invoice.MarkSent("due_upon_receipt", new DateOnly(2026, 10, 1), "a@b.co", "Hi", "{}", DateTimeOffset.UtcNow);
        var token = invoice.UpdatedAt;
        invoice.ApplyPayment(40m, token.AddSeconds(1));

        Assert.Equal((InvoiceStatus.PartiallyPaid, 40m, 60m), (invoice.Status, invoice.AmountPaid, invoice.BalanceDue));
        Assert.True(invoice.UpdatedAt > token);
        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.ApplyPayment(60.01m, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.ApplyPayment(0m, DateTimeOffset.UtcNow));

        invoice.ApplyPayment(60m, DateTimeOffset.UtcNow);

        Assert.Equal((InvoiceStatus.Paid, 100m, 0m), (invoice.Status, invoice.AmountPaid, invoice.BalanceDue));
        Assert.Throws<InvalidOperationException>(() => invoice.ApplyPayment(1m, DateTimeOffset.UtcNow));
    }

    private static PaymentBodyText Body(
        decimal? amount = 25.50m, string? method = "cash", string? reference = null, string? note = null) =>
        new(Guid.NewGuid().ToString(), amount, "2026-10-09", method, reference, Guid.NewGuid().ToString(), true, note, "2026-10-09T10:00:00Z");

    private static IReadOnlyDictionary<string, string[]> Errors(PaymentBodyText body) =>
        Assert.IsType<BillingOutcome<PaymentInput>.Invalid>(PaymentFieldRules.Validate(body, Today)).Errors;
}
