using System.Net;

namespace FieldOps.Application.Features.Catalog;

/// <summary>Persistence for catalog categories; every operation is scoped by the session organization.</summary>
public interface ICatalogCategoryStore
{
    Task<IReadOnlyList<CatalogCategoryView>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Case-insensitive name match across active and inactive categories, optionally excluding one.</summary>
    Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeId, CancellationToken cancellationToken);

    /// <exception cref="DuplicateCatalogCategoryNameException">A concurrent save won the unique index.</exception>
    Task<CatalogCategoryView> CreateAsync(
        Guid organizationId, string name, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken);

    /// <summary>Null when the category does not exist in the organization.</summary>
    /// <exception cref="DuplicateCatalogCategoryNameException">A concurrent save won the unique index.</exception>
    Task<CatalogCategoryView?> RenameAsync(
        Guid organizationId, Guid id, string name, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken);

    Task<CategoryWriteOutcome> SetActiveAsync(
        Guid organizationId, Guid id, bool isActive, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken);
}

/// <summary>Catalog category use cases: list, create, rename, activate and deactivate.</summary>
public sealed class CatalogCategoryHandler(ICatalogCategoryStore store)
{
    public const string NameKey = "name";

    public async Task<IReadOnlyList<CatalogCategoryView>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await store.ListAsync(organizationId, cancellationToken);

    public async Task<CatalogResult<CatalogCategoryView>> CreateAsync(
        Guid organizationId, Guid actorUserId, IPAddress? clientIp, string? name, CancellationToken cancellationToken)
    {
        var error = Validate(name, out var trimmed);

        if (error is not null)
        {
            return CatalogResult<CatalogCategoryView>.Invalid(NameKey, error);
        }

        if (await store.NameExistsAsync(organizationId, trimmed, null, cancellationToken))
        {
            return Duplicate();
        }

        try
        {
            return CatalogResult<CatalogCategoryView>.Ok(
                await store.CreateAsync(organizationId, trimmed, actorUserId, clientIp, cancellationToken));
        }
        catch (DuplicateCatalogCategoryNameException)
        {
            return Duplicate();
        }
    }

    public async Task<CatalogResult<CatalogCategoryView>> RenameAsync(
        Guid organizationId, Guid id, Guid actorUserId, IPAddress? clientIp, string? name, CancellationToken cancellationToken)
    {
        // The id is resolved first so another organization's id is 404 whatever the body is.
        var exists = (await store.ListAsync(organizationId, cancellationToken)).Any(category => category.Id == id);

        if (!exists)
        {
            return CatalogResult<CatalogCategoryView>.NotFound();
        }

        var error = Validate(name, out var trimmed);

        if (error is not null)
        {
            return CatalogResult<CatalogCategoryView>.Invalid(NameKey, error);
        }

        if (await store.NameExistsAsync(organizationId, trimmed, id, cancellationToken))
        {
            return Duplicate();
        }

        try
        {
            var view = await store.RenameAsync(organizationId, id, trimmed, actorUserId, clientIp, cancellationToken);

            return view is null ? CatalogResult<CatalogCategoryView>.NotFound() : CatalogResult<CatalogCategoryView>.Ok(view);
        }
        catch (DuplicateCatalogCategoryNameException)
        {
            return Duplicate();
        }
    }

    public async Task<CatalogResult<NoValue>> SetActiveAsync(
        Guid organizationId, Guid id, bool isActive, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken) =>
        await store.SetActiveAsync(organizationId, id, isActive, actorUserId, clientIp, cancellationToken)
            == CategoryWriteOutcome.NotFound
            ? CatalogResult<NoValue>.NotFound()
            : CatalogResult<NoValue>.NoOp();

    private static string? Validate(string? name, out string trimmed)
    {
        trimmed = name?.Trim() ?? string.Empty;

        return trimmed.Length == 0
            ? CatalogMessages.CategoryNameRequired
            : trimmed.Length > Domain.Catalog.ServiceCategory.NameMaxLength
                ? CatalogMessages.CategoryNameTooLong
                : null;
    }

    private static CatalogResult<CatalogCategoryView> Duplicate() =>
        CatalogResult<CatalogCategoryView>.Conflict(NameKey, CatalogMessages.CategoryDuplicateName);
}
