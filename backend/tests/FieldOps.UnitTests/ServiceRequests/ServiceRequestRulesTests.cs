using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.ServiceRequests;

public class ServiceRequestRulesTests
{
    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    // FR-06, AC-09, AC-10, AC-18: the transition table, including implied transitions and closed statuses.
    [Theory]
    [InlineData(RequestStatus.New, RequestAction.StartReview, true, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.NeedsReview, RequestAction.StartReview, false, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.New, RequestAction.Assign, true, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.AssessmentScheduled, RequestAction.Assign, true, RequestStatus.AssessmentScheduled)]
    [InlineData(RequestStatus.New, RequestAction.RequestInformation, true, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.ReadyForQuote, RequestAction.RequestInformation, true, RequestStatus.ReadyForQuote)]
    [InlineData(RequestStatus.New, RequestAction.ScheduleAssessment, true, RequestStatus.AssessmentScheduled)]
    [InlineData(RequestStatus.NeedsReview, RequestAction.ScheduleAssessment, true, RequestStatus.AssessmentScheduled)]
    [InlineData(RequestStatus.AssessmentScheduled, RequestAction.ScheduleAssessment, false, RequestStatus.AssessmentScheduled)]
    [InlineData(RequestStatus.AssessmentScheduled, RequestAction.CancelAssessment, true, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.NeedsReview, RequestAction.CancelAssessment, false, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.New, RequestAction.MarkReadyForQuote, true, RequestStatus.ReadyForQuote)]
    [InlineData(RequestStatus.NeedsReview, RequestAction.MarkReadyForQuote, true, RequestStatus.ReadyForQuote)]
    [InlineData(RequestStatus.AssessmentScheduled, RequestAction.MarkReadyForQuote, false, RequestStatus.AssessmentScheduled)]
    [InlineData(RequestStatus.Cancelled, RequestAction.MarkReadyForQuote, false, RequestStatus.Cancelled)]
    [InlineData(RequestStatus.AssessmentScheduled, RequestAction.CompleteAssessment, true, RequestStatus.ReadyForQuote)]
    [InlineData(RequestStatus.NeedsReview, RequestAction.CompleteAssessment, false, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.ReadyForQuote, RequestAction.MoveToReview, true, RequestStatus.NeedsReview)]
    [InlineData(RequestStatus.New, RequestAction.MoveToReview, false, RequestStatus.New)]
    [InlineData(RequestStatus.ReadyForQuote, RequestAction.Cancel, true, RequestStatus.Cancelled)]
    [InlineData(RequestStatus.Quoted, RequestAction.Cancel, false, RequestStatus.Quoted)]
    [InlineData(RequestStatus.Converted, RequestAction.Assign, false, RequestStatus.Converted)]
    [InlineData(RequestStatus.Cancelled, RequestAction.Cancel, false, RequestStatus.Cancelled)]
    public void TryApply_FollowsTheTransitionTable(
        RequestStatus from, RequestAction action, bool allowed, RequestStatus expectedTo)
    {
        Assert.Equal(allowed, RequestTransitions.TryApply(from, action, out var to));
        Assert.Equal(expectedTo, to);
    }

    // AC-01, BR-04: title fallbacks, card date kind and avatar initials.
    [Fact]
    public void CardRules_DeriveTitleDateKindAndInitials()
    {
        Assert.Equal("Drain cleaning", RequestCardRules.Title("Drain cleaning", "Plumbing", "x"));
        Assert.Equal("Plumbing", RequestCardRules.Title(null, "Plumbing", "x"));
        Assert.Equal("First line", RequestCardRules.Title(null, null, "First line\nsecond line"));
        Assert.Equal(new string('a', 60) + "…", RequestCardRules.Title(null, null, new string('a', 80)));

        var created = At("2026-10-01T10:00:00Z");
        var preferred = At("2026-10-05T13:00:00Z");
        var assessment = At("2026-10-04T15:00:00Z");

        Assert.Equal(("assessment", assessment), RequestCardRules.CardDate(RequestStatus.AssessmentScheduled, assessment, "asap", preferred, created));
        Assert.Equal(("preferred", preferred), RequestCardRules.CardDate(RequestStatus.New, null, "date", preferred, created));
        Assert.Equal(("asap", null), RequestCardRules.CardDate(RequestStatus.New, null, "asap", null, created));
        Assert.Equal(("flexible", null), RequestCardRules.CardDate(RequestStatus.New, null, "flexible", null, created));
        Assert.Equal(("created", created), RequestCardRules.CardDate(RequestStatus.New, null, null, null, created));

        Assert.Equal("AL", RequestCardRules.Initials("ada king lovelace"));
        Assert.Equal("O", RequestCardRules.Initials("Ola"));
        Assert.Equal(string.Empty, RequestCardRules.Initials(null));
    }

    // AC-06, AC-07, BR-06, BR-07: delta math (null previous, rounding, points) and request-number search parsing.
    [Fact]
    public void MetricMathAndSearch_FollowTheirRules()
    {
        Assert.Null(RequestMetricMath.DeltaPercent(5, 0));
        Assert.Equal(50, RequestMetricMath.DeltaPercent(3, 2));
        Assert.Equal(-33, RequestMetricMath.DeltaPercent(2, 3));
        Assert.Equal(0, RequestMetricMath.Rate(0, 0));
        Assert.Equal(67, RequestMetricMath.Rate(2, 3));
        Assert.Null(RequestMetricMath.DeltaPoints(40, 0));
        Assert.Equal(7, RequestMetricMath.DeltaPoints(47, 40));

        Assert.True(RequestSearch.TryParseNumber("REQ-1048", "REQ", out var number));
        Assert.Equal(1048, number);
        Assert.True(RequestSearch.TryParseNumber(" req-7 ", "REQ", out number));
        Assert.Equal(7, number);
        Assert.True(RequestSearch.TryParseNumber("1048", "REQ", out number));
        Assert.False(RequestSearch.TryParseNumber("ABC-1048", "REQ", out _));
        Assert.False(RequestSearch.TryParseNumber("pipe", "REQ", out _));
        Assert.Equal("%50\\%\\_a\\\\b%", RequestSearch.ContainsPattern("50%_a\\b"));
    }

    // AC-16, BR-15: future start, same local day, at most 8 hours and DST-skipped local times.
    [Fact]
    public void AssessmentSlotRules_ValidateInOrganizationTime()
    {
        var chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var now = At("2026-03-01T12:00:00Z");

        Assert.True(AssessmentSlotRules.TryResolve("2026-03-02T09:00", "2026-03-02T17:00", chicago, now, out var start, out var end, out _));
        Assert.Equal(At("2026-03-02T15:00:00Z"), start);
        Assert.Equal(At("2026-03-02T23:00:00Z"), end);

        string[] Keys(string from, string to) =>
            AssessmentSlotRules.TryResolve(from, to, chicago, now, out _, out _, out var errors) ? [] : [.. errors.Keys];

        Assert.Equal(["start"], Keys("2026-02-28T09:00", "2026-02-28T10:00"));
        Assert.Equal(["end"], Keys("2026-03-02T09:00", "2026-03-02T08:00"));
        Assert.Equal(["end"], Keys("2026-03-02T09:00", "2026-03-03T01:00"));
        Assert.Equal(["end"], Keys("2026-03-02T08:00", "2026-03-02T16:01"));
        Assert.Equal(["start"], Keys("2026-03-08T02:30", "2026-03-08T03:30"));
        Assert.Equal(["start", "end"], Keys("not a date", "2026-03-02T10:00x"));
    }
}
