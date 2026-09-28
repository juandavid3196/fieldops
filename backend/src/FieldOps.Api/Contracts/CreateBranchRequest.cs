using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST /branches (BR-03). It has no organization identifier;
/// unknown properties (including a client-supplied "organizationId") are
/// ignored by the JSON deserializer (FR-02).
/// </summary>
public sealed record CreateBranchRequest(
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
    JsonElement? BusinessHours);
