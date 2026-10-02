using System.Globalization;
using System.Text;

namespace FieldOps.Application.Features.PublicRequests;

/// <summary>
/// Pure public slug generation from an organization name (public service
/// request BR-21). The database backfill in the migration mirrors these rules.
/// </summary>
public static class PublicSlugGenerator
{
    public const int MaxLength = 60;

    public const string FallbackBase = "organization";

    // Suffixes up to "-99999" keep the truncated base at least this long, so
    // every candidate starts with the lookup prefix.
    private const int LookupPrefixLength = MaxLength - 6;

    /// <summary>Diacritics removed, lowercase, runs outside a-z0-9 become one hyphen, trimmed; never empty.</summary>
    public static string Normalize(string? name)
    {
        var decomposed = (name ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (character is >= '̀' and <= 'ͯ')
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);

            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-');

        return result.Length == 0 ? FallbackBase : result;
    }

    /// <summary>Prefix every candidate slug for this name starts with (for the taken-slug lookup).</summary>
    public static string LookupPrefix(string? name) => Truncate(Normalize(name), LookupPrefixLength);

    /// <summary>The base if free, otherwise the lowest free <c>-2</c>, <c>-3</c>, ... (total length at most 60).</summary>
    public static string Generate(string? name, IReadOnlySet<string> takenSlugs)
    {
        ArgumentNullException.ThrowIfNull(takenSlugs);

        var baseSlug = Normalize(name);

        for (var number = 1; ; number++)
        {
            var candidate = Candidate(baseSlug, number);

            if (!takenSlugs.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Candidate(string baseSlug, int number)
    {
        if (number == 1)
        {
            return Truncate(baseSlug, MaxLength);
        }

        var suffix = "-" + number.ToString(CultureInfo.InvariantCulture);

        return Truncate(baseSlug, MaxLength - suffix.Length) + suffix;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd('-');
}
