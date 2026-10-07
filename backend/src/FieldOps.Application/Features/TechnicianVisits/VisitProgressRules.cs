namespace FieldOps.Application.Features.TechnicianVisits;

/// <summary>Pure validation of the mobile-job-progress inputs (BR-08 to BR-10, BR-13); errors are keyed by the request field.</summary>
public static class VisitProgressRules
{
    public const int TaskNotesMaxLength = 1000;

    public const int TaskLabelMaxLength = 240;

    public const int DescriptionMaxLength = 240;

    public const int UnitMaxLength = 40;

    public const int TechnicianNotesMaxLength = 4000;

    public const int SearchMinLength = 2;

    public const int SearchMaxLength = 80;

    public const decimal MaxQuantity = 99_999.999m;

    public const int MaxDecimals = 3;

    /// <summary>Trims and checks <paramref name="max"/>; whitespace-only becomes null (BR-08, BR-13).</summary>
    public static string? OptionalText(string? value, int max, string field, string label, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (trimmed is { Length: > 0 } && trimmed.Length > max)
        {
            errors[field] = [$"{label} must be {max} characters or fewer."];

            return null;
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Trims and checks 1 to <paramref name="max"/> characters.</summary>
    public static string? RequiredText(string? value, int max, string field, string label, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            errors[field] = [$"{label} is required."];

            return null;
        }

        if (trimmed.Length > max)
        {
            errors[field] = [$"{label} must be {max} characters or fewer."];

            return null;
        }

        return trimmed;
    }

    /// <summary>A quantity of 0 to 99999.999 with at most three decimals; zero is valid only when <paramref name="allowZero"/>.</summary>
    public static decimal? Quantity(decimal? value, bool allowZero, string field, Dictionary<string, string[]> errors)
    {
        if (value is not { } quantity)
        {
            errors[field] = ["Enter a quantity."];

            return null;
        }

        if (quantity < 0 || quantity > MaxQuantity || (quantity == 0 && !allowZero))
        {
            errors[field] = [allowZero
                ? $"Enter a quantity between 0 and {MaxQuantity:0.###}."
                : $"Enter a quantity greater than 0 and up to {MaxQuantity:0.###}."];

            return null;
        }

        if (Decimals(quantity) > MaxDecimals)
        {
            errors[field] = [$"Use at most {MaxDecimals} decimals."];

            return null;
        }

        return quantity;
    }

    /// <summary>The lookup text trimmed, 2 to 80 characters (BR-10); null with an error otherwise.</summary>
    public static string? Search(string? value, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (trimmed is null || trimmed.Length < SearchMinLength || trimmed.Length > SearchMaxLength)
        {
            errors["search"] = [$"Enter {SearchMinLength} to {SearchMaxLength} characters to search."];

            return null;
        }

        return trimmed;
    }

    /// <summary>Whole seconds of a closed time entry.</summary>
    public static int WholeSeconds(DateTimeOffset start, DateTimeOffset end) =>
        (int)Math.Max(0, Math.Floor((end - start).TotalSeconds));

    /// <summary>Significant decimals of <paramref name="value"/>; trailing zeros do not count.</summary>
    public static int Decimals(decimal value)
    {
        var normalized = value / 1.0000000000000000000000000000m;

        return (decimal.GetBits(normalized)[3] >> 16) & 0xFF;
    }
}
