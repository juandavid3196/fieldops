using System.Globalization;

namespace FieldOps.Application.Validation;

/// <summary>
/// Parses the <c>updatedAt</c> concurrency token carried by PUT bodies
/// (BR-07): an ISO 8601 timestamp, sent back exactly as received on success.
/// A missing or malformed value fails validation with the same message on
/// every endpoint that carries it.
/// </summary>
public static class UpdatedAtValidation
{
    public const string InvalidMessage = "Enter a valid value.";

    public static bool TryParse(string? value, out DateTimeOffset parsed)
    {
        parsed = default;

        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
    }
}
