namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /account/profile. It has no user, organization, email or role
/// field; unknown properties are ignored by the JSON serializer (BR-01).
/// </summary>
public sealed record UpdateAccountProfileRequest(string? FirstName, string? LastName, string? Phone);

/// <summary>Body of POST /account/password (BR-02); the confirmation is client-only.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
