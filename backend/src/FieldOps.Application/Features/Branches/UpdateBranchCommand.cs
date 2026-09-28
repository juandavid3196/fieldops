using System.Net;
using System.Text.Json;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Branch update input (BR-03, BR-07). The organization id comes only from
/// the validated session (FR-02); the request body carries no organization
/// identifier.
/// </summary>
public sealed record UpdateBranchCommand(
    Guid OrganizationId,
    Guid BranchId,
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
    string? UpdatedAt,
    Guid ActorUserId,
    IPAddress? ClientIp);
