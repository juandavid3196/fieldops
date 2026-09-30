using System.Text.RegularExpressions;

namespace FieldOps.Domain.Catalog;

public sealed partial class CatalogItem
{
    public const int NameMaxLength = 160;

    public const int DescriptionMaxLength = 1000;

    public const decimal MaxMoney = 999_999_999_999.99m;

    private CatalogItem()
    {
    }

    private CatalogItem(
        Guid id,
        Guid organizationId,
        CatalogItemType type,
        string name,
        decimal unitPrice)
    {
        Id = id;
        OrganizationId = organizationId;
        Type = type;
        Name = name;
        Unit = "unit";
        UnitCost = 0m;
        UnitPrice = unitPrice;
        TaxRate = 0m;
        IsActive = true;
        IsTaxable = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public CatalogItemType Type { get; private set; }

    public string? Sku { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Unit { get; private set; } = "unit";

    public decimal UnitCost { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TaxRate { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsTaxable { get; private set; }

    /// <summary>Database-generated <c>lower(name)</c>; never assigned by the domain.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CatalogItem Create(
        Guid organizationId,
        CatalogItemType type,
        string name,
        decimal unitPrice)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Catalog item name is required.",
                nameof(name));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Unit price cannot be negative.");
        }

        return new CatalogItem(
            Guid.NewGuid(),
            organizationId,
            type,
            name.Trim(),
            unitPrice);
    }

    /// <summary>Creates a catalog item from validated values (BR-07).</summary>
    public static CatalogItem Create(
        Guid organizationId,
        CatalogItemType type,
        string name,
        string? description,
        decimal unitCost,
        decimal unitPrice,
        bool isTaxable,
        bool isActive)
    {
        var item = Create(organizationId, type, CollapseName(name), unitPrice);
        ValidateMoney(unitCost, nameof(unitCost));
        ValidateMoney(unitPrice, nameof(unitPrice));

        item.Description = NormalizeDescription(description);
        item.UnitCost = unitCost;
        item.IsTaxable = isTaxable;
        item.IsActive = isActive;

        return item;
    }

    /// <summary>
    /// Applies the editable fields; returns false (and leaves
    /// <see cref="UpdatedAt"/> untouched) when nothing changed.
    /// </summary>
    public bool Update(
        CatalogItemType type,
        string name,
        string? description,
        decimal unitCost,
        decimal unitPrice,
        bool isTaxable,
        bool isActive,
        DateTimeOffset now)
    {
        var collapsed = CollapseName(name);
        var normalizedDescription = NormalizeDescription(description);
        ValidateMoney(unitCost, nameof(unitCost));
        ValidateMoney(unitPrice, nameof(unitPrice));

        if (Type == type
            && string.Equals(Name, collapsed, StringComparison.Ordinal)
            && string.Equals(Description, normalizedDescription, StringComparison.Ordinal)
            && UnitCost == unitCost
            && UnitPrice == unitPrice
            && IsTaxable == isTaxable
            && IsActive == isActive)
        {
            return false;
        }

        Type = type;
        Name = collapsed;
        Description = normalizedDescription;
        UnitCost = unitCost;
        UnitPrice = unitPrice;
        IsTaxable = isTaxable;
        IsActive = isActive;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Changes the active flag; returns false when it already has that value.</summary>
    public bool SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Trims and collapses internal whitespace to single spaces (BR-07).</summary>
    public static string CollapseName(string? name) =>
        WhitespaceRun().Replace((name ?? string.Empty).Trim(), " ");

    /// <summary>The comparison key of BR-08: the lower-cased, whitespace-collapsed name.</summary>
    public static string NormalizeName(string? name) => CollapseName(name).ToLowerInvariant();

    /// <summary>
    /// Estimated margin percent (BR-09): (price - cost) / price * 100, rounded
    /// half away from zero to one decimal; null when the price is zero.
    /// </summary>
    public static decimal? EstimatedMarginPercent(decimal unitCost, decimal unitPrice) =>
        unitPrice == 0m
            ? null
            : Math.Round((unitPrice - unitCost) / unitPrice * 100m, 1, MidpointRounding.AwayFromZero);

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void ValidateMoney(decimal value, string parameterName)
    {
        if (value < 0 || value > MaxMoney)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Amount is outside the allowed range.");
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
