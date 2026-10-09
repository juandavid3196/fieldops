using System.Text.Json.Serialization;

namespace FieldOps.Api.Contracts;

// Bodies of the customer payment endpoints (customer-invoice-payments API contracts). They reject unknown keys with a 400, so a
// card field, an amount, an organization id or a status sent by the browser never reaches FieldOps (AC-16). Every known value
// is read as text or nullable so a missing one becomes a field error after the token was checked.

/// <summary>Body of POST /public/invoice-links/completion-report and /photos.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StrictInvoiceLinkTokenRequest(string? Token);

/// <summary>Body of POST /public/invoice-links/payments/card-intent and /payments/bank-transfer-notice.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceLinkKeyRequest(string? Token, string? IdempotencyKey);

/// <summary>Body of POST /public/invoice-links/payments/status.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentStatusRequest(string? Token, string? AttemptId);

/// <summary>Body of POST /public/invoice-links/receipt.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceiptRequest(string? Token, string? PaymentId);

/// <summary>Body of POST /public/invoice-links/photos/content.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhotoContentRequest(string? Token, string? PhotoId);

/// <summary>Body of POST /public/invoice-links/review; the rating is a number so a fractional or text value is a field error.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceReviewRequest(string? Token, decimal? Rating, string? Comment);

/// <summary>Body of PUT /organization-settings/bank-details (BR-25).</summary>
public sealed record UpdateBankDetailsRequest(string? BankName, string? AccountNumber, string? RoutingNumber, string? UpdatedAt);

/// <summary>Response of GET and PUT /organization-settings/bank-details (BR-25); the full account number is never returned.</summary>
public sealed record BankDetailsResponse(
    bool Configured, string? BankName, string? AccountNumberMasked, string? RoutingNumber, DateTimeOffset? UpdatedAt);
