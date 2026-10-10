using System.Globalization;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.OnlinePayments;

/// <summary>Pure rules of the customer payment flow: receipt numbers, field validation and the public labels (BR-05, BR-11, BR-22).</summary>
public static class OnlinePaymentRules
{
    public const string ReceiptPrefix = "RCT";

    /// <summary><c>RCT-&lt;invoice number&gt;-&lt;sequence&gt;</c> with a sequence of at least two digits (BR-11 b).</summary>
    public static string ReceiptNumber(long invoiceNumber, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{ReceiptPrefix}-{invoiceNumber}-{sequence:D2}");

    /// <summary>The label of an online card payment (BR-05): brand capitalized, or "Card" when unknown.</summary>
    public static string CardLabel(string? brand, string? last4)
    {
        var name = string.IsNullOrWhiteSpace(brand) ? "Card" : Capitalize(brand.Trim().Replace('_', ' '));

        return string.IsNullOrWhiteSpace(last4) ? name : $"{name} ending in {last4}";
    }

    /// <summary>The method label of a payment (BR-05): card brand and last four for online cards, else the hub label.</summary>
    public static string MethodLabel(PaymentMethod method, string? brand, string? last4) =>
        method == PaymentMethod.CardOnline ? CardLabel(brand, last4) : PaymentMethodCodes.Label(method);

    public static string PaymentStatusCode(PaymentStatus status) => status switch
    {
        PaymentStatus.PartiallyRefunded => "partially_refunded",
        PaymentStatus.Refunded => "refunded",
        _ => "succeeded",
    };

    public static string AttemptStatusCode(PaymentAttemptStatus status) => status switch
    {
        PaymentAttemptStatus.Succeeded => "succeeded",
        PaymentAttemptStatus.Failed => "failed",
        PaymentAttemptStatus.PartiallyRefunded => "partially_refunded",
        PaymentAttemptStatus.Refunded => "refunded",
        _ => "pending",
    };

    /// <summary>Parses the client key of an idempotent request; a missing, malformed or empty key is one field error.</summary>
    public static bool TryParseKey(string? text, out Guid key) =>
        Guid.TryParse(text?.Trim(), out key) && key != Guid.Empty;

    /// <summary>The first word of the Bill to name of a person customer (BR-03); null for companies or an empty name.</summary>
    public static string? FirstName(string? billToName, bool isPerson)
    {
        if (!isPerson || string.IsNullOrWhiteSpace(billToName))
        {
            return null;
        }

        return billToName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    /// <summary>Rating (integer 1 to 5) and comment (trimmed, at most 500 characters) of a review (BR-22).</summary>
    public static (int Rating, string? Comment)? ValidateReview(decimal? rating, string? comment, Dictionary<string, string[]> errors)
    {
        var valid = rating is { } value && value == decimal.Truncate(value) && value is >= 1 and <= 5;

        if (!valid)
        {
            errors["rating"] = [OnlinePaymentMessages.RatingInvalid];
        }

        var trimmed = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (trimmed is { Length: > OnlinePaymentMessages.MaxCommentLength })
        {
            errors["comment"] = [OnlinePaymentMessages.CommentTooLong];
        }

        return errors.Count > 0 ? null : ((int)rating!.Value, trimmed);
    }

    private static string Capitalize(string value) =>
        string.Concat(char.ToUpperInvariant(value[0]), value.AsSpan(1).ToString().ToLowerInvariant());
}
