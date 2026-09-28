using System.Text;
using System.Text.Json;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Validates and canonicalizes <c>branch.businessHours</c> per BR-15: an
/// object keyed by lowercase weekdays, each open day mapping to
/// <c>{ "start": "HH:mm", "end": "HH:mm" }</c> in 24-hour time with
/// <c>start &lt; end</c>; closed days are omitted; unknown keys or extra
/// properties are invalid; all days closed is allowed and canonicalized as
/// <c>{}</c>.
/// </summary>
public static class BusinessHoursValidator
{
    public static readonly string[] Days =
    [
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
    ];

    public const string RootKey = "branch.businessHours";

    public const string RequiredMessage = "This field is required.";

    public const string StructuralInvalidMessage = "Enter a valid value.";

    public const string EndNotAfterStartMessage = "End time must be after start time.";

    /// <summary>
    /// Validates the shape of <paramref name="businessHours"/>, reporting
    /// every failure through <paramref name="addError"/> (dotted key,
    /// message), and returns the canonical compact JSON via
    /// <paramref name="canonicalJson"/> when valid. <paramref name="rootKey"/>
    /// lets callers with a different request shape (e.g. company settings and
    /// branches BR-03: <c>businessHours</c> instead of
    /// <c>branch.businessHours</c>) reuse this logic without duplicating it
    /// (AS-03).
    /// </summary>
    public static bool TryValidate(
        JsonElement? businessHours,
        Action<string, string> addError,
        out string canonicalJson,
        string rootKey = RootKey)
    {
        canonicalJson = "{}";

        if (businessHours is not { ValueKind: JsonValueKind.Object } root)
        {
            addError(rootKey, StructuralInvalidMessage);
            return false;
        }

        var isValid = true;
        var openDays = new SortedDictionary<int, (string Start, string End)>();

        foreach (var property in root.EnumerateObject())
        {
            var dayIndex = Array.IndexOf(Days, property.Name);

            if (dayIndex < 0 || property.Value.ValueKind != JsonValueKind.Object)
            {
                addError(rootKey, StructuralInvalidMessage);
                isValid = false;
                continue;
            }

            var extraProperties = property.Value.EnumerateObject()
                .Any(dayProperty => dayProperty.Name is not ("start" or "end"));

            if (extraProperties)
            {
                addError(rootKey, StructuralInvalidMessage);
                isValid = false;
                continue;
            }

            var day = property.Name;
            var startKey = $"{rootKey}.{day}.start";
            var endKey = $"{rootKey}.{day}.end";

            var start = TryGetString(property.Value, "start");
            var end = TryGetString(property.Value, "end");

            var startValid = ValidateTime(start, startKey, addError, ref isValid);
            var endValid = ValidateTime(end, endKey, addError, ref isValid);

            if (startValid && endValid && string.CompareOrdinal(end, start) <= 0)
            {
                addError(endKey, EndNotAfterStartMessage);
                isValid = false;
                endValid = false;
            }

            if (startValid && endValid)
            {
                openDays[dayIndex] = (start!, end!);
            }
        }

        if (!isValid)
        {
            return false;
        }

        canonicalJson = BuildJson(openDays);
        return true;
    }

    private static bool ValidateTime(
        string? value,
        string key,
        Action<string, string> addError,
        ref bool isValid)
    {
        if (string.IsNullOrEmpty(value))
        {
            addError(key, RequiredMessage);
            isValid = false;
            return false;
        }

        if (!IsValidTime(value))
        {
            addError(key, StructuralInvalidMessage);
            isValid = false;
            return false;
        }

        return true;
    }

    private static string? TryGetString(JsonElement dayObject, string propertyName) =>
        dayObject.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsValidTime(string value) =>
        value.Length == 5
        && value[2] == ':'
        && int.TryParse(value.AsSpan(0, 2), out var hour) && hour is >= 0 and <= 23
        && int.TryParse(value.AsSpan(3, 2), out var minute) && minute is >= 0 and <= 59;

    private static string BuildJson(SortedDictionary<int, (string Start, string End)> openDays)
    {
        if (openDays.Count == 0)
        {
            return "{}";
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var (dayIndex, hours) in openDays)
            {
                writer.WriteStartObject(Days[dayIndex]);
                writer.WriteString("start", hours.Start);
                writer.WriteString("end", hours.End);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
