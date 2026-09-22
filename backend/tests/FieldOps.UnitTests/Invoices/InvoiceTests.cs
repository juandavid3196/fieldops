using FieldOps.Domain.Invoices;

namespace FieldOps.UnitTests.Invoices;

public class InvoiceTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var invoice = Invoice.Create(
            organizationId,
            branchId,
            1,
            workOrderId,
            customerId,
            " USD ",
            100m,
            10m,
            110m,
            110m,
            createdByUserId);

        Assert.NotEqual(Guid.Empty, invoice.Id);
        Assert.Equal(organizationId, invoice.OrganizationId);
        Assert.Equal(branchId, invoice.BranchId);
        Assert.Equal(1, invoice.InvoiceNumber);
        Assert.Equal(workOrderId, invoice.WorkOrderId);
        Assert.Equal(customerId, invoice.CustomerId);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(0m, invoice.AmountPaid);
        Assert.Equal(110m, invoice.BalanceDue);
        Assert.Null(invoice.IssueDate);
        Assert.Null(invoice.DueDate);
    }

    [Fact]
    public void Create_WithNonPositiveInvoiceNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Invoice.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "USD",
                100m,
                10m,
                110m,
                110m,
                Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Create_WithNegativeAmount_Throws(
        decimal subtotal,
        decimal taxTotal,
        decimal total,
        decimal balanceDue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Invoice.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "USD",
                subtotal,
                taxTotal,
                total,
                balanceDue,
                Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithDueDateBeforeIssueDate_Throws()
    {
        var issueDate = new DateOnly(2026, 1, 10);
        var dueDate = new DateOnly(2026, 1, 1);

        Assert.Throws<ArgumentException>(
            () => Invoice.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "USD",
                100m,
                10m,
                110m,
                110m,
                Guid.NewGuid(),
                issueDate,
                dueDate));
    }

    [Fact]
    public void Create_WithBlankCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Invoice.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "   ",
                100m,
                10m,
                110m,
                110m,
                Guid.NewGuid()));
    }
}
