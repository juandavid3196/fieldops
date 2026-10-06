using System.Globalization;
using FieldOps.Application.Features.Quotes;

namespace FieldOps.UnitTests.Quotes;

/// <summary>quote-builder AC-09 to AC-12: rounding, discount allocation, optional exclusion, tax label, margin and overflow.</summary>
public class QuoteCalculatorTests
{
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static QuoteLineInput Line(
        decimal quantity, decimal price, bool taxable = true, bool optional = false, decimal cost = 0m) =>
        new(taxable, optional, quantity, price, cost);

    private static QuoteCalculation Calculate(decimal discount, decimal rate, params QuoteLineInput[] lines)
    {
        var result = QuoteCalculator.Calculate(lines, discount, rate, "USD");

        Assert.True(result.IsValid, result.Error);

        return result.Value!;
    }

    [Fact]
    public void Calculate_DesignSample_RoundsEachLineAndLabelsTheRate()
    {
        // AC-09, plus the optional 120.00 line of AC-11 that must not change any total.
        var calculation = Calculate(
            0m,
            8.25m,
            Line(2, 95m),
            Line(1, 38m),
            Line(1, 42m),
            Line(1, 65m, taxable: false),
            Line(1, 120m, optional: true));

        Assert.Equal([190.00m, 38.00m, 42.00m, 65.00m, 120.00m], calculation.Lines.Select(line => line.LineSubtotal));
        Assert.Equal([15.68m, 3.14m, 3.47m, 0.00m, 9.90m], calculation.Lines.Select(line => line.LineTax));
        Assert.Equal([8.25m, 8.25m, 8.25m, 0m, 8.25m], calculation.Lines.Select(line => line.TaxRate));
        Assert.Equal(335.00m, calculation.Subtotal);
        Assert.Equal(22.29m, calculation.TaxTotal);
        Assert.Equal(357.29m, calculation.Total);
        Assert.Equal("Tax (8.25%)", calculation.TaxLabel);
        Assert.Equal(2, decimal.GetBits(calculation.Total)[3] >> 16);
    }

    [Theory]
    [InlineData("10.00", "5.47", "2.72", "1.81")]
    [InlineData("0", "0", "0", "0")]
    [InlineData("183.33", "100.00", "50.00", "33.33")]
    public void Calculate_Discount_FloorsSharesAndGivesRemainingCentsToTheLargestLine(
        string discount, string first, string second, string third)
    {
        var calculation = Calculate(D(discount), 10m, Line(1, 100m), Line(1, 50m), Line(1, 33.33m));

        Assert.Equal(
            [D(first), D(second), D(third)],
            calculation.Lines.Select(line => line.DiscountShare));
        Assert.Equal(D(discount), calculation.Lines.Sum(line => line.DiscountShare));
        Assert.Equal(183.33m - D(discount) + calculation.TaxTotal, calculation.Total);

        // Tax per line on the discounted base (10 % of 94.53 = 9.45).
        if (discount == "10.00")
        {
            Assert.Equal(9.45m, calculation.Lines[0].LineTax);
            Assert.Equal(173.33m, calculation.Subtotal - calculation.DiscountTotal);
        }

        if (discount == "183.33")
        {
            Assert.Equal(0m, calculation.TaxTotal);
        }
    }

    [Fact]
    public void Calculate_DiscountTies_GoToTheFirstLargestLine()
    {
        var calculation = Calculate(0.01m, 0m, Line(1, 1m), Line(1, 1m), Line(1, 1m));

        Assert.Equal([0.01m, 0m, 0m], calculation.Lines.Select(line => line.DiscountShare));
    }

    [Theory]
    [InlineData("183.34")]
    [InlineData("-0.01")]
    [InlineData("1.005")]
    public void Calculate_InvalidDiscount_Fails(string discount)
    {
        var result = QuoteCalculator.Calculate([Line(1, 100m), Line(1, 50m), Line(1, 33.33m)], D(discount), 10m, "USD");

        Assert.False(result.IsValid);
        Assert.Equal("discountTotal", result.ErrorKey);
        Assert.Equal(
            discount == "183.34" ? "Discount can't exceed the subtotal." : "Enter a discount of 0 or more.",
            result.Error);
    }

    [Fact]
    public void Calculate_AnyDiscountWithoutRegularLines_Fails()
    {
        Assert.False(QuoteCalculator.Calculate([Line(1, 10m, optional: true)], 0.01m, 10m, "USD").IsValid);
        Assert.True(QuoteCalculator.Calculate([Line(1, 10m, optional: true)], 0m, 10m, "USD").IsValid);
    }

    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("0.004", "0.00")]
    [InlineData("1.115", "1.12")]
    public void Calculate_RoundsHalfAwayFromZero(string quantity, string subtotal)
    {
        var calculation = Calculate(0m, 0m, Line(D(quantity), 1m));

        Assert.Equal(D(subtotal), calculation.Lines[0].LineSubtotal);
    }

    [Fact]
    public void Calculate_MarginExcludesOptionalLinesAndHandlesZeroRevenue()
    {
        // AC-12: (173.33 - 60.00) / 173.33 * 100 = 65.4 %, gross profit 113.33.
        var calculation = Calculate(
            10m,
            10m,
            Line(1, 100m, cost: 40m),
            Line(1, 50m, cost: 20m),
            Line(1, 33.33m),
            Line(1, 80m, optional: true, cost: 500m));

        Assert.Equal(65.4m, calculation.Margin.Percent);
        Assert.Equal(113.33m, calculation.Margin.GrossProfit);

        var free = Calculate(0m, 10m, Line(1, 0m, cost: 5m));

        Assert.Null(free.Margin.Percent);
        Assert.Equal(-5m, free.Margin.GrossProfit);
    }

    [Fact]
    public void TaxLabel_IsPlainWithoutOneUniformRate()
    {
        Assert.Equal("Tax", QuoteCalculator.TaxLabel([]));
        Assert.Equal("Tax", QuoteCalculator.TaxLabel([8.25m, 5m]));
        Assert.Equal("Tax (10%)", QuoteCalculator.TaxLabel([10.0000m]));
        Assert.Equal("Tax (7.5%)", QuoteCalculator.TaxLabel([7.5m, 7.50m]));
    }

    [Theory]
    [InlineData(999_999.999, 999_999_999.99)]
    [InlineData(1_000_000, 999_999_999.99)]
    public void Calculate_AmountsAboveTheColumnPrecision_AreRejected(double quantity, double price)
    {
        // Each line fits the field limits but not numeric(14,2): a 400, never a database error.
        var result = QuoteCalculator.Calculate([Line((decimal)quantity, (decimal)price)], 0m, 8.25m, "USD");

        Assert.False(result.IsValid);
        Assert.Equal("lines", result.ErrorKey);
        Assert.Equal("This quote total is too large.", result.Error);

        var sum = QuoteCalculator.Calculate(
            [Line(1, 600_000_000_000m, taxable: false), Line(1, 400_000_000_000m, taxable: false)], 0m, 0m, "USD");

        Assert.Equal("This quote total is too large.", sum.Error);
        Assert.True(QuoteCalculator.Calculate([Line(1, 999_999_999_999.99m, taxable: false)], 0m, 0m, "USD").IsValid);
    }
}
