using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Shared mapping of payments to the public items (customer-invoice-payments BR-05) and the receipt numbering.</summary>
internal static class PublicPaymentMapper
{
    /// <summary>The payment as the customer sees it: opaque id, numbers, gross and refunded amount; never reference, note or recorder.</summary>
    public static PublicPaymentItem ToItem(Payment payment, string paymentPrefix, TimeZoneInfo zone) =>
        new(
            payment.Id,
            InvoiceHubRules.DisplayNumber(paymentPrefix, payment.PaymentNumber),
            payment.ReceiptNumber,
            OrganizationTime.LocalDate(payment.PaidAt, zone),
            PaymentMethodCodes.Code(payment.Method),
            OnlinePaymentRules.MethodLabel(payment.Method, payment.CardBrand, payment.CardLast4),
            QuoteCalculator.Money(payment.Amount),
            QuoteCalculator.Money(payment.RefundedAmount),
            OnlinePaymentRules.PaymentStatusCode(payment.Status));

    /// <summary>Receipts already issued for the invoice (BR-11 b); read under the invoice lock so the next sequence is gap-free.</summary>
    public static Task<int> ReceiptCountAsync(FieldOpsDbContext dbContext, Guid organizationId, Guid invoiceId, CancellationToken cancellationToken) =>
        dbContext.Payments.AsNoTracking()
            .CountAsync(
                payment => payment.OrganizationId == organizationId
                    && payment.ReceiptNumber != null
                    && dbContext.PaymentAllocations.Any(allocation => allocation.PaymentId == payment.Id && allocation.InvoiceId == invoiceId),
                cancellationToken);
}
