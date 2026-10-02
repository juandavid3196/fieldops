using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Features.Catalog;

public sealed record CatalogImageInfo(string ContentType, int SizeBytes, DateTimeOffset UpdatedAt);

public sealed record CatalogImageContent(string ContentType, byte[] Content, DateTimeOffset UpdatedAt);

public sealed record CatalogUsage(int Quotes, int Jobs, int Invoices);

/// <summary>A list/export row (BR-04).</summary>
public sealed record CatalogItemRow(
    Guid Id,
    string Type,
    string Name,
    string? Description,
    decimal UnitCost,
    decimal UnitPrice,
    decimal? EstimatedMarginPercent,
    bool IsTaxable,
    bool IsActive,
    bool HasImage,
    DateTimeOffset UpdatedAt);

/// <summary>The item detail: the row fields plus image metadata and usage (BR-04, BR-10).</summary>
public sealed record CatalogItemDetail(
    Guid Id,
    string Type,
    string Name,
    string? Description,
    decimal UnitCost,
    decimal UnitPrice,
    decimal? EstimatedMarginPercent,
    bool IsTaxable,
    bool IsActive,
    bool HasImage,
    DateTimeOffset UpdatedAt,
    CatalogImageInfo? Image,
    CatalogUsage Usage,
    Guid? CategoryId = null,
    string? CategoryName = null,
    bool? CategoryIsActive = null);

public sealed record CatalogListPage(IReadOnlyList<CatalogItemRow> Items, int Page, int PageSize, int TotalCount);

public sealed record CatalogSummary(
    int ActiveItems,
    int ActiveServices,
    int ActiveProducts,
    int InactiveItems,
    int AllItems,
    int Services,
    int Products,
    string Currency,
    string PublicRequestReadiness);

/// <summary>A catalog category with its item counts.</summary>
public sealed record CatalogCategoryView(Guid Id, string Name, bool IsActive, int ItemCount, int ActiveServiceCount);

public enum CategoryWriteOutcome
{
    NotFound,
    NoChange,
    Changed,
}

public sealed record CatalogOrganizationFormat(string Currency, string Timezone);

/// <summary>Validated item fields (BR-07), ready to persist.</summary>
public sealed record CatalogItemValues(
    CatalogItemType Type,
    string Name,
    string? Description,
    decimal UnitCost,
    decimal UnitPrice,
    bool IsTaxable,
    bool IsActive,
    Guid? CategoryId = null);

public enum CatalogSortField
{
    Name,
    Type,
    UnitCost,
    UnitPrice,
    Taxable,
    Status,
}

/// <summary>Validated BR-03 filters and sort.</summary>
public sealed record CatalogListFilter(
    CatalogItemType? Type,
    string? Search,
    bool? Taxable,
    bool? Active,
    CatalogSortField Sort,
    bool Descending);

public enum CatalogStateOutcome
{
    NotFound,
    NoChange,
    Changed,
}

public enum CatalogImageRemoval
{
    ItemNotFound,
    NoImage,
    Removed,
}

public enum CatalogResultKind
{
    Succeeded,
    NoContent,
    Invalid,
    NotFound,
    Conflict,
}

public sealed record CatalogRowError(int Row, string Column, string Message);

/// <summary>Outcome of a catalog use case, mapped to HTTP by the controller.</summary>
public sealed record CatalogResult<T>(
    CatalogResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyList<CatalogRowError>? RowErrors = null,
    string? Message = null)
{
    public static CatalogResult<T> Ok(T value) => new(CatalogResultKind.Succeeded, value);

    public static CatalogResult<T> NoOp() => new(CatalogResultKind.NoContent);

    public static CatalogResult<T> NotFound() => new(CatalogResultKind.NotFound);

    public static CatalogResult<T> Invalid(string key, string message) =>
        new(
            CatalogResultKind.Invalid,
            Errors: new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    public static CatalogResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(CatalogResultKind.Invalid, Errors: errors);

    public static CatalogResult<T> InvalidRows(IReadOnlyList<CatalogRowError> rowErrors) =>
        new(CatalogResultKind.Invalid, RowErrors: rowErrors);

    public static CatalogResult<T> Conflict(string key, string message) =>
        new(
            CatalogResultKind.Conflict,
            Errors: new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] },
            Message: message);

    public static CatalogResult<T> ConflictWithoutField(string message) =>
        new(CatalogResultKind.Conflict, Message: message);
}

public static class CatalogMessages
{
    public const string DuplicateName = "An item with this name already exists for this type.";

    public const string ImportConflict = "Some items already exist. Review the file and try again.";

    public const string TypeRequired = "Select a type.";

    public const string NameRequired = "Enter a name.";

    public const string NameTooLong = "Name must be 160 characters or fewer.";

    public const string DescriptionTooLong = "Description must be 1,000 characters or fewer.";

    public const string UnitCostInvalid = "Enter a unit cost of 0 or more.";

    public const string UnitPriceInvalid = "Enter a unit price of 0 or more.";

    public const string BooleanInvalid = "Enter true or false.";

    public const string CategoryInvalid = "Choose an active category.";

    public const string CategoryNameRequired = "Enter a category name.";

    public const string CategoryNameTooLong = "Category name must be 120 characters or fewer.";

    public const string CategoryDuplicateName = "A category with this name already exists.";
}

public static class CatalogAuditActions
{
    public const string EntityType = "catalog_item";

    public const string Created = "catalog_item.created";

    public const string Updated = "catalog_item.updated";

    public const string Activated = "catalog_item.activated";

    public const string Deactivated = "catalog_item.deactivated";

    public const string ImageUpdated = "catalog_item.image_updated";

    public const string ImageRemoved = "catalog_item.image_removed";

    public const string Imported = "catalog_item.imported";
}

public static class CatalogCategoryAuditActions
{
    public const string EntityType = "catalog_category";

    public const string Created = "catalog_category.created";

    public const string Renamed = "catalog_category.renamed";

    public const string Deactivated = "catalog_category.deactivated";

    public const string Reactivated = "catalog_category.reactivated";
}

/// <summary>Raised when a category save loses the UNIQUE (organization_id, name) race.</summary>
public sealed class DuplicateCatalogCategoryNameException(Exception innerException)
    : Exception("Another category already uses this name.", innerException);

/// <summary>Raised when a save loses the <c>ux_catalog_items_org_type_name</c> race (BR-08).</summary>
public sealed class DuplicateCatalogItemNameException(Exception innerException)
    : Exception("Another catalog item already uses this name for this type.", innerException);
