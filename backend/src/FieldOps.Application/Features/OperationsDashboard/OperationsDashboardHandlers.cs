using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.OperationsDashboard;

internal static class OperationsDashboardSupport
{
    /// <summary>
    /// Validates the query text, resolves the branch scope server-side and checks the date range, category and
    /// technician. Returns the failure outcome (400 / 404) or the validated query. Organization comes from the session only.
    /// </summary>
    public static async Task<(DashboardQuery? Query, DispatchOutcome<T>? Failure)> PrepareAsync<T>(
        IOperationsDashboardStore store,
        IBranchScopeResolver scopes,
        MembershipCall call,
        OperationsQueryText text,
        int? period,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        Guid? branchId = null;
        Guid? technicianId = null;
        Guid? categoryId = null;

        if (!string.IsNullOrWhiteSpace(text.BranchId))
        {
            if (DispatchHandlerSupport.TryGuid(text.BranchId, out var parsed))
            {
                branchId = parsed;
            }
            else
            {
                errors["branchId"] = [OperationsDashboardMessages.BranchInvalid];
            }
        }

        if (!string.IsNullOrWhiteSpace(text.TechnicianId))
        {
            if (DispatchHandlerSupport.TryGuid(text.TechnicianId, out var parsed))
            {
                technicianId = parsed;
            }
            else
            {
                errors["technicianId"] = [OperationsDashboardMessages.TechnicianInvalid];
            }
        }

        if (!string.IsNullOrWhiteSpace(text.CategoryId))
        {
            if (DispatchHandlerSupport.TryGuid(text.CategoryId, out var parsed))
            {
                categoryId = parsed;
            }
            else
            {
                errors["categoryId"] = [OperationsDashboardMessages.CategoryInvalid];
            }
        }

        if (!DateOnly.TryParseExact(text.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors["date"] = [OperationsDashboardMessages.DateInvalid];
        }

        if (period is null)
        {
            errors["period"] = [OperationsDashboardMessages.PeriodInvalid];
        }

        if (errors.Count > 0)
        {
            return (null, new DispatchOutcome<T>.Invalid(errors));
        }

        var scope = await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken);
        var context = await store.ResolveAsync(call.OrganizationId, scope, branchId, cancellationToken);

        if (context is null)
        {
            return (null, new DispatchOutcome<T>.NotFound());
        }

        var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(context.TimezoneId));

        if (!OperationsDashboardRules.DateInRange(date, today))
        {
            errors["date"] = [OperationsDashboardMessages.DateOutOfRange];
        }

        if (categoryId is { } category && !await store.CategoryIsActiveAsync(call.OrganizationId, category, cancellationToken))
        {
            errors["categoryId"] = [OperationsDashboardMessages.CategoryInvalid];
        }

        if (errors.Count > 0)
        {
            return (null, new DispatchOutcome<T>.Invalid(errors));
        }

        if (technicianId is { } technician
            && !await store.TechnicianInScopeAsync(call.OrganizationId, context.BranchIds, technician, cancellationToken))
        {
            return (null, new DispatchOutcome<T>.NotFound());
        }

        return (new DashboardQuery(context.TimezoneId, context.BranchIds, date, technicianId, categoryId), null);
    }
}

public sealed class GetOperationsOptionsHandler(IOperationsDashboardStore store, IBranchScopeResolver scopes)
{
    public async Task<OperationsOptionsView> HandleAsync(MembershipCall call, CancellationToken cancellationToken) =>
        await store.GetOptionsAsync(
            call.OrganizationId, await DispatchHandlerSupport.ScopeAsync(scopes, call, cancellationToken), cancellationToken);
}

public sealed class GetOperationsDashboardHandler(
    IOperationsDashboardStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<DispatchOutcome<OperationsDashboardView>> HandleAsync(
        MembershipCall call, OperationsQueryText text, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (query, failure) = await OperationsDashboardSupport.PrepareAsync<OperationsDashboardView>(
            store, scopes, call, text, 0, now, cancellationToken);

        if (query is null)
        {
            return failure!;
        }

        var source = await store.LoadDashboardAsync(call.OrganizationId, query, now, cancellationToken);

        return new DispatchOutcome<OperationsDashboardView>.Succeeded(
            OperationsDashboardBuilder.Build(source, query.TimezoneId, query.Date, now));
    }
}

public sealed class GetServicePerformanceHandler(
    IOperationsDashboardStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<DispatchOutcome<PerformanceView>> HandleAsync(
        MembershipCall call, OperationsQueryText text, CancellationToken cancellationToken)
    {
        int? days = text.Period?.Trim() switch
        {
            "7" => 7,
            "30" => 30,
            _ => null,
        };

        var now = timeProvider.GetUtcNow();
        var (query, failure) = await OperationsDashboardSupport.PrepareAsync<PerformanceView>(
            store, scopes, call, text, days, now, cancellationToken);

        if (query is null || days is not { } length)
        {
            // A period outside 7/30 is reported by Prepare as a validation error.
            return failure!;
        }

        var source = await store.LoadPerformanceAsync(call.OrganizationId, query, length, cancellationToken);
        var zone = OrganizationTime.FindZone(query.TimezoneId);
        var from = query.Date.AddDays(1 - length);

        return new DispatchOutcome<PerformanceView>.Succeeded(new PerformanceView(
            from,
            query.Date,
            new CompletedRatioView(source.Completed, source.Total),
            new FixRateView(source.Fixed, source.FixTotal),
            new RatingView(source.RatingAverage, source.RatingCount),
            OperationsDashboardRules.Daily(source.Completions, from, length, zone)));
    }
}
