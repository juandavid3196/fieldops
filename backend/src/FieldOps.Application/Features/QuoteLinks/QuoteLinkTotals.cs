using FieldOps.Application.Features.Quotes;

namespace FieldOps.Application.Features.QuoteLinks;

/// <summary>The frozen figures of a sent version: the stored totals and the tax rates of its non-optional lines.</summary>
public sealed record FrozenQuoteTotals(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    string Currency,
    IReadOnlyList<decimal> NonOptionalTaxRates);

/// <summary>An optional line the customer selected, with its frozen rate and amounts.</summary>
public sealed record FrozenOptionalLine(decimal TaxRate, decimal LineSubtotal, decimal LineTax);

/// <summary>Option totals of a sent version (customer-quote-approval BR-11): frozen values only, never the calculator, the catalog or the settings.</summary>
public static class QuoteLinkTotals
{
    /// <summary>
    /// Version totals plus the selected optional lines. The discount is unchanged (optional lines have no discount share) and
    /// the tax label follows quote-builder BR-15 over the non-optional lines plus the selected taxable lines. Null when a
    /// figure would not fit a numeric(14,2) column.
    /// </summary>
    public static PublicTotals? WithOptions(FrozenQuoteTotals version, IReadOnlyList<FrozenOptionalLine> selected)
    {
        var subtotal = version.Subtotal + selected.Sum(line => line.LineSubtotal);
        var tax = version.TaxTotal + selected.Sum(line => line.LineTax);
        var total = subtotal - version.DiscountTotal + tax;

        if (subtotal > QuoteCalculator.MaxAmount || tax > QuoteCalculator.MaxAmount || total > QuoteCalculator.MaxAmount)
        {
            return null;
        }

        return new PublicTotals(
            QuoteCalculator.Money(subtotal),
            QuoteCalculator.Money(version.DiscountTotal),
            QuoteCalculator.TaxLabel(version.NonOptionalTaxRates.Concat(selected.Select(line => line.TaxRate))),
            QuoteCalculator.Money(tax),
            QuoteCalculator.Money(total),
            version.Currency);
    }
}

/// <summary>The shape of the public token (quote-builder BR-27): 32 bytes as 43 base64url characters.</summary>
public static class QuoteLinkTokens
{
    public const int Length = 43;

    public static bool IsWellFormed(string? token) =>
        token is { Length: Length } && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
