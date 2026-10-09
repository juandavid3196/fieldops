using FieldOps.Domain.Invoices;

namespace FieldOps.UnitTests.OnlinePayments;

/// <summary>Online card payment invariants, refund deltas and the attempt state machine (customer-invoice-payments BR-11, BR-13, BR-15, SA-15).</summary>
public class PaymentDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    // SA-15: a card_online payment has no receiver and a last four; every other method needs a receiver and no online member.
    [Fact]
    public void CardOnlinePayment_HasNoReceiverAndKeepsOnlyBrandAndLastFour()
    {
        var payment = Payment.CreateOnline(Guid.NewGuid(), Guid.NewGuid(), 5, 357.28m, " USD ", Now, Guid.NewGuid(), "RCT-1042-01", " Visa ", "4242");

        Assert.Equal((PaymentMethod.CardOnline, PaymentStatus.Succeeded, 0m, 357.28m), (payment.Method, payment.Status, payment.RefundedAmount, payment.NetAmount));
        Assert.Null(payment.ReceivedByUserId);
        Assert.Null(payment.RecordedByUserId);
        Assert.Equal(("visa", "4242", "RCT-1042-01", "USD"), (payment.CardBrand, payment.CardLast4, payment.ReceiptNumber, payment.Currency));

        Assert.Throws<ArgumentException>(() => Payment.CreateOnline(Guid.NewGuid(), Guid.NewGuid(), 5, 1m, "USD", Now, Guid.NewGuid(), "RCT-1-01", "visa", "42"));
        Assert.Throws<ArgumentException>(() => Payment.CreateOnline(Guid.NewGuid(), Guid.NewGuid(), 5, 1m, "USD", Now, Guid.NewGuid(), " ", "visa", "4242"));
        Assert.Throws<ArgumentException>(() => Payment.Create(Guid.NewGuid(), Guid.NewGuid(), 5, PaymentMethod.CardOnline, 1m, "USD", Now, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => Payment.Create(Guid.NewGuid(), Guid.NewGuid(), 5, PaymentMethod.Cash, 1m, "USD", Now, null, Guid.NewGuid()));

        var external = Payment.Create(Guid.NewGuid(), Guid.NewGuid(), 6, PaymentMethod.Cash, 10m, "USD", Now, Guid.NewGuid(), Guid.NewGuid(), receiptNumber: " RCT-9-01 ");
        Assert.Equal("RCT-9-01", external.ReceiptNumber);
        Assert.Null(external.CardLast4);
    }

    // BR-15: a cumulative refund moves the payment by the new delta only; a replay or a smaller total changes nothing.
    [Fact]
    public void PaymentRefund_AppliesOnlyTheNewDelta()
    {
        var payment = Payment.CreateOnline(Guid.NewGuid(), Guid.NewGuid(), 5, 357.28m, "USD", Now, Guid.NewGuid(), "RCT-1042-01", "visa", "4242");

        Assert.Equal(100m, payment.ApplyRefund(100m));
        Assert.Equal((PaymentStatus.PartiallyRefunded, 100m, 257.28m), (payment.Status, payment.RefundedAmount, payment.NetAmount));
        Assert.Equal(0m, payment.ApplyRefund(100m));
        Assert.Equal(0m, payment.ApplyRefund(50m));
        Assert.Equal(257.28m, payment.ApplyRefund(357.28m));
        Assert.Equal((PaymentStatus.Refunded, 357.28m, 0m), (payment.Status, payment.RefundedAmount, payment.NetAmount));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.ApplyRefund(357.29m));
    }

    // BR-15: the invoice moves paid -> partially_paid -> sent with the amounts, and the status is recomputed from them.
    [Fact]
    public void InvoiceRefund_RecomputesTheStatusAndReopensAPaidInvoice()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1042, Guid.NewGuid(), Guid.NewGuid(), "USD", 357.28m, 0m, 357.28m, 357.28m, Guid.NewGuid(),
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), 0m, "due_upon_receipt");
        invoice.MarkSent("due_upon_receipt", new DateOnly(2026, 10, 1), "a@b.co", "Hi", "{}", Now);
        invoice.ApplyPayment(357.28m, Now.AddMinutes(1));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        invoice.ApplyRefund(100m, Now.AddMinutes(2));
        Assert.Equal((InvoiceStatus.PartiallyPaid, 257.28m, 100m), (invoice.Status, invoice.AmountPaid, invoice.BalanceDue));

        invoice.ApplyRefund(257.28m, Now.AddMinutes(3));
        Assert.Equal((InvoiceStatus.Sent, 0m, 357.28m), (invoice.Status, invoice.AmountPaid, invoice.BalanceDue));
        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.ApplyRefund(0.01m, Now.AddMinutes(4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.ApplyRefund(0m, Now.AddMinutes(4)));
    }

    // BR-06, BR-13, BR-14: one pending card attempt with a 30-minute expiry; failure keeps the lock rules, a late success still applies.
    [Fact]
    public void Attempt_FollowsPendingFailedSucceededAndRefundedTransitions()
    {
        var card = InvoicePaymentAttempt.CreateCard(Guid.NewGuid(), Guid.NewGuid(), 357.28m, "USD", Guid.NewGuid(), Now);
        var bank = InvoicePaymentAttempt.CreateBankTransfer(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", Guid.NewGuid(), Now);

        Assert.Equal((PaymentAttemptStatus.Pending, Now.AddMinutes(30), PaymentMethod.CardOnline), (card.Status, card.ExpiresAt, card.Method));
        Assert.Equal((PaymentAttemptStatus.Pending, (DateTimeOffset?)null, PaymentMethod.BankTransfer), (bank.Status, bank.ExpiresAt, bank.Method));
        Assert.Throws<ArgumentException>(() => card.MarkSucceeded(Guid.Empty, Now));

        card.AttachProviderIntent("pi_1", Now);
        card.AttachProviderIntent("pi_2", Now);
        Assert.Equal("pi_1", card.ProviderPaymentIntentId);

        card.RecordFailureCategory(PaymentFailureCategories.CardDeclined, Now);
        Assert.Equal(PaymentAttemptStatus.Pending, card.Status);
        Assert.True(card.MarkFailed(PaymentFailureCategories.Expired, Now.AddMinutes(31)));
        Assert.False(card.MarkFailed(PaymentFailureCategories.Canceled, Now.AddMinutes(32)));
        Assert.Equal((PaymentAttemptStatus.Failed, PaymentFailureCategories.Expired), (card.Status, card.FailureCategory));

        var payment = Guid.NewGuid();
        card.MarkSucceeded(payment, Now.AddMinutes(33));
        Assert.Equal((PaymentAttemptStatus.Succeeded, payment), (card.Status, card.PaymentId));
        Assert.Throws<InvalidOperationException>(() => card.MarkSucceeded(Guid.NewGuid(), Now));
        Assert.True(card.ApplyRefund(100m, Now));
        Assert.False(card.ApplyRefund(100m, Now));
        Assert.Equal(PaymentAttemptStatus.PartiallyRefunded, card.Status);
        Assert.True(card.ApplyRefund(357.28m, Now));
        Assert.Equal(PaymentAttemptStatus.Refunded, card.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoicePaymentAttempt.CreateCard(Guid.NewGuid(), Guid.NewGuid(), 0m, "USD", Guid.NewGuid(), Now));
    }

    // BR-22: the review keeps a trimmed comment, a null technician is allowed and the rating stays in range.
    [Fact]
    public void Review_NormalizesTheCommentAndValidatesTheRating()
    {
        var review = InvoiceReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 5, "  Great  ", Now);

        Assert.Equal(((short)5, "Great", (Guid?)null), (review.Rating, review.Comment, review.TechnicianId));
        Assert.Null(InvoiceReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 1, "  ", Now).Comment);
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 6, null, Now));
        Assert.Throws<ArgumentException>(() => InvoiceReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 3, new string('x', 501), Now));
    }
}
