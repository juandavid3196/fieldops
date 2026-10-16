using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Reports;

internal sealed class JobsOnTimeProvider(IReportsStore store) : IReportDataProvider
{
    public string Key => ReportKeys.JobsOnTime;

    public async Task<ReportResult> LoadAsync(ReportContext context, CancellationToken cancellationToken) =>
        JobsOnTimeBuilder.Build(await store.LoadJobsAsync(context, cancellationToken), context);
}

/// <summary>Completed jobs and on-time performance (reports-workspace BR-11): visits scheduled in the period.</summary>
public static class JobsOnTimeBuilder
{
    private const string Unassigned = "Unassigned";

    private sealed record Line(JobVisit Visit, string Group, ArrivalResult Result, int? ActualMinutes, int? Variance);

    public static string ResultCode(ArrivalResult result) => result switch
    {
        ArrivalResult.OnTime => "on_time",
        ArrivalResult.Late => "late",
        ArrivalResult.NotStarted => "not_started",
        _ => "not_due",
    };

    public static string ResultLabel(ArrivalResult result) => result switch
    {
        ArrivalResult.OnTime => "On time",
        ArrivalResult.Late => "Late",
        ArrivalResult.NotStarted => "Not started",
        _ => "Not due yet",
    };

    public static string GroupLabel(string group) => group switch
    {
        "scheduled" => "Scheduled",
        "active" => "Active",
        _ => "Completed",
    };

    /// <summary>BR-11: actual minutes (completed minus started minus pauses) and the variance against the estimate, whole minutes.</summary>
    public static (int? Actual, int? Variance) Duration(JobVisit visit, string group)
    {
        if (group != "completed" || visit.ActualStartedAt is not { } started || visit.ActualCompletedAt is not { } completed)
        {
            return (null, null);
        }

        var actual = (int)Math.Round(
            Math.Max(0d, (completed - started).TotalMinutes - visit.PauseSeconds / 60d), MidpointRounding.AwayFromZero);

        return (actual, visit.EstimatedMinutes is { } estimate ? actual - estimate : null);
    }

    public static ReportResult Build(JobsSource source, ReportContext context)
    {
        var current = Window(source, context, context.CurrentStart, context.CurrentEnd, context.Reference);
        var previous = Window(source, context, context.PreviousStart, context.CurrentStart, context.CurrentStart);

        var rows = current.Select(line => ToRow(line, context)).ToList();
        var now = Metrics(current);
        var before = Metrics(previous);
        var jobsNow = source.OrderCompletions.Count(at => at >= context.CurrentStart && at < context.CurrentEnd);
        var jobsBefore = source.OrderCompletions.Count(at => at >= context.PreviousStart && at < context.CurrentStart);

        var totals = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = current.Count,
            ["statusLabel"] = now.Completed,
            ["onTimeLabel"] = now.OnTime,
            ["rows"] = current.Count,
            ["completed"] = now.Completed,
            ["onTime"] = now.OnTime,
            ["eligible"] = now.Eligible,
        };

        return new ReportResult(
            [
                ReportRules.Count("scheduledVisits", "Scheduled visits", current.Count, previous.Count),
                ReportRules.Count("completedVisits", "Completed visits", now.Completed, before.Completed),
                ReportRules.Percent("completionRate", "Completion rate", now.CompletionRate, before.CompletionRate),
                ReportRules.Percent("onTimeArrival", "On-time arrival", now.OnTimePercent, before.OnTimePercent),
                ReportRules.MinutesMetric("avgDurationVariance", "Avg duration variance", now.AverageVariance, before.AverageVariance),
                ReportRules.Count("jobsCompleted", "Jobs completed", jobsNow, jobsBefore),
            ],
            null,
            BuildChart(current, context),
            rows,
            totals);
    }

    private sealed record Metric(int Completed, int Eligible, int OnTime, int? CompletionRate, int? OnTimePercent, int? AverageVariance);

    private static Metric Metrics(List<Line> lines)
    {
        var completed = lines.Count(line => line.Group == "completed");
        var eligible = lines.Count(line => line.Result != ArrivalResult.NotDue);
        var onTime = lines.Count(line => line.Result == ArrivalResult.OnTime);
        var variances = lines.Where(line => line.Variance is not null).Select(line => line.Variance!.Value).ToList();

        return new Metric(
            completed,
            eligible,
            onTime,
            OperationsDashboardRules.Percent(completed, lines.Count),
            OperationsDashboardRules.Percent(onTime, eligible),
            variances.Count == 0 ? null : (int)Math.Round(variances.Average(), MidpointRounding.AwayFromZero));
    }

    private static List<Line> Window(JobsSource source, ReportContext context, DateTimeOffset start, DateTimeOffset end, DateTimeOffset reference)
    {
        var query = context.Query;
        var lines = new List<Line>();

        foreach (var visit in source.Visits.Where(item => item.ScheduledStart >= start && item.ScheduledStart < end))
        {
            var group = OperationsDashboardRules.StatusGroup(visit.Status);
            var result = OperationsDashboardRules.Arrival(
                new DashboardArrival(visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt), reference);

            if ((query.Status != "all" && group != query.Status) || (query.OnTime != "all" && ResultCode(result) != query.OnTime))
            {
                continue;
            }

            var (actual, variance) = Duration(visit, group);

            lines.Add(new Line(visit, group, result, actual, variance));
        }

        return lines;
    }

    private static ReportRow ToRow(Line line, ReportContext context)
    {
        var visit = line.Visit;
        var order = RequestCardRules.DisplayNumber(context.Organization.WorkOrderPrefix, visit.OrderNumber);
        var job = $"{order} · Visit {visit.VisitNumber}";
        var arrival = new DashboardArrival(visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt);
        var technician = visit.TechnicianName ?? Unassigned;
        var number = (visit.OrderNumber * 10000) + visit.VisitNumber;

        var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = job,
            ["scheduledStart"] = visit.ScheduledStart.ToUniversalTime(),
            ["customer"] = visit.CustomerName,
            ["service"] = visit.Title,
            ["technician"] = technician,
            ["status"] = line.Group,
            ["statusLabel"] = GroupLabel(line.Group),
            ["arrivalLimit"] = OperationsDashboardRules.ArrivalLimit(arrival).ToUniversalTime(),
            ["actualStart"] = visit.ActualStartedAt?.ToUniversalTime(),
            ["onTime"] = ResultCode(line.Result),
            ["onTimeLabel"] = ResultLabel(line.Result),
            ["estimatedMinutes"] = visit.EstimatedMinutes,
            ["actualMinutes"] = line.ActualMinutes,
            ["varianceMinutes"] = line.Variance,
            ["branch"] = context.BranchName(visit.BranchId),
        };

        var sort = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = number,
            ["status"] = GroupLabel(line.Group),
            ["onTime"] = ResultLabel(line.Result),
            ["variance"] = line.Variance,
        };

        var technicianId = visit.TechnicianId is { } id && visit.TechnicianBranchId is { } branch && context.Scope.Contains(branch) ? id : (Guid?)null;

        return new ReportRow(
            visit.VisitId,
            order,
            number,
            cells,
            ReportRules.Links(context.Role, job: visit.OrderId, visit: visit.VisitId, customer: visit.CustomerId, technician: technicianId),
            sort);
    }

    // Stacked bars per period bucket by the local scheduled date: on time, late and not started.
    private static ReportChart BuildChart(List<Line> lines, ReportContext context)
    {
        var dated = lines.Select(line => (Date: OrganizationTime.LocalDate(line.Visit.ScheduledStart, context.Zone), line.Result)).ToList();

        decimal Count(BusinessOverview.BusinessBucket bucket, ArrivalResult result) =>
            dated.Count(item => item.Result == result && item.Date >= bucket.Start && item.Date <= bucket.End);

        return new ReportChart(
            "stacked_series",
            [new ReportSeries("on_time", "On time"), new ReportSeries("late", "Late"), new ReportSeries("not_started", "Not started")],
            [.. context.Period.Buckets.Select(bucket => new ReportPoint(
                ReportRules.BucketLabel(bucket),
                ReportRules.Day(bucket.Start),
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["on_time"] = Count(bucket, ArrivalResult.OnTime),
                    ["late"] = Count(bucket, ArrivalResult.Late),
                    ["not_started"] = Count(bucket, ArrivalResult.NotStarted),
                }))]);
    }
}
