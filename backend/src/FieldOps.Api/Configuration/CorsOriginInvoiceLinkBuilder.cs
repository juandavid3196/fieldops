using FieldOps.Application.Features.InvoiceDelivery;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Builds <c>{configured frontend origin}/invoices/view#token={token}</c> (invoice-draft-delivery BR-18) from the first
/// validated CORS origin. The token travels in the fragment so it never reaches a server, proxy or referrer log; the
/// raw token is already URL-safe.
/// </summary>
public sealed class CorsOriginInvoiceLinkBuilder(IOptions<CorsSettings> settings) : IInvoiceLinkBuilder
{
    public string BuildLink(string rawToken) =>
        $"{settings.Value.AllowedOrigins[0].TrimEnd('/')}/invoices/view#token={rawToken}";
}
