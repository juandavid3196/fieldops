namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// The session organization's BR-01 fields plus <c>updatedAt</c> (FR-03).
/// <c>canManage</c> is derived by the caller from the session role, not
/// stored here.
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
    DateTimeOffset UpdatedAt);
