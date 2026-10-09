using System.Text.Json.Serialization;
using FieldOps.Application.Features.InvoicePayments;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST /invoices/{id}/payments (invoices-payments-management BR-11). Unknown keys, such as a status, a number, a
/// customer, a currency or an organization id, are rejected with a 400; every known value is read as text or nullable so a
/// missing one becomes a field error.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordPaymentRequestBody(
    string? IdempotencyKey,
    decimal? Amount,
    string? PaidDate,
    string? Method,
    string? Reference,
    string? ReceivedByUserId,
    bool? SendReceipt,
    string? Note,
    string? UpdatedAt)
{
    public PaymentBodyText ToText() =>
        new(IdempotencyKey, Amount, PaidDate, Method, Reference, ReceivedByUserId, SendReceipt, Note, UpdatedAt);
}
