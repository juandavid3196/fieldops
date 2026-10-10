using System.Security.Cryptography;

namespace FieldOps.Application.Features.PortalAccess;

/// <summary>The portal user behind a session: used as the audit actor and the responder of a quote.</summary>
public sealed record PortalActor(Guid UserId, Guid ContactId);

/// <summary>
/// The validated portal session reduced to what every portal query needs. It is built server-side from the
/// revalidated session only; no request body or header can supply any of these values (customer portal BR-37).
/// </summary>
public sealed record PortalScope(Guid OrganizationId, Guid CustomerId, Guid ContactId, Guid UserId)
{
    public PortalActor Actor => new(UserId, ContactId);
}

/// <summary>
/// How a quote or invoice resource is reached by the shared use cases: by the public access token (the token row
/// yields the organization and resource) or by a portal session plus the resource id (customer portal BR-30, BR-31).
/// </summary>
public abstract record ResourceAccess
{
    private ResourceAccess()
    {
    }

    public static ResourceAccess FromToken(string rawToken) => new Token(rawToken);

    public sealed record Token(string Raw) : ResourceAccess;

    public sealed record Portal(PortalScope Scope, Guid ResourceId) : ResourceAccess;

    /// <summary>The rate-limit key: the resource for a token; the user and the resource for a portal session.</summary>
    public Guid ThrottleKey(Guid resolvedResourceId) =>
        this is Portal portal
            ? new Guid(SHA256.HashData([.. portal.Scope.UserId.ToByteArray(), .. portal.ResourceId.ToByteArray()])[..16])
            : resolvedResourceId;
}
