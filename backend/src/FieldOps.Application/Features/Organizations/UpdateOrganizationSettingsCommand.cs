using System.Net;
using System.Text.Json;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization settings update input (BR-01, BR-02, BR-04, BR-07). The
/// organization id comes only from the validated session (FR-02); the
/// request body carries no organization identifier. <c>PricesIncludeTax</c>
/// stays a raw JSON value so a missing or wrongly typed value maps to a
/// per-key error instead of failing model binding. <c>ConfirmCurrencyChange</c>
/// is never stored or audited (BR-06).
/// </summary>
public sealed record UpdateOrganizationSettingsCommand(
    Guid OrganizationId,
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
    bool ConfirmCurrencyChange,
    string? UpdatedAt,
    Guid ActorUserId,
    IPAddress? ClientIp);
