using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Dispatch;

/// <summary>BR-09 and BR-10 conflict classification, impact and ranking on plain data (UTC; Wednesday 2030-01-02).</summary>
public class DispatchConflictClassifierTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly DateTimeOffset Day = new(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private static TechnicianSchedule Schedule(params AvailabilityOverride[] exceptions) =>
        new(
            TechnicianStatus.Active,
            "UTC",
            [new AvailabilitySlot((int)DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(18, 0), 100, [new TimeRange(new TimeOnly(9, 30), new TimeOnly(10, 30))])],
            [.. exceptions],
            []);

    private static DispatchTechnician Tech(
        string name, IEnumerable<Guid>? skills = null, IEnumerable<DispatchCommitment>? commitments = null, params AvailabilityOverride[] exceptions) =>
        new(Guid.NewGuid(), name, Schedule(exceptions), skills?.ToHashSet() ?? [], [.. commitments ?? []]);

    private static DispatchCommitment Visit(Guid tech, int fromHour, int fromMinute, int toHour, int toMinute, VisitStatus status, Guid? id = null, string number = "WO-7") =>
        new(tech, CommitmentKinds.Visit, At(fromHour, fromMinute), At(toHour, toMinute), id ?? Guid.NewGuid(), status, number);

    // The availability codes are exclusive: time off, then break, then outside availability; a free slot passes with "Available".
    [Theory]
    [InlineData(9, 0, true, "time_off", "Time off")]
    [InlineData(9, 0, false, "break", "On break 9:30 – 10:30 AM")]
    [InlineData(19, 0, false, "outside_availability", "Outside availability")]
    [InlineData(11, 0, false, "", "Available")]
    public void Classify_ReturnsOneAvailabilityCodeInPriorityOrder(int hour, int minute, bool timeOff, string code, string label)
    {
        var technician = Tech("Ann", exceptions: timeOff ? [new AvailabilityOverride(At(8), At(18), false)] : []);

        var result = DispatchConflictClassifier.Classify(At(hour, minute), At(hour + 1, minute), [], [technician], Utc, null);

        Assert.Equal(code.Length == 0 ? [] : [code], result.Conflicts.Select(item => item.Code).ToArray());
        Assert.Equal(code.Length == 0, result.Checks[0].Availability.Passed);
        Assert.Equal(label, result.Checks[0].Availability.Label);
        Assert.Equal(technician.Id, result.Checks[0].TechnicianId);
        Assert.True(result.Checks[0].Overlap.Passed);
    }

    // Overlaps list each commitment with the work order or assessment text; the dispatched visit and finished or unscheduled visits are ignored.
    [Fact]
    public void Classify_ReportsOverlapsWithAssessmentsAndIgnoresTheDispatchedAndFinishedVisits()
    {
        var self = Guid.NewGuid();
        var ann = Guid.NewGuid();
        var technician = new DispatchTechnician(
            ann,
            "Ann",
            Schedule(),
            new HashSet<Guid>(),
            [
                Visit(ann, 11, 30, 12, 30, VisitStatus.Assigned),
                new DispatchCommitment(ann, CommitmentKinds.Assessment, At(11, 45), At(12, 15), null, null, null),
                Visit(ann, 11, 0, 13, 0, VisitStatus.Completed, number: "WO-8"),
                Visit(ann, 11, 0, 13, 0, VisitStatus.Approved, number: "WO-9"),
                Visit(ann, 11, 0, 13, 0, VisitStatus.Scheduled, self, "WO-1"),
                Visit(ann, 15, 0, 16, 0, VisitStatus.Assigned, number: "WO-10"),
            ]);

        var result = DispatchConflictClassifier.Classify(At(11), At(12, 30), [], [technician], Utc, self);

        Assert.Equal(
            ["Overlaps #WO-7 11:30 AM – 12:30 PM", "Overlaps assessment 11:45 AM – 12:15 PM"],
            result.Conflicts.Select(item => item.Label).ToArray());
        Assert.All(result.Conflicts, item => Assert.Equal("overlap", item.Code));
        Assert.False(result.Checks[0].Overlap.Passed);
        Assert.Equal("overlap", result.Checks[0].Overlap.Code);
        Assert.Equal("Overlaps #WO-7 11:30 AM – 12:30 PM", result.Checks[0].Overlap.Label);
        Assert.Equal("Available", result.Checks[0].Availability.Label);
    }

    // The union of the selected technicians' skills must cover the requirement; the gap is reported once for the selection.
    [Fact]
    public void Classify_ReportsMissingSkillsOnceForTheSelection_AndPassesWhenTheUnionCovers()
    {
        var welding = new DispatchSkill(Guid.NewGuid(), "Welding");
        var plumbing = new DispatchSkill(Guid.NewGuid(), "Plumbing");
        var weldingOnly = Tech("Ann", [welding.Id]);
        var plumbingOnly = Tech("Bob", [plumbing.Id]);
        var nothing = Tech("Cy");

        var covered = DispatchConflictClassifier.Classify(At(11), At(12), [welding, plumbing], [weldingOnly, plumbingOnly], Utc, null);
        var gap = DispatchConflictClassifier.Classify(At(11), At(12), [welding, plumbing], [weldingOnly, nothing], Utc, null);
        var none = DispatchConflictClassifier.Classify(At(11), At(12), [], [nothing], Utc, null);

        Assert.Empty(covered.Conflicts);
        Assert.Equal("Skills match (Plumbing, Welding)", covered.Skills.Label);
        var missing = Assert.Single(gap.Conflicts);
        Assert.Null(missing.TechnicianId);
        Assert.Equal("missing_skills", missing.Code);
        Assert.Equal("Missing skills: Plumbing", missing.Label);
        Assert.False(gap.Skills.Passed);
        Assert.Equal("No required skills", none.Skills.Label);
    }

    // Impact counts the proposed job, previous and next commitment of the day; ranking picks the conflict-free technician with the lowest load, ties by name.
    [Fact]
    public void ImpactAndRank_PickTheBestMatchByLoadThenName_AndNoneWhenEveryoneConflicts()
    {
        var busy = Guid.NewGuid();
        var light = Guid.NewGuid();
        var tied = Guid.NewGuid();
        var off = Guid.NewGuid();
        var candidates = new[]
        {
            new DispatchTechnician(busy, "Zoe", Schedule(), new HashSet<Guid>(), [Visit(busy, 8, 0, 9, 0, VisitStatus.Assigned), Visit(busy, 14, 0, 17, 0, VisitStatus.Assigned, number: "WO-9")]),
            new DispatchTechnician(light, "Ann", Schedule(), new HashSet<Guid>(), []),
            new DispatchTechnician(tied, "Bea", Schedule(), new HashSet<Guid>(), []),
            new DispatchTechnician(off, "Cy", Schedule(new AvailabilityOverride(At(8), At(18), false)), new HashSet<Guid>(), []),
        };

        var impact = DispatchConflictClassifier.Impact(candidates[0], At(11), At(12), At(0), At(0).AddDays(1), Utc, null);
        Assert.Equal(3, impact.Jobs);
        Assert.Equal(60d + 180d + 60d, impact.ScheduledMinutes);
        Assert.Equal(540d, impact.AvailableMinutes);
        Assert.Equal("Previous job: #WO-7 ends 9:00 AM", impact.Previous);
        Assert.Equal("Next job: #WO-9 starts 2:00 PM", impact.Next);
        Assert.Equal("No previous job", DispatchConflictClassifier.Impact(candidates[1], At(11), At(12), At(0), At(0).AddDays(1), Utc, null).Previous);

        var ranking = DispatchConflictClassifier.Rank(At(11), At(12), [], candidates, At(0), At(0).AddDays(1), Utc, null);

        Assert.Equal([light], ranking.Where(item => item.BestMatch).Select(item => item.TechnicianId).ToArray());
        Assert.Equal(1, ranking.Single(item => item.TechnicianId == off).ConflictCount);
        Assert.Equal(11, ranking.Single(item => item.TechnicianId == light).LoadPercent);
        Assert.Equal(56, ranking.Single(item => item.TechnicianId == busy).LoadPercent);

        var allConflict = DispatchConflictClassifier.Rank(At(19), At(20), [], candidates, At(0), At(0).AddDays(1), Utc, null);
        Assert.DoesNotContain(allConflict, item => item.BestMatch);
    }
}
