using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Domain.Invoices;
using FieldOps.Infrastructure.Payments;
using FieldOps.UnitTests.Authentication;
using Microsoft.Extensions.Options;

namespace FieldOps.UnitTests.OnlinePayments;

/// <summary>Pure rules of customer-invoice-payments: numbering, labels, failure mapping, minor units, review fields, webhook verification, settings and the per-invoice throttle.</summary>
public class OnlinePaymentRulesTests
{
    private const string Secret = "whsec_unit_test_secret";

    // BR-11 b: the receipt sequence has two digits at least and the invoice number is the numeric part only.
    [Theory]
    [InlineData(1042, 1, "RCT-1042-01")]
    [InlineData(1042, 2, "RCT-1042-02")]
    [InlineData(7, 9, "RCT-7-09")]
    [InlineData(7, 10, "RCT-7-10")]
    [InlineData(7, 123, "RCT-7-123")]
    public void ReceiptNumber_UsesTheInvoiceNumberAndAtLeastTwoDigits(long invoice, int sequence, string expected) =>
        Assert.Equal(expected, OnlinePaymentRules.ReceiptNumber(invoice, sequence));

    // BR-13: the provider code (decline code first) maps to the stored category.
    [Theory]
    [InlineData("card_declined", PaymentFailureCategories.CardDeclined)]
    [InlineData("generic_decline", PaymentFailureCategories.CardDeclined)]
    [InlineData("insufficient_funds", PaymentFailureCategories.InsufficientFunds)]
    [InlineData("expired_card", PaymentFailureCategories.ExpiredCard)]
    [InlineData("incorrect_cvc", PaymentFailureCategories.IncorrectCvc)]
    [InlineData("payment_intent_authentication_failure", PaymentFailureCategories.AuthenticationFailed)]
    [InlineData("processing_error", PaymentFailureCategories.ProcessingError)]
    [InlineData("something_new", PaymentFailureCategories.Other)]
    [InlineData(null, PaymentFailureCategories.Other)]
    public void FailureCategory_MapsTheProviderCode(string? code, string expected) =>
        Assert.Equal(expected, PaymentFailureCategories.FromProviderCode(code));

    // AS-01: two-decimal currencies convert exactly; anything else is a gateway failure and never sent.
    [Fact]
    public void MinorUnits_ConvertTwoDecimalCurrenciesAndRejectTheRest()
    {
        Assert.Equal(35728L, PaymentMinorUnits.ToMinor(357.28m, "USD"));
        Assert.Equal(1L, PaymentMinorUnits.ToMinor(0.01m, "eur"));
        Assert.True(PaymentMinorUnits.TryFromMinor(35728, "USD", out var amount));
        Assert.Equal(357.28m, amount);
        Assert.False(PaymentMinorUnits.TryFromMinor(100, "JPY", out _));
        Assert.Throws<PaymentGatewayUnavailableException>(() => PaymentMinorUnits.ToMinor(10m, "JPY"));
        Assert.Throws<PaymentGatewayUnavailableException>(() => PaymentMinorUnits.ToMinor(10.001m, "USD"));
        Assert.Throws<PaymentGatewayUnavailableException>(() => PaymentMinorUnits.ToMinor(0m, "USD"));
    }

    // BR-05, BR-03: the card label, the first name of a person and the review fields.
    [Fact]
    public void Labels_FirstNameAndReviewFieldsFollowTheRules()
    {
        Assert.Equal("Visa ending in 4242", OnlinePaymentRules.CardLabel("visa", "4242"));
        Assert.Equal("Card ending in 4242", OnlinePaymentRules.CardLabel(null, "4242"));
        Assert.Equal("Online card", PaymentMethodCodes.Label(PaymentMethod.CardOnline));
        Assert.Equal("card_online", PaymentMethodCodes.Code(PaymentMethod.CardOnline));
        Assert.Equal("Cash", OnlinePaymentRules.MethodLabel(PaymentMethod.Cash, null, null));
        Assert.Equal("Carla", OnlinePaymentRules.FirstName("  Carla Customer ", isPerson: true));
        Assert.Null(OnlinePaymentRules.FirstName("Acme Corp", isPerson: false));

        var errors = new Dictionary<string, string[]>();
        Assert.Equal((4, "Great job"), OnlinePaymentRules.ValidateReview(4m, "  Great job ", errors));
        Assert.Equal((5, (string?)null), OnlinePaymentRules.ValidateReview(5m, "   ", errors));

        foreach (var (rating, comment, field) in new (decimal?, string?, string)[]
        {
            (null, null, "rating"), (0m, null, "rating"), (6m, null, "rating"), (3.5m, null, "rating"), (3m, new string('x', 501), "comment"),
        })
        {
            var invalid = new Dictionary<string, string[]>();
            Assert.Null(OnlinePaymentRules.ValidateReview(rating, comment, invalid));
            Assert.Equal(field, Assert.Single(invalid).Key);
        }

        Assert.Empty(errors);
    }

    // BR-19, BR-20: the receipt and completion report documents carry the approved content only, with the approved file names.
    [Fact]
    public void PdfComposers_UseTheApprovedContentAndFileNames()
    {
        var source = new ReceiptSource(
            "Acme", ["1 Main St", "+1 555 0100"], null, null, "RCT-1042-01", "PAY-7", new DateOnly(2026, 10, 9), ["Carla Customer", "9 Bill Rd"],
            "INV-1042", "Visa ending in 4242", "USD", 357.28m, 100m, 357.28m, 257.28m, 100m);
        var receipt = ReceiptPdfDocumentComposer.Compose(source);

        Assert.Equal("RCT-1042-01.pdf", ReceiptPdfDocumentComposer.FileName(source));
        Assert.Equal(("RECEIPT", "Thank you for your payment!"), (receipt.Heading, receipt.ThankYou));
        Assert.Equal(
            [("Receipt number", "RCT-1042-01"), ("Payment number", "PAY-7"), ("Paid on", "Oct 9, 2026"), ("Invoice number", "INV-1042"), ("Payment method", "Visa ending in 4242")],
            receipt.Meta.ToArray());
        Assert.Equal(
            ["Amount", "Refunded", "Net amount", "Invoice total", "Total paid", "Balance"],
            receipt.Amounts.Select(row => row.Label).ToArray());
        Assert.Equal(("−100.00 USD", "257.28 USD"), (receipt.Amounts[1].Value, receipt.Amounts[2].Value));
        Assert.Equal(
            ["Amount", "Invoice total", "Total paid", "Balance"],
            ReceiptPdfDocumentComposer.Compose(source with { RefundedAmount = 0m }).Amounts.Select(row => row.Label).ToArray());

        var reportSource = new CompletionReportSource(
            "Acme", [], null, null, "WO-12", "Fix drain", "1 Seed St, Austin", new DateOnly(2026, 10, 9), "Tina Tech", "All done",
            [new CompletionChecklistItem("Shut off water", true), new CompletionChecklistItem("Clean up", false)],
            "Signature obtained", "Carla Customer", new DateOnly(2026, 10, 9));
        var report = CompletionReportPdfDocumentComposer.Compose(reportSource);

        Assert.Equal("WO-12-completion-report.pdf", CompletionReportPdfDocumentComposer.FileName(reportSource));
        Assert.Equal(
            [("Shut off water", "Completed"), ("Clean up", "Not completed")],
            report.Checklist.Select(item => (item.Label, item.State)).ToArray());
        Assert.Equal(
            [("Work order", "WO-12"), ("Service", "Fix drain"), ("Completed on", "Oct 9, 2026"), ("Technician", "Tina Tech")],
            report.Meta.ToArray());
        Assert.Equal(
            [("Acknowledgement", "Signature obtained"), ("Signer", "Carla Customer"), ("Signed on", "Oct 9, 2026")],
            report.SignOff.ToArray());
        Assert.Empty(CompletionReportPdfDocumentComposer.Compose(reportSource with { AcknowledgementLabel = null, SignerName = null, SignedOn = null }).SignOff);
    }

    // BR-09: only a signed payload inside the tolerance is accepted; the four handled types are reduced to events, others are unhandled.
    [Fact]
    public void WebhookParser_VerifiesTheSignatureAndReducesHandledEvents()
    {
        var attempt = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        var succeeded = Event("evt_1", "payment_intent.succeeded", Intent(35728, attempt, organization, invoice));
        var failed = Event(
            "evt_2",
            "payment_intent.payment_failed",
            Intent(1000, attempt, organization, invoice, error: ""","last_payment_error":{"type":"card_error","code":"card_declined","decline_code":"insufficient_funds"}"""));
        var refunded = Event("evt_3", "charge.refunded", Charge(35728, 10000));
        var other = Event("evt_4", "customer.created", """{"id":"cus_1","object":"customer"}""");

        var handled = Assert.IsType<GatewayWebhookParse.Handled>(Parse(succeeded, Sign(succeeded)));
        Assert.Equal(("evt_1", "payment_intent.succeeded", "pi_1", 357.28m, "USD"), (handled.Event.EventId, handled.Event.Type, handled.Event.IntentId, handled.Event.Amount, handled.Event.Currency));
        Assert.Equal((attempt, organization, invoice), (handled.Event.AttemptId, handled.Event.OrganizationId, handled.Event.InvoiceId));

        var decline = Assert.IsType<GatewayWebhookParse.Handled>(Parse(failed, Sign(failed))).Event;
        Assert.Equal("insufficient_funds", decline.FailureCode);

        var refund = Assert.IsType<GatewayWebhookParse.Handled>(Parse(refunded, Sign(refunded))).Event;
        Assert.Equal((357.28m, 100.00m, "pi_1"), (refund.Amount, refund.CumulativeRefunded, refund.IntentId));

        Assert.IsType<GatewayWebhookParse.Unhandled>(Parse(other, Sign(other)));

        // Missing, malformed, foreign-secret, tampered and stale signatures are all the same invalid result.
        Assert.IsType<GatewayWebhookParse.Invalid>(Parse(succeeded, null));
        Assert.IsType<GatewayWebhookParse.Invalid>(Parse(succeeded, "garbage"));
        Assert.IsType<GatewayWebhookParse.Invalid>(Parse(succeeded, Sign(succeeded, secret: "whsec_other")));
        Assert.IsType<GatewayWebhookParse.Invalid>(Parse(succeeded + " ", Sign(succeeded)));
        Assert.IsType<GatewayWebhookParse.Invalid>(Parse(succeeded, Sign(succeeded, at: DateTimeOffset.UtcNow.AddMinutes(-10))));
        Assert.IsType<GatewayWebhookParse.Invalid>(StripeWebhookParser.Parse(succeeded, Sign(succeeded), null));
    }

    // BR-04, BR-24: absent keys only disable features; a wrong prefix or a malformed key fails start without echoing the value.
    [Fact]
    public void Settings_AllowAbsentKeysAndRejectMalformedOnes()
    {
        var stripe = new StripeSettingsValidator();

        Assert.True(stripe.Validate(null, new StripeSettings()).Succeeded);
        Assert.True(stripe.Validate(null, new StripeSettings { SecretKey = "sk_test_x" }).Succeeded);
        Assert.True(stripe.Validate(null, new StripeSettings { SecretKey = "rk_test_x", PublishableKey = "pk_test_x", WebhookSecret = "whsec_x" }).Succeeded);
        Assert.False(new StripeSettings { SecretKey = "sk_test_x" }.CardAvailable);
        Assert.True(new StripeSettings { SecretKey = "sk_a", PublishableKey = "pk_a", WebhookSecret = "whsec_a" }.CardAvailable);

        var wrong = stripe.Validate(null, new StripeSettings { SecretKey = "pk_oops", PublishableKey = "sk_oops", WebhookSecret = "secret-value" });
        Assert.True(wrong.Failed);
        Assert.Equal(3, wrong.Failures!.Count());
        Assert.DoesNotContain(wrong.Failures!, failure => failure.Contains("oops", StringComparison.Ordinal) || failure.Contains("secret-value", StringComparison.Ordinal));

        var bank = new BankDetailsSettingsValidator();
        Assert.True(bank.Validate(null, new BankDetailsSettings()).Succeeded);
        Assert.True(bank.Validate(null, new BankDetailsSettings { EncryptionKey = Convert.ToBase64String(new byte[32]) }).Succeeded);
        Assert.True(bank.Validate(null, new BankDetailsSettings { EncryptionKey = Convert.ToBase64String(new byte[16]) }).Failed);
        Assert.True(bank.Validate(null, new BankDetailsSettings { EncryptionKey = "not base64!" }).Failed);
    }

    // BR-23: card intent 10, bank notice 3 and review 3 per invoice and hour, counted per action and invoice, then released by the window.
    [Fact]
    public void InvoiceActionThrottle_LimitsPerActionAndInvoiceWithinTheHour()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var throttle = new InMemoryInvoiceActionThrottle(time);
        var invoice = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(throttle.TryAcquire(InvoiceAction.Review, invoice));
        }

        var retry = throttle.TryAcquire(InvoiceAction.Review, invoice);
        Assert.NotNull(retry);
        Assert.Equal(TimeSpan.FromHours(1), retry);
        Assert.Null(throttle.TryAcquire(InvoiceAction.Review, Guid.NewGuid()));
        Assert.Null(throttle.TryAcquire(InvoiceAction.BankTransferNotice, invoice));

        for (var i = 0; i < 10; i++)
        {
            Assert.Null(throttle.TryAcquire(InvoiceAction.CardIntent, invoice));
        }

        Assert.NotNull(throttle.TryAcquire(InvoiceAction.CardIntent, invoice));

        time.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(throttle.TryAcquire(InvoiceAction.Review, invoice));
        Assert.Null(throttle.TryAcquire(InvoiceAction.CardIntent, invoice));
    }

    private static GatewayWebhookParse Parse(string payload, string? signature) => StripeWebhookParser.Parse(payload, signature, Secret);

    private static string Event(string id, string type, string payloadObject) =>
        $$$"""{"id":"{{{id}}}","object":"event","api_version":"2020-08-27","created":1700000000,"livemode":false,"type":"{{{type}}}","data":{"object":{{{payloadObject}}}}}""";

    private static string Intent(long amount, Guid attempt, Guid organization, Guid invoice, string error = "") =>
        $$$"""{"id":"pi_1","object":"payment_intent","amount":{{{amount}}},"currency":"usd","status":"succeeded","metadata":{"attemptId":"{{{attempt}}}","organizationId":"{{{organization}}}","invoiceId":"{{{invoice}}}"}{{{error}}}}""";

    private static string Charge(long amount, long refunded) =>
        $$$"""{"id":"ch_1","object":"charge","amount":{{{amount}}},"amount_refunded":{{{refunded}}},"currency":"usd","payment_intent":"pi_1","metadata":{}}""";

    private static string Sign(string payload, string secret = Secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }
}
