using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>Body of GET /branches/{id}, POST /branches and PUT /branches/{id} (FR-06 to FR-08).</summary>
public sealed record BranchDetailResponse(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    string? Timezone,
    JsonElement BusinessHours,
    bool IsActive,
    bool IsMain,
    IReadOnlyList<string> ServicePostalCodes,
    bool UsesCompanyBilling,
    DateTimeOffset UpdatedAt);
