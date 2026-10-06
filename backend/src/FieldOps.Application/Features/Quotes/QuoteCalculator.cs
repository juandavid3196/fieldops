using System.Globalization;

namespace FieldOps.Application.Features.Quotes;

/// <summary>One line as the calculator sees it; <see cref="Taxable"/> picks the organization rate or zero (BR-13).</summary>
public sealed record QuoteLineInput(bool Taxable, bool IsOptional, decimal Quantity, decimal UnitPrice, decimal UnitCost);

public sealed record CalculatedLine(decimal LineSubtotal, decimal DiscountShare, decimal TaxRate, decimal LineTax, decimal LineTotal);

public sealed record QuoteMargin(decimal? Percent, decimal GrossProfit);

public sealed record QuoteCalculation(
    IReadOnlyList<CalculatedLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    string TaxLabel,
    decimal TaxTotal,
    decimal Total,
    string Currency,
    QuoteMargin Margin);

/// <summary>A calculation or the single error (key and message) that stops it.</summary>
public sealed record QuoteCalculationResult(QuoteCalculation? Value, string? ErrorKey, string? Error)
{
    public bool IsValid => Value is not null;
}

/// <summary>
/// Pure quote arithmetic (quote-builder BR-13 to BR-16): decimal only, every rounding to 2 decimals half away
/// from zero, optional lines excluded from the totals.
/// </summary>
public static class QuoteCalculator
{
    /// <summary>The largest amount of a numeric(14,2) column.</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    public const string TotalTooLargeMessage = "This quote total is too large.";

    public const string DiscountTooLargeMessage = "Discount can't exceed the subtotal.";

    public const string DiscountInvalidMessage = "Enter a discount of 0 or more.";

    public static QuoteCalculationResult Calculate(
        IReadOnlyList<QuoteLineInput> lines, decimal discountTotal, decimal taxRate, string currency)
    {
        if (discountTotal < 0 || Round(discountTotal) != discountTotal)
        {
            return Fail("discountTotal", DiscountInvalidMessage);
        }

        var subtotals = new decimal[lines.Count];
        decimal subtotal = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            subtotals[index] = Round(lines[index].Quantity * lines[index].UnitPrice);

            if (subtotals[index] > MaxAmount)
            {
                return Fail("lines", TotalTooLargeMessage);
            }

            if (!lines[index].IsOptional)
            {
                subtotal += subtotals[index];
            }
        }

        if (subtotal > MaxAmount)
        {
            return Fail("lines", TotalTooLargeMessage);
        }

        if (discountTotal > subtotal)
        {
            return Fail("discountTotal", DiscountTooLargeMessage);
        }

        var shares = AllocateDiscount(lines, subtotals, subtotal, discountTotal);
        var result = new CalculatedLine[lines.Count];
        decimal taxTotal = 0m;
        decimal cost = 0m;

        for (var index = 0; index < lines.Count; index++)
        {
            var rate = lines[index].Taxable ? taxRate : 0m;
            var tax = Round((subtotals[index] - shares[index]) * rate / 100m);
            var total = subtotals[index] - shares[index] + tax;

            if (Math.Abs(tax) > MaxAmount || Math.Abs(total) > MaxAmount)
            {
                return Fail("lines", TotalTooLargeMessage);
            }

            result[index] = new CalculatedLine(Money(subtotals[index]), Money(shares[index]), rate, Money(tax), Money(total));

            if (!lines[index].IsOptional)
            {
                taxTotal += tax;
                cost += Round(lines[index].Quantity * lines[index].UnitCost);
            }
        }

        var grand = subtotal - discountTotal + taxTotal;

        if (taxTotal > MaxAmount || grand > MaxAmount)
        {
            return Fail("lines", TotalTooLargeMessage);
        }

        var label = TaxLabel(
            lines.Select((line, index) => (line, rate: result[index].TaxRate))
                .Where(item => !item.line.IsOptional && item.rate > 0m)
                .Select(item => item.rate));

        return new QuoteCalculationResult(
            new QuoteCalculation(
                result,
                Money(subtotal),
                Money(discountTotal),
                label,
                Money(taxTotal),
                Money(grand),
                currency,
                Margin(subtotal, discountTotal, cost)),
            null,
            null);
    }

    /// <summary>"Tax (8.25%)" when every taxable non-optional line has the same rate, otherwise "Tax" (BR-15).</summary>
    public static string TaxLabel(IEnumerable<decimal> taxableRates)
    {
        var distinct = taxableRates.Where(rate => rate > 0m).Distinct().ToList();

        return distinct.Count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"Tax ({distinct[0].ToString("0.####", CultureInfo.InvariantCulture)}%)")
            : "Tax";
    }

    /// <summary>Margin (BR-16): revenue is subtotal minus discount, cost the sum of the rounded quantity times unit cost.</summary>
    public static QuoteMargin Margin(decimal subtotal, decimal discountTotal, decimal cost)
    {
        var revenue = subtotal - discountTotal;
        var gross = revenue - cost;

        return new QuoteMargin(
            revenue == 0m ? null : Math.Round(gross / revenue * 100m, 1, MidpointRounding.AwayFromZero),
            Money(gross));
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Two decimals, so JSON money always has the same scale.</summary>
    public static decimal Money(decimal value) => Round(value) + 0.00m;

    // Floor shares per cent, then the remaining cents to the largest non-optional line (first on ties).
    private static decimal[] AllocateDiscount(
        IReadOnlyList<QuoteLineInput> lines, decimal[] subtotals, decimal subtotal, decimal discount)
    {
        var shares = new decimal[lines.Count];

        if (discount == 0m || subtotal == 0m)
        {
            return shares;
        }

        decimal allocated = 0m;
        var largest = -1;

        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].IsOptional)
            {
                continue;
            }

            shares[index] = Math.Floor(discount * subtotals[index] * 100m / subtotal) / 100m;
            allocated += shares[index];

            if (largest < 0 || subtotals[index] > subtotals[largest])
            {
                largest = index;
            }
        }

        shares[largest] += discount - allocated;

        return shares;
    }

    private static QuoteCalculationResult Fail(string key, string message) => new(null, key, message);
}
