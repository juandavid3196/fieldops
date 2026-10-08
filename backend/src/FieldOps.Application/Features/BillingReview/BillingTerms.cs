using FieldOps.Application.Features.Quotes;
using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.BillingReview;

/// <summary>Invoice payment terms: the mapping from the frozen quote text, labels and the due date (BR-14).</summary>
public static class BillingTerms
{
    public static string FromQuoteTerms(string? text)
    {
        if (string.Equals(text, QuoteTerms.PresetText(QuoteTerms.Net15), StringComparison.Ordinal))
        {
            return PaymentTermsCodes.Net15;
        }

        return string.Equals(text, QuoteTerms.PresetText(QuoteTerms.Net30), StringComparison.Ordinal)
            ? PaymentTermsCodes.Net30
            : PaymentTermsCodes.DueUponReceipt;
    }

    public static bool IsValid(string? code) => code is not null && PaymentTermsCodes.All.Contains(code);

    public static int Days(string terms) => terms switch
    {
        PaymentTermsCodes.Net15 => 15,
        PaymentTermsCodes.Net30 => 30,
        _ => 0,
    };

    public static DateOnly DueDate(DateOnly issueDate, string terms) => issueDate.AddDays(Days(terms));

    public static string Label(string terms) => terms switch
    {
        PaymentTermsCodes.Net15 => "Net 15",
        PaymentTermsCodes.Net30 => "Net 30",
        _ => "Due upon receipt",
    };
}
