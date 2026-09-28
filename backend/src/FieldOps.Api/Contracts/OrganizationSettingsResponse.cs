namespace FieldOps.Api.Contracts;

/// <summary>Body of GET/PUT /organization-settings (FR-03).</summary>
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
    DateTimeOffset UpdatedAt,
    bool CanManage);
