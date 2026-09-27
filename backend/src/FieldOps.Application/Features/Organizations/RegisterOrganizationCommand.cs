using System.Net;
using System.Text.Json;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization registration input (BR-01). It carries no organization,
/// branch, user or role identifier: every identifier is generated
/// server-side (FR-05).
/// </summary>
public sealed record RegisterOrganizationCommand(
    RegisterOrganizationCommand.OrganizationInput Organization,
    RegisterOrganizationCommand.BranchInput Branch,
    RegisterOrganizationCommand.OwnerInput Owner,
    IPAddress? ClientIp)
{
    public sealed record OrganizationInput(
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
        long? NextInvoiceNumber);

    public sealed record BranchInput(
        string? Name,
        string? Code,
        string? Phone,
        string? Email,
        string? Timezone,
        string? AddressLine1,
        string? City,
        string? StateRegion,
        string? PostalCode,
        string? CountryCode,
        JsonElement? BusinessHours);

    public sealed record OwnerInput(
        string? FirstName,
        string? LastName,
        string? Email,
        string? Password,
        string? Phone);
}
