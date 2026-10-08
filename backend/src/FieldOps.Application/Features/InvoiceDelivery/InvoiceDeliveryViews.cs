using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.QuoteLinks;

namespace FieldOps.Application.Features.InvoiceDelivery;

/// <summary>Machine codes, messages and limits of the invoice delivery (invoice-draft-delivery BR-08 to BR-21).</summary>
public static class InvoiceDeliveryMessages
{
    public const int MessageMaxLength = 500;

    public const int RecipientMaxLength = 254;

    public const int TokenLifetimeDays = 365;

    public const string ChangedCode = "invoice_changed";

    public const string ChangedTitle = "This invoice changed. Refresh to see the latest.";

    public const string NotDraftCode = "invoice_not_draft";

    public const string NotDraftTitle = "This invoice has already been sent and can't be edited.";

    public const string NotSentCode = "invoice_not_sent";

    public const string NotSentTitle = "Only sent invoices can be emailed again.";

    public const string UnavailableCode = "invoice_link_unavailable";

    public const string UnavailableTitle = "This link isn't available.";

    public const string TermsInvalid = "Select payment terms.";

    public const string RecipientInvalid = "Enter a valid email address.";

    public const string RecipientRequired = "Enter the customer's email address.";

    public const string MessageRequired = "Enter a message.";

    public const string MessageTooLong = "Message must be 500 characters or fewer.";

    public const string RecipientCheckLabel = "Recipient email added";

    public const string PdfContentType = "application/pdf";

    public const string EmailSent = "sent";

    public const string EmailFailed = "failed";

    public const string EmailNotSent = "not_sent";
}

/// <summary>The organization block of the preview (BR-04); each address line is omitted when empty.</summary>
public sealed record InvoiceOrganizationView(
    string Name, IReadOnlyList<string> AddressLines, string? Phone, string? Email, bool HasLogo);

/// <summary>Bill to (BR-04); also the customer part of the frozen snapshot (SA-09).</summary>
public sealed record InvoiceBillTo(string Name, string? Email, string? Phone, IReadOnlyList<string> AddressLines);

/// <summary>A preview line; <see cref="Quantity"/> has no trailing zeros and the unit is returned apart.</summary>
public sealed record InvoiceLineView(
    string Description, string? Detail, string Quantity, string Unit, decimal UnitPrice, decimal TaxRate, decimal Amount);

public sealed record InvoiceTotalsView(decimal Subtotal, decimal DiscountTotal, string TaxLabel, decimal TaxTotal, decimal Total);

/// <summary>The preview shared by the internal detail and the public invoice (API contracts, <c>InvoicePreview</c>).</summary>
public sealed record InvoicePreview(
    string Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string PaymentTerms,
    string Currency,
    string Timezone,
    InvoiceOrganizationView Organization,
    InvoiceBillTo BillTo,
    string? ServiceAddress,
    string WorkOrderNumber,
    IReadOnlyList<InvoiceLineView> Lines,
    InvoiceTotalsView Totals,
    string? CompletionNote);

/// <summary>The customer-facing content frozen when the invoice is sent (SA-09 shape).</summary>
public sealed record InvoiceCustomerSnapshot(InvoiceBillTo BillTo, string? ServiceAddress, string? CompletionNote);

public sealed record InvoiceDelivery(string? RecipientEmail, string? Message, bool Saved);

public sealed record InvoiceCheck(string Key, bool Met, string Label);

/// <summary>The internal invoice (API contracts, <c>InvoiceDetail</c> = preview + fields).</summary>
public sealed record InvoiceDetail(
    string Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string PaymentTerms,
    string Currency,
    string Timezone,
    InvoiceOrganizationView Organization,
    InvoiceBillTo BillTo,
    string? ServiceAddress,
    string WorkOrderNumber,
    IReadOnlyList<InvoiceLineView> Lines,
    InvoiceTotalsView Totals,
    string? CompletionNote,
    Guid Id,
    string Status,
    Guid WorkOrderId,
    string CustomerName,
    InvoiceDelivery Delivery,
    IReadOnlyList<InvoiceCheck> Checks,
    DateTimeOffset CreatedAt,
    string CreatedByName,
    DateTimeOffset? SentAt,
    bool CanAct,
    DateTimeOffset UpdatedAt);

/// <summary>The public invoice (BR-20): the preview and the status label, with no ids or internal data.</summary>
public sealed record PublicInvoice(
    string Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string PaymentTerms,
    string Currency,
    string Timezone,
    InvoiceOrganizationView Organization,
    InvoiceBillTo BillTo,
    string? ServiceAddress,
    string WorkOrderNumber,
    IReadOnlyList<InvoiceLineView> Lines,
    InvoiceTotalsView Totals,
    string? CompletionNote,
    string Status);

/// <summary>What the PDF is composed from: the preview and whether the invoice is still a draft (BR-12).</summary>
public sealed record InvoicePdfSource(InvoicePreview Preview, bool IsDraft, byte[]? Logo, string? LogoContentType);

public sealed record InvoicePdfFile(string FileName, byte[] Content);

/// <summary>Raw delivery body before validation (BR-08); everything is optional text so a missing value is a field error.</summary>
public sealed record DeliveryBodyText(string? PaymentTerms, string? RecipientEmail, string? Message, string? UpdatedAt);

/// <summary>Validated delivery values; the recipient is null when it was empty on save.</summary>
public sealed record DeliveryInput(string PaymentTerms, string? RecipientEmail, string Message);

/// <summary>The data of the delivery email (BR-16); the raw link is added by the handler.</summary>
public sealed record InvoiceEmailData(
    Guid InvoiceId,
    string RecipientEmail,
    string OrganizationName,
    string? OrganizationPhone,
    string DisplayNumber,
    decimal Total,
    string Currency,
    DateOnly? DueDate,
    string Message);

/// <summary>The email after the commit: the data and the link holding the raw token.</summary>
public sealed record InvoiceEmail(InvoiceEmailData Data, string Link);

public enum InvoiceEmailStatus
{
    Sent,
    Failed,
}

/// <summary>The store result of a send: no email when the invoice was already sent.</summary>
public sealed record InvoiceSent(bool Changed, InvoiceDetail Invoice, InvoiceEmailData? Email);

public sealed record InvoiceResent(InvoiceDetail Invoice, InvoiceEmailData Email);

public sealed record InvoiceSendResponse(bool Changed, string EmailStatus, InvoiceDetail Invoice);

public sealed record InvoiceResendResponse(string EmailStatus, InvoiceDetail Invoice);

/// <summary>
/// Persistence port of the internal invoice endpoints. Every call starts from the session organization and branch
/// scope; a missing, foreign, out-of-scope or void invoice is one identical "not found". Mutations lock the invoice row.
/// </summary>
public interface IInvoiceDeliveryStore
{
    Task<InvoiceDetail?> GetDetailAsync(BillingActor actor, Guid invoiceId, bool canAct, CancellationToken cancellationToken);

    Task<InvoicePdfSource?> GetPdfSourceAsync(BillingActor actor, Guid invoiceId, CancellationToken cancellationToken);

    Task<BillingOutcome<InvoiceDetail>> SaveDraftAsync(
        BillingActor actor, Guid invoiceId, DeliveryBodyText body, CancellationToken cancellationToken);

    Task<BillingOutcome<InvoiceSent>> SendAsync(
        BillingActor actor, Guid invoiceId, DeliveryBodyText body, string tokenHash, CancellationToken cancellationToken);

    Task<BillingOutcome<InvoiceResent>> ResendAsync(
        BillingActor actor, Guid invoiceId, string? updatedAt, string tokenHash, CancellationToken cancellationToken);
}

/// <summary>
/// Persistence port of the public invoice link (BR-19). The raw token is the only credential: the organization and
/// invoice always come from the token row, and every read is side-effect free (BR-22). Null means "unavailable".
/// </summary>
public interface IInvoiceLinkStore
{
    Task<PublicInvoice?> ViewAsync(string token, CancellationToken cancellationToken);

    Task<InvoicePdfSource?> GetPdfSourceAsync(string token, CancellationToken cancellationToken);

    Task<PublicBinary?> GetLogoAsync(string token, CancellationToken cancellationToken);
}

public interface IInvoiceNotifier
{
    Task<InvoiceEmailStatus> SendAsync(InvoiceEmail email, CancellationToken cancellationToken);
}

public interface IInvoiceLinkBuilder
{
    string BuildLink(string rawToken);
}

/// <summary>Renders the PDF of a composed document (BR-12); the implementation lives in Infrastructure.</summary>
public interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfDocument document);
}
