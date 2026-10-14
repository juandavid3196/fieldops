using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.WorkOrders;

internal static class JobsHandlerSupport
{
    public const string BranchIdMessage = "Branch id is malformed.";

    /// <summary>A blank branch id means every in-scope branch; a malformed one is a validation error.</summary>
    public static bool TryParseBranchId(string? value, Dictionary<string, string[]> errors, out Guid? branchId)
    {
        branchId = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            branchId = parsed;

            return true;
        }

        errors["branchId"] = [BranchIdMessage];

        return false;
    }

    /// <summary>BR-02: a branch of another organization, inactive, unknown or out of scope is one identical outcome.</summary>
    public static async Task<bool> BranchVisibleAsync(
        IJobsStore store, Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken) =>
        branchId is not { } id
        || (scope.Contains(id) && await store.BranchAvailableAsync(organizationId, scope, id, cancellationToken));
}

public sealed class ListWorkOrdersHandler(IJobsStore store, IBranchScopeResolver scopes)
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public async Task<ServiceRequestResult<JobsPage>> HandleAsync(
        MembershipCall call, bool canManage, JobsListRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!JobsRules.TryParseTab(request.Tab, out var tab))
        {
            errors["tab"] = ["Tab must be all, active, unscheduled or completed."];
        }

        var search = request.Q?.Trim();

        if (search is { Length: > JobsRules.SearchMaxLength })
        {
            errors["q"] = [$"Search must be {JobsRules.SearchMaxLength} characters or fewer."];
        }

        if (!JobsRules.TryParseStatus(request.Status, tab, out var status))
        {
            errors["status"] = ["Status is not valid for this tab."];
        }

        if (!JobsRules.TryParsePriority(request.Priority, out var priority))
        {
            errors["priority"] = ["Priority must be urgent, high, normal or low."];
        }

        if (!JobsRules.TryParseSort(request.Sort, out var sort))
        {
            errors["sort"] = ["Sort must be newest, oldest, updated, schedule or priority."];
        }

        var pageNumber = request.Page ?? 1;
        var size = request.PageSize ?? DefaultPageSize;

        if (pageNumber < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (size is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        }

        JobsHandlerSupport.TryParseBranchId(request.BranchId, errors, out var branchId);

        if (errors.Count > 0)
        {
            return ServiceRequestResult<JobsPage>.Invalid(errors);
        }

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        if (!await JobsHandlerSupport.BranchVisibleAsync(store, call.OrganizationId, scope, branchId, cancellationToken))
        {
            return ServiceRequestResult<JobsPage>.NotFound();
        }

        var filter = new JobsFilter(
            tab,
            string.IsNullOrEmpty(search) ? null : search,
            branchId,
            status,
            priority,
            sort,
            pageNumber,
            size);

        return ServiceRequestResult<JobsPage>.Ok(
            await store.ListAsync(call.OrganizationId, scope, filter, canManage, cancellationToken));
    }
}

public sealed class GetJobsSummaryHandler(IJobsStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<JobsSummary>> HandleAsync(
        MembershipCall call, string? branchIdText, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!JobsHandlerSupport.TryParseBranchId(branchIdText, errors, out var branchId))
        {
            return ServiceRequestResult<JobsSummary>.Invalid(errors);
        }

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        if (!await JobsHandlerSupport.BranchVisibleAsync(store, call.OrganizationId, scope, branchId, cancellationToken))
        {
            return ServiceRequestResult<JobsSummary>.NotFound();
        }

        return ServiceRequestResult<JobsSummary>.Ok(
            await store.SummaryAsync(call.OrganizationId, scope, branchId, cancellationToken));
    }
}

public sealed class GetJobsOptionsHandler(IJobsStore store, IBranchScopeResolver scopes)
{
    public async Task<JobsOptions> HandleAsync(MembershipCall call, bool canManage, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        return await store.OptionsAsync(call.OrganizationId, scope, canManage, cancellationToken);
    }
}
