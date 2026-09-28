using System.Net;
using System.Text.Json;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Branch creation input (BR-03). The organization id comes only from the
/// validated session (FR-02); the request body carries no organization
/// identifier.
/// </summary>
public sealed record CreateBranchCommand(
    Guid OrganizationId,
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
    Guid ActorUserId,
    IPAddress? ClientIp);
