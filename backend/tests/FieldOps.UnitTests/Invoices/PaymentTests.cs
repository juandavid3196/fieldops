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

        var payment = Payment.Create(
            organizationId,
            customerId,
            1,
            PaymentMethod.BankTransfer,
            250m,
            " USD ",
            paidAt);

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(organizationId, payment.OrganizationId);
        Assert.Equal(customerId, payment.CustomerId);
        Assert.Equal(1, payment.PaymentNumber);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(250m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal(paidAt, payment.PaidAt);
        Assert.Null(payment.RecordedByUserId);
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
                DateTimeOffset.UtcNow));
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
                DateTimeOffset.UtcNow));
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
                DateTimeOffset.UtcNow));
    }
}
