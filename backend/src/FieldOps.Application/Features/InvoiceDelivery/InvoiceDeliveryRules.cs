using System.Globalization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Customers;

namespace FieldOps.Application.Features.InvoiceDelivery;

/// <summary>Pure rules of the invoice delivery: field validation, defaults, readiness checks and changed fields (BR-06 to BR-09).</summary>
public static class InvoiceDeliveryRules
{
    public const string FieldPaymentTerms = "paymentTerms";

    public const string FieldRecipientEmail = "recipientEmail";

    public const string FieldMessage = "message";

    /// <summary>
    /// Validates the delivery fields (Delivery field rules). The recipient may be empty on save (stored as null) and is
    /// required on send. Returns null and fills <paramref name="errors"/> when a field is invalid.
    /// </summary>
    public static DeliveryInput? Validate(DeliveryBodyText body, bool recipientRequired, Dictionary<string, string[]> errors)
    {
        var terms = body.PaymentTerms?.Trim();

        if (!BillingTerms.IsValid(terms))
        {
            errors[FieldPaymentTerms] = [InvoiceDeliveryMessages.TermsInvalid];
        }

        var recipient = string.IsNullOrWhiteSpace(body.RecipientEmail) ? null : body.RecipientEmail.Trim();

        if (recipient is null)
        {
            if (recipientRequired)
            {
                errors[FieldRecipientEmail] = [InvoiceDeliveryMessages.RecipientRequired];
            }
        }
        else if (!CustomerNormalizer.IsValidEmail(recipient))
        {
            errors[FieldRecipientEmail] = [InvoiceDeliveryMessages.RecipientInvalid];
        }

        var message = body.Message?.Trim() ?? string.Empty;

        if (message.Length == 0)
        {
            errors[FieldMessage] = [InvoiceDeliveryMessages.MessageRequired];
        }
        else if (message.Length > InvoiceDeliveryMessages.MessageMaxLength)
        {
            errors[FieldMessage] = [InvoiceDeliveryMessages.MessageTooLong];
        }

        return errors.Count > 0 ? null : new DeliveryInput(terms!, recipient, message);
    }

    /// <summary>The due date is always the issue date plus the days of the terms (BR-08).</summary>
    public static DateOnly? DueDate(DateOnly? issueDate, string terms) =>
        issueDate is { } issued ? BillingTerms.DueDate(issued, terms) : null;

    /// <summary>The names of the delivery fields whose stored value differs from the input (BR-30 audit metadata).</summary>
    public static IReadOnlyList<string> ChangedFields(string? currentTerms, string? currentRecipient, string? currentMessage, DeliveryInput input)
    {
        var changed = new List<string>();

        if (!string.Equals(currentTerms, input.PaymentTerms, StringComparison.Ordinal))
        {
            changed.Add(FieldPaymentTerms);
        }

        if (!string.Equals(currentRecipient, input.RecipientEmail, StringComparison.Ordinal))
        {
            changed.Add(FieldRecipientEmail);
        }

        if (!string.Equals(currentMessage, input.Message, StringComparison.Ordinal))
        {
            changed.Add(FieldMessage);
        }

        return changed;
    }

    /// <summary>The template message of an invoice that has no stored one, truncated to the 500-character limit (BR-06).</summary>
    public static string DefaultMessage(
        string? contactFirstName, string customerName, string invoiceNumber, string workOrderTitle, string organizationName)
    {
        var greeting = !string.IsNullOrWhiteSpace(contactFirstName)
            ? contactFirstName.Trim()
            : !string.IsNullOrWhiteSpace(customerName) ? customerName.Trim() : "there";
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"Hi {greeting},\n\nHere's your invoice {invoiceNumber} for {workOrderTitle}. Thank you for choosing {organizationName}! If you have any questions, please don't hesitate to reach out.\n\nBest regards,\n{organizationName}");

        return text.Length <= InvoiceDeliveryMessages.MessageMaxLength ? text : text[..InvoiceDeliveryMessages.MessageMaxLength];
    }

    /// <summary>The readiness checks (BR-07): the effective recipient is a valid email, and the tax is always checked.</summary>
    public static IReadOnlyList<InvoiceCheck> Checks(string? effectiveRecipient, string taxLabel) =>
    [
        new InvoiceCheck("recipient", effectiveRecipient is not null && CustomerNormalizer.IsValidEmail(effectiveRecipient.Trim()), InvoiceDeliveryMessages.RecipientCheckLabel),
        new InvoiceCheck("tax", true, $"Tax checked ({taxLabel})"),
    ];

    /// <summary>Quantity without trailing zeros ("2.500" is "2.5", "3.000" is "3").</summary>
    public static string Quantity(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The PDF file name <c>&lt;INV-n&gt;.pdf</c> with only safe characters (BR-12).</summary>
    public static string SafeFileName(string number)
    {
        var safe = new string(number.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());

        return safe.Length == 0 ? "invoice.pdf" : safe + ".pdf";
    }
}
