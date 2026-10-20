using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /branches/{branchId}/business-hours (business-hours BR-01 to BR-06). It has no organization
/// identifier; unknown top-level properties are ignored. <c>Days</c> stays a raw element so structure violations are
/// reported by the validator, and <c>UpdatedAt</c> a raw string like <see cref="UpdateBranchRequest"/>.
/// </summary>
public sealed record UpdateBusinessHoursRequest(JsonElement? Days, string? UpdatedAt);
