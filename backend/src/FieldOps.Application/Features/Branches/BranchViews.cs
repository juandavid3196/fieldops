namespace FieldOps.Application.Features.Branches;

/// <summary>List row fields (FR-05).</summary>
public sealed record BranchListItemView(
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
    bool IsActive);

/// <summary>Detail fields, including business hours and the concurrency token (FR-06).</summary>
public sealed record BranchDetailView(
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
    string BusinessHours,
    bool IsActive,
    DateTimeOffset UpdatedAt);
