using System.Globalization;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Features.Catalog;

/// <summary>Unvalidated item body (BR-07); money stays text so malformed values map to a field error.</summary>
public sealed record CatalogItemInput(
    string? Type,
    string? Name,
    string? Description,
    string? UnitCost,
    string? UnitPrice,
    bool? IsTaxable,
    bool? IsActive);

/// <summary>Item field validation shared by the JSON body and the CSV import (BR-07, BR-13).</summary>
public static class CatalogItemRules
{
    public const string TypeKey = "type";

    public const string NameKey = "name";

    public const string DescriptionKey = "description";

    public const string UnitCostKey = "unitCost";

    public const string UnitPriceKey = "unitPrice";

    public static string TypeText(CatalogItemType type) => type == CatalogItemType.Service ? "service" : "product";

    public static bool TryParseType(string? text, out CatalogItemType type)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "service":
                type = CatalogItemType.Service;
                return true;
            case "product":
                type = CatalogItemType.Product;
                return true;
            default:
                type = default;
                return false;
        }
    }

    /// <summary>Validates every field; returns the values or the per-key messages (one per key).</summary>
    public static CatalogItemValues? Validate(
        CatalogItemInput input, out IReadOnlyDictionary<string, string> errors)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!TryParseType(input.Type, out var type))
        {
            found[TypeKey] = CatalogMessages.TypeRequired;
        }

        var name = CatalogItem.CollapseName(input.Name);

        if (name.Length == 0)
        {
            found[NameKey] = CatalogMessages.NameRequired;
        }
        else if (name.Length > CatalogItem.NameMaxLength)
        {
            found[NameKey] = CatalogMessages.NameTooLong;
        }

        var description = input.Description?.Trim();

        if (description is { Length: > CatalogItem.DescriptionMaxLength })
        {
            found[DescriptionKey] = CatalogMessages.DescriptionTooLong;
        }

        if (!TryParseMoney(input.UnitCost, out var unitCost))
        {
            found[UnitCostKey] = CatalogMessages.UnitCostInvalid;
        }

        if (!TryParseMoney(input.UnitPrice, out var unitPrice))
        {
            found[UnitPriceKey] = CatalogMessages.UnitPriceInvalid;
        }

        errors = found;

        return found.Count > 0
            ? null
            : new CatalogItemValues(
                type,
                name,
                string.IsNullOrEmpty(description) ? null : description,
                unitCost,
                unitPrice,
                input.IsTaxable ?? true,
                input.IsActive ?? true);
    }

    /// <summary>Invariant decimal, no sign, separators or symbols; 0 to 999,999,999,999.99 with at most two decimals.</summary>
    public static bool TryParseMoney(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value >= 0m
            && value <= CatalogItem.MaxMoney
            && decimal.Round(value, 2, MidpointRounding.ToZero) == value;
    }
}

/// <summary>Validation of the BR-03 list/export query (all values arrive as text so bad input maps to a 400 key).</summary>
public static class CatalogQueryParser
{
    public const int PageSize = 10;

    public const int SearchMaxLength = 100;

    public static CatalogListFilter? Parse(
        string? type,
        string? search,
        string? taxStatus,
        string? status,
        string? sort,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var found = new Dictionary<string, string[]>(StringComparer.Ordinal);

        CatalogItemType? parsedType = null;

        if (!string.IsNullOrEmpty(type))
        {
            if (CatalogItemRules.TryParseType(type, out var value) && type == type.ToLowerInvariant())
            {
                parsedType = value;
            }
            else
            {
                found["type"] = ["Use service or product."];
            }
        }

        var trimmedSearch = search?.Trim();

        if (trimmedSearch is { Length: > SearchMaxLength })
        {
            found["search"] = ["Search must be 100 characters or fewer."];
        }

        bool? taxable = null;

        if (!string.IsNullOrEmpty(taxStatus))
        {
            switch (taxStatus)
            {
                case "taxable":
                    taxable = true;
                    break;
                case "non_taxable":
                    taxable = false;
                    break;
                default:
                    found["taxStatus"] = ["Use taxable or non_taxable."];
                    break;
            }
        }

        bool? active = null;

        if (!string.IsNullOrEmpty(status))
        {
            switch (status)
            {
                case "active":
                    active = true;
                    break;
                case "inactive":
                    active = false;
                    break;
                default:
                    found["status"] = ["Use active or inactive."];
                    break;
            }
        }

        var sortField = CatalogSortField.Name;
        var descending = false;

        if (!string.IsNullOrEmpty(sort))
        {
            var key = sort.StartsWith('-') ? sort[1..] : sort;
            descending = sort.StartsWith('-');

            switch (key)
            {
                case "name":
                    sortField = CatalogSortField.Name;
                    break;
                case "type":
                    sortField = CatalogSortField.Type;
                    break;
                case "unitCost":
                    sortField = CatalogSortField.UnitCost;
                    break;
                case "unitPrice":
                    sortField = CatalogSortField.UnitPrice;
                    break;
                case "taxable":
                    sortField = CatalogSortField.Taxable;
                    break;
                case "status":
                    sortField = CatalogSortField.Status;
                    break;
                default:
                    found["sort"] = ["Use a supported sort."];
                    break;
            }
        }

        errors = found;

        return found.Count > 0
            ? null
            : new CatalogListFilter(
                parsedType,
                string.IsNullOrEmpty(trimmedSearch) ? null : trimmedSearch,
                taxable,
                active,
                sortField,
                descending);
    }

    /// <summary>Page is an integer of at least 1 (default 1).</summary>
    public static bool TryParsePage(string? text, out int page)
    {
        page = 1;

        return string.IsNullOrEmpty(text)
            || (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1);
    }

    /// <summary>The page size is fixed at 10: any other value is rejected.</summary>
    public static bool IsValidPageSize(string? text) =>
        string.IsNullOrEmpty(text)
        || (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size == PageSize);
}
