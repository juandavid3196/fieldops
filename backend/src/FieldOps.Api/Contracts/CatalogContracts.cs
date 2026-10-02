using System.Text.Json;
using FieldOps.Application.Features.Catalog;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST/PUT /catalog-items (BR-07). Values arrive as raw JSON so a wrong
/// JSON type maps to a field error instead of an unkeyed model-binding failure.
/// No organization identifier is accepted; unknown properties are ignored.
/// </summary>
public sealed record CatalogItemRequest(
    JsonElement? Type,
    JsonElement? Name,
    JsonElement? Description,
    JsonElement? UnitCost,
    JsonElement? UnitPrice,
    JsonElement? IsTaxable,
    JsonElement? IsActive,
    JsonElement? CategoryId = null)
{
    /// <summary>Converts to the application input, or returns the per-key errors of wrongly typed values.</summary>
    public CatalogItemInput? ToInput(out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var type = Text(Type, CatalogItemRules.TypeKey, CatalogMessages.TypeRequired, errors);
        var name = Text(Name, CatalogItemRules.NameKey, CatalogMessages.NameRequired, errors);
        var description = Text(Description, CatalogItemRules.DescriptionKey, "Enter a valid description.", errors);
        var unitCost = Money(UnitCost, CatalogItemRules.UnitCostKey, CatalogMessages.UnitCostInvalid, errors);
        var unitPrice = Money(UnitPrice, CatalogItemRules.UnitPriceKey, CatalogMessages.UnitPriceInvalid, errors);
        var taxable = Flag(IsTaxable, "isTaxable", errors);
        var active = Flag(IsActive, "isActive", errors);
        var categoryId = Text(CategoryId, CatalogItemRules.CategoryKey, CatalogMessages.CategoryInvalid, errors);

        return errors.Count > 0
            ? null
            : new CatalogItemInput(type, name, description, unitCost, unitPrice, taxable, active, categoryId);
    }

    private static string? Text(JsonElement? value, string key, string message, Dictionary<string, string[]> errors)
    {
        switch (value?.ValueKind)
        {
            case null or JsonValueKind.Undefined or JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
                return value.Value.GetString();
            default:
                errors[key] = [message];
                return null;
        }
    }

    private static string? Money(JsonElement? value, string key, string message, Dictionary<string, string[]> errors)
    {
        switch (value?.ValueKind)
        {
            case null or JsonValueKind.Undefined or JsonValueKind.Null:
                return null;
            case JsonValueKind.Number:
                return value.Value.GetRawText();
            case JsonValueKind.String:
                return value.Value.GetString();
            default:
                errors[key] = [message];
                return null;
        }
    }

    private static bool? Flag(JsonElement? value, string key, Dictionary<string, string[]> errors)
    {
        switch (value?.ValueKind)
        {
            case null or JsonValueKind.Undefined or JsonValueKind.Null:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                errors[key] = [CatalogMessages.BooleanInvalid];
                return null;
        }
    }
}

/// <summary>Query of GET /catalog-items and /export (BR-03); strings so bad input maps to a 400 key.</summary>
public sealed class CatalogListRequest
{
    public string? Type { get; init; }

    public string? Search { get; init; }

    public string? TaxStatus { get; init; }

    public string? Status { get; init; }

    public string? Sort { get; init; }

    public string? Page { get; init; }

    public string? PageSize { get; init; }

    public CatalogListQuery ToQuery() => new(Type, Search, TaxStatus, Status, Sort, Page, PageSize);
}

public sealed record CatalogListResponse(
    IReadOnlyList<CatalogItemRow> Items, int Page, int PageSize, int TotalCount);

public sealed record CatalogImageResponse(string ContentType, int SizeBytes, DateTimeOffset UpdatedAt);

public sealed record CatalogImportResponse(int ImportedCount);

/// <summary>Body of POST/PUT /catalog-categories; a wrongly typed name is reported as missing.</summary>
public sealed record CatalogCategoryRequest(JsonElement? Name)
{
    public string? NameText => Name is { ValueKind: JsonValueKind.String } name ? name.GetString() : null;
}

public sealed record CatalogCategoryListResponse(IReadOnlyList<CatalogCategoryView> Items);
