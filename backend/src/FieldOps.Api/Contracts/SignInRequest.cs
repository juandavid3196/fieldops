namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST /sessions. It has no organization, membership, user or role
/// field; unknown properties are ignored by the JSON serializer.
/// </summary>
public sealed record SignInRequest(
    string? Email,
    string? Password,
    bool? RememberMe);
