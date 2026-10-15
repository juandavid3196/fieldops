using FieldOps.Application.Features.Notifications;

namespace FieldOps.UnitTests.Notifications;

public class NotificationRulesTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // BR-11, BR-13, BR-14: query parsing, and date windows as whole local days of the organization time zone (half-open UTC ranges).
    [Fact]
    public void Rules_ParseTheQueryAndBuildLocalDayWindows()
    {
        foreach (var (text, days, from, to) in new (string, int, string?, string?)[]
        {
            ("any", 0, null, null),
            ("today", 1, "2026-10-15T04:00:00.0000000+00:00", "2026-10-16T04:00:00.0000000+00:00"),
            ("7d", 7, "2026-10-09T04:00:00.0000000+00:00", "2026-10-16T04:00:00.0000000+00:00"),
            ("30d", 30, "2026-09-16T04:00:00.0000000+00:00", "2026-10-16T04:00:00.0000000+00:00"),
        })
        {
            Assert.True(NotificationRules.TryParseDate(text, out var parsed));
            Assert.Equal(days, parsed);

            var window = NotificationRules.Window(parsed, new DateOnly(2026, 10, 15), NewYork);

            Assert.Equal(from, window?.From.ToString("O"));
            Assert.Equal(to, window?.To.ToString("O"));
        }

        // The day the clocks go back is 25 hours long.
        var (start, end) = NotificationRules.Today(new DateOnly(2026, 11, 1), NewYork);
        Assert.Equal(TimeSpan.FromHours(25), end - start);

        Assert.False(NotificationRules.TryParseDate("90d", out _));
        Assert.True(NotificationRules.TryParseTab(null, out var unread) && !unread);
        Assert.True(NotificationRules.TryParseTab("unread", out unread) && unread);
        Assert.False(NotificationRules.TryParseTab("drafts", out _));
        Assert.True(NotificationRules.TryParseType(" payment ", out var type));
        Assert.Equal("payment", type);
        Assert.False(NotificationRules.TryParseType("work_order", out _));
    }
}
