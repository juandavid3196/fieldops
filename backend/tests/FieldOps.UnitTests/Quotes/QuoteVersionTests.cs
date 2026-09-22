using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteVersionTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var version = QuoteVersion.Create(
            organizationId,
            quoteId,
            1,
            " Replace unit and repipe ",
            100m,
            10m,
            110m,
            " USD ",
            createdByUserId);

        Assert.NotEqual(Guid.Empty, version.Id);
        Assert.Equal(organizationId, version.OrganizationId);
        Assert.Equal(quoteId, version.QuoteId);
        Assert.Equal(1, version.VersionNo);
        Assert.Equal("Replace unit and repipe", version.Scope);
        Assert.Equal(100m, version.Subtotal);
        Assert.Equal(10m, version.TaxTotal);
        Assert.Equal(110m, version.Total);
        Assert.Equal("USD", version.Currency);
        Assert.False(version.IsImmutable);
        Assert.Null(version.SentAt);
        Assert.Null(version.ValidUntil);
    }

    [Fact]
    public void Create_WithNonPositiveVersionNo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuoteVersion.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                "Scope",
                100m,
                10m,
                110m,
                "USD",
                Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Create_WithNegativeAmount_Throws(decimal subtotal, decimal taxTotal, decimal total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuoteVersion.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                "Scope",
                subtotal,
                taxTotal,
                total,
                "USD",
                Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithBlankScope_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => QuoteVersion.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                "  ",
                100m,
                10m,
                110m,
                "USD",
                Guid.NewGuid()));
    }
}
