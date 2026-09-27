using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST /organization-registrations, per BR-01. It has no
/// organization, branch, user or role identifier; unknown properties
/// (including a client-supplied "organizationId") are ignored by the JSON
/// serializer (FR-05).
/// </summary>
public sealed record OrganizationRegistrationRequest(
    OrganizationRegistrationRequest.OrganizationSection? Organization,
    OrganizationRegistrationRequest.BranchSection? Branch,
    OrganizationRegistrationRequest.OwnerSection? Owner)
{
    public sealed record OrganizationSection(
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

    public sealed record BranchSection(
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

    public sealed record OwnerSection(
        string? FirstName,
        string? LastName,
        string? Email,
        string? Password,
        string? Phone);
}
