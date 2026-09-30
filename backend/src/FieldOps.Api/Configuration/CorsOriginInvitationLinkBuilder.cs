using FieldOps.Application.Features.Users;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Builds <c>{configured frontend origin}/auth/invitation#token={token}</c>
/// (BR-01) from the first validated CORS origin, the API's only configured
/// frontend origin. The token travels in the fragment so it never reaches a
/// server, proxy or referrer log; the raw token is already URL-safe.
/// </summary>
public sealed class CorsOriginInvitationLinkBuilder(IOptions<CorsSettings> settings) : IInvitationLinkBuilder
{
    public string BuildAcceptLink(string rawToken) =>
        $"{settings.Value.AllowedOrigins[0].TrimEnd('/')}/auth/invitation#token={rawToken}";
}
