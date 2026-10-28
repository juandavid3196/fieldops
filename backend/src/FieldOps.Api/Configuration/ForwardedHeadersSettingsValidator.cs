using System.Net;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// When forwarded headers are enabled the trusted proxies must be explicit:
/// a list of addresses/networks, or the deliberate TrustAllProxies opt-in.
/// </summary>
public sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var section = ForwardedHeadersSettings.SectionName;
        var failures = new List<string>();
        var hasList = options.KnownProxies.Length > 0 || options.KnownNetworks.Length > 0;

        if (options.TrustAllProxies && hasList)
        {
            failures.Add($"'{section}:TrustAllProxies' cannot be combined with KnownProxies or KnownNetworks.");
        }

        if (!options.TrustAllProxies && !hasList)
        {
            failures.Add($"'{section}:KnownProxies' or '{section}:KnownNetworks' is required, or set '{section}:TrustAllProxies' explicitly.");
        }

        if (options.KnownProxies.Any(proxy => !IPAddress.TryParse(proxy, out _)))
        {
            failures.Add($"'{section}:KnownProxies' contains an invalid IP address.");
        }

        if (options.KnownNetworks.Any(network => !IPNetwork.TryParse(network, out _)))
        {
            failures.Add($"'{section}:KnownNetworks' contains an invalid CIDR network.");
        }

        if (options.ForwardLimit is < 1 or > 5)
        {
            failures.Add($"'{section}:ForwardLimit' must be between 1 and 5.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
