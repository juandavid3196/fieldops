using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Organizations;
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
internal sealed partial class InvoiceLinkStore(
    FieldOpsDbContext dbContext,
    TimeProvider timeProvider,
    IPaymentGateway gateway,
    IBankDetailsEncryptor encryptor) : IInvoiceLinkStore
{
    public async Task<PublicInvoice?> ViewAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var loaded = await ResolveAsync(access, cancellationToken);

        return loaded is null
            ? null
            : await BuildPublicInvoiceAsync(loaded.Value.Invoice, loaded.Value.Loaded, cancellationToken);
    }

    public async Task<InvoicePdfSource?> GetPdfSourceAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var loaded = await ResolveAsync(access, cancellationToken);

        return loaded is null
            ? null
            : await new InvoicePreviewReader(dbContext).ReadPdfSourceAsync(loaded.Value.Invoice, cancellationToken);
    }

    public async Task<PublicBinary?> GetLogoAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(access, cancellationToken);

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

    private async Task<(Invoice Invoice, InvoicePreviewReader.Loaded Loaded)?> ResolveAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var invoice = await ResolveInvoiceAsync(access, cancellationToken);

        return invoice is null
            ? null
            : (invoice, await new InvoicePreviewReader(dbContext).ReadAsync(invoice, cancellationToken));
    }

    // BR-19: the hash must match an unrevoked, unexpired token of an invoice that is neither draft nor void.
    private Task<Invoice?> ResolveInvoiceAsync(ResourceAccess access, CancellationToken cancellationToken) =>
        ValidInvoices(dbContext, access, timeProvider.GetUtcNow()).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The invoice of a usable token (customer-invoice-payments BR-01) or of a portal session and id (customer portal BR-31), no-tracking; shared by every public flow.</summary>
    internal static IQueryable<Invoice> ValidInvoices(FieldOpsDbContext dbContext, ResourceAccess access, DateTimeOffset now)
    {
        if (access is ResourceAccess.Portal portal)
        {
            var organizationId = portal.Scope.OrganizationId;
            var customerId = portal.Scope.CustomerId;
            var invoiceId = portal.ResourceId;

            return dbContext.Invoices.AsNoTracking()
                .Where(invoice => invoice.Id == invoiceId
                    && invoice.OrganizationId == organizationId
                    && invoice.CustomerId == customerId
                    && invoice.Status != InvoiceStatus.Draft
                    && invoice.Status != InvoiceStatus.Void
                    && invoice.CustomerSnapshot != null);
        }

        var hash = QuoteAccessTokens.Hash(((ResourceAccess.Token)access).Raw);

        return
            from grant in dbContext.InvoiceAccessTokens.AsNoTracking()
            join invoice in dbContext.Invoices.AsNoTracking()
                on new { grant.OrganizationId, Id = grant.InvoiceId } equals new { invoice.OrganizationId, invoice.Id }
            where grant.TokenHash == hash
                && grant.RevokedAt == null
                && grant.ExpiresAt > now
                && invoice.Status != InvoiceStatus.Draft
                && invoice.Status != InvoiceStatus.Void
                && invoice.CustomerSnapshot != null
            select invoice;
    }
}
