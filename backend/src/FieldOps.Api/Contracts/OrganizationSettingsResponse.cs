namespace FieldOps.Api.Contracts;

/// <summary>Body of GET/PUT /organization-settings (FR-03, FR-07).</summary>
public sealed record OrganizationSettingsResponse(
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
    OrganizationLogoResponse? Logo,
    DateTimeOffset UpdatedAt,
    bool CanManage);

/// <summary>Logo metadata: body of PUT /organization-settings/logo and the settings <c>logo</c> member (BR-07).</summary>
public sealed record OrganizationLogoResponse(
    string ContentType,
    int SizeBytes,
    DateTimeOffset UpdatedAt);
