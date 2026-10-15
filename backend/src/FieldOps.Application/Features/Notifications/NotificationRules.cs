using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Notifications;

/// <summary>Query parsing and the organization-local date windows of the inbox (BR-11, BR-14).</summary>
public static class NotificationRules
{
    public const int SearchMaxLength = 100;

    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public static bool TryParseTab(string? value, out bool unreadOnly)
    {
        unreadOnly = false;

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "all", StringComparison.Ordinal))
        {
            return true;
        }

        unreadOnly = string.Equals(value.Trim(), "unread", StringComparison.Ordinal);

        return unreadOnly;
    }

    public static bool TryParseType(string? value, out string? type)
    {
        type = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();

        if (!NotificationCatalog.Types.Contains(trimmed, StringComparer.Ordinal))
        {
            return false;
        }

        type = trimmed;

        return true;
    }

    /// <summary>Days covered by a date option, counting today; 0 means any date.</summary>
    public static bool TryParseDate(string? value, out int days)
    {
        days = 0;

        switch (value?.Trim())
        {
            case null or "" or "any":
                return true;
            case "today":
                days = 1;

                return true;
            case "7d":
                days = 7;

                return true;
            case "30d":
                days = 30;

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A window of N days covers today and the previous N - 1 local days, as a half-open UTC range. Null for any date.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To)? Window(int days, DateOnly today, TimeZoneInfo zone) =>
        days <= 0
            ? null
            : (OrganizationTime.StartOfDayUtc(today.AddDays(1 - days), zone), OrganizationTime.StartOfDayUtc(today.AddDays(1), zone));

    /// <summary>The current local day as a half-open UTC range.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Today(DateOnly today, TimeZoneInfo zone) =>
        (OrganizationTime.StartOfDayUtc(today, zone), OrganizationTime.StartOfDayUtc(today.AddDays(1), zone));
}
