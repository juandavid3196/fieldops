using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Search;

/// <summary>Read-only, no-tracking, organization-first search reads (global-search BR-02, BR-09).</summary>
public interface ISearchStore
{
    /// <summary>The document prefixes and the active branch count: one query.</summary>
    Task<SearchOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// One projected query per eligible type. Each returns its top 6 by rank, <c>updated_at</c> descending and id; the store
    /// keeps 5 and flags the overflow. Entities outside the branch scope never match.
    /// </summary>
    Task<SearchMatches> SearchAsync(
        Guid organizationId,
        BranchScope scope,
        SearchEligibility eligibility,
        SearchTerms terms,
        CancellationToken cancellationToken);
}
