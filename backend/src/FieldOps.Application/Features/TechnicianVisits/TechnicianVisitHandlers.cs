using System.Globalization;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;

namespace FieldOps.Application.Features.TechnicianVisits;

internal static class TechnicianVisitRules
{
    /// <summary>Resolves the caller's profile (BR-02): none is not linked, inactive or suspended is forbidden.</summary>
    public static async Task<(TechnicianVisitProfile? Profile, TechnicianVisitResult<T>? Failure)> ResolveAsync<T>(
        ITechnicianVisitStore store, Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var profile = await store.GetProfileAsync(organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return (null, TechnicianVisitResult<T>.NotFound(TechnicianVisitCodes.ProfileNotLinked));
        }

        return profile.Status == TechnicianStatus.Active
            ? (profile, null)
            : (null, TechnicianVisitResult<T>.Forbidden(TechnicianVisitCodes.TechnicianInactive));
    }

    public static bool IsCompleted(string status) => status is "completed" or "approved";

    /// <summary>BR-09: the first visit in route order on the job, otherwise the first startable one.</summary>
    public static Guid? NextVisitId(IReadOnlyList<TodayVisit> visits) =>
        visits.FirstOrDefault(visit => visit.Status is "on_the_way" or "in_progress" or "paused")?.VisitId
        ?? visits.FirstOrDefault(visit => visit.Status is "scheduled" or "assigned")?.VisitId;

    /// <summary>BR-07 over today's visits.</summary>
    public static TodayMetrics Metrics(IReadOnlyList<TodayVisit> visits)
    {
        var completed = visits.Count(visit => IsCompleted(visit.Status));
        var minutes = visits.Sum(visit => (visit.End - visit.Start).TotalMinutes);

        return new TodayMetrics(visits.Count, (int)Math.Round(minutes, MidpointRounding.AwayFromZero), completed, visits.Count - completed);
    }

    public static string Date(DateTimeOffset instant, TimeZoneInfo zone) =>
        BranchTime.LocalDate(instant, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>GET /technician/today (technician-todays-jobs BR-02 to BR-05, BR-07, BR-09).</summary>
public sealed class GetTodayVisitsHandler(ITechnicianVisitStore store, TimeProvider timeProvider)
{
    public async Task<TechnicianVisitResult<TodayJobs>> HandleAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<TodayJobs>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var now = timeProvider.GetUtcNow();
        var zone = BranchTime.FindZone(profile.ZoneId);
        var (dayStart, dayEnd) = BranchTime.Day(now, zone);
        var visits = await store.ListScheduledAsync(
            organizationId, profile.Id, profile.ZoneId, dayStart, dayEnd, cancellationToken);

        return TechnicianVisitResult<TodayJobs>.Ok(new TodayJobs(
            TechnicianVisitRules.Date(now, zone),
            profile.ZoneId,
            new TodayTechnician(
                profile.FirstName,
                RequestCardRules.Initials($"{profile.FirstName} {profile.LastName}"),
                profile.ColorHex),
            TechnicianVisitRules.Metrics(visits),
            TechnicianVisitRules.NextVisitId(visits),
            visits));
    }
}

/// <summary>GET /technician/visits/{visitId} (BR-03, BR-11).</summary>
public sealed class GetTechnicianVisitHandler(ITechnicianVisitStore store)
{
    public async Task<TechnicianVisitResult<TechnicianVisitDetail>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid visitId, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<TechnicianVisitDetail>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var found = await store.FindAsync(organizationId, profile.Id, profile.ZoneId, visitId, cancellationToken);

        if (found is null)
        {
            return TechnicianVisitResult<TechnicianVisitDetail>.NotFound();
        }

        return TechnicianVisitResult<TechnicianVisitDetail>.Ok(new TechnicianVisitDetail(
            found.Visit,
            TechnicianVisitRules.Date(found.Visit.Start, BranchTime.FindZone(profile.ZoneId)),
            profile.ZoneId,
            found.DispatchNote));
    }
}
