using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.Quotes;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Persistence of the public invoice link (invoice-draft-delivery BR-18 to BR-22). The organization and invoice come only
/// from the token row and every later query filters by them. Every read is no-tracking and writes nothing: no status
/// change, no audit, no view tracking. Null is the one identical "unavailable" outcome.
/// </summary>
internal sealed class InvoiceLinkStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IInvoiceLinkStore
{
    public async Task<PublicInvoice?> ViewAsync(string token, CancellationToken cancellationToken)
    {
        var loaded = await ResolveAsync(token, cancellationToken);

        if (loaded is null)
        {
            return null;
        }

        var preview = loaded.Value.Loaded.Preview;

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
            "sent");
    }

    public async Task<InvoicePdfSource?> GetPdfSourceAsync(string token, CancellationToken cancellationToken)
    {
        var loaded = await ResolveAsync(token, cancellationToken);

        return loaded is null
            ? null
            : await new InvoicePreviewReader(dbContext).ReadPdfSourceAsync(loaded.Value.Invoice, cancellationToken);
    }

    public async Task<PublicBinary?> GetLogoAsync(string token, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(token, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var logo = await dbContext.OrganizationLogos.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == invoice.OrganizationId)
            .Select(candidate => new { candidate.ContentType, candidate.Content })
            .SingleOrDefaultAsync(cancellationToken);

        return logo is null ? null : new PublicBinary(logo.ContentType, logo.Content);
    }

    private async Task<(Invoice Invoice, InvoicePreviewReader.Loaded Loaded)?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(token, cancellationToken);

        return invoice is null
            ? null
            : (invoice, await new InvoicePreviewReader(dbContext).ReadAsync(invoice, cancellationToken));
    }

    // BR-19: the hash must match an unrevoked, unexpired token of an invoice that is neither draft nor void.
    private async Task<Invoice?> ResolveInvoiceAsync(string token, CancellationToken cancellationToken)
    {
        var hash = QuoteAccessTokens.Hash(token);
        var now = timeProvider.GetUtcNow();

        return await (
            from access in dbContext.InvoiceAccessTokens.AsNoTracking()
            join invoice in dbContext.Invoices.AsNoTracking()
                on new { access.OrganizationId, Id = access.InvoiceId } equals new { invoice.OrganizationId, invoice.Id }
            where access.TokenHash == hash
                && access.RevokedAt == null
                && access.ExpiresAt > now
                && invoice.Status != InvoiceStatus.Draft
                && invoice.Status != InvoiceStatus.Void
                && invoice.CustomerSnapshot != null
            select invoice)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
