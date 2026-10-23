using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Team;

/// <summary>Raw query of GET /team/technicians/{id}/job-history (BR-17); strings so bad input maps to a 400 key.</summary>
public sealed record JobHistoryQueryText(string? Search, string? Status, string? Period, string? Sort, string? Page, string? PageSize);

public enum JobOutcome
{
    Completed,
    Reassigned,
    Cancelled,
}

public enum JobHistoryPeriod
{
    Days30,
    Days90,
    Months12,
    All,
}

public sealed record JobHistoryQuery(
    string? Search, JobOutcome? Status, JobHistoryPeriod Period, bool Newest, int Page, int PageSize)
{
    public long Skip => (long)(Page - 1) * PageSize;
}

/// <summary>One participating visit as loaded for the page; the participation date is the local date of the zone.</summary>
public sealed record JobHistoryRow(
    Guid VisitId,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    int VisitNumber,
    int VisitCount,
    string PropertyName,
    string City,
    DateOnly ParticipationDate,
    int LoggedMinutes,
    JobOutcome Outcome);

/// <summary>The inputs of the period metrics of one row (BR-16); loaded for every row of the period.</summary>
public sealed record JobHistorySummaryRow(
    JobOutcome Outcome,
    int LoggedMinutes,
    DateTimeOffset? ScheduledStart,
    DateTimeOffset? ArrivalWindowEnd,
    DateTimeOffset? ActualStartedAt,
    DateTimeOffset? ActualCompletedAt,
    int PauseSeconds);

public sealed record JobHistoryData(
    string ZoneId,
    DateOnly Today,
    DateOnly? From,
    IReadOnlyList<JobHistorySummaryRow> SummaryRows,
    IReadOnlyList<JobHistoryRow> Items,
    int Total);

public sealed record JobHistoryPeriodView(string? From, string To);

public sealed record JobHistorySummaryView(int CompletedJobs, int? OnTimePercent, int LoggedMinutes, int? AvgDurationMinutes);

public sealed record JobHistoryPropertyView(string Name, string City);

public sealed record JobHistoryItemView(
    Guid VisitId,
    Guid WorkOrderId,
    string DisplayNumber,
    string Title,
    int VisitNumber,
    int VisitCount,
    JobHistoryPropertyView Property,
    string ParticipationDate,
    int LoggedMinutes,
    string Outcome,
    bool CanOpen);

public sealed record JobHistoryPage(
    string Timezone,
    JobHistoryPeriodView Period,
    JobHistorySummaryView Summary,
    IReadOnlyList<JobHistoryItemView> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Participation history of one technician. Every query filters by organization and branch scope first.</summary>
public interface ITechnicianJobHistoryStore
{
    /// <summary>The history of an in-scope profile, or null when the profile is not visible.</summary>
    Task<JobHistoryData?> GetAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        JobHistoryQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Pure rules of the job history (technician-profile-overview-history BR-13 to BR-17).</summary>
public static class JobHistoryRules
{
    public const int DefaultPageSize = 10;

    public static readonly int[] PageSizes = [10, 25, 50];

    public static string OutcomeCode(JobOutcome outcome) => outcome switch
    {
        JobOutcome.Completed => "completed",
        JobOutcome.Reassigned => "reassigned",
        _ => "cancelled",
    };

    /// <summary>BR-13 for the visit status and whether the technician still has an active assignment; null when excluded.</summary>
    public static JobOutcome? Classify(VisitStatus status, bool hasActiveAssignment)
    {
        if (status == VisitStatus.Cancelled)
        {
            return JobOutcome.Cancelled;
        }

        if ((status is VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved) && hasActiveAssignment)
        {
            return JobOutcome.Completed;
        }

        return hasActiveAssignment ? null : JobOutcome.Reassigned;
    }

    /// <summary>BR-14: local days ending today; <c>From</c> is null for all time. Future dates are excluded by the caller.</summary>
    public static (DateOnly? From, DateOnly To) Bounds(JobHistoryPeriod period, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = BranchTime.LocalDate(now, zone);

        return period switch
        {
            JobHistoryPeriod.Days30 => (today.AddDays(-29), today),
            JobHistoryPeriod.Days90 => (today.AddDays(-89), today),
            JobHistoryPeriod.Months12 => (today.AddMonths(-12).AddDays(1), today),
            _ => (null, today),
        };
    }

    /// <summary>BR-16 metrics over every row of the period.</summary>
    public static JobHistorySummaryView Summarize(IReadOnlyList<JobHistorySummaryRow> rows, DateTimeOffset now)
    {
        var completed = rows.Where(row => row.Outcome == JobOutcome.Completed).ToList();

        var eligible = 0;
        var onTime = 0;

        foreach (var row in completed.Where(row => row.ActualStartedAt is not null && row.ScheduledStart is not null))
        {
            var result = OperationsDashboardRules.Arrival(
                new DashboardArrival(VisitStatus.Completed, row.ScheduledStart!.Value, row.ArrivalWindowEnd, row.ActualStartedAt), now);

            if (result is ArrivalResult.OnTime or ArrivalResult.Late)
            {
                eligible++;

                if (result == ArrivalResult.OnTime)
                {
                    onTime++;
                }
            }
        }

        var durations = completed
            .Where(row => row.ActualStartedAt is not null && row.ActualCompletedAt is not null)
            .Select(row => Math.Max(0d, (row.ActualCompletedAt!.Value - row.ActualStartedAt!.Value).TotalSeconds - row.PauseSeconds) / 60d)
            .ToList();

        return new JobHistorySummaryView(
            completed.Count,
            OperationsDashboardRules.Percent(onTime, eligible),
            rows.Sum(row => row.LoggedMinutes),
            durations.Count == 0 ? null : (int)Math.Round(durations.Average(), MidpointRounding.AwayFromZero));
    }

    /// <summary>The validated query, or null with the per-key errors.</summary>
    public static JobHistoryQuery? Parse(JobHistoryQueryText text, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var search = text.Search?.Trim();

        if (search is { Length: > TeamQueryParser.SearchMaxLength })
        {
            errors[TeamFieldKeys.Search] = [TeamMessages.SearchTooLong];
        }

        JobOutcome? status = null;

        switch (text.Status)
        {
            case null or "" or "all":
                break;
            case "completed":
                status = JobOutcome.Completed;
                break;
            case "reassigned":
                status = JobOutcome.Reassigned;
                break;
            case "cancelled":
                status = JobOutcome.Cancelled;
                break;
            default:
                errors[TeamFieldKeys.Status] = [TeamMessages.QueryInvalid];
                break;
        }

        var period = JobHistoryPeriod.Days90;

        switch (text.Period)
        {
            case null or "" or "90d":
                break;
            case "30d":
                period = JobHistoryPeriod.Days30;
                break;
            case "12m":
                period = JobHistoryPeriod.Months12;
                break;
            case "all":
                period = JobHistoryPeriod.All;
                break;
            default:
                errors[TeamFieldKeys.Period] = [TeamMessages.QueryInvalid];
                break;
        }

        var newest = true;

        switch (text.Sort)
        {
            case null or "" or "newest":
                break;
            case "oldest":
                newest = false;
                break;
            default:
                errors[TeamFieldKeys.Sort] = [TeamMessages.QueryInvalid];
                break;
        }

        var page = 1;

        if (!string.IsNullOrEmpty(text.Page)
            && (!int.TryParse(text.Page, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors[TeamFieldKeys.Page] = [TeamMessages.QueryInvalid];
        }

        var pageSize = DefaultPageSize;

        if (!string.IsNullOrEmpty(text.PageSize)
            && (!int.TryParse(text.PageSize, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || !PageSizes.Contains(pageSize)))
        {
            errors[TeamFieldKeys.PageSize] = [TeamMessages.QueryInvalid];
        }

        return errors.Count > 0
            ? null
            : new JobHistoryQuery(string.IsNullOrEmpty(search) ? null : search, status, period, newest, page, pageSize);
    }
}

/// <summary>GET /team/technicians/{id}/job-history (FR-06, FR-07, FR-09, BR-12 to BR-17, BR-20, BR-21).</summary>
public sealed class GetTechnicianJobHistoryHandler(
    ITechnicianJobHistoryStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<TeamResult<JobHistoryPage>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid technicianId,
        JobHistoryQueryText text,
        bool canReadWorkOrders,
        CancellationToken cancellationToken)
    {
        var query = JobHistoryRules.Parse(text, out var errors);

        if (query is null)
        {
            return TeamResult<JobHistoryPage>.Invalid(errors);
        }

        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var data = await store.GetAsync(organizationId, scope, technicianId, query, now, cancellationToken);

        if (data is null)
        {
            return TeamResult<JobHistoryPage>.NotFound();
        }

        // Rows are already limited to work orders in the caller's branch scope, so readability is the permission alone.
        var items = data.Items
            .Select(row => new JobHistoryItemView(
                row.VisitId,
                row.WorkOrderId,
                row.DisplayNumber,
                row.Title,
                row.VisitNumber,
                row.VisitCount,
                new JobHistoryPropertyView(row.PropertyName, row.City),
                SkillsAvailabilityMapping.DateText(row.ParticipationDate),
                row.LoggedMinutes,
                JobHistoryRules.OutcomeCode(row.Outcome),
                canReadWorkOrders))
            .ToList();

        return TeamResult<JobHistoryPage>.Ok(new JobHistoryPage(
            data.ZoneId,
            new JobHistoryPeriodView(
                data.From is { } from ? SkillsAvailabilityMapping.DateText(from) : null,
                SkillsAvailabilityMapping.DateText(data.Today)),
            JobHistoryRules.Summarize(data.SummaryRows, now),
            items,
            data.Total,
            query.Page,
            query.PageSize));
    }
}
