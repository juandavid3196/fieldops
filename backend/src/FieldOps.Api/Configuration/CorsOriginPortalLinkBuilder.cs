using FieldOps.Application.Features.PortalInvitations;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Builds the portal links of the emails from the first configured frontend origin (customer portal BR-11, BR-15):
/// <c>{origin}/portal/activate#token={token}</c> and <c>{origin}/portal/reset-password#token={token}</c>. The token travels in
/// the fragment so it never reaches a server, proxy or referrer log; the raw token is already URL-safe.
/// </summary>
public sealed class CorsOriginPortalLinkBuilder(IOptions<CorsSettings> settings) : IPortalLinkBuilder
{
    public string BuildActivationLink(string rawToken) => $"{Origin()}/portal/activate#token={rawToken}";

    public string BuildResetLink(string rawToken) => $"{Origin()}/portal/reset-password#token={rawToken}";

    private string Origin() => settings.Value.AllowedOrigins[0].TrimEnd('/');
}
