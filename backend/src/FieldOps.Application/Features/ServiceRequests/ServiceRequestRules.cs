using System.Globalization;
using FieldOps.Domain.Requests;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>Messages and limits of the requests pipeline (BR-06 to BR-18).</summary>
public static class ServiceRequestMessages
{
    public const int SearchMaxLength = 100;

    public const int BodyMaxLength = 2000;

    public const int ReasonMaxLength = 500;

    public const int MaxAttachments = 5;

    public const int MaxAssessmentHours = 8;

    public const string ConflictTitle = "This request changed. Refresh to see the latest.";

    public const string AssessmentNotStartedTitle = "This assessment hasn't started yet.";

    public const string AssigneeNotAllowed = "Choose a team member who can manage this request.";

    public const string BranchNotAllowed = "Choose a branch you have access to.";

    public const string AssigneeLacksBranch = "The assignee doesn't have access to this branch.";

    public const string NoEmail = "This customer has no email address.";

    public const string NoContact = "This request has no customer contact.";

    public const string BodyRequired = "This field is required.";

    public const string ReasonRequired = "This field is required.";

    public const string UrgencyInvalid = "Select standard, urgent or emergency.";

    public const string FilterInvalid = "Choose a valid filter.";

    public const string SearchTooLong = "Use 100 characters or fewer.";

    public const string StartInvalid = "Enter a valid start date and time.";

    public const string StartNotFuture = "The start must be in the future.";

    public const string EndInvalid = "Enter a valid end date and time.";

    public const string EndSameDay = "The end must be after the start on the same day.";

    public const string EndTooLong = "An assessment can last up to 8 hours.";

    public const string TechnicianNotAllowed = "Choose a technician from this request's branch.";

    public const string TechnicianBusy = "This technician already has an assessment at that time.";

    public const string TooManyAttachments = "A request can have up to 5 files.";

    public const string CustomerNotAllowed = "Choose a customer you have access to.";

    public const string ContactNotAllowed = "Choose a contact of this customer.";

    public const string PropertyNotAllowed = "Choose a property of this customer.";

    public const string PropertyBranchNotAllowed = "Choose a property in a branch you have access to.";

    public static string TooLong(int maxLength) =>
        string.Create(CultureInfo.InvariantCulture, $"Use {maxLength} characters or fewer.");
}

/// <summary>Pure card and detail derivations (BR-04).</summary>
public static class RequestCardRules
{
    public const int TitleMaxLength = 60;

    /// <summary>Service name, else category name, else the first description line cut at 60 characters plus an ellipsis (OD-08).</summary>
    public static string Title(string? serviceName, string? categoryName, string description)
    {
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            return serviceName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            return categoryName.Trim();
        }

        var firstLine = (description ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return firstLine.Length <= TitleMaxLength ? firstLine : firstLine[..TitleMaxLength].TrimEnd() + "…";
    }

    /// <summary>
    /// The card date kind and instant: the active assessment start on <c>assessment_scheduled</c>, then the
    /// preferred start (date mode), ASAP, flexible, and the creation date when there are no preferences.
    /// </summary>
    public static (string Kind, DateTimeOffset? Date) CardDate(
        RequestStatus status,
        DateTimeOffset? assessmentStart,
        string? dateMode,
        DateTimeOffset? preferredStart,
        DateTimeOffset createdAt)
    {
        if (status == RequestStatus.AssessmentScheduled && assessmentStart is { } start)
        {
            return ("assessment", start);
        }

        return dateMode switch
        {
            "date" when preferredStart is { } preferred => ("preferred", preferred),
            "asap" => ("asap", null),
            "flexible" => ("flexible", null),
            _ => ("created", createdAt),
        };
    }

    /// <summary>Initials of the first and last words of a name, upper case.</summary>
    public static string Initials(string? name)
    {
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(words[0][0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0])),
        };
    }

    public static string DisplayNumber(string prefix, long number) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{number}");
}

/// <summary>Metric comparison math (BR-07): whole-percent deltas and percentage points; null when the previous value is 0.</summary>
public static class RequestMetricMath
{
    public static int? DeltaPercent(int current, int previous) =>
        previous == 0
            ? null
            : (int)Math.Round((current - previous) * 100.0 / previous, MidpointRounding.AwayFromZero);

    public static int Rate(int numerator, int denominator) =>
        denominator <= 0
            ? 0
            : (int)Math.Round(numerator * 100.0 / denominator, MidpointRounding.AwayFromZero);

    public static int? DeltaPoints(int current, int previous) => previous == 0 ? null : current - previous;
}

/// <summary>Search term handling (BR-06): escaped contains pattern and an optional exact request number.</summary>
public static class RequestSearch
{
    public const char EscapeCharacter = '\\';

    public static string ContainsPattern(string term) =>
        "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    /// <summary><c>REQ-1048</c> (the organization prefix, any case) or <c>1048</c> match a request number exactly.</summary>
    public static bool TryParseNumber(string term, string prefix, out long number)
    {
        number = 0;
        var digits = term.Trim();
        var lead = prefix + "-";

        if (digits.StartsWith(lead, StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[lead.Length..];
        }

        return digits.Length is > 0 and <= 18
            && digits.All(char.IsAsciiDigit)
            && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number > 0;
    }
}

/// <summary>Organization-local time helpers: days, weekdays and local date-times as UTC instants.</summary>
public static class OrganizationTime
{
    private static readonly string[] LocalFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    public static TimeZoneInfo FindZone(string? timezone)
    {
        try
        {
            return string.IsNullOrWhiteSpace(timezone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The UTC instant of local midnight; a midnight inside a DST gap uses the offset an hour later.</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(local) ? zone.GetUtcOffset(local.AddHours(1)) : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateTimeOffset ToZone(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone);

    /// <summary>Parses <c>YYYY-MM-DDTHH:mm</c> as local organization time; a time skipped by DST is rejected.</summary>
    public static bool TryParseLocal(string? text, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        utc = default;

        if (string.IsNullOrWhiteSpace(text)
            || !DateTime.TryParseExact(
                text.Trim(), LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || zone.IsInvalidTime(local))
        {
            return false;
        }

        utc = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();

        return true;
    }
}

/// <summary>Assessment slot rules (BR-15): future start, same local day, at most 8 hours.</summary>
public static class AssessmentSlotRules
{
    public static bool TryResolve(
        string? startText,
        string? endText,
        TimeZoneInfo zone,
        DateTimeOffset now,
        out DateTimeOffset start,
        out DateTimeOffset end,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        start = default;
        end = default;

        var startOk = OrganizationTime.TryParseLocal(startText, zone, out start);
        var endOk = OrganizationTime.TryParseLocal(endText, zone, out end);

        if (!startOk)
        {
            errors["start"] = [ServiceRequestMessages.StartInvalid];
        }
        else if (start <= now)
        {
            errors["start"] = [ServiceRequestMessages.StartNotFuture];
        }

        if (!endOk)
        {
            errors["end"] = [ServiceRequestMessages.EndInvalid];
        }
        else if (startOk)
        {
            if (end <= start || OrganizationTime.LocalDate(end, zone) != OrganizationTime.LocalDate(start, zone))
            {
                errors["end"] = [ServiceRequestMessages.EndSameDay];
            }
            else if (end - start > TimeSpan.FromHours(ServiceRequestMessages.MaxAssessmentHours))
            {
                errors["end"] = [ServiceRequestMessages.EndTooLong];
            }
        }

        return errors.Count == 0;
    }
}
