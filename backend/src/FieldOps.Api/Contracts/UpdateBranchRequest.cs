using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /branches/{id} (BR-03, BR-07). It has no organization
/// identifier; unknown properties are ignored by the JSON deserializer
/// (FR-02). <c>UpdatedAt</c> stays a raw string, per
/// <see cref="UpdateOrganizationSettingsRequest"/>.
/// </summary>
public sealed record UpdateBranchRequest(
    string? Name,
    string? Code,
    string? Phone,
    string? Email,
    string? Timezone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    JsonElement? BusinessHours,
    JsonElement? ServicePostalCodes,
    JsonElement? UsesCompanyBilling,
    string? UpdatedAt);
