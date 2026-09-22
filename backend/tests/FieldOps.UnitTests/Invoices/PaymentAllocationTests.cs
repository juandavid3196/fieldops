using FieldOps.Domain.Invoices;

namespace FieldOps.UnitTests.Invoices;

public class PaymentAllocationTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var paymentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var allocation = PaymentAllocation.Create(paymentId, invoiceId, 50m);

        Assert.NotEqual(Guid.Empty, allocation.Id);
        Assert.Equal(paymentId, allocation.PaymentId);
        Assert.Equal(invoiceId, allocation.InvoiceId);
        Assert.Equal(50m, allocation.Amount);
    }

    [Fact]
    public void Create_WithNonPositiveAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PaymentAllocation.Create(Guid.NewGuid(), Guid.NewGuid(), 0m));
    }

    [Fact]
    public void Create_WithEmptyInvoiceId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PaymentAllocation.Create(Guid.NewGuid(), Guid.Empty, 50m));
    }
}
