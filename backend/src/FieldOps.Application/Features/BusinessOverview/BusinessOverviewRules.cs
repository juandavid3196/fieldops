using System.Globalization;
using FieldOps.Application.Features.BillingReview;

namespace FieldOps.Application.Features.BusinessOverview;

public static class BusinessOverviewMessages
{
    public const decimal MarginTarget = 40.0m;

    public const decimal OvertimeAlertPercent = 10.0m;

    public const int MaxPeriodDays = 366;

    public const string DateInvalid = "Enter a valid date.";

    public const string RangeInvalid = "The end date must be on or after the start date.";

    public const string FutureDate = "The end date cannot be in the future.";

    public const string RangeTooLong = "The period cannot be longer than 366 days.";

    public const string BranchInvalid = "Select a valid branch.";

    public const string CompareInvalid = "Compare must be true or false.";
}

public sealed record BusinessBucket(DateOnly Start, DateOnly End);

/// <summary>The resolved period, its previous period and the chart buckets (BR-03). All dates are organization-local.</summary>
public sealed record BusinessPeriod(
    DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo, string Granularity, IReadOnlyList<BusinessBucket> Buckets)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>A validated request: the period, the optional branch and, for the export, the comparison flag.</summary>
public sealed record BusinessRequest(DateOnly From, DateOnly To, Guid? BranchId, bool Compare);

/// <summary>Pure rules of the business overview: query parsing, period, margins, refunds and changes. No EF, no clock.</summary>
public static class BusinessOverviewRules
{
    /// <summary>Parses the text values; the range is checked against today by <see cref="ValidateRange"/>.</summary>
    public static BillingOutcome<BusinessRequest> Parse(BusinessOverviewQueryText text, bool withCompare)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var from = Date(text.From, "from", errors);
        var to = Date(text.To, "to", errors);
        Guid? branchId = null;

        if (!string.IsNullOrWhiteSpace(text.BranchId))
        {
            if (Guid.TryParse(text.BranchId.Trim(), out var parsed) && parsed != Guid.Empty)
            {
                branchId = parsed;
            }
            else
            {
                errors["branchId"] = [BusinessOverviewMessages.BranchInvalid];
            }
        }

        var compare = true;

        if (withCompare && !string.IsNullOrWhiteSpace(text.Compare))
        {
            switch (text.Compare.Trim().ToLowerInvariant())
            {
                case "true":
                    break;
                case "false":
                    compare = false;
                    break;
                default:
                    errors["compare"] = [BusinessOverviewMessages.CompareInvalid];
                    break;
            }
        }

        return errors.Count > 0 || from is not { } start || to is not { } end
            ? new BillingOutcome<BusinessRequest>.Invalid(errors)
            : new BillingOutcome<BusinessRequest>.Succeeded(new BusinessRequest(start, end, branchId, compare));
    }

    /// <summary>The range errors that need today: reversed, future and longer than 366 days (BR-03).</summary>
    public static Dictionary<string, string[]> ValidateRange(DateOnly from, DateOnly to, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (from > to)
        {
            errors["to"] = [BusinessOverviewMessages.RangeInvalid];
        }
        else if (to > today)
        {
            errors["to"] = [BusinessOverviewMessages.FutureDate];
        }
        else if (to.DayNumber - from.DayNumber + 1 > BusinessOverviewMessages.MaxPeriodDays)
        {
            errors["to"] = [BusinessOverviewMessages.RangeTooLong];
        }

        return errors;
    }

    /// <summary>BR-03: the previous period has the same length and ends the day before <paramref name="from"/>; granularity day, week or month.</summary>
    public static BusinessPeriod ResolvePeriod(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        var granularity = days <= 31 ? "day" : days <= 92 ? "week" : "month";
        var buckets = new List<BusinessBucket>();

        for (var cursor = from; cursor <= to;)
        {
            var natural = granularity switch
            {
                "day" => cursor,
                "week" => cursor.AddDays((7 - (int)cursor.DayOfWeek) % 7),
                _ => new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month)),
            };
            var end = natural < to ? natural : to;

            buckets.Add(new BusinessBucket(cursor, end));
            cursor = end.AddDays(1);
        }

        return new BusinessPeriod(from, to, from.AddDays(-days), from.AddDays(-1), granularity, buckets);
    }

    /// <summary>BR-05: profit ÷ revenue × 100, one decimal; null when the revenue is 0.</summary>
    public static decimal? Margin(decimal profit, decimal revenue) =>
        revenue == 0m ? null : Math.Round(profit / revenue * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>BR-09: below target only with revenue and a margin under the target.</summary>
    public static bool BelowTarget(decimal revenue, decimal? margin) =>
        revenue > 0m && margin is { } value && value < BusinessOverviewMessages.MarginTarget;

    /// <summary>BR-11, BR-17: (current − previous) ÷ |previous| × 100, one decimal; null when the previous value is 0.</summary>
    public static decimal? ChangePercent(decimal current, decimal previous) =>
        previous == 0m ? null : Math.Round((current - previous) / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>BR-13: the allocation reduced by the refunded share of its payment, rounded to cents per allocation.</summary>
    public static decimal NetAllocation(decimal allocation, decimal paymentAmount, decimal refunded) =>
        paymentAmount <= 0m
            ? 0m
            : Math.Round(allocation * (paymentAmount - refunded) / paymentAmount, 2, MidpointRounding.AwayFromZero);

    /// <summary>BR-06: accepted ÷ sent × 100 as an integer; null when nothing was sent.</summary>
    public static int? ConversionPercent(int accepted, int sent) =>
        sent == 0 ? null : (int)Math.Round((double)accepted / sent * 100, MidpointRounding.AwayFromZero);

    /// <summary>BR-10: export money has two decimals and no symbol.</summary>
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static DateOnly? Date(string? text, string key, Dictionary<string, string[]> errors)
    {
        if (DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[key] = [BusinessOverviewMessages.DateInvalid];

        return null;
    }
}
