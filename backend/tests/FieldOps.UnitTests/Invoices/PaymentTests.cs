using FieldOps.Domain.Invoices;

namespace FieldOps.UnitTests.Invoices;

public class PaymentTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var paidAt = DateTimeOffset.UtcNow;
        var receivedBy = Guid.NewGuid();
        var recordedBy = Guid.NewGuid();
        var key = Guid.NewGuid();

        var payment = Payment.Create(
            organizationId,
            customerId,
            1,
            PaymentMethod.BankTransfer,
            250m,
            " USD ",
            paidAt,
            receivedBy,
            key,
            recordedBy,
            "CHK-1",
            "note");

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(organizationId, payment.OrganizationId);
        Assert.Equal(customerId, payment.CustomerId);
        Assert.Equal(1, payment.PaymentNumber);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(250m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal(paidAt, payment.PaidAt);
        Assert.Equal(recordedBy, payment.RecordedByUserId);
        Assert.Equal(receivedBy, payment.ReceivedByUserId);
        Assert.Equal(key, payment.IdempotencyKey);
        Assert.Equal("CHK-1", payment.ExternalReference);
        Assert.Equal("note", payment.Notes);
        Assert.Null(payment.ReceiptStorageKey);
    }

    [Fact]
    public void Create_WithNonPositiveAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Payment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                PaymentMethod.Cash,
                0m,
                "USD",
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithNonPositivePaymentNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Payment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                PaymentMethod.Cash,
                100m,
                "USD",
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyCustomerId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Payment.Create(
                Guid.NewGuid(),
                Guid.Empty,
                1,
                PaymentMethod.Cash,
                100m,
                "USD",
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                Guid.NewGuid()));
    }
}
