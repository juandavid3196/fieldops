using System.Globalization;
using System.Net;
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

    public static TechnicianVisitDetail Detail(FoundVisit found, TechnicianVisitProfile profile) =>
        new(
            found.Visit,
            Date(found.Visit.Start, BranchTime.FindZone(profile.ZoneId)),
            profile.ZoneId,
            found.DispatchNote,
            found.Extras,
            found.NeedsReschedule,
            found.Cancellation);

    /// <summary>BR-08: the extensions of a start-travel refusal; the blocking visit travels with the code.</summary>
    public static IReadOnlyDictionary<string, object?> ActiveVisitExtension(ActiveVisitSummary? active) =>
        new Dictionary<string, object?> { ["activeVisit"] = active };
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
        var unresolved = await store.ListUnresolvedAsync(
            organizationId, profile.Id, profile.ZoneId, dayStart, cancellationToken);

        return TechnicianVisitResult<TodayJobs>.Ok(new TodayJobs(
            TechnicianVisitRules.Date(now, zone),
            profile.ZoneId,
            new TodayTechnician(
                profile.FirstName,
                RequestCardRules.Initials($"{profile.FirstName} {profile.LastName}"),
                profile.ColorHex),
            TechnicianVisitRules.Metrics(visits),
            TechnicianVisitRules.NextVisitId(visits),
            visits,
            unresolved));
    }
}

/// <summary>GET /technician/schedule (technician-schedule-active-visits BR-01 to BR-04, BR-06, BR-08).</summary>
public sealed class GetTechnicianScheduleHandler(ITechnicianVisitStore store, TimeProvider timeProvider)
{
    public const string WeekStartInvalid = "Choose a valid week.";

    private static readonly DateOnly FirstWeek = new(2000, 1, 3);

    private static readonly DateOnly LastWeek = new(2099, 12, 28);

    public async Task<TechnicianVisitResult<TechnicianWeek>> HandleAsync(
        Guid organizationId, Guid membershipId, string? weekStartText, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<TechnicianWeek>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var now = timeProvider.GetUtcNow();
        var zone = BranchTime.FindZone(profile.ZoneId);
        var today = BranchTime.LocalDate(now, zone);
        var monday = BranchTime.MondayOf(today);

        if (weekStartText is not null)
        {
            if (!TryParseWeekStart(weekStartText, out monday))
            {
                return TechnicianVisitResult<TechnicianWeek>.Invalid(
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["weekStart"] = [WeekStartInvalid] });
            }
        }

        var (from, to) = BranchTime.WeekOf(monday, zone);
        var (dayStart, _) = BranchTime.Day(now, zone);
        var visits = await store.ListWeekAsync(
            organizationId, profile.Id, profile.ZoneId, from, to, dayStart, cancellationToken);
        var unresolved = await store.ListUnresolvedAsync(
            organizationId, profile.Id, profile.ZoneId, dayStart, cancellationToken);

        return TechnicianVisitResult<TechnicianWeek>.Ok(new TechnicianWeek(
            profile.ZoneId,
            Format(today),
            Format(monday),
            Days(monday, visits, zone),
            visits,
            unresolved));
    }

    /// <summary>BR-04: counts and minutes per local day over the visits that are not cancelled.</summary>
    public static IReadOnlyList<ScheduleDay> Days(DateOnly monday, IReadOnlyList<ScheduleVisit> visits, TimeZoneInfo zone) =>
        [
            .. Enumerable.Range(0, 7).Select(offset =>
            {
                var date = monday.AddDays(offset);
                var day = visits
                    .Where(visit => visit.Status != "cancelled" && BranchTime.LocalDate(visit.Start, zone) == date)
                    .ToList();

                return new ScheduleDay(Format(date), day.Count, day.Sum(visit => visit.DurationMinutes));
            }),
        ];

    /// <summary>BR-02: an ISO date that is a Monday between 2000-01-03 and 2099-12-28.</summary>
    public static bool TryParseWeekStart(string? text, out DateOnly monday) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out monday)
        && monday.DayOfWeek == DayOfWeek.Monday
        && monday >= FirstWeek
        && monday <= LastWeek;

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>GET /technician/visits/{visitId} (technician-todays-jobs BR-03, BR-11; mobile-job-details BR-03 to BR-06).</summary>
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

        return TechnicianVisitResult<TechnicianVisitDetail>.Ok(TechnicianVisitRules.Detail(found, profile));
    }
}

/// <summary>The session identity of a travel mutation; the technician and organization never come from the client.</summary>
public sealed record TravelCall(Guid OrganizationId, Guid MembershipId, Guid UserId, IPAddress? IpAddress);

internal static class TravelHandlerSupport
{
    /// <summary>BR-01 and BR-02 first, then the locked transaction in the store, then the mapping of its outcome.</summary>
    public static async Task<(TechnicianVisitResult<TravelResult> Result, TravelOutcome? Saved)> RunAsync(
        ITechnicianVisitStore store,
        TimeProvider timeProvider,
        TravelKind kind,
        TravelCall call,
        Guid visitId,
        CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<TravelResult>(
            store, call.OrganizationId, call.MembershipId, cancellationToken);

        if (profile is null)
        {
            return (failure!, null);
        }

        var outcome = await store.TravelAsync(
            new TravelActor(call.OrganizationId, call.UserId, profile.Id, profile.ZoneId, call.IpAddress),
            visitId,
            kind,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return outcome.Kind switch
        {
            TravelOutcomeKind.Saved => (
                TechnicianVisitResult<TravelResult>.Ok(
                    new TravelResult(outcome.Changed, TechnicianVisitRules.Detail(outcome.Found!, profile))),
                outcome),
            TravelOutcomeKind.NotPrimary => (
                TechnicianVisitResult<TravelResult>.Forbidden(TechnicianVisitCodes.NotPrimaryTechnician), null),
            TravelOutcomeKind.StatusInvalid => (
                TechnicianVisitResult<TravelResult>.Conflict(TechnicianVisitCodes.VisitStatusInvalid), null),
            TravelOutcomeKind.NotToday => (
                TechnicianVisitResult<TravelResult>.Conflict(TechnicianVisitCodes.VisitNotToday), null),
            TravelOutcomeKind.AnotherActive => (
                TechnicianVisitResult<TravelResult>.Conflict(
                    TechnicianVisitCodes.AnotherVisitActive,
                    extensions: TechnicianVisitRules.ActiveVisitExtension(outcome.ActiveVisit)),
                null),
            _ => (TechnicianVisitResult<TravelResult>.NotFound(), null),
        };
    }
}

/// <summary>POST /technician/visits/{visitId}/start-travel (BR-01, BR-02, BR-07, BR-08, BR-10, BR-11).</summary>
public sealed class StartTravelHandler(ITechnicianVisitStore store, ITravelNotifier notifier, TimeProvider timeProvider)
{
    public async Task<TechnicianVisitResult<TravelResult>> HandleAsync(
        TravelCall call, Guid visitId, CancellationToken cancellationToken)
    {
        var (result, saved) = await TravelHandlerSupport.RunAsync(
            store, timeProvider, TravelKind.Start, call, visitId, cancellationToken);

        // BR-11: only after the commit of an effective transition; a failed send is logged by the notifier and ignored.
        if (saved is { Changed: true, Email: { } email })
        {
            await notifier.SendAsync(email, cancellationToken);
        }

        return result;
    }
}

/// <summary>POST /technician/visits/{visitId}/arrive (BR-01, BR-02, BR-09, BR-10); arrival never sends email.</summary>
public sealed class ArriveHandler(ITechnicianVisitStore store, TimeProvider timeProvider)
{
    public async Task<TechnicianVisitResult<TravelResult>> HandleAsync(
        TravelCall call, Guid visitId, CancellationToken cancellationToken) =>
        (await TravelHandlerSupport.RunAsync(store, timeProvider, TravelKind.Arrive, call, visitId, cancellationToken)).Result;
}

/// <summary>GET /technician/visits/{visitId}/assessment-photos/{photoId} (BR-01, BR-06).</summary>
public sealed class GetVisitAssessmentPhotoHandler(ITechnicianVisitStore store)
{
    public async Task<TechnicianVisitResult<AssessmentPhotoImage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid visitId, Guid photoId, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<AssessmentPhotoImage>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var image = await store.FindAssessmentPhotoAsync(organizationId, profile.Id, visitId, photoId, cancellationToken);

        return image is null
            ? TechnicianVisitResult<AssessmentPhotoImage>.NotFound()
            : TechnicianVisitResult<AssessmentPhotoImage>.Ok(image);
    }
}
