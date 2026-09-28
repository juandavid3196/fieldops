namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /organization-settings (BR-01, BR-07). It has no organization
/// identifier; unknown properties (including a client-supplied
/// "organizationId") are ignored by the JSON deserializer (FR-02).
/// <c>UpdatedAt</c> stays a raw string so it can be validated as a field
/// (BR-07: missing/malformed -&gt; 400 with key <c>updatedAt</c>) instead of
/// failing model binding for the whole body.
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
    string? UpdatedAt);
