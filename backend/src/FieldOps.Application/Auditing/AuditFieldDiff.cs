using System.Text.Json;

namespace FieldOps.Application.Auditing;

/// <summary>
/// Builds BR-12 <c>before_data</c>/<c>after_data</c> JSON for
/// organization-settings and branch audit rows, from request-key to value
/// maps. <c>email</c>/<c>phone</c> are never stored as real values: a
/// changed one appears as <see cref="ChangedMask"/>. Pure and stateless, so
/// it is directly unit-testable without a database.
/// </summary>
public static class AuditFieldDiff
{
    public const string ChangedMask = "[changed]";

    private static readonly HashSet<string> MaskedKeys = new(StringComparer.Ordinal) { "email", "phone" };

    /// <summary>
    /// Create mode: <c>before_data</c> is null; <c>after_data</c> is every
    /// field, with a non-null <c>email</c>/<c>phone</c> masked.
    /// </summary>
    public static (string? Before, string? After) ForCreate(IReadOnlyDictionary<string, object?> fields)
    {
        var masked = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in fields)
        {
            masked[key] = ShouldMaskKey(key) && value is not null ? ChangedMask : value;
        }

        return (null, Serialize(masked));
    }

    /// <summary>
    /// Update mode: only fields whose value actually changed appear on
    /// either side; a changed <c>email</c>/<c>phone</c> is masked on both
    /// sides instead of showing the real values.
    /// </summary>
    public static (string? Before, string? After) ForUpdate(
        IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after)
    {
        var beforeChanged = new Dictionary<string, object?>(StringComparer.Ordinal);
        var afterChanged = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, afterValue) in after)
        {
            var beforeValue = before.TryGetValue(key, out var value) ? value : null;

            if (Equals(beforeValue, afterValue))
            {
                continue;
            }

            if (ShouldMaskKey(key))
            {
                beforeChanged[key] = ChangedMask;
                afterChanged[key] = ChangedMask;
            }
            else
            {
                beforeChanged[key] = beforeValue;
                afterChanged[key] = afterValue;
            }
        }

        return (Serialize(beforeChanged), Serialize(afterChanged));
    }

    /// <summary>
    /// State-change mode (deactivate/reactivate, BR-12): both sides contain
    /// only <c>isActive</c>, with the state before and after the change.
    /// </summary>
    public static (string Before, string After) ForStateChange(bool before, bool after) =>
        (SerializeIsActive(before), SerializeIsActive(after));

    private static string SerializeIsActive(bool isActive) =>
        Serialize(new Dictionary<string, object?>(StringComparer.Ordinal) { ["isActive"] = isActive });

    private static bool ShouldMaskKey(string key) => MaskedKeys.Contains(key);

    private static string Serialize(IReadOnlyDictionary<string, object?> fields) =>
        JsonSerializer.Serialize(fields);
}
