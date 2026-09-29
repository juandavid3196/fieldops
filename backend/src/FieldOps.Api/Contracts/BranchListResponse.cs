namespace FieldOps.Api.Contracts;

/// <summary>Body of GET /branches (FR-05).</summary>
public sealed record BranchListResponse(IReadOnlyList<BranchListItemResponse> Items);

public sealed record BranchListItemResponse(
    Guid Id,
    string Name,
    string Code,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    string? Timezone,
    bool IsActive,
    bool IsMain,
    int TechnicianCount);
