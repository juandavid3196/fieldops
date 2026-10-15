using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Quotes;

internal static class QuotesWorkspaceSupport
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
        IQuotesStore store, Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken) =>
        branchId is not { } id
        || (scope.Contains(id) && await store.BranchAvailableAsync(organizationId, scope, id, cancellationToken));
}

public sealed class ListQuotesHandler(IQuotesStore store, IBranchScopeResolver scopes)
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public async Task<ServiceRequestResult<QuotesPage>> HandleAsync(
        MembershipCall call, QuotesPermissions permissions, QuotesListRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!QuotesWorkspaceRules.TryParseTab(request.Tab, out var tab))
        {
            errors["tab"] = ["Tab must be all, drafts, sent, approved or closed."];
        }

        var search = request.Q?.Trim();

        if (search is { Length: > QuotesWorkspaceRules.SearchMaxLength })
        {
            errors["q"] = [$"Search must be {QuotesWorkspaceRules.SearchMaxLength} characters or fewer."];
        }

        if (!QuotesWorkspaceRules.TryParseStatus(request.Status, tab, out var status))
        {
            errors["status"] = ["Status is not valid for this tab."];
        }

        if (!QuotesWorkspaceRules.TryParseDate(request.Date, out var date))
        {
            errors["date"] = ["Date must be any, today, 7d, 30d or 90d."];
        }

        if (!QuotesWorkspaceRules.TryParseSort(request.Sort, out var sort))
        {
            errors["sort"] = ["Sort must be updated, newest, oldest, total or expiring."];
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

        QuotesWorkspaceSupport.TryParseBranchId(request.BranchId, errors, out var branchId);

        if (errors.Count > 0)
        {
            return ServiceRequestResult<QuotesPage>.Invalid(errors);
        }

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        if (!await QuotesWorkspaceSupport.BranchVisibleAsync(store, call.OrganizationId, scope, branchId, cancellationToken))
        {
            return ServiceRequestResult<QuotesPage>.NotFound();
        }

        var filter = new QuotesFilter(
            tab,
            string.IsNullOrEmpty(search) ? null : search,
            branchId,
            status,
            date,
            sort,
            pageNumber,
            size);

        return ServiceRequestResult<QuotesPage>.Ok(
            await store.ListAsync(call.OrganizationId, scope, filter, permissions, cancellationToken));
    }
}

public sealed class GetQuotesSummaryHandler(IQuotesStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<QuotesSummary>> HandleAsync(
        MembershipCall call, string? branchIdText, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!QuotesWorkspaceSupport.TryParseBranchId(branchIdText, errors, out var branchId))
        {
            return ServiceRequestResult<QuotesSummary>.Invalid(errors);
        }

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        if (!await QuotesWorkspaceSupport.BranchVisibleAsync(store, call.OrganizationId, scope, branchId, cancellationToken))
        {
            return ServiceRequestResult<QuotesSummary>.NotFound();
        }

        return ServiceRequestResult<QuotesSummary>.Ok(
            await store.SummaryAsync(call.OrganizationId, scope, branchId, cancellationToken));
    }
}

public sealed class GetQuotesOptionsHandler(IQuotesStore store, IBranchScopeResolver scopes)
{
    public async Task<QuotesOptions> HandleAsync(
        MembershipCall call, QuotesPermissions permissions, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        return await store.OptionsAsync(call.OrganizationId, scope, permissions, cancellationToken);
    }
}
