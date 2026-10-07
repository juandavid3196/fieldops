using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Dispatch;

/// <summary>Raw calendar query values before parsing (dispatch-calendar BR-06).</summary>
public sealed record CalendarQueryText(
    string? BranchId, string? View, string? Date, IReadOnlyList<string> TechnicianIds, string? SkillId, string? Status);

/// <summary>Raw unscheduled query values before parsing (dispatch-calendar BR-07).</summary>
public sealed record UnscheduledQueryText(
    string? BranchId, string? Filter, string? Search, string? SkillId, string? Page, string? PageSize);

/// <summary>Raw evaluation body (POST /dispatch/visits/{id}/evaluation).</summary>
public sealed record EvaluationBodyText(
    string? Date, string? Start, string? End, IReadOnlyList<string>? TechnicianIds, string? PrimaryTechnicianId);

/// <summary>Raw dispatch body (PUT /dispatch/visits/{id}); booleans stay nullable so a missing one is a 400.</summary>
public sealed record DispatchBodyText(
    string? Date,
    string? Start,
    string? End,
    string? ArrivalWindow,
    IReadOnlyList<string>? TechnicianIds,
    string? PrimaryTechnicianId,
    string? DispatchNote,
    bool? NotifyCustomer,
    bool? SendTechnicianDetails,
    string? OverrideReason,
    string? UpdatedAt);

/// <summary>Pure BR-11 / BR-12 shape validation shared by the evaluation and the dispatch.</summary>
public static class DispatchValidator
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string TimeFormat = "HH:mm";

    public static (DateTimeOffset Start, DateTimeOffset End)? ValidateSlot(
        string? dateText,
        string? startText,
        string? endText,
        TimeZoneInfo zone,
        DateTimeOffset now,
        DateTimeOffset? storedStart,
        DateTimeOffset? storedEnd,
        Dictionary<string, string[]> errors)
    {
        var dateOk = DateOnly.TryParseExact(dateText?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
        var startOk = TryTime(startText, out var startTime);
        var endOk = TryTime(endText, out var endTime);

        if (!dateOk)
        {
            errors["date"] = [DispatchMessages.DateRequired];
        }

        if (!startOk)
        {
            errors["start"] = [DispatchMessages.StartRequired];
        }

        if (!endOk)
        {
            errors["end"] = [DispatchMessages.EndRequired];
        }

        if (!dateOk || !startOk || !endOk)
        {
            return null;
        }

        var local = date.ToString(DateFormat, CultureInfo.InvariantCulture);
        var startParsed = OrganizationTime.TryParseLocal($"{local}T{startTime:HH:mm}", zone, out var start);
        var endParsed = OrganizationTime.TryParseLocal($"{local}T{endTime:HH:mm}", zone, out var end);

        if (!startParsed)
        {
            errors["start"] = [DispatchMessages.StartRequired];
        }

        if (!endParsed)
        {
            errors["end"] = [DispatchMessages.EndRequired];
        }

        if (!startParsed || !endParsed)
        {
            return null;
        }

        if (endTime <= startTime || end <= start)
        {
            errors["end"] = [DispatchMessages.EndBeforeStart];

            return null;
        }

        var length = end - start;

        if (length < TimeSpan.FromMinutes(15) || length > TimeSpan.FromHours(12))
        {
            errors["end"] = [length < TimeSpan.FromMinutes(15) ? DispatchMessages.EndBeforeStart : DispatchMessages.EndRequired];

            return null;
        }

        var changed = storedStart != start || storedEnd != end;

        if (changed)
        {
            if (date < OrganizationTime.LocalDate(now, zone))
            {
                errors["date"] = [DispatchMessages.DatePast];

                return null;
            }

            if (start <= now)
            {
                errors["start"] = [DispatchMessages.StartPast];

                return null;
            }
        }

        return (start, end);
    }

    /// <summary>0 to 5 distinct ids and a primary among them (BR-11, BR-12 shape).</summary>
    public static (IReadOnlyList<Guid> Ids, Guid? Primary)? ValidateTechnicians(
        IReadOnlyList<string>? idTexts, string? primaryText, Dictionary<string, string[]> errors)
    {
        var texts = idTexts ?? [];
        var ids = new List<Guid>();

        if (texts.Count > DispatchMessages.MaxTechnicians)
        {
            errors["technicianIds"] = [DispatchMessages.TechniciansTooMany];

            return null;
        }

        foreach (var text in texts)
        {
            if (!Guid.TryParse(text?.Trim(), out var id) || id == Guid.Empty || ids.Contains(id))
            {
                errors["technicianIds"] = [DispatchMessages.TechniciansInvalid];

                return null;
            }

            ids.Add(id);
        }

        Guid? primary = null;

        if (!string.IsNullOrWhiteSpace(primaryText))
        {
            if (!Guid.TryParse(primaryText.Trim(), out var parsed) || !ids.Contains(parsed))
            {
                errors["primaryTechnicianId"] = [DispatchMessages.PrimaryRequired];

                return null;
            }

            primary = parsed;
        }

        if (ids.Count > 0 && primary is null)
        {
            errors["primaryTechnicianId"] = [DispatchMessages.PrimaryRequired];

            return null;
        }

        return (ids, primary);
    }

    public static (DateTimeOffset Start, DateTimeOffset End)? ArrivalWindow(
        string? code, DateTimeOffset start, TimeZoneInfo zone, Dictionary<string, string[]> errors)
    {
        (DateTimeOffset From, DateTimeOffset To)? window = code switch
        {
            DispatchCodes.AtStart => (start, start),
            DispatchCodes.StartPlus1h => (start, start.AddHours(1)),
            DispatchCodes.StartPlus2h => (start, start.AddHours(2)),
            DispatchCodes.Around1h => (start.AddHours(-1), start.AddHours(1)),
            _ => null,
        };

        var day = OrganizationTime.LocalDate(start, zone);

        if (window is not { } found
            || OrganizationTime.LocalDate(found.From, zone) != day
            || OrganizationTime.LocalDate(found.To, zone) != day)
        {
            errors["arrivalWindow"] = [DispatchMessages.ArrivalWindowInvalid];

            return null;
        }

        return found;
    }

    private static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
        && time.Minute % 15 == 0;
}

internal static class DispatchHandlerSupport
{
    public static async Task<BranchScope> ScopeAsync(
        IBranchScopeResolver scopes, MembershipCall call, CancellationToken cancellationToken) =>
        await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

    public static bool TryGuid(string? text, out Guid id) =>
        Guid.TryParse(text?.Trim(), out id) && id != Guid.Empty;
}

public sealed class GetDispatchOptionsHandler(IDispatchStore store, IBranchScopeResolver scopes)
{
    public async Task<DispatchOptionsView> HandleAsync(MembershipCall call, CancellationToken cancellationToken) =>
        await store.GetOptionsAsync(
            call.OrganizationId, await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken), cancellationToken);
}

public sealed class GetDispatchCalendarHandler(IDispatchStore store, IBranchScopeResolver scopes)
{
    public async Task<DispatchOutcome<DispatchCalendarView>> HandleAsync(
        MembershipCall call, CalendarQueryText text, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!DispatchHandlerSupport.TryGuid(text.BranchId, out var branchId))
        {
            errors["branchId"] = [DispatchMessages.BranchInvalid];
        }

        var view = text.View?.Trim().ToLowerInvariant();

        if (view is null || !DispatchCodes.Views.Contains(view))
        {
            errors["view"] = [DispatchMessages.ViewInvalid];
        }

        if (!DateOnly.TryParseExact(text.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors["date"] = [DispatchMessages.DateInvalid];
        }

        var status = string.IsNullOrWhiteSpace(text.Status) ? "all" : text.Status.Trim().ToLowerInvariant();

        if (!DispatchCodes.StatusFilters.Contains(status))
        {
            errors["status"] = [DispatchMessages.StatusInvalid];
        }

        Guid? skillId = null;

        if (!string.IsNullOrWhiteSpace(text.SkillId))
        {
            if (DispatchHandlerSupport.TryGuid(text.SkillId, out var parsedSkill))
            {
                skillId = parsedSkill;
            }
            else
            {
                errors["skillId"] = [DispatchMessages.SkillInvalid];
            }
        }

        if (errors.Count == 0 && skillId is { } skill && !await store.SkillIsActiveAsync(call.OrganizationId, skill, cancellationToken))
        {
            errors["skillId"] = [DispatchMessages.SkillInvalid];
        }

        if (errors.Count > 0)
        {
            return new DispatchOutcome<DispatchCalendarView>.Invalid(errors);
        }

        // Malformed team ids can never match a technician: they are ignored like the foreign ones (BR-06).
        var teamIds = text.TechnicianIds
            .Select(item => DispatchHandlerSupport.TryGuid(item, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var scope = await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken);
        var source = await store.LoadCalendarAsync(call.OrganizationId, scope, branchId, view!, date, cancellationToken);

        return source is null
            ? new DispatchOutcome<DispatchCalendarView>.NotFound()
            : new DispatchOutcome<DispatchCalendarView>.Succeeded(
                DispatchCalendarBuilder.Build(source, new CalendarQuery(branchId, view!, date, teamIds, skillId, status)));
    }
}

public sealed class ListUnscheduledVisitsHandler(IDispatchStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<DispatchOutcome<UnscheduledPageView>> HandleAsync(
        MembershipCall call, UnscheduledQueryText text, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!DispatchHandlerSupport.TryGuid(text.BranchId, out var branchId))
        {
            errors["branchId"] = [DispatchMessages.BranchInvalid];
        }

        var filter = string.IsNullOrWhiteSpace(text.Filter) ? "all" : text.Filter.Trim().ToLowerInvariant();

        if (!DispatchCodes.UnscheduledFilters.Contains(filter))
        {
            errors["filter"] = [DispatchMessages.FilterInvalid];
        }

        var search = text.Search?.Trim();

        if (search is { Length: > DispatchMessages.MaxSearchLength })
        {
            errors["search"] = [DispatchMessages.SearchTooLong];
        }

        Guid? skillId = null;

        if (!string.IsNullOrWhiteSpace(text.SkillId))
        {
            if (DispatchHandlerSupport.TryGuid(text.SkillId, out var parsedSkill))
            {
                skillId = parsedSkill;
            }
            else
            {
                errors["skillId"] = [DispatchMessages.SkillInvalid];
            }
        }

        var page = 1;
        var pageSize = DispatchMessages.DefaultPageSize;

        if (!string.IsNullOrWhiteSpace(text.Page)
            && (!int.TryParse(text.Page, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = [DispatchMessages.PagingInvalid];
        }

        if (!string.IsNullOrWhiteSpace(text.PageSize)
            && (!int.TryParse(text.PageSize, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || pageSize is < 1 or > DispatchMessages.MaxPageSize))
        {
            errors["pageSize"] = [DispatchMessages.PagingInvalid];
        }

        if (errors.Count == 0 && skillId is { } skill && !await store.SkillIsActiveAsync(call.OrganizationId, skill, cancellationToken))
        {
            errors["skillId"] = [DispatchMessages.SkillInvalid];
        }

        if (errors.Count > 0)
        {
            return new DispatchOutcome<UnscheduledPageView>.Invalid(errors);
        }

        var scope = await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken);

        return await store.ListUnscheduledAsync(
            call.OrganizationId,
            scope,
            new UnscheduledQuery(branchId, filter, string.IsNullOrEmpty(search) ? null : search, skillId, page, pageSize),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

public sealed class GetVisitDispatchHandler(IDispatchStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<VisitDispatchDetail?> HandleAsync(
        MembershipCall call, Guid visitId, bool canManage, CancellationToken cancellationToken) =>
        await store.GetDetailAsync(
            call.OrganizationId,
            await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken),
            visitId,
            canManage,
            timeProvider.GetUtcNow(),
            cancellationToken);
}

/// <summary>POST evaluation: 404, locked 409, shape 400, then the store (hard technician rules and the BR-10 result).</summary>
public sealed class EvaluateVisitHandler(IDispatchStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<DispatchOutcome<VisitEvaluation>> HandleAsync(
        MembershipCall call, Guid visitId, EvaluationBodyText body, CancellationToken cancellationToken)
    {
        var scope = await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken);
        var context = await store.GetContextAsync(call.OrganizationId, scope, visitId, cancellationToken);

        if (context is null)
        {
            return new DispatchOutcome<VisitEvaluation>.NotFound();
        }

        if (context.IsLocked)
        {
            return new DispatchOutcome<VisitEvaluation>.Locked();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var slot = DispatchValidator.ValidateSlot(
            body.Date,
            body.Start,
            body.End,
            OrganizationTime.FindZone(context.Timezone),
            timeProvider.GetUtcNow(),
            context.ScheduledStart,
            context.ScheduledEnd,
            errors);
        var technicians = DispatchValidator.ValidateTechnicians(body.TechnicianIds, body.PrimaryTechnicianId, errors);

        if (slot is not { } found || technicians is not { } selection || errors.Count > 0)
        {
            return new DispatchOutcome<VisitEvaluation>.Invalid(errors);
        }

        return await store.EvaluateAsync(
            call.OrganizationId,
            scope,
            visitId,
            new EvaluationInput(found.Start, found.End, selection.Ids, selection.Primary),
            cancellationToken);
    }
}

/// <summary>PUT dispatch: 404, locked 409, shape 400, the transaction in the store, then the email after the commit (BR-17).</summary>
public sealed class DispatchVisitHandler(
    IDispatchStore store, IBranchScopeResolver scopes, IVisitNotifier notifier, TimeProvider timeProvider)
{
    public async Task<DispatchOutcome<VisitDispatchResult>> HandleAsync(
        MembershipCall call, Guid visitId, DispatchBodyText body, CancellationToken cancellationToken)
    {
        var scope = await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken);
        var context = await store.GetContextAsync(call.OrganizationId, scope, visitId, cancellationToken);

        if (context is null)
        {
            return new DispatchOutcome<VisitDispatchResult>.NotFound();
        }

        if (context.IsLocked)
        {
            return new DispatchOutcome<VisitDispatchResult>.Locked();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var zone = OrganizationTime.FindZone(context.Timezone);
        var slot = DispatchValidator.ValidateSlot(
            body.Date,
            body.Start,
            body.End,
            zone,
            timeProvider.GetUtcNow(),
            context.ScheduledStart,
            context.ScheduledEnd,
            errors);
        (DateTimeOffset Start, DateTimeOffset End)? window = null;

        if (!DispatchCodes.ArrivalWindows.Contains(body.ArrivalWindow?.Trim() ?? string.Empty))
        {
            errors["arrivalWindow"] = [DispatchMessages.ArrivalWindowInvalid];
        }
        else if (slot is { } found)
        {
            window = DispatchValidator.ArrivalWindow(body.ArrivalWindow!.Trim(), found.Start, zone, errors);
        }

        var technicians = DispatchValidator.ValidateTechnicians(body.TechnicianIds, body.PrimaryTechnicianId, errors);

        var note = string.IsNullOrWhiteSpace(body.DispatchNote) ? null : body.DispatchNote.Trim();

        if (note is { Length: > DispatchMessages.MaxNoteLength })
        {
            errors["dispatchNote"] = [DispatchMessages.NoteTooLong];
        }

        var reason = string.IsNullOrWhiteSpace(body.OverrideReason) ? null : body.OverrideReason.Trim();

        if (reason is { Length: < DispatchMessages.MinReasonLength })
        {
            errors["overrideReason"] = [DispatchMessages.ReasonTooShort];
        }
        else if (reason is { Length: > DispatchMessages.MaxReasonLength })
        {
            errors["overrideReason"] = [DispatchMessages.ReasonTooLong];
        }

        if (body.NotifyCustomer is null)
        {
            errors["notifyCustomer"] = [DispatchMessages.OptionRequired];
        }
        else if (body.NotifyCustomer is true && !context.HasEmail)
        {
            errors["notifyCustomer"] = [DispatchMessages.NoEmail];
        }

        if (body.SendTechnicianDetails is null)
        {
            errors["sendTechnicianDetails"] = [DispatchMessages.OptionRequired];
        }

        if (!UpdatedAtValidation.TryParse(body.UpdatedAt, out var updatedAt))
        {
            errors["updatedAt"] = [DispatchMessages.UpdatedAtInvalid];
        }

        if (errors.Count > 0 || slot is not { } times || window is not { } arrival || technicians is not { } selection)
        {
            return new DispatchOutcome<VisitDispatchResult>.Invalid(errors);
        }

        var outcome = await store.DispatchAsync(
            new DispatchActor(call.OrganizationId, call.UserId, scope, call.IpAddress),
            visitId,
            new DispatchInput(
                times.Start,
                times.End,
                arrival.Start,
                arrival.End,
                selection.Ids,
                selection.Primary,
                note,
                body.NotifyCustomer!.Value,
                body.SendTechnicianDetails!.Value,
                reason,
                updatedAt),
            cancellationToken);

        switch (outcome)
        {
            case DispatchOutcome<DispatchSaved>.Succeeded succeeded:
                var result = succeeded.Value.Result;

                if (succeeded.Value.Email is { } email)
                {
                    result = result with { Notified = await notifier.SendAsync(email, cancellationToken) };
                }

                return new DispatchOutcome<VisitDispatchResult>.Succeeded(result);
            case DispatchOutcome<DispatchSaved>.Locked:
                return new DispatchOutcome<VisitDispatchResult>.Locked();
            case DispatchOutcome<DispatchSaved>.Changed:
                return new DispatchOutcome<VisitDispatchResult>.Changed();
            case DispatchOutcome<DispatchSaved>.Conflicts conflicts:
                return new DispatchOutcome<VisitDispatchResult>.Conflicts(conflicts.Items);
            case DispatchOutcome<DispatchSaved>.Invalid invalid:
                return new DispatchOutcome<VisitDispatchResult>.Invalid(invalid.Errors);
            default:
                return new DispatchOutcome<VisitDispatchResult>.NotFound();
        }
    }
}
