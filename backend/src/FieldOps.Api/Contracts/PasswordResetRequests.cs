namespace FieldOps.Api.Contracts;

/// <summary>
/// Bodies of the password reset endpoints. None carries a user or
/// organization identifier; unknown properties are ignored.
/// </summary>
public sealed record PasswordResetRequest(string? Email);

public sealed record PasswordResetTokenRequest(string? Token);

public sealed record ConfirmPasswordResetRequestBody(string? Token, string? Password);
