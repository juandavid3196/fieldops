using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace FieldOps.Application.Features.AuditTrail;

/// <summary>
/// Summary text (BR-15) and change rows (BR-13) over already redacted before/after data, so emails and phones appear
/// only as <c>[changed]</c> and secrets as <c>[redacted]</c>.
/// </summary>
public static class AuditSummaryBuilder
{
    public const int MaxLength = 160;

    public const string Dash = "—";

    public static string Build(string actionLabel, string reference, JsonNode? before, JsonNode? after)
    {
        var beforeKeys = Keys(before);
        var afterKeys = Keys(after);
        string text;

        if (beforeKeys.Count > 0 && afterKeys.Count > 0)
        {
            var changed = Union(before, after)
                .Where(key => IsChanged(Value(before, key), Value(after, key)))
                .ToList();

            text = changed.Count == 0
                ? actionLabel
                : $"{Humanize(changed[0])}: {Display(Value(before, changed[0]))} → {Display(Value(after, changed[0]))}"
                    + (changed.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" (+{changed.Count - 1} more)") : string.Empty);
        }
        else if (afterKeys.Count > 0)
        {
            text = string.Equals(reference, Dash, StringComparison.Ordinal) ? actionLabel : $"{actionLabel} {reference}";
        }
        else
        {
            text = actionLabel;
        }

        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }

    /// <summary>One row per key in the union of the redacted before and after objects (BR-13).</summary>
    public static IReadOnlyList<AuditChange> Changes(JsonNode? before, JsonNode? after) =>
        [.. Union(before, after).Select(key => new AuditChange(key, Humanize(key), Display(Value(before, key)), Display(Value(after, key))))];

    /// <summary>A display string: "—" for null or absent, the text of a string, the compact JSON of anything else.</summary>
    public static string Display(JsonNode? value) =>
        value switch
        {
            null => Dash,
            JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
            _ => value.ToJsonString(),
        };

    /// <summary>"firstName" and "first_name" become "First name".</summary>
    public static string Humanize(string key)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < key.Length; index++)
        {
            var current = key[index];

            if (current is '_' or '.' or '-')
            {
                builder.Append(' ');
            }
            else if (char.IsUpper(current) && index > 0 && !char.IsUpper(key[index - 1]) && key[index - 1] is not ('_' or '.' or '-'))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(index == 0 ? current : char.ToLowerInvariant(current));
            }
        }

        var words = builder.ToString().Trim();

        return words.Length == 0 ? key : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // A masked value on both sides cannot be compared: the stored diff only holds changed keys, so it counts as changed.
    private static bool IsChanged(JsonNode? before, JsonNode? after) =>
        !JsonNode.DeepEquals(before, after) || (IsMask(before) && IsMask(after));

    private static bool IsMask(JsonNode? value) =>
        value is JsonValue scalar
        && scalar.TryGetValue<string>(out var text)
        && (string.Equals(text, AuditRedactor.Changed, StringComparison.Ordinal) || string.Equals(text, AuditRedactor.Redacted, StringComparison.Ordinal));

    private static List<string> Keys(JsonNode? node) =>
        node is JsonObject obj ? [.. obj.Select(property => property.Key)] : [];

    private static List<string> Union(JsonNode? before, JsonNode? after)
    {
        var keys = Keys(before);

        foreach (var key in Keys(after).Where(key => !keys.Contains(key, StringComparer.Ordinal)))
        {
            keys.Add(key);
        }

        return keys;
    }

    private static JsonNode? Value(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;
}
