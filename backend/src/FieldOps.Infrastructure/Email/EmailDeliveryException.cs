namespace FieldOps.Infrastructure.Email;

/// <summary>
/// A provider rejected or could not receive a message. Carries only the
/// provider and, for HTTP providers, the status code: never the recipient,
/// bodies, credentials or the provider's response text.
/// </summary>
internal sealed class EmailDeliveryException(string provider, int? statusCode)
    : Exception($"Email delivery failed via {provider}" + (statusCode is { } code ? $" (HTTP {code})." : "."))
{
    public string Provider { get; } = provider;

    public int? StatusCode { get; } = statusCode;
}
