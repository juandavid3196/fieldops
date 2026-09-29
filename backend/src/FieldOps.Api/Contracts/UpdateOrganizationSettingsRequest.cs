using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /organization-settings (BR-01, BR-04, BR-07). It has no
/// organization identifier; unknown properties (including a client-supplied
/// "organizationId") are ignored by the JSON deserializer (FR-02).
/// <c>UpdatedAt</c> stays a raw string so it can be validated as a field
/// (BR-07: missing/malformed -&gt; 400 with key <c>updatedAt</c>) instead of
/// failing model binding for the whole body; <c>PricesIncludeTax</c> stays a
/// raw JSON value for the same reason (missing or wrong type -&gt; 400 with
/// its own key). <c>ConfirmCurrencyChange</c> is optional and never stored.
/// </summary>
public sealed record UpdateOrganizationSettingsRequest(
    string? Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    string? Timezone,
    string? Currency,
    decimal? DefaultTaxRate,
    string? QuotePrefix,
    string? WorkOrderPrefix,
    string? InvoicePrefix,
    long? NextInvoiceNumber,
    long? NextQuoteNumber,
    long? NextWorkOrderNumber,
    string? Website,
    string? AddressLine1,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    JsonElement? PricesIncludeTax,
    bool? ConfirmCurrencyChange,
    string? UpdatedAt);
