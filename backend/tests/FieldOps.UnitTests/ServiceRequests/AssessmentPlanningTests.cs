using System.Globalization;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.ServiceRequests;

/// <summary>schedule-assessment BR-05 slot states, BR-06 workload, BR-08/BR-11 slot rules and BR-13/BR-14 email text.</summary>
public class AssessmentPlanningTests
{
    private const string Zone = "America/Chicago";

    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById(Zone);

    // Wednesday 2026-09-30 (CDT, UTC-5): window 08:00-17:00 local = 13:00-22:00Z with a 12:00-13:00 local break = 17:00-18:00Z.
    private static TechnicianSchedule Schedule(
        IEnumerable<AvailabilityOverride>? exceptions = null, IEnumerable<AssignedVisit>? visits = null, bool weekdays = false) =>
        new(
            TechnicianStatus.Active,
            Zone,
            weekdays
                ? [.. Enumerable.Range(1, 5).Select(day => new AvailabilitySlot(day, new TimeOnly(8, 0), new TimeOnly(17, 0), 100, []))]
                : [new AvailabilitySlot(3, new TimeOnly(8, 0), new TimeOnly(17, 0), 100, [new TimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))])],
            [.. exceptions ?? []],
            [.. visits ?? []]);

    private static DateTimeOffset Utc(string hourUtc) =>
        DateTimeOffset.Parse($"2026-09-30T{hourUtc}:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static SlotCommitment Commitment(string from, string to, Guid? assessmentId = null, string kind = CommitmentKinds.Assessment) =>
        new(Utc(from), Utc(to), kind, "Drain cleaning", assessmentId);

    // AC-06: first match wins (time off, conflict, available, available after, outside) and the rescheduled assessment is ignored.
    [Fact]
    public void Evaluate_FirstMatchWinsAndSearchesWholeHoursInOrganizationTime()
    {
        var own = Guid.NewGuid();
        var timeOff = new[] { new AvailabilityOverride(Utc("14:00"), Utc("16:00"), false) };

        SlotState Eval(string from, string to, IEnumerable<AvailabilityOverride>? exceptions = null, params SlotCommitment[] commitments) =>
            AssessmentSlotEvaluator.Evaluate(Schedule(exceptions), commitments, Utc(from), Utc(to), Chicago, own);

        Assert.Equal(SlotStates.Available, Eval("15:00", "16:00").State);

        // Time off wins over a conflict at the same time; neither is a warning.
        var off = Eval("15:00", "16:00", timeOff, Commitment("15:30", "16:30"));
        Assert.Equal(SlotStates.TimeOff, off.State);
        Assert.True(off.Blocking);

        var conflict = Eval("15:00", "16:00", null, Commitment("16:00", "17:00"), Commitment("15:30", "16:30"), Commitment("15:45", "16:15", kind: CommitmentKinds.Visit));
        Assert.Equal(SlotStates.Conflict, conflict.State);
        Assert.Equal((Utc("15:30"), Utc("16:30")), (conflict.From, conflict.To));
        Assert.True(conflict.Blocking);

        // The assessment being rescheduled never conflicts with itself.
        Assert.Equal(SlotStates.Available, Eval("15:00", "16:00", null, Commitment("15:30", "16:30", own)).State);

        // A slot inside the break, before the window and after the window: warnings only.
        var afterBreak = Eval("17:00", "18:00");
        Assert.Equal((SlotStates.AvailableAfter, Utc("18:00"), false), (afterBreak.State, afterBreak.AvailableAfter, afterBreak.Blocking));
        Assert.Equal(Utc("19:00"), Eval("17:00", "18:00", null, Commitment("18:00", "19:00")).AvailableAfter);
        Assert.Equal(Utc("13:00"), Eval("12:00", "13:00").AvailableAfter);
        Assert.Equal(SlotStates.OutsideAvailability, Eval("22:00", "23:00").State);

        // The hour search uses the organization zone: with UTC the same schedule yields the same instant.
        var utcSearch = AssessmentSlotEvaluator.Evaluate(Schedule(), [], Utc("12:00"), Utc("13:00"), TimeZoneInfo.Utc);
        Assert.Equal(Utc("13:00"), utcSearch.AvailableAfter);
        Assert.Equal(
            [SlotStates.Available, SlotStates.AvailableAfter, SlotStates.OutsideAvailability, SlotStates.Conflict, SlotStates.TimeOff],
            new[] { SlotStates.TimeOff, SlotStates.Conflict, SlotStates.Available, SlotStates.OutsideAvailability, SlotStates.AvailableAfter }
                .OrderBy(AssessmentSlotEvaluator.Rank));
    }

    // AC-07: visits and scheduled assessments count, the rescheduled assessment and cancelled visits do not.
    [Fact]
    public void Workload_CountsVisitsAndAssessmentsExcludingRescheduledOne()
    {
        var own = Guid.NewGuid();
        var (from, to) = BranchTime.Week(Utc("15:00"), Chicago);
        var visits = new[]
        {
            new AssignedVisit(VisitStatus.Scheduled, Utc("15:00").AddDays(-1), Utc("17:00").AddDays(-1), null),
            new AssignedVisit(VisitStatus.Completed, Utc("15:00"), Utc("16:00"), null),
            new AssignedVisit(VisitStatus.Cancelled, Utc("15:00"), Utc("16:00"), null),
        };
        var commitments = new[]
        {
            Commitment("18:00", "19:00"),
            Commitment("20:00", "21:00", own),
            Commitment("20:00", "21:00", kind: CommitmentKinds.Visit),
        };

        var workload = AssessmentSlotEvaluator.Workload(Schedule(visits: visits, weekdays: true), commitments, from, to, own);

        // 120 + 60 minutes of visits and 60 of assessments over 5 x 540 available minutes.
        Assert.Equal((3, 240d, 9, WorkloadStates.Percent), (workload.Jobs, workload.ScheduledMinutes, workload.Percent, workload.State));
        Assert.Equal(
            WorkloadStates.NoAvailability,
            AssessmentSlotEvaluator.Workload(Schedule(visits: visits) with { Slots = [] }, [Commitment("18:00", "19:00")], from, to).State);
    }

    // BR-08, BR-11: start on the hour, listed duration; both in organization time.
    [Theory]
    [InlineData("2026-10-06T09:30", "2026-10-06T10:30", "start")]
    [InlineData("2026-10-06T09:00", "2026-10-06T09:45", "end")]
    [InlineData("2026-10-06T09:00", "2026-10-06T10:00", null)]
    [InlineData("2026-10-06T08:00", "2026-10-06T16:00", null)]
    public void AssessmentSlotRules_RequireWholeHourStartAndListedDuration(string start, string end, string? errorKey)
    {
        var resolved = AssessmentSlotRules.TryResolve(
            start, end, Chicago, Utc("12:00"), out _, out _, out var errors);

        Assert.Equal(errorKey is null, resolved);
        string[] expected = errorKey is null ? [] : [errorKey];
        Assert.Equal(expected, errors.Keys.ToArray());
    }

    // BR-13, BR-14: the three texts and subjects in organization time, encoded HTML, optional phone line.
    [Fact]
    public void AssessmentEmailComposer_UsesBr13TextSubjectAndEncodesValues()
    {
        AssessmentEmail Email(AssessmentEmailKind kind, string? first = "Pat", string? phone = "+1 555 0100") =>
            new(Guid.NewGuid(), "REQ-7", kind, "pat@example.com", first, "Acme <Plumbing>", phone, Zone, Utc("14:00"), "Tina Tech");

        var scheduled = AssessmentEmailComposer.Compose(Email(AssessmentEmailKind.Scheduled));
        Assert.Equal("Acme <Plumbing>: assessment visit for REQ-7", scheduled.Subject);
        Assert.Contains(
            "Hi Pat,\n\nYour assessment visit is scheduled for Wed, Sep 30 between 9:00 AM and 10:00 AM. Tina Tech will inspect the issue before we prepare your quote.",
            scheduled.TextBody.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("Questions? Call us at +1 555 0100.", scheduled.TextBody);
        Assert.Contains("Acme &lt;Plumbing&gt;", scheduled.HtmlBody);
        Assert.DoesNotContain("<Plumbing>", scheduled.HtmlBody);

        var rescheduled = AssessmentEmailComposer.Compose(Email(AssessmentEmailKind.Rescheduled, first: null, phone: null));
        Assert.Equal("Acme <Plumbing>: assessment visit rescheduled for REQ-7", rescheduled.Subject);
        Assert.Contains("Hi there,", rescheduled.TextBody);
        Assert.Contains("has been rescheduled to Wed, Sep 30 between 9:00 AM and 10:00 AM.", rescheduled.TextBody);
        Assert.DoesNotContain("Questions?", rescheduled.TextBody);

        var cancelled = AssessmentEmailComposer.Compose(Email(AssessmentEmailKind.Cancelled));
        Assert.Equal("Acme <Plumbing>: assessment visit cancelled for REQ-7", cancelled.Subject);
        Assert.Contains(
            "Your assessment visit on Wed, Sep 30 between 9:00 AM and 10:00 AM has been cancelled. We'll contact you if we need to arrange another visit.",
            cancelled.TextBody);
    }
}
