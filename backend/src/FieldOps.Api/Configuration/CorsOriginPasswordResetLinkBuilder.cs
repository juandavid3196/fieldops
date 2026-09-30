using FieldOps.Application.Features.PasswordResets;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Builds <c>{first configured frontend origin}/auth/reset-password#token={token}</c>
/// (BR-10). The token travels in the fragment so it never reaches a server,
/// proxy or referrer log; the raw token is already URL-safe.
/// </summary>
public sealed class CorsOriginPasswordResetLinkBuilder(IOptions<CorsSettings> settings) : IPasswordResetLinkBuilder
{
    public string BuildResetLink(string rawToken) =>
        $"{settings.Value.AllowedOrigins[0].TrimEnd('/')}/auth/reset-password#token={rawToken}";
}
