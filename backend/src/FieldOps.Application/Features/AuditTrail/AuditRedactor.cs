using System.Text.Json;
using System.Text.Json.Nodes;

namespace FieldOps.Application.Features.AuditTrail;

/// <summary>
/// BR-12 redaction, applied recursively to objects and arrays before any audit value leaves the server. A key containing a
/// secret fragment is replaced by <see cref="Redacted"/>; a key containing <c>email</c> or <c>phone</c> with a value by
/// <see cref="Changed"/>. Pure over System.Text.Json.
/// </summary>
public static class AuditRedactor
{
    public const string Redacted = "[redacted]";

    public const string Changed = "[changed]";

    private static readonly string[] SecretFragments =
    [
        "password", "token", "secret", "hash", "apikey", "api_key", "authorization", "cookie", "cvc", "cvv",
        "cardnumber", "card_number", "iban", "accountnumber", "account_number", "routing",
    ];

    private static readonly string[] ContactFragments = ["email", "phone"];

    /// <summary>Parses and redacts stored JSON; null for a null, blank or malformed document.</summary>
    public static JsonNode? Redact(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return Walk(JsonNode.Parse(json));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonNode? Redact(JsonNode? node) => Walk(node);

    private static JsonNode? Walk(JsonNode? node) =>
        node switch
        {
            JsonObject obj => new JsonObject(obj.Select(property =>
                KeyValuePair.Create(property.Key, MaskValue(property.Key, property.Value)))),
            JsonArray array => new JsonArray([.. array.Select(Walk)]),
            null => null,
            _ => node.DeepClone(),
        };

    private static JsonNode? MaskValue(string key, JsonNode? value)
    {
        if (SecretFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return JsonValue.Create(Redacted);
        }

        return value is not null && ContactFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            ? JsonValue.Create(Changed)
            : Walk(value);
    }
}
