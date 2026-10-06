using System.Globalization;
using FieldOps.Application.Features.QuoteLinks;

namespace FieldOps.UnitTests.QuoteLinks;

public class QuoteLinkTotalsTests
{
    // Frozen version: 190.00 subtotal, 10.00 discount, 14.85 tax (8.25 % on 180.00) = 194.85.
    private static readonly FrozenQuoteTotals Version = new(190m, 10m, 14.85m, "USD", [8.25m]);

    private static readonly FrozenOptionalLine Taxable = new(8.25m, 120m, 9.90m);

    private static readonly FrozenOptionalLine Untaxed = new(0m, 40m, 0m);

    private static readonly FrozenOptionalLine OtherRate = new(5m, 100m, 5m);

    // AC-08: none, one and all options; the discount never changes; the label follows the frozen rates.
    [Theory]
    [InlineData("none", "190.00", "10.00", "14.85", "194.85", "Tax (8.25%)")]
    [InlineData("taxable", "310.00", "10.00", "24.75", "324.75", "Tax (8.25%)")]
    [InlineData("untaxed", "230.00", "10.00", "14.85", "234.85", "Tax (8.25%)")]
    [InlineData("both", "350.00", "10.00", "24.75", "364.75", "Tax (8.25%)")]
    [InlineData("other-rate", "290.00", "10.00", "19.85", "299.85", "Tax")]
    public void WithOptions_SumsFrozenLineValuesOnly(string selection, string subtotal, string discount, string tax, string total, string label)
    {
        IReadOnlyList<FrozenOptionalLine> selected = selection switch
        {
            "taxable" => [Taxable],
            "untaxed" => [Untaxed],
            "both" => [Taxable, Untaxed],
            "other-rate" => [OtherRate],
            _ => [],
        };

        var totals = QuoteLinkTotals.WithOptions(Version, selected);

        Assert.NotNull(totals);
        Assert.Equal(
            (subtotal, discount, tax, total, label),
            (Text(totals.Subtotal), Text(totals.DiscountTotal), Text(totals.TaxTotal), Text(totals.Total), totals.TaxLabel));
        Assert.Equal("USD", totals.Currency);
    }

    [Fact]
    public void WithOptions_WhenAFigureDoesNotFitTheColumn_ReturnsNull()
    {
        var huge = new FrozenQuoteTotals(999_999_999_999.99m, 0m, 0m, "USD", []);

        Assert.Null(QuoteLinkTotals.WithOptions(huge, [new FrozenOptionalLine(0m, 0.01m, 0m)]));
    }

    [Fact]
    public void IsWellFormed_AcceptsOnlyFortyThreeBase64UrlCharacters()
    {
        Assert.True(QuoteLinkTokens.IsWellFormed(new string('A', 43)));
        Assert.True(QuoteLinkTokens.IsWellFormed(string.Concat(Enumerable.Repeat("a-_Z9", 8)) + "a-_"));

        foreach (var invalid in new[] { null, "", "short", new string('A', 42), new string('A', 44), new string('A', 42) + "=", new string('A', 42) + "+", new string('A', 42) + "/" })
        {
            Assert.False(QuoteLinkTokens.IsWellFormed(invalid));
        }
    }

    private static string Text(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
