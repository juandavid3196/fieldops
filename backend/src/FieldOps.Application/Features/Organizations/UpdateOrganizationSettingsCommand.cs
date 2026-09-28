using System.Net;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization settings update input (BR-01, BR-02, BR-07). The
/// organization id comes only from the validated session (FR-02); the
/// request body carries no organization identifier.
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
    string? UpdatedAt,
    Guid ActorUserId,
    IPAddress? ClientIp);
