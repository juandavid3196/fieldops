using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Token-scoped, read-only documents of the public invoice link (customer-invoice-payments BR-19 to BR-21): the receipt and
/// completion report sources and the before/after photos. Every query starts from the token invoice; a payment, photo or
/// visit of another invoice is the same null as an unusable token.
/// </summary>
internal sealed partial class InvoiceLinkStore
{
    public async Task<ReceiptSource?> GetReceiptSourceAsync(ResourceAccess access, Guid paymentId, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(access, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var payment = await (
            from allocation in dbContext.PaymentAllocations.AsNoTracking()
            join candidate in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals candidate.Id
            where allocation.InvoiceId == invoice.Id
                && candidate.OrganizationId == invoice.OrganizationId
                && candidate.Id == paymentId
                && candidate.ReceiptNumber != null
            select candidate)
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return null;
        }

        var source = await new InvoicePreviewReader(dbContext).ReadPdfSourceAsync(invoice, cancellationToken);
        var preview = source.Preview;
        var paymentPrefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == invoice.OrganizationId)
            .Select(organization => organization.PaymentPrefix)
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(preview.Timezone);

        return new ReceiptSource(
            preview.Organization.Name,
            OrganizationLines(preview),
            source.Logo,
            source.LogoContentType,
            payment.ReceiptNumber!,
            InvoiceHubRules.DisplayNumber(paymentPrefix, payment.PaymentNumber),
            OrganizationTime.LocalDate(payment.PaidAt, zone),
            BillToLines(preview),
            preview.Number,
            OnlinePaymentRules.MethodLabel(payment.Method, payment.CardBrand, payment.CardLast4),
            payment.Currency,
            payment.Amount,
            payment.RefundedAmount,
            invoice.Total,
            invoice.AmountPaid,
            invoice.BalanceDue);
    }

    public async Task<CompletionReportSource?> GetCompletionReportSourceAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(access, cancellationToken);

        if (invoice is null || await LatestCompletedVisitAsync(invoice.OrganizationId, invoice.WorkOrderId, cancellationToken) is not { } visit)
        {
            return null;
        }

        var source = await new InvoicePreviewReader(dbContext).ReadPdfSourceAsync(invoice, cancellationToken);
        var preview = source.Preview;
        var zone = OrganizationTime.FindZone(preview.Timezone);
        var title = await dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == invoice.OrganizationId && order.Id == invoice.WorkOrderId)
            .Select(order => order.Title)
            .SingleAsync(cancellationToken);
        var checklist = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(item => item.VisitId == visit.Id)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new CompletionChecklistItem(item.Label, item.IsCompleted))
            .ToListAsync(cancellationToken);
        var signoff = await dbContext.CustomerSignoffs.AsNoTracking()
            .Where(candidate => candidate.VisitId == visit.Id)
            .Select(candidate => new { candidate.AcknowledgementMethod, candidate.SignerName, candidate.SignedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return new CompletionReportSource(
            preview.Organization.Name,
            OrganizationLines(preview),
            source.Logo,
            source.LogoContentType,
            preview.WorkOrderNumber,
            title,
            preview.ServiceAddress,
            OrganizationTime.LocalDate(visit.CompletedAt, zone),
            await PrimaryTechnicianNameAsync(visit.Id, cancellationToken),
            visit.Summary,
            checklist,
            signoff is null ? null : CompletionVerifier.MethodLabel(signoff.AcknowledgementMethod),
            signoff?.SignerName,
            signoff is null ? null : OrganizationTime.LocalDate(signoff.SignedAt, zone));
    }

    public async Task<IReadOnlyList<InvoicePhoto>?> ListPhotosAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(access, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var timezone = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == invoice.OrganizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(timezone);
        var photos = await PublicPhotos(invoice.OrganizationId, invoice.WorkOrderId)
            .OrderBy(evidence => evidence.EvidenceType == VisitEvidenceType.Before ? 0 : 1)
            .ThenBy(evidence => evidence.CreatedAt)
            .ThenBy(evidence => evidence.Id)
            .Select(evidence => new { evidence.Id, evidence.EvidenceType, evidence.Caption, evidence.CreatedAt })
            .ToListAsync(cancellationToken);

        return photos
            .Select(photo => new InvoicePhoto(
                photo.Id,
                photo.EvidenceType == VisitEvidenceType.Before ? "before" : "after",
                string.IsNullOrWhiteSpace(photo.Caption) ? null : photo.Caption.Trim(),
                OrganizationTime.LocalDate(photo.CreatedAt, zone)))
            .ToList();
    }

    public async Task<PublicBinary?> GetPhotoAsync(ResourceAccess access, Guid photoId, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(access, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var photo = await PublicPhotos(invoice.OrganizationId, invoice.WorkOrderId)
            .Where(evidence => evidence.Id == photoId)
            .Select(evidence => new { evidence.MimeType, evidence.Content })
            .FirstOrDefaultAsync(cancellationToken);

        return photo?.Content is null ? null : new PublicBinary(photo.MimeType, photo.Content);
    }

    // The organization block of the PDFs: address lines, phone and email, each omitted when empty (BR-19, BR-20).
    private static IReadOnlyList<string> OrganizationLines(InvoicePreview preview)
    {
        var lines = new List<string>(preview.Organization.AddressLines);

        if (!string.IsNullOrWhiteSpace(preview.Organization.Phone))
        {
            lines.Add(preview.Organization.Phone);
        }

        if (!string.IsNullOrWhiteSpace(preview.Organization.Email))
        {
            lines.Add(preview.Organization.Email);
        }

        return lines;
    }

    // Bill to from the frozen snapshot (BR-19): name, email, phone and address lines.
    private static IReadOnlyList<string> BillToLines(InvoicePreview preview)
    {
        var lines = new List<string> { preview.BillTo.Name };

        if (!string.IsNullOrWhiteSpace(preview.BillTo.Email))
        {
            lines.Add(preview.BillTo.Email);
        }

        if (!string.IsNullOrWhiteSpace(preview.BillTo.Phone))
        {
            lines.Add(preview.BillTo.Phone);
        }

        lines.AddRange(preview.BillTo.AddressLines);

        return lines;
    }
}
