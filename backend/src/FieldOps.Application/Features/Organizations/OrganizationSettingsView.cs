namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// The session organization's settings fields plus <c>updatedAt</c>,
/// <c>hasInvoices</c> and the logo metadata (FR-03, FR-07). <c>canManage</c>
/// is derived by the caller from the session role, not stored here.
/// </summary>
public sealed record OrganizationSettingsView(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    string Timezone,
    string Currency,
    decimal DefaultTaxRate,
    string QuotePrefix,
    string WorkOrderPrefix,
    string InvoicePrefix,
    long NextInvoiceNumber,
    long NextQuoteNumber,
    long NextWorkOrderNumber,
    string? Website,
    string? AddressLine1,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    bool PricesIncludeTax,
    bool HasInvoices,
    OrganizationLogoMetadata? Logo,
    DateTimeOffset UpdatedAt);

/// <summary>Logo metadata exposed by settings and by the logo upload (BR-07).</summary>
public sealed record OrganizationLogoMetadata(
    string ContentType,
    int SizeBytes,
    DateTimeOffset UpdatedAt);
