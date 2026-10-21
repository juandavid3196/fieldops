using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Search;

/// <summary>
/// The global search (global-search FR-01 to FR-05). The organization and branch scope come from the session membership;
/// the API has already applied the Global Search policy and computed the per-type eligibility. Read-only: no write, no audit
/// row, and the query text is never stored or logged here.
/// </summary>
public sealed class GlobalSearchHandler(ISearchStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<BillingOutcome<GlobalSearchView>> HandleAsync(
        MembershipCall call,
        SearchEligibility eligibility,
        string? query,
        string? limit,
        CancellationToken cancellationToken)
    {
        var parsed = SearchRules.Parse(query, limit);

        if (parsed is BillingOutcome<SearchRequest>.Invalid invalid)
        {
            return new BillingOutcome<GlobalSearchView>.Invalid(invalid.Errors);
        }

        var request = ((BillingOutcome<SearchRequest>.Succeeded)parsed).Value;
        var scope = await scopeResolver.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var organization = await store.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var terms = SearchRules.BuildTerms(request.Query, organization);
        var matches = await store.SearchAsync(call.OrganizationId, scope, eligibility, terms, cancellationToken);
        var (kept, hasMore) = SearchRules.Merge(matches, request.Limit);
        var showBranch = organization.ActiveBranchCount >= 2;

        return new BillingOutcome<GlobalSearchView>.Succeeded(new GlobalSearchView(
            request.Query,
            [.. kept.Select(hit => SearchRules.ToItem(hit, organization, showBranch))],
            hasMore));
    }
}
