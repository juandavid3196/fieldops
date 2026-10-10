using System.Security.Cryptography;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class InvoiceLinkStore
{
    /// <summary>The latest completed (or approved) visit of a work order and what the public page needs from it (BR-03).</summary>
    internal sealed record CompletedVisit(Guid Id, DateTimeOffset CompletedAt, string? Summary);

    /// <summary>
    /// The public invoice (customer-invoice-payments BR-03 to BR-05): the frozen preview plus the live payment facts. Reads
    /// only; the bank account number is decrypted here and only while the options are shown (BR-04).
    /// </summary>
    private async Task<PublicInvoice> BuildPublicInvoiceAsync(
        Invoice invoice, InvoicePreviewReader.Loaded loaded, CancellationToken cancellationToken)
    {
        var preview = loaded.Preview;
        var organizationId = invoice.OrganizationId;
        var org = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new
            {
                candidate.Timezone,
                candidate.PaymentPrefix,
                candidate.Email,
                candidate.Phone,
                candidate.BankName,
                candidate.BankAccountNumberCiphertext,
                candidate.BankRoutingNumber,
            })
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(org.Timezone);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone);
        var (display, days) = InvoiceHubRules.Display(invoice.Status, invoice.DueDate, today);

        var payments = await (
            from allocation in dbContext.PaymentAllocations.AsNoTracking()
            join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
            where allocation.InvoiceId == invoice.Id && payment.OrganizationId == organizationId
            orderby payment.PaidAt descending, payment.PaymentNumber descending
            select payment)
            .ToListAsync(cancellationToken);

        var attempts = await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .Where(attempt => attempt.OrganizationId == organizationId
                && attempt.InvoiceId == invoice.Id
                && attempt.Status == PaymentAttemptStatus.Pending)
            .Select(attempt => new { attempt.Id, attempt.Method, attempt.CreatedAt })
            .ToListAsync(cancellationToken);
        var card = attempts.FirstOrDefault(attempt => attempt.Method == PaymentMethod.CardOnline);
        var transfer = attempts
            .Where(attempt => attempt.Method == PaymentMethod.BankTransfer)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .FirstOrDefault();

        var visit = await LatestCompletedVisitAsync(organizationId, invoice.WorkOrderId, cancellationToken);
        var technician = visit is null ? null : await PrimaryTechnicianNameAsync(visit.Id, cancellationToken);
        var photoCount = visit is null ? 0 : await CountPhotosAsync(organizationId, invoice.WorkOrderId, cancellationToken);
        var reviewed = await dbContext.InvoiceReviews.AsNoTracking()
            .AnyAsync(review => review.OrganizationId == organizationId && review.WorkOrderId == invoice.WorkOrderId, cancellationToken);
        var isPerson = await dbContext.Customers.AsNoTracking()
            .AnyAsync(
                customer => customer.OrganizationId == organizationId
                    && customer.Id == invoice.CustomerId
                    && customer.Type == Domain.Customers.CustomerType.Person,
                cancellationToken);

        var latestPaid = invoice.Status == InvoiceStatus.Paid
            ? payments.FirstOrDefault(payment => payment.Status != PaymentStatus.Refunded)
            : null;
        var paidOn = latestPaid is null ? (DateOnly?)null : OrganizationTime.LocalDate(latestPaid.PaidAt, zone);
        var serviceCompletedOn = visit is null ? (DateOnly?)null : OrganizationTime.LocalDate(visit.CompletedAt, zone);
        var payable = invoice.Status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid && invoice.BalanceDue > 0m;

        return new PublicInvoice(
            preview.Number,
            preview.IssueDate,
            preview.DueDate,
            preview.PaymentTerms,
            preview.Currency,
            preview.Timezone,
            preview.Organization,
            preview.BillTo,
            preview.ServiceAddress,
            preview.WorkOrderNumber,
            preview.Lines,
            preview.Totals,
            preview.CompletionNote,
            invoice.Status switch
            {
                InvoiceStatus.Paid => InvoiceStatusFilters.Paid,
                InvoiceStatus.PartiallyPaid => InvoiceStatusFilters.PartiallyPaid,
                _ => InvoiceStatusFilters.Sent,
            },
            display == InvoiceStatusFilters.Overdue,
            days,
            QuoteCalculator.Money(invoice.AmountPaid),
            QuoteCalculator.Money(invoice.BalanceDue),
            OnlinePaymentRules.FirstName(preview.BillTo.Name, isPerson),
            new PublicTimeline(
                serviceCompletedOn,
                invoice.SentAt is { } sent ? OrganizationTime.LocalDate(sent, zone) : null,
                paidOn,
                latestPaid?.ReceiptNumber is null ? null : paidOn),
            new PublicService(
                loaded.WorkOrderTitle,
                preview.WorkOrderNumber,
                preview.CompletionNote,
                technician,
                serviceCompletedOn,
                visit is not null,
                photoCount),
            payable ? PaymentOptions(invoice, preview.Number, org.Phone, org.Email, org.BankName, org.BankAccountNumberCiphertext, org.BankRoutingNumber) : null,
            card is null ? null : new PublicActiveAttempt(card.Id, OnlinePaymentRules.AttemptStatusCode(PaymentAttemptStatus.Pending)),
            transfer is null ? null : OrganizationTime.LocalDate(transfer.CreatedAt, zone),
            payments.Select(payment => PublicPaymentMapper.ToItem(payment, org.PaymentPrefix, zone)).ToList(),
            new PublicReviewState(invoice.Status == InvoiceStatus.Paid && !reviewed, reviewed),
            invoice.RecipientEmail);
    }

    // BR-04: the card option needs every Stripe key; the bank option needs the stored details and a usable key.
    private PublicPaymentOptions PaymentOptions(
        Invoice invoice,
        string invoiceNumber,
        string? phone,
        string? email,
        string? bankName,
        byte[]? ciphertext,
        string? routing)
    {
        var capabilities = gateway.Capabilities;
        var bank = new PublicBankOption(false);

        if (bankName is not null && ciphertext is not null && routing is not null && encryptor.IsAvailable)
        {
            try
            {
                bank = new PublicBankOption(
                    true,
                    bankName,
                    encryptor.Decrypt(ciphertext),
                    routing.Trim(),
                    invoiceNumber,
                    QuoteCalculator.Money(invoice.BalanceDue));
            }
            catch (CryptographicException)
            {
                // A value that does not decrypt with the configured key is not offered; nothing is logged.
            }
        }

        return new PublicPaymentOptions(
            new PublicCardOption(capabilities.CardAvailable, capabilities.CardAvailable ? capabilities.PublishableKey : null),
            bank,
            new PublicCashOption(string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(), string.IsNullOrWhiteSpace(email) ? null : email.Trim()));
    }

    internal async Task<CompletedVisit?> LatestCompletedVisitAsync(Guid organizationId, Guid workOrderId, CancellationToken cancellationToken)
    {
        var visit = await dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.WorkOrderId == workOrderId
                && candidate.ActualCompletedAt != null
                && (candidate.Status == VisitStatus.Completed || candidate.Status == VisitStatus.Approved))
            .OrderByDescending(candidate => candidate.ActualCompletedAt)
            .ThenByDescending(candidate => candidate.VisitNumber)
            .Select(candidate => new { candidate.Id, candidate.ActualCompletedAt, candidate.CompletionSummary })
            .FirstOrDefaultAsync(cancellationToken);

        return visit is null ? null : new CompletedVisit(visit.Id, visit.ActualCompletedAt!.Value, visit.CompletionSummary);
    }

    internal async Task<string?> PrimaryTechnicianNameAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var name = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            where assignment.VisitId == visitId && assignment.IsPrimary && assignment.UnassignedAt == null
            select new { profile.FirstName, profile.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        return name is null
            ? null
            : string.Join(' ', new[] { name.FirstName, name.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim() is { Length: > 0 } full
                ? full
                : null;
    }

    // Before and after photos with stored content of the completed or approved visits of the work order (BR-21).
    private IQueryable<VisitEvidence> PublicPhotos(Guid organizationId, Guid workOrderId) =>
        from evidence in dbContext.VisitEvidences.AsNoTracking()
        join visit in dbContext.Visits.AsNoTracking() on evidence.VisitId equals visit.Id
        where visit.OrganizationId == organizationId
            && visit.WorkOrderId == workOrderId
            && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
            && (evidence.EvidenceType == VisitEvidenceType.Before || evidence.EvidenceType == VisitEvidenceType.After)
            && evidence.Content != null
        select evidence;

    private Task<int> CountPhotosAsync(Guid organizationId, Guid workOrderId, CancellationToken cancellationToken) =>
        PublicPhotos(organizationId, workOrderId).CountAsync(cancellationToken);
}
