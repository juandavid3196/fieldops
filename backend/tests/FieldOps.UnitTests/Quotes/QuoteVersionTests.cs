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

    [Fact]
    public void SentVersion_RejectsEveryMutation()
    {
        var version = QuoteVersion.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Scope", 100m, 10m, 110m, "USD", Guid.NewGuid());
        version.ReplaceDraft("Hello", null, "Payment due upon completion.", new DateOnly(2030, 1, 1), 0m, 100m, 10m, 110m, "USD");
        version.Freeze(DateTimeOffset.UtcNow);

        Assert.True(version.IsImmutable);
        Assert.NotNull(version.SentAt);
        Assert.Throws<InvalidOperationException>(
            () => version.ReplaceDraft(null, null, "Terms", new DateOnly(2030, 1, 1), 0m, 1m, 0m, 1m, "USD"));
        Assert.Throws<InvalidOperationException>(() => version.Freeze(DateTimeOffset.UtcNow));

        var revision = version.CreateRevision(2, Guid.NewGuid(), new DateOnly(2030, 2, 1), "USD", 100m, 10m, 110m);

        Assert.False(revision.IsImmutable);
        Assert.Equal(2, revision.VersionNo);
        Assert.Equal("Scope", revision.Scope);
        Assert.Equal("Hello", revision.CustomerNotes);
        Assert.Equal("Payment due upon completion.", revision.Terms);
        Assert.Equal(new DateOnly(2030, 2, 1), revision.ValidUntil);
    }
}
