using System.Text.Json;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Branch service ZIP code rules (BR-09): normalization, per-code format and
/// the 200-code cap. Shared by the create and update validators and handlers.
/// </summary>
public static class ServicePostalCodes
{
    public const int MaxCodes = 200;

    public const int MaxCodeLength = 30;

    public const string InvalidCodesMessage = "Enter valid ZIP codes separated by commas.";

    public const string TooManyMessage = "Enter up to 200 ZIP codes.";

    public const string InvalidValueMessage = "Enter a valid value.";

    /// <summary>
    /// Normalizes raw codes: each is trimmed, empties are dropped and
    /// duplicates removed keeping first order.
    /// </summary>
    public static string[] Normalize(IEnumerable<string?> codes) =>
        codes
            .Select(code => (code ?? string.Empty).Trim())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool IsValidCode(string code) =>
        code.Length is >= 1 and <= MaxCodeLength
        && code.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-');

    /// <summary>
    /// Reads the request value. Returns <c>null</c> in <paramref name="error"/>
    /// on success. A missing/null value yields <c>[]</c> when
    /// <paramref name="required"/> is false and the wrong-type error
    /// otherwise; a non-array value or non-string item is the wrong-type
    /// error.
    /// </summary>
    public static bool TryRead(
        JsonElement? value,
        bool required,
        out string[] codes,
        out string? error)
    {
        codes = [];
        error = null;

        if (value is null || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (required)
            {
                error = InvalidValueMessage;
                return false;
            }

            return true;
        }

        if (value.Value.ValueKind != JsonValueKind.Array)
        {
            error = InvalidValueMessage;
            return false;
        }

        var raw = new List<string?>();

        foreach (var item in value.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = InvalidValueMessage;
                return false;
            }

            raw.Add(item.GetString());
        }

        var normalized = Normalize(raw);

        if (normalized.Any(code => !IsValidCode(code)))
        {
            error = InvalidCodesMessage;
            return false;
        }

        if (normalized.Length > MaxCodes)
        {
            error = TooManyMessage;
            return false;
        }

        codes = normalized;
        return true;
    }

    /// <summary>Reads a boolean request value; missing/null yields <paramref name="defaultValue"/> when not required.</summary>
    public static bool TryReadBoolean(
        JsonElement? value,
        bool required,
        bool defaultValue,
        out bool result,
        out string? error)
    {
        result = defaultValue;
        error = null;

        if (value is null || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (required)
            {
                error = InvalidValueMessage;
                return false;
            }

            return true;
        }

        if (value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = value.Value.ValueKind == JsonValueKind.True;
            return true;
        }

        error = InvalidValueMessage;
        return false;
    }
}
