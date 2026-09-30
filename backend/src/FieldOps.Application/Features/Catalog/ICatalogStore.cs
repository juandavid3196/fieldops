using System.Net;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Features.Catalog;

/// <summary>
/// Persistence for the products and services catalog. Every operation is
/// scoped by the session organization id; ids of other organizations are
/// reported as not found.
/// </summary>
public interface ICatalogStore
{
    Task<CatalogOrganizationFormat?> GetOrganizationFormatAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<CatalogListPage> ListAsync(
        Guid organizationId, CatalogListFilter filter, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogItemRow>> ListAllAsync(
        Guid organizationId, CatalogListFilter filter, CancellationToken cancellationToken);

    Task<CatalogSummary> GetSummaryAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<CatalogItemDetail?> GetDetailAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    Task<bool> ItemExistsAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(
        Guid organizationId,
        CatalogItemType type,
        string normalizedName,
        Guid? excludeItemId,
        CancellationToken cancellationToken);

    /// <summary>The (type, normalized name) pairs among <paramref name="normalizedNames"/> that already exist.</summary>
    Task<HashSet<(CatalogItemType Type, string NormalizedName)>> FindExistingNamesAsync(
        Guid organizationId, IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);

    /// <exception cref="DuplicateCatalogItemNameException">A concurrent create won the unique index.</exception>
    Task<CatalogItemDetail> CreateAsync(
        Guid organizationId,
        CatalogItemValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>Null when the item does not exist in the organization.</summary>
    /// <exception cref="DuplicateCatalogItemNameException">A concurrent change won the unique index.</exception>
    Task<CatalogItemDetail?> UpdateAsync(
        Guid organizationId,
        Guid itemId,
        CatalogItemValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CatalogStateOutcome> SetActiveAsync(
        Guid organizationId,
        Guid itemId,
        bool isActive,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CatalogImageContent?> GetImageAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>Null when the item does not exist in the organization.</summary>
    Task<CatalogImageInfo?> UpsertImageAsync(
        Guid organizationId,
        Guid itemId,
        string contentType,
        byte[] content,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CatalogImageRemoval> DeleteImageAsync(
        Guid organizationId,
        Guid itemId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds every item and one <c>catalog_item.imported</c> audit row in a single save.
    /// </summary>
    /// <exception cref="DuplicateCatalogItemNameException">A concurrent create won the unique index.</exception>
    Task<int> ImportAsync(
        Guid organizationId,
        IReadOnlyList<CatalogItemValues> items,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);
}
