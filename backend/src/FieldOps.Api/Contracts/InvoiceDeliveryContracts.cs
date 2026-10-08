using FieldOps.Application.Features.InvoiceDelivery;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /invoices/{id}/draft and POST /invoices/{id}/send (invoice-draft-delivery BR-08). Every value is read as
/// text so a missing one is a field error; unknown keys, such as an organization id, an amount or a status, are ignored.
/// </summary>
public sealed record InvoiceDeliveryRequestBody(string? PaymentTerms, string? RecipientEmail, string? Message, string? UpdatedAt)
{
    public DeliveryBodyText ToText() => new(PaymentTerms, RecipientEmail, Message, UpdatedAt);
}

/// <summary>Body of POST /invoices/{id}/resend-email.</summary>
public sealed record InvoiceResendRequestBody(string? UpdatedAt);

/// <summary>Body of the public invoice-link endpoints: the token is the only credential.</summary>
public sealed record InvoiceLinkTokenRequest(string? Token);
