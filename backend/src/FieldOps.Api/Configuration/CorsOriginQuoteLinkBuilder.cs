using FieldOps.Application.Features.Quotes;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Builds <c>{configured frontend origin}/quote-approval#token={token}</c> (quote-builder BR-27) from the first
/// validated CORS origin. The token travels in the fragment so it never reaches a server, proxy or referrer
/// log; the raw token is already URL-safe.
/// </summary>
public sealed class CorsOriginQuoteLinkBuilder(IOptions<CorsSettings> settings) : IQuoteLinkBuilder
{
    public string BuildLink(string rawToken) =>
        $"{settings.Value.AllowedOrigins[0].TrimEnd('/')}/quote-approval#token={rawToken}";
}
