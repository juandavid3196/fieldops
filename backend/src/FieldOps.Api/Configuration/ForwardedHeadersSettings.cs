namespace FieldOps.Api.Configuration;

/// <summary>
/// Reverse proxy trust, bound from the "ForwardedHeaders" section. Disabled
/// by default (local runs have no proxy in front of Kestrel); enabled in
/// deployments behind a TLS-terminating proxy.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; init; }

    /// <summary>Single proxy IP addresses whose X-Forwarded-* headers are honored.</summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>Proxy networks in CIDR notation whose X-Forwarded-* headers are honored.</summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// Explicit opt-in to honor the headers from any peer. Only safe when the
    /// container is reachable exclusively through a proxy that overwrites them.
    /// </summary>
    public bool TrustAllProxies { get; init; }

    /// <summary>Number of proxy hops read from X-Forwarded-For.</summary>
    public int ForwardLimit { get; init; } = 1;
}
